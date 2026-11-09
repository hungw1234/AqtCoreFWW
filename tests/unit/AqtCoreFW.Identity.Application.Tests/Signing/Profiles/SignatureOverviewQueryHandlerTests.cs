using AqtCoreFW.Identity.Application.Signing.Profiles;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Application.Tests.Signing.Profiles;

// The overview is a pure read-projection; the security boundary (JWT-pin / IDOR) is proven in the
// profile + enroll suites. These tests pin the aggregation: caller-scoping, the national-id flag, the
// active+Approved unlock gate, full-history enrollment exposure, and the tenant-pinned 404.
public sealed class SignatureOverviewQueryHandlerTests
{
    private const string Cccd = "012345678901";

    private readonly IUserSignatureProfileRepository _profiles = Substitute.For<IUserSignatureProfileRepository>();
    private readonly ISignerCertEnrollmentRepository _enrollments = Substitute.For<ISignerCertEnrollmentRepository>();
    private readonly UserManager<ApplicationUser> _userManager = MakeUserManager();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();

    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    private SignatureOverviewQueryHandler Sut() => new(_profiles, _enrollments, _userManager);

    private void StubUser(string? cccd) =>
        _userManager.FindByIdAsync(_callerId.ToString())
            .Returns(new ApplicationUser { Id = _callerId, TenantId = _tenantId, SigningNationalId = cccd });

    private SignerCertEnrollment Enrollment(CertApprovalStatus status, string serial = "S1")
    {
        // Always create Pending, then drive the lifecycle through the entity's own transitions —
        // creating directly in a terminal state and then transitioning would throw.
        var e = SignerCertEnrollment.Create(
            _tenantId, _callerId, serial, "CN=CA", "T", $"CN=A, SERIALNUMBER=CCCD:{Cccd}", _callerId,
            EnrollmentKind.UsbTokenVgca, CertApprovalStatus.Pending,
            CertSubjectType.Personal, "A", Gen);
        if (status == CertApprovalStatus.Approved) e.Approve(_callerId);
        if (status == CertApprovalStatus.Rejected) e.Reject(_callerId, "bad cert");
        return e;
    }

    [Fact]
    public async Task Handle_AggregatesProfilesAndEnrollments_ScopedToCaller()
    {
        StubUser(Cccd);
        var profile = UserSignatureProfile.Create(Gen, _tenantId, _callerId, "Default", _callerId);
        profile.MarkDefault(_callerId);
        _profiles.ListByUserAsync(_tenantId, _callerId, Arg.Any<CancellationToken>())
            .Returns(new List<UserSignatureProfile> { profile });
        _enrollments.GetByUserAsync(_tenantId, _callerId, Arg.Any<CancellationToken>())
            .Returns(new[] { Enrollment(CertApprovalStatus.Approved) });

        var result = await Sut().Handle(new SignatureOverviewQuery(_tenantId, _callerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.NationalIdEstablished.Should().BeTrue();
        result.Value.HasApprovedEnrollment.Should().BeTrue();
        result.Value.Profiles.Should().ContainSingle(p => p.DisplayName == "Default" && p.IsDefault);
        result.Value.Enrollments.Should().ContainSingle(e => e.ApprovalStatus == CertApprovalStatus.Approved);
        // Teeth: both reads are scoped to the caller's (tenant, user) — not any body/route value.
        await _profiles.Received(1).ListByUserAsync(_tenantId, _callerId, Arg.Any<CancellationToken>());
        await _enrollments.Received(1).GetByUserAsync(_tenantId, _callerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoNationalId_FlagsFalse()
    {
        StubUser(null);
        _profiles.ListByUserAsync(_tenantId, _callerId, Arg.Any<CancellationToken>())
            .Returns(new List<UserSignatureProfile>());
        _enrollments.GetByUserAsync(_tenantId, _callerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SignerCertEnrollment>());

        var result = await Sut().Handle(new SignatureOverviewQuery(_tenantId, _callerId), CancellationToken.None);

        result.Value.NationalIdEstablished.Should().BeFalse();
        result.Value.HasApprovedEnrollment.Should().BeFalse();
        result.Value.Profiles.Should().BeEmpty();
        result.Value.Enrollments.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OnlyPendingOrRejected_NotApprovedGate_False()
    {
        StubUser(Cccd);
        _profiles.ListByUserAsync(_tenantId, _callerId, Arg.Any<CancellationToken>())
            .Returns(new List<UserSignatureProfile>());
        // A Pending row and a Rejected row (IsActive=false) — neither satisfies the active+Approved gate.
        _enrollments.GetByUserAsync(_tenantId, _callerId, Arg.Any<CancellationToken>())
            .Returns(new[] { Enrollment(CertApprovalStatus.Pending, "S1"), Enrollment(CertApprovalStatus.Rejected, "S2") });

        var result = await Sut().Handle(new SignatureOverviewQuery(_tenantId, _callerId), CancellationToken.None);

        result.Value.HasApprovedEnrollment.Should().BeFalse(
            "only an active + Approved enrollment unlocks signing — mirrors the binding guard");
        // Full history is still exposed for the FE to render (Pending + Rejected both present).
        result.Value.Enrollments.Should().HaveCount(2);
        result.Value.Enrollments.Should().Contain(e => e.ApprovalStatus == CertApprovalStatus.Rejected && e.RejectionReason == "bad cert");
    }

    [Fact]
    public async Task Handle_UserOfAnotherTenant_ReturnsNotFound()
    {
        // Tenant-pin: the user exists but belongs to a different tenant → 404 (no cross-tenant leak).
        _userManager.FindByIdAsync(_callerId.ToString())
            .Returns(new ApplicationUser { Id = _callerId, TenantId = Guid.NewGuid(), SigningNationalId = Cccd });

        var result = await Sut().Handle(new SignatureOverviewQuery(_tenantId, _callerId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is AqtCoreFW.SharedKernel.Errors.NotFoundError);
        // No repo touched after the 404 — neither read fires (no info leak / wasted query).
        await _profiles.DidNotReceive().ListByUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _enrollments.DidNotReceive().GetByUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static UserManager<ApplicationUser> MakeUserManager()
    {
        var store = Substitute.For<IUserStore<ApplicationUser>, IUserEmailStore<ApplicationUser>>();
        return Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());
    }
}
