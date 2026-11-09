using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// Integration teeth for the signing provider-allowlist guard, exercised through the two orchestrator callers.
///
/// <para>The guard fires from <c>SignatureOrchestrator.RequestSignatureAsync</c> — the shared chokepoint — so
/// these tests prove: (1) the self-sign path denies an enrolled-wrong-kind request with a <b>403</b> and
/// <b>no <c>SigningRequest</c> row persisted</b> (early 403, provably distinct from the late binding-guard 403
/// that occurs at cert-arrival AFTER a request exists — red-team d); (2) the Forms path
/// (<see cref="ISigningRequestClient"/> → <c>InProcessSigningRequestClient</c>) is covered too (F-A);
/// (3) an unknown provider key still 404s (unchanged); (4) a 0-enrollment signer is deferred (not denied).</para>
///
/// <para><b>Removal-test teeth (red-team d):</b> if the guard call is deleted from the orchestrator, the
/// enrolled-wrong-kind request proceeds past routing and no longer yields a <see cref="ForbiddenError"/> — the
/// <c>ForbiddenError</c> + zero-row assertions below both flip, so this test fails, distinguishing the early
/// 403 from the late one.</para>
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class SigningProviderAllowlistHttpTests : IClassFixture<WebAppFixture>
{
    private const string MysignKey = "remote-viettel-mysign"; // EnrollmentKind.RemoteViettelMysign
    private readonly WebAppFixture _app;

    public SigningProviderAllowlistHttpTests(WebAppFixture app) => _app = app;

    // Seeds an Approved USB-VGCA enrollment — the WRONG kind for a Mysign request → the guard must deny.
    private async Task SeedApprovedUsbEnrollmentAsync(Guid tenantId, Guid signerId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var enrollment = SignerCertEnrollment.Create(
            tenantId, signerId, "USB-SERIAL-" + signerId.ToString("N")[..8], "CN=VGCA USB CA",
            "USB-THUMB-" + signerId.ToString("N")[..8], subject: null, enrolledBy: signerId,
            kind: EnrollmentKind.UsbTokenVgca, approvalStatus: CertApprovalStatus.Approved,
            subjectType: CertSubjectType.Personal, displayName: null, gen: gen);
        db.Set<SignerCertEnrollment>().Add(enrollment);
        await db.SaveChangesAsync();
    }

    private async Task<int> CountSigningRequestsAsync(Guid tenantId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.SigningRequests.CountAsync(r => r.TenantId == tenantId);
    }

    private static RequestSignatureInput SelfSignInput(Guid tenantId, Guid signerId, string providerKey) =>
        new(
            TenantId: tenantId,
            ArtifactKind: SigningArtifactKind.DetachedData,
            ArtifactRef: "allowlist-test-artifact",
            ProviderKey: providerKey,
            AssignedSignerId: signerId,
            InitiatedBy: signerId);

    // ── (1) Self-sign path: enrolled-wrong-kind → 403 + no SigningRequest row ───────────────────────

    [Fact]
    public async Task SelfSignPath_EnrolledWrongKind_Denied403_NoSigningRequestPersisted()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        await SeedApprovedUsbEnrollmentAsync(tenantId, signerId);

        using var scope = _app.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISignatureOrchestrator>();

        var result = await orchestrator.RequestSignatureAsync(
            SelfSignInput(tenantId, signerId, MysignKey), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError,
            "the allowlist guard denies before routing/execution");
        (await CountSigningRequestsAsync(tenantId)).Should().Be(0,
            "the early 403 must occur before any SigningRequest row is created (distinct from the late 403)");
    }

    // ── (2) Forms path (F-A): denied at the orchestrator via the cross-module client ────────────────

    [Fact]
    public async Task FormsPath_EnrolledWrongKind_DeniedAtOrchestrator_NoSigningRequestPersisted()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        await SeedApprovedUsbEnrollmentAsync(tenantId, signerId);

        using var scope = _app.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ISigningRequestClient>();

        var outcome = await client.RequestSignatureAsync(new SigningRequestClientInput
        {
            TenantId = tenantId,
            ArtifactKind = "DetachedData",
            ArtifactRef = "allowlist-forms-artifact",
            ProviderKey = MysignKey,
            AssignedSignerId = signerId,
            InitiatedBy = signerId,
        }, CancellationToken.None);

        outcome.IsSuccess.Should().BeFalse();
        // The client maps the guard's typed ForbiddenError verbatim: its metadata ErrorCode
        // (ErrorCodes.Security.Forbidden = the 403 code) wins over the generic CLR-type fallback. Proves the
        // guard covers the Forms→orchestrator caller, not just the MediatR handler (F-A).
        outcome.ErrorCode.Should().Be(ErrorCodes.Security.Forbidden);
        (await CountSigningRequestsAsync(tenantId)).Should().Be(0);
    }

    // ── (3) Unknown provider key → 404 (unchanged), not a guard 403 ─────────────────────────────────

    [Fact]
    public async Task UnknownProviderKey_Returns404_NotForbidden()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        await SeedApprovedUsbEnrollmentAsync(tenantId, signerId);

        using var scope = _app.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISignatureOrchestrator>();

        var result = await orchestrator.RequestSignatureAsync(
            SelfSignInput(tenantId, signerId, "no-such-provider"), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is NotFoundError, "an unknown provider key is deferred → 404");
        result.Errors.Should().NotContain(e => e is ForbiddenError);
    }

    // ── (4) 0-enrollment signer is deferred (not denied by the allowlist) ───────────────────────────

    [Fact]
    public async Task ZeroEnrollmentSigner_NotDeniedByAllowlist()
    {
        // No enrollment rows for this signer → the guard defers to the late SignerBindingGuard. Assert at the
        // guard boundary (deterministic): the allowlist decision is Ok, regardless of downstream routing.
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();

        using var scope = _app.Services.CreateScope();
        var guard = scope.ServiceProvider.GetRequiredService<ISigningProviderAllowlistGuard>();

        var decision = await guard.EnsureAllowedAsync(MysignKey, tenantId, signerId, CancellationToken.None);

        decision.IsSuccess.Should().BeTrue("a 0-enrollment signer must pass the allowlist (bootstrap defer)");
    }
}
