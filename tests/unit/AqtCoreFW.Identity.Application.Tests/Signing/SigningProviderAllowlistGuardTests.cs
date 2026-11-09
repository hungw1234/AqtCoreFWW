using System.Diagnostics.Metrics;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Application.Telemetry;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Errors;

namespace AqtCoreFW.Identity.Application.Tests.Signing;

/// <summary>
/// Unit matrix for <see cref="SigningProviderAllowlistGuard"/> — the early fail-closed provider-allowlist
/// authorization. Covers the enrolled/bootstrap/unknown/unmapped predicate, the F-B subject-is-the-signer
/// contract, and the deny side effects (audit row + Prometheus counter). Repos + audit are substituted;
/// the counter is observed via a scoped <see cref="MeterListener"/>.
/// </summary>
public sealed class SigningProviderAllowlistGuardTests
{
    private const string MysignKey = "remote-viettel-mysign";  // → EnrollmentKind.RemoteViettelMysign
    private const string UsbKey     = "usb-vgca";              // → EnrollmentKind.UsbTokenVgca
    private const string DenyAction = "SIGNING_PROVIDER_ALLOWLIST_DENIED";

    private readonly ISignatureProviderResolver _resolver = Substitute.For<ISignatureProviderResolver>();
    private readonly IProviderEnrollmentKindMap _providerKindMap = Substitute.For<IProviderEnrollmentKindMap>();
    private readonly ISignerCertEnrollmentRepository _enrollments = Substitute.For<ISignerCertEnrollmentRepository>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _signerId = Guid.NewGuid();

    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    public SigningProviderAllowlistGuardTests()
    {
        // The two real mapped keys resolve to their kind; any other (e.g. "registered-but-unmapped") returns
        // the substitute default (false) → the guard's unmapped-deny branch. The real key→kind table is covered
        // by ProviderEnrollmentKindMapTests + the arch drift test.
        _providerKindMap.TryResolve(MysignKey, out Arg.Any<EnrollmentKind>())
            .Returns(ci => { ci[1] = EnrollmentKind.RemoteViettelMysign; return true; });
        _providerKindMap.TryResolve(UsbKey, out Arg.Any<EnrollmentKind>())
            .Returns(ci => { ci[1] = EnrollmentKind.UsbTokenVgca; return true; });
    }

    private SigningProviderAllowlistGuard Sut() => new(_resolver, _providerKindMap, _enrollments, _audit);

    // A provider key resolves (registered) unless explicitly told to throw.
    private void ProviderRegistered(string key) =>
        _resolver.Resolve(key).Returns(Substitute.For<IDigitalSignatureProvider>());

    private void ProviderUnknown(string key) =>
        _resolver.Resolve(key).Returns(_ => throw new KeyNotFoundException($"No provider '{key}'."));

    private void ActiveEnrollments(Guid userId, params SignerCertEnrollment[] rows) =>
        _enrollments.GetActiveByUserAsync(_tenantId, userId, Arg.Any<CancellationToken>())
            .Returns(rows);

    private void HasAnyEnrollment(Guid userId, bool value) =>
        _enrollments.HasAnyEnrollmentAsync(_tenantId, userId, Arg.Any<CancellationToken>())
            .Returns(value);

    private SignerCertEnrollment Enroll(EnrollmentKind kind, CertApprovalStatus status, Guid? userId = null) =>
        SignerCertEnrollment.Create(
            _tenantId, userId ?? _signerId, "S1", "CN=CA", "T1", "CN=A", userId ?? _signerId,
            kind, status, CertSubjectType.Personal, "A", Gen);

