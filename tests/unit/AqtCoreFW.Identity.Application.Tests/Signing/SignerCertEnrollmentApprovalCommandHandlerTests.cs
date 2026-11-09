using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;

namespace AqtCoreFW.Identity.Application.Tests.Signing;

// Admin approve/reject of self-service Pending enrollments. The cross-tenant guarantee is a unit-level
// teeth here (handler queries the repo with the admin's own TenantId → a foreign-tenant PublicId
// resolves to NotFound); the X-Tenant-Id-header bypass (H4 g) is a controller/HTTP concern covered in
// the integration suite.
public sealed class SignerCertEnrollmentApprovalCommandHandlerTests
{
    private readonly ISignerCertEnrollmentRepository _repo = Substitute.For<ISignerCertEnrollmentRepository>();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    private ApproveSignerCertEnrollmentCommandHandler ApproveSut() => new(_repo);
    private RejectSignerCertEnrollmentCommandHandler RejectSut() => new(_repo);

    private SignerCertEnrollment PendingEnrollment() => SignerCertEnrollment.Create(
        _tenantId, _userId, "S1", "CN=CA", "T1", "CN=A", _userId,
        EnrollmentKind.UsbTokenVgca, CertApprovalStatus.Pending,
        CertSubjectType.Personal, "A", Gen);

    // Both repo reads (no-track GetByPublicId + tracked GetById) return the same in-memory entity.
    private void StubResolves(SignerCertEnrollment e)
    {
        _repo.GetByPublicIdAsync(_tenantId, e.PublicId, Arg.Any<CancellationToken>()).Returns(e);
        _repo.GetByIdAsync(_tenantId, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(e);
    }

    // ── Approve ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_Pending_TransitionsToApproved_AndSaves()
    {
        // Distinct instances prove the handler mutates the TRACKED entity (from GetByIdAsync), not the
        // AsNoTracking read (from GetByPublicIdAsync) — mutating the latter would no-op on SaveChanges.
        var noTrack = PendingEnrollment();
        var tracked = PendingEnrollment();
        _repo.GetByPublicIdAsync(_tenantId, noTrack.PublicId, Arg.Any<CancellationToken>()).Returns(noTrack);
        _repo.GetByIdAsync(_tenantId, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(tracked);

        var result = await ApproveSut().Handle(
            new ApproveSignerCertEnrollmentCommand(_tenantId, _adminId, noTrack.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tracked.ApprovalStatus.Should().Be(CertApprovalStatus.Approved);
        tracked.ApprovedBy.Should().Be(_adminId);
        noTrack.ApprovalStatus.Should().Be(CertApprovalStatus.Pending, "the AsNoTracking read must not be mutated");
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_MissingPublicId_ReturnsNotFound_NoSave()
    {
        _repo.GetByPublicIdAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((SignerCertEnrollment?)null);

        var result = await ApproveSut().Handle(
            new ApproveSignerCertEnrollmentCommand(_tenantId, _adminId, Guid.NewGuid()), CancellationToken.None);

        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _repo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_ForeignTenantPublicId_ReturnsNotFound_TenantScoped()
    {
        // Teeth (e): the handler resolves scoped to the admin's own tenant. A PublicId that lives in
        // another tenant is simply not returned by the tenant-scoped repo lookup → NotFound, no leak.
        var foreignPublicId = Guid.NewGuid();
        _repo.GetByPublicIdAsync(_tenantId, foreignPublicId, Arg.Any<CancellationToken>())
            .Returns((SignerCertEnrollment?)null);

        var result = await ApproveSut().Handle(
            new ApproveSignerCertEnrollmentCommand(_tenantId, _adminId, foreignPublicId), CancellationToken.None);

        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _repo.Received(1).GetByPublicIdAsync(_tenantId, foreignPublicId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_AlreadyApproved_ReturnsConflict_NoSave()
    {
        var e = PendingEnrollment();
        e.Approve(_adminId); // now terminal
        StubResolves(e);

        var result = await ApproveSut().Handle(
            new ApproveSignerCertEnrollmentCommand(_tenantId, _adminId, e.PublicId), CancellationToken.None);

        result.Errors.Should().ContainSingle(err => err is ConflictError);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Reject ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reject_Pending_SetsRejected_DeactivatesForReEnroll_AndSaves()
    {
        var e = PendingEnrollment();
        StubResolves(e);

        var result = await RejectSut().Handle(
            new RejectSignerCertEnrollmentCommand(_tenantId, _adminId, e.PublicId, "wrong cert"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        e.ApprovalStatus.Should().Be(CertApprovalStatus.Rejected);
        e.IsActive.Should().BeFalse("Reject deactivates so the same serial can be re-enrolled (H3)");
        e.RejectionReason.Should().Be("wrong cert");
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reject_MissingPublicId_ReturnsNotFound_NoSave()
    {
        _repo.GetByPublicIdAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((SignerCertEnrollment?)null);

        var result = await RejectSut().Handle(
            new RejectSignerCertEnrollmentCommand(_tenantId, _adminId, Guid.NewGuid(), "x"), CancellationToken.None);

        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reject_AlreadyRejected_ReturnsConflict_NoSave()
    {
        var e = PendingEnrollment();
        e.Reject(_adminId, "first"); // terminal
        StubResolves(e);

        var result = await RejectSut().Handle(
            new RejectSignerCertEnrollmentCommand(_tenantId, _adminId, e.PublicId, "second"), CancellationToken.None);

        result.Errors.Should().ContainSingle(err => err is ConflictError);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
