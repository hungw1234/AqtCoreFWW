using AqtCoreFW.Forms.Application.Queries.GetFormTemplates;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Forms.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetFormTemplatesQueryHandler.
/// Uses a hand-written stub to avoid NSubstitute proxy issues with the handler's
/// private FormTemplateRow nested type.
/// Tests cover: pagination, SearchTerm LIKE injection, no-search path, boundary cases.
/// </summary>
public sealed class GetFormTemplatesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    // Stub that records calls and returns typed empty results for count (int) and rows (T).
    private sealed class FakeReadDbConnection : IReadDbConnection
    {
        public int QueryCallCount { get; private set; }
        public string? LastSql { get; private set; }
        public Exception? ThrowOnQuery { get; set; }

        public Task<T> QuerySingleAsync<T>(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
        {
            QueryCallCount++;
            LastSql = sql;
            if (ThrowOnQuery is not null) throw ThrowOnQuery;
            // Return 0 for COUNT(*) queries
            return Task.FromResult(default(T)!);
        }

        public Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
        {
            QueryCallCount++;
            LastSql = sql;
            if (ThrowOnQuery is not null) throw ThrowOnQuery;
            return Task.FromResult(Enumerable.Empty<T>());
        }

        public Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
            => Task.FromResult(default(T));

        public Task<int> ExecuteAsync(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
            => Task.FromResult(0);

        // Bypass variants — delegate to standard stubs (audit tables only; not used by Forms handlers)
        public Task<IEnumerable<T>> QueryWithAuditAdminBypassAsync<T>(
            string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null)
            => QueryAsync<T>(sql, param, cancellationToken, commandTimeout);

        public Task<T> QuerySingleWithAuditAdminBypassAsync<T>(
            string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null)
            => QuerySingleAsync<T>(sql, param, cancellationToken, commandTimeout);

        public Task<T?> QueryFirstOrDefaultWithPublicFormsBypassAsync<T>(
            string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null)
            => QueryFirstOrDefaultAsync<T>(sql, param, cancellationToken, commandTimeout);

        // Streaming variant — this handler never calls it; return an empty async stream.
        public async IAsyncEnumerable<T> QueryUnbufferedAsync<T>(
            string sql, object? param = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default, int? commandTimeout = null)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    [Fact]
    public async Task Handle_NoSearch_ReturnsSuccess()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetFormTemplatesQueryHandler(db);
        var query = new GetFormTemplatesQuery(TenantId);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NoSearch_IssuesTwoDbCalls()
    {
        // Count + data queries
        var db = new FakeReadDbConnection();
        var handler = new GetFormTemplatesQueryHandler(db);

        await handler.Handle(new GetFormTemplatesQuery(TenantId), CancellationToken.None);

        db.QueryCallCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_NoSearch_SqlDoesNotContainLike()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetFormTemplatesQueryHandler(db);

        await handler.Handle(new GetFormTemplatesQuery(TenantId, SearchTerm: null), CancellationToken.None);

        db.LastSql.Should().NotContain("LIKE");
    }

    [Fact]
    public async Task Handle_WithSearchTerm_ReturnsSuccess()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetFormTemplatesQueryHandler(db);
        var query = new GetFormTemplatesQuery(TenantId, SearchTerm: "intake");

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithSearchTerm_SqlContainsLikeOnNameAndCode()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetFormTemplatesQueryHandler(db);

        await handler.Handle(new GetFormTemplatesQuery(TenantId, SearchTerm: "survey"), CancellationToken.None);

        // Last SQL is the data query — must include LIKE clauses for Name, NameVi, Code
        db.LastSql.Should().Contain("LIKE");
        db.LastSql.Should().Contain("Name");
        db.LastSql.Should().Contain("Code");
    }

    [Fact]
    public async Task Handle_WithWhitespaceSearchTerm_SqlDoesNotContainLike()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetFormTemplatesQueryHandler(db);

        await handler.Handle(new GetFormTemplatesQuery(TenantId, SearchTerm: "  "), CancellationToken.None);

        db.LastSql.Should().NotContain("LIKE");
    }

    [Fact]
    public async Task Handle_WithPagination_ReturnsSuccess()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetFormTemplatesQueryHandler(db);

        var result = await handler.Handle(new GetFormTemplatesQuery(TenantId, Page: 2, PageSize: 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_PageSizeBeyondMax_ClampsToMaxInMeta()
    {
        // P7-FU1.1-FU: pageSize > 1000 must clamp to 1000 to prevent DoS via crafted requests.
        var db = new FakeReadDbConnection();
        var handler = new GetFormTemplatesQueryHandler(db);

        var result = await handler.Handle(
            new GetFormTemplatesQuery(TenantId, Page: 1, PageSize: 99_999),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Meta.PageSize.Should().Be(1000);
    }

    [Fact]
    public async Task Handle_PageSizeZeroOrNegative_ClampsToOne()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetFormTemplatesQueryHandler(db);

        var result = await handler.Handle(
            new GetFormTemplatesQuery(TenantId, Page: 1, PageSize: -5),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Meta.PageSize.Should().Be(1);
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsEmptyPagedResult()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetFormTemplatesQueryHandler(db);

        var result = await handler.Handle(new GetFormTemplatesQuery(TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }
}