    // ── Allow / defer paths ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrolledSigner_ApprovedMatchingKind_Allowed_NoAuditNoCounter()
    {
        ProviderRegistered(MysignKey);
        HasAnyEnrollment(_signerId, true);
        ActiveEnrollments(_signerId, Enroll(EnrollmentKind.RemoteViettelMysign, CertApprovalStatus.Approved));

        using var counter = new DenyCounterProbe();
        var result = await Sut().EnsureAllowedAsync(MysignKey, _tenantId, _signerId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        counter.Count.Should().Be(0);
        await _audit.DidNotReceive().LogCommandAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<Type?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ZeroEnrollments_Allowed_DeferToBindingGuard()
    {
        // Bootstrap tier: a signer with NO enrollment rows passes the guard (late SignerBindingGuard decides).
        ProviderRegistered(MysignKey);
        HasAnyEnrollment(_signerId, false);
        ActiveEnrollments(_signerId /* none */);

        var result = await Sut().EnsureAllowedAsync(MysignKey, _tenantId, _signerId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownProviderKey_Allowed_DeferToOrchestrator404()
    {
        // Resolver throws KeyNotFoundException → the guard defers so the orchestrator's own routing 404s.
        ProviderUnknown("no-such-provider");

        var result = await Sut().EnsureAllowedAsync("no-such-provider", _tenantId, _signerId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _enrollments.DidNotReceive()
            .GetActiveByUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── Deny paths ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrolledSigner_ApprovedWrongKind_Denied_NotEnrolled_WithAuditAndCounter()
    {
        ProviderRegistered(MysignKey);
        HasAnyEnrollment(_signerId, true);
        ActiveEnrollments(_signerId, Enroll(EnrollmentKind.UsbTokenVgca, CertApprovalStatus.Approved));

        using var counter = new DenyCounterProbe();
        var result = await Sut().EnsureAllowedAsync(MysignKey, _tenantId, _signerId, CancellationToken.None);

        AssertDenied(result);
        counter.Count.Should().Be(1);
        await AssertDenyAudited("not_enrolled");
    }

    [Fact]
    public async Task EnrolledSigner_PendingOnly_Denied()
    {
        // Pending ≠ Approved → no enrolled-tier match; HasAny=true → no bootstrap defer → deny.
        ProviderRegistered(MysignKey);
        HasAnyEnrollment(_signerId, true);
        ActiveEnrollments(_signerId, Enroll(EnrollmentKind.RemoteViettelMysign, CertApprovalStatus.Pending));

        var result = await Sut().EnsureAllowedAsync(MysignKey, _tenantId, _signerId, CancellationToken.None);

        AssertDenied(result);
        await AssertDenyAudited("not_enrolled");
    }

    [Fact]
    public async Task EnrolledSigner_RejectedInactiveOnly_Denied_NoBootstrapDowngrade()
    {
        // A Rejected/revoked (inactive) row keeps the signer in the enrolled tier (HasAny=true), so the guard
        // must NOT fall back to the bootstrap defer even though GetActive returns nothing.
        ProviderRegistered(MysignKey);
        HasAnyEnrollment(_signerId, true);
        ActiveEnrollments(_signerId /* GetActive excludes the inactive Rejected row */);

        var result = await Sut().EnsureAllowedAsync(MysignKey, _tenantId, _signerId, CancellationToken.None);

        AssertDenied(result);
        await AssertDenyAudited("not_enrolled");
    }

    [Fact]
    public async Task RegisteredButUnmappedProvider_Denied_Unmapped_FailClosed()
    {
        // Provider resolves (registered) but has no ProviderEnrollmentKindMap entry → fail-closed deny.
        const string unmapped = "registered-but-unmapped";
        ProviderRegistered(unmapped);

        using var counter = new DenyCounterProbe();
        var result = await Sut().EnsureAllowedAsync(unmapped, _tenantId, _signerId, CancellationToken.None);

        AssertDenied(result);
        counter.Count.Should().Be(1);
        await AssertDenyAudited("unmapped");
        // Fail-closed BEFORE any enrollment read.
        await _enrollments.DidNotReceive()
            .GetActiveByUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── F-B: the subject is the SIGNER, never the initiator ─────────────────────────────────────────

    [Fact]
    public async Task SubjectIsSigner_AllowsOnSignersEnrollment_NotInitiators()
    {
        // Initiator ≠ signer. The signer holds the matching Approved enrollment; the guard reads the SIGNER.
        var initiatorId = Guid.NewGuid();
        ProviderRegistered(MysignKey);
        HasAnyEnrollment(_signerId, true);
        ActiveEnrollments(_signerId, Enroll(EnrollmentKind.RemoteViettelMysign, CertApprovalStatus.Approved));
        // Initiator has nothing — must be irrelevant.
        HasAnyEnrollment(initiatorId, false);
        ActiveEnrollments(initiatorId /* none */);

        var result = await Sut().EnsureAllowedAsync(MysignKey, _tenantId, _signerId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SubjectIsSigner_DeniesWhenSignerLacksEnrollment_EvenIfInitiatorHasIt()
    {
        // Initiator ≠ signer. The INITIATOR holds the matching enrollment, the signer does not — the guard
        // must deny (it must not be fooled by the initiator's enrollment).
        var initiatorId = Guid.NewGuid();
        ProviderRegistered(MysignKey);
        HasAnyEnrollment(initiatorId, true);
        ActiveEnrollments(initiatorId, Enroll(EnrollmentKind.RemoteViettelMysign, CertApprovalStatus.Approved, initiatorId));
        HasAnyEnrollment(_signerId, true);   // signer has an unrelated row but no matching Approved kind
        ActiveEnrollments(_signerId, Enroll(EnrollmentKind.UsbTokenVgca, CertApprovalStatus.Approved));

        var result = await Sut().EnsureAllowedAsync(MysignKey, _tenantId, _signerId, CancellationToken.None);

        AssertDenied(result);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static void AssertDenied(FluentResults.Result result)
    {
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
    }

    private Task AssertDenyAudited(string reason) =>
        _audit.Received(1).LogCommandAsync(
            Arg.Is<string>(a => a == DenyAction),
            Arg.Is<string>(m => m == "Identity"),
            Arg.Is<string>(rt => rt == "SigningRequest"),
            Arg.Any<string?>(),
            Arg.Any<Type?>(),
            Arg.Is<string?>(snapshot => snapshot != null && snapshot.Contains($"\"reason\":\"{reason}\"")),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());

    /// <summary>
    /// Scoped observer of the deny counter. Sums measurements for the single instrument while alive, so a test
    /// can assert its own act incremented the counter without cross-test bleed (methods within a class run
    /// sequentially; no other test exercises this guard's static meter).
    /// </summary>
    private sealed class DenyCounterProbe : IDisposable
    {
        private readonly MeterListener _listener = new();
        private long _count;

        public DenyCounterProbe()
        {
            _listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == SigningMetrics.MeterName
                    && instrument.Name == "signing.provider_allowlist.denied")
                    l.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
                Interlocked.Add(ref _count, measurement));
            _listener.Start();
        }

        public long Count => Interlocked.Read(ref _count);

        public void Dispose() => _listener.Dispose();
    }
}
