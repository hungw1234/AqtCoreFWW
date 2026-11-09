using AqtCoreFW.Reporting.Application.Commands.CreateWidget;
using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Enums;
using AqtCoreFW.Reporting.Domain.Errors;
using AqtCoreFW.Reporting.Domain.Repositories;
using NSubstitute;

namespace AqtCoreFW.Reporting.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateWidgetCommandHandler.
/// Validates SQL security checks: must start with SELECT, no dangerous DDL/DML.
/// </summary>
public sealed class CreateWidgetCommandHandlerTests
{
    private readonly IReportWidgetRepository _repository =
        Substitute.For<IReportWidgetRepository>();
    private readonly CreateWidgetCommandHandler _sut;

    public CreateWidgetCommandHandlerTests()
    {
        _sut = new CreateWidgetCommandHandler(_repository);
    }

    [Fact]
    public async Task Handle_ValidSelectQuery_ReturnsWidgetId()
    {
        var command = BuildCommand(
            dataQuery: "SELECT Status, COUNT(*) FROM forms.FormTemplates WHERE TenantId = @TenantId GROUP BY Status");

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // T1 entity: Id is long (0 before EF SaveChanges) — just verify command succeeded
        result.Value.Should().Be(0L);
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsWidgetWithCorrectProperties()
    {
        var tenantId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var command = BuildCommand(
            dataQuery: "SELECT Id FROM forms.FormTemplates WHERE TenantId = @TenantId",
            tenantId: tenantId,
            createdBy: createdBy,
            type: WidgetType.Table);

        ReportWidget? captured = null;
        await _repository.AddAsync(
            Arg.Do<ReportWidget>(w => captured = w),
            Arg.Any<CancellationToken>());

        await _sut.Handle(command, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Type.Should().Be(WidgetType.Table);
        captured.TenantId.Should().Be(tenantId);
        captured.CreatedBy.Should().Be(createdBy);
    }

    [Fact]
    public async Task Handle_SqlNotStartingWithSelect_ReturnsError()
    {
        var command = BuildCommand(dataQuery: "DROP TABLE forms.FormTemplates");

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("ModuleErrorCode") &&
            e.Metadata["ModuleErrorCode"].ToString() == ReportingErrors.Codes.DangerousSql);

        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Handle_SqlWithDangerousKeyword_ReturnsError()
    {
        // SELECT start but contains DROP
        var command = BuildCommand(
            dataQuery: "SELECT * FROM forms.FormTemplates WHERE TenantId = @TenantId; DROP TABLE forms.FormTemplates");

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Handle_SqlWithUnionKeyword_ReturnsError()
    {
        var command = BuildCommand(
            dataQuery: "SELECT 1 WHERE @TenantId IS NOT NULL UNION SELECT password FROM users");

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Helpers ---

    private static CreateWidgetCommand BuildCommand(
        string? dataQuery = null,
        Guid? tenantId = null,
        Guid? createdBy = null,
        WidgetType type = WidgetType.BarChart) =>
        new(Name: "Test Widget",
            Type: type,
            DataQuery: dataQuery ?? "SELECT COUNT(*) FROM forms.FormTemplates WHERE TenantId = @TenantId",
            TenantId: tenantId ?? Guid.NewGuid(),
            CreatedBy: createdBy ?? Guid.NewGuid());
}
