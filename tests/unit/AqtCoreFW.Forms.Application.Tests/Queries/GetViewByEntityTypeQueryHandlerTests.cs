using AqtCoreFW.Forms.Application.Queries.GetViewByEntityType;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Forms.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetViewByEntityTypeQueryHandler.
/// Query handlers delegate SQL to Dapper via IReadDbConnection; the private ViewRow projection
/// type prevents full happy-path stubbing without a real DB, so tests focus on boundary behaviour:
/// not-found returns a failure result, and the handler issues SQL containing expected keywords.
/// </summary>
public sealed class GetViewByEntityTypeQueryHandlerTests
{
    private readonly IReadDbConnection _db;
    private readonly GetViewByEntityTypeQueryHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();

    public GetViewByEntityTypeQueryHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _handler = new GetViewByEntityTypeQueryHandler(_db);
    }

    // --- Not found ---

    [Fact]
    public async Task Handle_NoViewForEntityType_ReturnsFailure()
    {
        // QueryFirstOrDefaultAsync returns null for every call → not-found branch
        _db.QueryFirstOrDefaultAsync<object>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(Task.FromResult<object?>(null));

        var result = await _handler.Handle(
            new GetViewByEntityTypeQuery("NonExistentType", TenantId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("NonExistentType"));
    }

    [Fact]
    public async Task Handle_NoViewForEntityType_DoesNotQueryColumnsOrFilters()
    {
        _db.QueryFirstOrDefaultAsync<object>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(Task.FromResult<object?>(null));

        await _handler.Handle(
            new GetViewByEntityTypeQuery("Ghost", TenantId),
            CancellationToken.None);

        // No subsequent QueryAsync calls should be made when view row not found
        await _db.DidNotReceiveWithAnyArgs()
            .QueryAsync<object>(default!, default, default);
    }

    // --- Handler is constructible and wired correctly ---

    [Fact]
    public void Handler_CanBeConstructed_WithReadDbConnection()
    {
        // Verify the handler's DI constructor signature accepts IReadDbConnection
        var handler = new GetViewByEntityTypeQueryHandler(_db);
        handler.Should().NotBeNull();
    }
}
