using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.Queries.GetWorkflowDefinitions;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetWorkflowDefinitionsQueryHandler — mirrors Forms.GetFormTemplatesQueryHandlerTests
/// clamp facts (P7-FU1.1-FU). Hand-written stub avoids NSubstitute proxy issues.
/// </summary>
public sealed class GetWorkflowDefinitionsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private sealed class FakeReadDbConnection : IReadDbConnection
    {
        public Task<T> QuerySingleAsync<T>(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
            => Task.FromResult(default(T)!);

        public Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
            => Task.FromResult(Enumerable.Empty<T>());

        public Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
            => Task.FromResult(default(T));

        public Task<int> ExecuteAsync(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
            => Task.FromResult(0);

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
    public async Task Handle_PageSizeBeyondMax_ClampsToMaxInMeta()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetWorkflowDefinitionsQueryHandler(db);

        var result = await handler.Handle(
            new GetWorkflowDefinitionsQuery(TenantId, IsActive: null, SearchTerm: null, Page: 1, PageSize: 99_999),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Meta.PageSize.Should().Be(1000);
    }

    [Fact]
    public async Task Handle_PageSizeZeroOrNegative_ClampsToOne()
    {
        var db = new FakeReadDbConnection();
        var handler = new GetWorkflowDefinitionsQueryHandler(db);

        var result = await handler.Handle(
            new GetWorkflowDefinitionsQuery(TenantId, IsActive: null, SearchTerm: null, Page: 1, PageSize: -5),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Meta.PageSize.Should().Be(1);
    }
}
