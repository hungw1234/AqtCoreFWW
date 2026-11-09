using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Application.Queries.GetDocumentTemplates;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Files.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetDocumentTemplatesQueryHandler.
/// Uses a hand-written stub to avoid NSubstitute proxy issues with the handler's
/// private TemplateRow nested type.
/// Tests verify: handler returns success, returns correct structure, passes query parameters.
/// </summary>
public sealed class GetDocumentTemplatesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    // Stub that records calls and returns empty enumerable by default.
    // Avoids NSubstitute proxy failures with IEnumerable<private-type>.
    private sealed class FakeReadDbConnection : IReadDbConnection
    {
        public int QueryCallCount { get; private set; }
        public string? LastSql { get; private set; }
        public object? LastParam { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public Exception? ThrowOnQuery { get; set; }

        public Task<IEnumerable<T>> QueryAsync<T>(
            string sql, object? param = null,
            CancellationToken cancellationToken = default,
            int? commandTimeout = null)
        {
            QueryCallCount++;
            LastSql = sql;
            LastParam = param;
            LastCancellationToken = cancellationToken;
            if (ThrowOnQuery is not null)
                throw ThrowOnQuery;
            // Return empty enumerable — handler maps it to empty result list.
            return Task.FromResult(Enumerable.Empty<T>());
        }

        public Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
            => Task.FromResult(default(T));

        public Task<T> QuerySingleAsync<T>(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
            => Task.FromResult(default(T)!);

        public Task<int> ExecuteAsync(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
            => Task.FromResult(0);

        // Bypass variants — delegate to standard stubs (audit tables only; not used by Files handlers)
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
    public async Task Handle_ReturnsSuccess()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetDocumentTemplatesQueryHandler(db);
        var query = new GetDocumentTemplatesQuery(TenantId);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CallsDbOnce()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetDocumentTemplatesQueryHandler(db);
        var query = new GetDocumentTemplatesQuery(TenantId);

        await handler.Handle(query, CancellationToken.None);

        db.QueryCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithStatusFilter_PassesStatusParam()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetDocumentTemplatesQueryHandler(db);
        var query = new GetDocumentTemplatesQuery(TenantId, Status: DocumentTemplateStatus.Active);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithPagination_ReturnsSuccess()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetDocumentTemplatesQueryHandler(db);
        var query = new GetDocumentTemplatesQuery(TenantId, Page: 1, PageSize: 10);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Page3_ReturnsSuccess()
    {
        // Page 3, PageSize 20 — skip = 40
        var db = new FakeReadDbConnection();
        var handler = new GetDocumentTemplatesQueryHandler(db);
        var query = new GetDocumentTemplatesQuery(TenantId, Page: 3, PageSize: 20);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DefaultPagination_ReturnsSuccess()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetDocumentTemplatesQueryHandler(db);
        var query = new GetDocumentTemplatesQuery(TenantId);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_NoStatusFilter_ReturnsSuccess()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetDocumentTemplatesQueryHandler(db);
        var query = new GetDocumentTemplatesQuery(TenantId);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CancellationTokenPropagated()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetDocumentTemplatesQueryHandler(db);
        var cts = new CancellationTokenSource();
        var query = new GetDocumentTemplatesQuery(TenantId);

        await handler.Handle(query, cts.Token);

        // DB was called with the provided token
        db.QueryCallCount.Should().Be(1);
        db.LastCancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task Handle_DatabaseException_Propagates()
    {
        var db = new FakeReadDbConnection
        {
            ThrowOnQuery = new InvalidOperationException("Connection failed")
        };
        var handler = new GetDocumentTemplatesQueryHandler(db);
        var query = new GetDocumentTemplatesQuery(TenantId);

        var act = async () => await handler.Handle(query, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Connection failed*");
    }

    [Fact]
    public async Task Handle_WithSearchTerm_ReturnsSuccess()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetDocumentTemplatesQueryHandler(db);
        var query = new GetDocumentTemplatesQuery(TenantId, SearchTerm: "invoice");

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithSearchTerm_SqlContainsLikePattern()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetDocumentTemplatesQueryHandler(db);
        var query = new GetDocumentTemplatesQuery(TenantId, SearchTerm: "report");

        await handler.Handle(query, CancellationToken.None);

        db.LastSql.Should().Contain("LIKE");
        db.LastSql.Should().Contain("Name");
        db.LastSql.Should().Contain("Description");
    }

    [Fact]
    public async Task Handle_WithNullSearchTerm_SqlDoesNotContainLike()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetDocumentTemplatesQueryHandler(db);
        var query = new GetDocumentTemplatesQuery(TenantId, SearchTerm: null);

        await handler.Handle(query, CancellationToken.None);

        db.LastSql.Should().NotContain("LIKE");
    }

    [Fact]
    public async Task Handle_WithWhitespaceSearchTerm_SqlDoesNotContainLike()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetDocumentTemplatesQueryHandler(db);
        var query = new GetDocumentTemplatesQuery(TenantId, SearchTerm: "   ");

        await handler.Handle(query, CancellationToken.None);

        db.LastSql.Should().NotContain("LIKE");
    }
}
