using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.GetMigrationItems;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetMigrationItemsQueryHandler.
/// Verifies paging, status filter, and PageSize cap at 200.
/// Phase-4b B4c: MigrationId is long (T1 bigint PK). WorkflowVersionMigrationItem.Create takes IGuidGenerator.
/// </summary>
public sealed class GetMigrationItemsQueryHandlerTests
{
    private readonly IWorkflowVersionMigrationRepository _repo =
        Substitute.For<IWorkflowVersionMigrationRepository>();

    private readonly GetMigrationItemsQueryHandler _handler;

    // MigrationId is long (T1 bigint PK)
    private const long MigrationId = 1L;

    public GetMigrationItemsQueryHandlerTests()
    {
        _handler = new GetMigrationItemsQueryHandler(_repo);
    }

    private static WorkflowVersionMigrationItem MakeItem(MigrationItemStatus status)
    {
        var gen = TestGuidGenerator.Create();
        var item = WorkflowVersionMigrationItem.Create(gen, MigrationId, 10L, 1L, 2L);
        if (status == MigrationItemStatus.Succeeded) item.MarkSucceeded();
        else if (status == MigrationItemStatus.Failed) item.MarkFailed("err");
        else if (status == MigrationItemStatus.Skipped) item.MarkSkipped("skip");
        return item;
    }

    [Fact]
    public async Task Handle_ReturnsPaged_WithCorrectCounts()
    {
        var items = Enumerable.Range(0, 3)
            .Select(_ => MakeItem(MigrationItemStatus.Succeeded))
            .ToList();

        _repo.ListItemsAsync(MigrationId, null, 1, 50, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<WorkflowVersionMigrationItem>)items, 3));

        var result = await _handler.Handle(
            new GetMigrationItemsQuery(MigrationId, 1, 50), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(3);
        result.Value.TotalCount.Should().Be(3);
        result.Value.Page.Should().Be(1);
        result.Value.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task Handle_CapsPageSizeAt200()
    {
        _repo.ListItemsAsync(MigrationId, null, 1, 200, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<WorkflowVersionMigrationItem>)[], 0));

        // Request PageSize=999 → clamped to 200
        var result = await _handler.Handle(
            new GetMigrationItemsQuery(MigrationId, 1, 999), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PageSize.Should().Be(200);
        await _repo.Received(1).ListItemsAsync(MigrationId, null, 1, 200, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PassesStatusFilter_ToRepository()
    {
        var failed = MakeItem(MigrationItemStatus.Failed);

        _repo.ListItemsAsync(MigrationId, MigrationItemStatus.Failed, 1, 50, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<WorkflowVersionMigrationItem>)[failed], 1));

        var result = await _handler.Handle(
            new GetMigrationItemsQuery(MigrationId, 1, 50, MigrationItemStatus.Failed), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle(i => i.Status == MigrationItemStatus.Failed);
    }
}
