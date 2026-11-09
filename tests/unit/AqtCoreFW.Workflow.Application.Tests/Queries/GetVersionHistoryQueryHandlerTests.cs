using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.GetVersionHistory;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetVersionHistoryQueryHandler.
/// IReadDbConnection is mocked — SQL correctness verified in integration tests.
/// </summary>
public sealed class GetVersionHistoryQueryHandlerTests
{
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly GetVersionHistoryQueryHandler _handler;

    public GetVersionHistoryQueryHandlerTests()
    {
        _handler = new GetVersionHistoryQueryHandler(_db);
    }

    [Fact]
    public async Task Handle_ReturnsRows_WhenDbReturnsData()
    {
        var tenantId = Guid.NewGuid();
        var defKey = "loan-approval";
        var rows = new List<VersionHistoryDto>
        {
            new() { Id = Guid.NewGuid(), DefinitionKey = defKey, Version = 2, IsLatest = true, IsActive = true, InstanceCount = 5 },
            new() { Id = Guid.NewGuid(), DefinitionKey = defKey, Version = 1, IsLatest = false, IsActive = false, InstanceCount = 0 }
        };

        _db.QueryAsync<VersionHistoryDto>(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<int?>())
            .Returns(rows);

        var result = await _handler.Handle(
            new GetVersionHistoryQuery(defKey, tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Version.Should().Be(2); // newest first
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoVersionsExist()
    {
        _db.QueryAsync<VersionHistoryDto>(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<int?>())
            .Returns([]);

        var result = await _handler.Handle(
            new GetVersionHistoryQuery("unknown-key", Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
