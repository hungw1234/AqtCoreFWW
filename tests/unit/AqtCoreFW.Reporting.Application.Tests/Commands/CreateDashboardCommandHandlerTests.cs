using AqtCoreFW.Reporting.Application.Commands.CreateDashboard;
using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Repositories;
using NSubstitute;

namespace AqtCoreFW.Reporting.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateDashboardCommandHandler.
/// Validates that a DashboardLayout is created and persisted with correct properties.
/// </summary>
public sealed class CreateDashboardCommandHandlerTests
{
    private readonly IDashboardLayoutRepository _repository =
        Substitute.For<IDashboardLayoutRepository>();
    private readonly CreateDashboardCommandHandler _sut;

    public CreateDashboardCommandHandlerTests()
    {
        _sut = new CreateDashboardCommandHandler(_repository);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsDashboardId()
    {
        var command = new CreateDashboardCommand(
            Name: "My Dashboard",
            OwnerId: Guid.NewGuid(),
            TenantId: Guid.NewGuid());

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // T1 entity: Id is long (0 before EF SaveChanges) — just verify command succeeded
        result.Value.Should().Be(0L);
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsLayoutWithCorrectProperties()
    {
        var ownerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var command = new CreateDashboardCommand("Work Dashboard", ownerId, tenantId, IsDefault: true);

        DashboardLayout? captured = null;
        await _repository.AddAsync(
            Arg.Do<DashboardLayout>(l => captured = l),
            Arg.Any<CancellationToken>());

        await _sut.Handle(command, CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Any<DashboardLayout>(),
            Arg.Any<CancellationToken>());

        captured.Should().NotBeNull();
        captured!.Name.Should().Be("Work Dashboard");
        captured.OwnerId.Should().Be(ownerId);
        captured.TenantId.Should().Be(tenantId);
        captured.IsDefault.Should().BeTrue();
        captured.WidgetPositions.Should().Be("[]");
    }

    [Fact]
    public async Task Handle_DefaultFalse_LayoutIsNotDefault()
    {
        var command = new CreateDashboardCommand("Non-default", Guid.NewGuid(), Guid.NewGuid(), IsDefault: false);

        DashboardLayout? captured = null;
        await _repository.AddAsync(
            Arg.Do<DashboardLayout>(l => captured = l),
            Arg.Any<CancellationToken>());

        await _sut.Handle(command, CancellationToken.None);

        captured!.IsDefault.Should().BeFalse();
    }
}
