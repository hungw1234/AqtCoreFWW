using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;

namespace AqtCoreFW.Identity.Domain.Tests.Signing;

/// <summary>
/// Unit tests for <see cref="SignerCertEnrollment"/> approval lifecycle (P1 expansion):
/// - Factory contract (H2): Kind + ApprovalStatus required, no defaults.
/// - Approve: Pending → Approved, sets ApprovedBy/At.
/// - Reject: Pending → Rejected, sets reason + IsActive=false (H3).
/// - Terminal invariant: Approve/Reject on already-terminal row throws.
/// - Reject reason validation: empty reason throws.
/// Red-before-green evidence: see phase-01 report (temporarily broke invariant guards to prove RED).
/// </summary>
public sealed class SignerCertEnrollmentApprovalTests
{
    private static readonly Guid Tenant    = Guid.NewGuid();
    private static readonly Guid User      = Guid.NewGuid();
    private static readonly Guid AdminId   = Guid.NewGuid();
    private static readonly Guid Enroller  = Guid.NewGuid();

    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    private static SignerCertEnrollment CreatePending() => SignerCertEnrollment.Create(
        tenantId:       Tenant,
        userId:         User,
        serialNumber:   "SER-001",
        issuer:         "CN=Test CA",
        thumbprint:     "THUMB-ABC",
        subject:        null,
        enrolledBy:     Enroller,
        kind:           EnrollmentKind.RemoteViettelMysign,
        approvalStatus: CertApprovalStatus.Pending,
        subjectType:    CertSubjectType.Personal,
        displayName:    "Test Signer",
        gen:            Gen);

    private static SignerCertEnrollment CreateApproved() => SignerCertEnrollment.Create(
        tenantId:       Tenant,
        userId:         User,
        serialNumber:   "SER-ADM-001",
        issuer:         "CN=Admin CA",
        thumbprint:     "THUMB-ADM",
        subject:        null,
        enrolledBy:     AdminId,
        kind:           EnrollmentKind.UsbTokenVgca,
        approvalStatus: CertApprovalStatus.Approved,
        subjectType:    CertSubjectType.Personal,
        displayName:    "Admin Signer",
        gen:            Gen);

    // ── H2: factory requires explicit Kind + ApprovalStatus ──────────────────────────────────

    [Fact]
    public void Create_WithKindAndStatus_SetsFieldsCorrectly()
    {
        var e = CreatePending();

        e.Kind.Should().Be(EnrollmentKind.RemoteViettelMysign);
        e.ApprovalStatus.Should().Be(CertApprovalStatus.Pending);
        e.IsActive.Should().BeTrue("new enrollment is active");
        e.ApprovedBy.Should().BeNull();
        e.ApprovedAt.Should().BeNull();
        e.RejectionReason.Should().BeNull();
        e.PublicId.Should().NotBe(Guid.Empty, "PublicId must be assigned by Create");
        e.CreatedBy.Should().Be(Enroller);
    }

    [Fact]
    public void Create_AdminPath_ApprovedStatus()
    {
        var e = CreateApproved();

        e.ApprovalStatus.Should().Be(CertApprovalStatus.Approved);
        e.Kind.Should().Be(EnrollmentKind.UsbTokenVgca);
        e.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_Throws_OnEmptyTenantId()
    {
        var act = () => SignerCertEnrollment.Create(
            Guid.Empty, User, "SER", "ISS", "THUMB", null, Enroller,
            EnrollmentKind.RemoteViettelMysign, CertApprovalStatus.Pending,
            CertSubjectType.Personal, null, Gen);

        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void Create_Throws_OnEmptyEnrolledBy()
    {
        var act = () => SignerCertEnrollment.Create(
            Tenant, User, "SER", "ISS", "THUMB", null, Guid.Empty,
            EnrollmentKind.RemoteViettelMysign, CertApprovalStatus.Pending,
            CertSubjectType.Personal, null, Gen);

        act.Should().Throw<ArgumentException>().WithParameterName("enrolledBy");
    }

    // ── Approve: Pending → Approved ───────────────────────────────────────────────────────────

    [Fact]
    public void Approve_OnPending_TransitionsToApproved()
    {
        var e = CreatePending();
        var before = DateTime.UtcNow;

        e.Approve(AdminId);

        e.ApprovalStatus.Should().Be(CertApprovalStatus.Approved);
        e.ApprovedBy.Should().Be(AdminId);
        e.ApprovedAt.Should().BeOnOrAfter(before);
        e.IsActive.Should().BeTrue("Approve must not deactivate");
        e.ModifiedBy.Should().Be(AdminId);
    }

    [Fact]
    public void Approve_OnAlreadyApproved_Throws()
    {
        var e = CreateApproved();

        var act = () => e.Approve(AdminId);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already Approved*");
    }

    [Fact]
    public void Approve_OnRejected_Throws()
    {
        var e = CreatePending();
        e.Reject(AdminId, "bad cert");

        var act = () => e.Approve(AdminId);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Rejected*");
    }

    [Fact]
    public void Approve_Throws_OnEmptyAdminId()
    {
        var e = CreatePending();
        var act = () => e.Approve(Guid.Empty);
        act.Should().Throw<ArgumentException>().WithParameterName("adminId");
    }

    // ── Reject: Pending → Rejected + IsActive=false (H3) ─────────────────────────────────────

    [Fact]
    public void Reject_OnPending_TransitionsToRejectedAndDeactivates()
    {
        var e = CreatePending();
        var before = DateTime.UtcNow;

        e.Reject(AdminId, "Certificate does not match CCCD record.");

        e.ApprovalStatus.Should().Be(CertApprovalStatus.Rejected);
        e.IsActive.Should().BeFalse("H3: Reject must set IsActive=false so re-enroll is unblocked");
        e.RejectionReason.Should().Be("Certificate does not match CCCD record.");
        e.ApprovedBy.Should().Be(AdminId);
        e.ApprovedAt.Should().BeOnOrAfter(before);
        e.ModifiedBy.Should().Be(AdminId);
    }

    [Fact]
    public void Reject_OnAlreadyRejected_Throws()
    {
        var e = CreatePending();
        e.Reject(AdminId, "first rejection");

        var act = () => e.Reject(AdminId, "second rejection");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already Rejected*");
    }

    [Fact]
    public void Reject_OnApproved_Throws()
    {
        var e = CreateApproved();

        var act = () => e.Reject(AdminId, "should fail");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already Approved*");
    }

    [Fact]
    public void Reject_WithEmptyReason_Throws()
    {
        var e = CreatePending();
        var act = () => e.Reject(AdminId, "   ");
        act.Should().Throw<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public void Reject_WithReasonOver512Chars_Throws()
    {
        var e = CreatePending();
        var longReason = new string('x', 513);
        var act = () => e.Reject(AdminId, longReason);
        act.Should().Throw<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public void Reject_Throws_OnEmptyAdminId()
    {
        var e = CreatePending();
        var act = () => e.Reject(Guid.Empty, "some reason");
        act.Should().Throw<ArgumentException>().WithParameterName("adminId");
    }

    // ── PublicId uniqueness ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_TwoCalls_ProduceDifferentPublicIds()
    {
        var e1 = CreatePending();
        var e2 = CreatePending();
        e1.PublicId.Should().NotBe(e2.PublicId);
    }
}
