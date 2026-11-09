using AqtCoreFW.Forms.Application.Commands.CreateViewDefinition;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateViewDefinitionCommandHandler.
/// Verifies happy path creation, DTO mapping, column/filter attachment, and error handling.
/// </summary>
public sealed class CreateViewDefinitionCommandHandlerTests
{
    private readonly IViewDefinitionRepository _repository;
    private readonly CreateViewDefinitionCommandHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();

    public CreateViewDefinitionCommandHandlerTests()
    {
        _repository = Substitute.For<IViewDefinitionRepository>();
        _handler = new CreateViewDefinitionCommandHandler(_repository);
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithDto()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsViewDefinition()
    {
        var cmd = BuildCommand(name: "Case List", entityType: "Case");

        await _handler.Handle(cmd, CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<ViewDefinition>(v =>
                v.TenantId == TestTenantId &&
                v.Name == "Case List" &&
                v.EntityType == "Case"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_DtoHasCorrectFields()
    {
        var cmd = BuildCommand(name: "Task Grid", entityType: "Task",
            type: ViewType.Grid, pageSize: 50);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.Value.Name.Should().Be("Task Grid");
        result.Value.EntityType.Should().Be("Task");
        result.Value.Type.Should().Be(ViewType.Grid.ToString());
        result.Value.PageSize.Should().Be(50);
        result.Value.TenantId.Should().Be(TestTenantId);
    }

    [Fact]
    public async Task Handle_WithColumns_AttachesColumnsToDto()
    {
        var cmd = BuildCommand(columns:
        [
            new CreateViewColumnDto("Status", "Status", 1, Sortable: true),
            new CreateViewColumnDto("CreatedAt", "Created", 2)
        ]);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.Value.Columns.Should().HaveCount(2);
        result.Value.Columns[0].FieldName.Should().Be("Status");
        result.Value.Columns[1].FieldName.Should().Be("CreatedAt");
    }

    [Fact]
    public async Task Handle_WithFilters_AttachesFiltersToDto()
    {
        var cmd = BuildCommand(filters:
        [
            new CreateViewFilterDto("Status", FilterOperator.Equals, "Open", IsRequired: true)
        ]);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.Value.Filters.Should().HaveCount(1);
        result.Value.Filters[0].FieldName.Should().Be("Status");
        result.Value.Filters[0].Operator.Should().Be(FilterOperator.Equals.ToString());
        result.Value.Filters[0].IsRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NoColumnsNoFilters_ReturnsEmptyCollections()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.Value.Columns.Should().BeEmpty();
        result.Value.Filters.Should().BeEmpty();
    }

    // --- Domain validation guard ---

    [Fact]
    public async Task Handle_EmptyName_ReturnsFailure()
    {
        var cmd = BuildCommand(name: "");

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_InvalidPageSize_ReturnsFailure()
    {
        var cmd = BuildCommand(pageSize: 0);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Helpers ---

    private static CreateViewDefinitionCommand BuildCommand(
        string name = "Test View",
        string entityType = "Case",
        ViewType type = ViewType.List,
        int pageSize = 20,
        List<CreateViewColumnDto>? columns = null,
        List<CreateViewFilterDto>? filters = null) =>
        new(TestTenantId, name, entityType, type, TestUserId,
            PageSize: pageSize, Columns: columns, Filters: filters);
}
