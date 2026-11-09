using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;

namespace AqtCoreFW.Identity.Application.Tests.Signing;

public sealed class ListPendingEnrollmentsQueryHandlerTests
{
    private readonly ISignerCertEnrollmentRepository _repo = Substitute.For<ISignerCertEnrollmentRepository>();
    private readonly Guid _tenantId = Guid.NewGuid();

    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    private ListPendingEnrollmentsQueryHandler Sut() => new(_repo);

    private SignerCertEnrollment Pending(string serial) => SignerCertEnrollment.Create(
        _tenantId, Guid.NewGuid(), serial, "CN=CA", "T", "CN=A", Guid.NewGuid(),
        EnrollmentKind.UsbTokenVgca, CertApprovalStatus.Pending,
        CertSubjectType.Personal, "A", Gen);

    [Fact]
    public async Task Handle_ReturnsPagedPending_MappedToDto_ScopedToTenant()
    {
        var items = new[] { Pending("S1"), Pending("S2") };
        _repo.ListPendingAsync(_tenantId, 0, 20, Arg.Any<CancellationToken>())
            .Returns((items, 2));

        var result = await Sut().Handle(new ListPendingEnrollmentsQuery(_tenantId, 1, 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(2);
        // M1: items expose PublicId, never the long id.
        result.Value.Items.Should().Contain(d => d.PublicId == items[0].PublicId && d.SerialNumber == "S1");
        await _repo.Received(1).ListPendingAsync(_tenantId, 0, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClampsPageSizeToAdminMax()
    {
        // M4: an oversized pageSize is clamped (anti admin-queue DoS) before hitting the repo.
        _repo.ListPendingAsync(_tenantId, 0, 100, Arg.Any<CancellationToken>())
            .Returns((Array.Empty<SignerCertEnrollment>(), 0));

        await Sut().Handle(new ListPendingEnrollmentsQuery(_tenantId, 1, 9999), CancellationToken.None);

        await _repo.Received(1).ListPendingAsync(_tenantId, 0, 100, Arg.Any<CancellationToken>());
    }
}
