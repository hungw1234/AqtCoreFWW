using AqtCoreFW.Forms.Application.Queries.ListFormSubmissions;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Forms.Application.Tests.Queries;

/// <summary>
/// Unit tests for ListFormSubmissionsQueryHandler.
/// Covers: pagination, status filter, SearchTerm LIKE on ReviewComment, FieldFiltersJson,
/// whitespace SearchTerm guard, and error propagation.
/// </summary>
public sealed class ListFormSubmissionsQueryHandlerTests
{
    private static readonly Guid TenantId   = Guid.NewGuid();
    // TemplatePublicId is Guid? (T2 FormTemplate.PublicId AK — external addressing after Phase-07 retier)
    private static readonly Guid TemplatePublicId = Guid.NewGuid();

    // Minimal stub — records SQL and call count; returns typed empty enumerables.
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
    public async Task Handle_NoFilters_ReturnsSuccess()
    {
        var db = new FakeReadDbConnection();
        var handler = new ListFormSubmissionsQueryHandler(db);
        var query = new ListFormSubmissionsQuery(TenantId, TemplatePublicId: null);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NoFilters_IssuesTwoDbCalls()
    {
        // Count + data queries
        var db = new FakeReadDbConnection();
        var handler = new ListFormSubmissionsQueryHandler(db);

        await handler.Handle(new ListFormSubmissionsQuery(TenantId, null), CancellationToken.None);

        db.QueryCallCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_WithTemplateId_ReturnsSuccess()
    {
        var db = new FakeReadDbConnection();
        var handler = new ListFormSubmissionsQueryHandler(db);
        var query = new ListFormSubmissionsQuery(TenantId, TemplatePublicId);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithStatus_ReturnsSuccess()
    {
        var db = new FakeReadDbConnection();
        var handler = new ListFormSubmissionsQueryHandler(db);
        var query = new ListFormSubmissionsQuery(TenantId, null, Status: "Approved");

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithSearchTerm_ReturnsSuccess()
    {
        var db = new FakeReadDbConnection();
        var handler = new ListFormSubmissionsQueryHandler(db);
        var query = new ListFormSubmissionsQuery(TenantId, null, SearchTerm: "approved");

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithSearchTerm_SqlContainsLikeOnReviewComment()
    {
        var db = new FakeReadDbConnection();
        var handler = new ListFormSubmissionsQueryHandler(db);

        await handler.Handle(
            new ListFormSubmissionsQuery(TenantId, null, SearchTerm: "compliant"),
            CancellationToken.None);

        // Last SQL is the data query — must include LIKE on ReviewComment
        db.LastSql.Should().Contain("LIKE");
        db.LastSql.Should().Contain("ReviewComment");
    }

    [Fact]
    public async Task Handle_WithNullSearchTerm_SqlDoesNotContainLike()
    {
        var db = new FakeReadDbConnection();
        var handler = new ListFormSubmissionsQueryHandler(db);

        await handler.Handle(
            new ListFormSubmissionsQuery(TenantId, null, SearchTerm: null),
            CancellationToken.None);

        db.LastSql.Should().NotContain("LIKE");
    }

    [Fact]
    public async Task Handle_WithWhitespaceSearchTerm_SqlDoesNotContainLike()
    {
        var db = new FakeReadDbConnection();
        var handler = new ListFormSubmissionsQueryHandler(db);

        await handler.Handle(
            new ListFormSubmissionsQuery(TenantId, null, SearchTerm: "   "),
            CancellationToken.None);

        db.LastSql.Should().NotContain("LIKE");
    }

    [Fact]
    public async Task Handle_WithFieldFiltersJson_ReturnsSuccess()
    {
        var db = new FakeReadDbConnection();
        var handler = new ListFormSubmissionsQueryHandler(db);
        var query = new ListFormSubmissionsQuery(
            TenantId, null,
            FieldFiltersJson: """[{"fieldKey":"department","value":"HR"}]""");

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithMalformedFieldFiltersJson_ReturnsSuccessIgnoringFilter()
    {
        // Malformed JSON is silently ignored — handler returns unfiltered results
        var db = new FakeReadDbConnection();
        var handler = new ListFormSubmissionsQueryHandler(db);
        var query = new ListFormSubmissionsQuery(
            TenantId, null,
            FieldFiltersJson: "not-valid-json");

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithPagination_ReturnsSuccess()
    {
        var db = new FakeReadDbConnection();
        var handler = new ListFormSubmissionsQueryHandler(db);
        var query = new ListFormSubmissionsQuery(TenantId, null, Page: 3, PageSize: 5);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsEmptyPagedResult()
    {
        var db = new FakeReadDbConnection();
        var handler = new ListFormSubmissionsQueryHandler(db);

        var result = await handler.Handle(new ListFormSubmissionsQuery(TenantId, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_DatabaseException_Propagates()
    {
        var db = new FakeReadDbConnection
        {
            ThrowOnQuery = new InvalidOperationException("DB unavailable")
        };
        var handler = new ListFormSubmissionsQueryHandler(db);

        var act = async () => await handler.Handle(
            new ListFormSubmissionsQuery(TenantId, null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB unavailable*");
    }
}
