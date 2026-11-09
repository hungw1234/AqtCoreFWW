using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.Identity.Application.Queries.ListExternalIdentities;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Queries;

/// <summary>
/// Tests for ListExternalIdentitiesQueryHandler.
/// Uses a reflection-based stub to work around the handler's internal ExternalIdentityRow type.
/// Verifies: returns paginated list, handles empty results, maps all fields.
/// </summary>
public sealed class ListExternalIdentitiesQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    // Reflection-based stub: constructs instances of any internal row type T
    // using positional constructor arguments, bypassing access restrictions.
    private sealed class FakeReadDbConnection : IReadDbConnection
    {
        // Each entry is a object[] of constructor args to pass to Activator.CreateInstance<T>
        private List<object[]> _rowArgs = [];

        public void SetRows(List<object[]> rows) => _rowArgs = rows;

        public Task<IEnumerable<T>> QueryAsync<T>(
            string sql, object? param = null,
            CancellationToken cancellationToken = default,
            int? commandTimeout = null)
        {
            if (_rowArgs.Count == 0)
                return Task.FromResult(Enumerable.Empty<T>());

            var result = _rowArgs
                .Select(args => (T)Activator.CreateInstance(typeof(T), args)!)
                .ToList();
            return Task.FromResult<IEnumerable<T>>(result);
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

        // Bypass variants — delegate to standard stubs (audit tables only; not used by Identity handlers)
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
    public async Task Handle_ReturnsPagedList()
    {
        // Arrange
        var db = new FakeReadDbConnection();
        db.SetRows([
            [1L, UserId, (int)ExternalIdentityProvider.OAuth, "google-123",
             "John Doe", "john@example.com", DateTime.UtcNow, null!, true, 2],
            [2L, UserId, (int)ExternalIdentityProvider.SSO, "octocat",
             "The Octocat", null!, DateTime.UtcNow, null!, true, 2]
        ]);
        var sut = new ListExternalIdentitiesQueryHandler(db);
        var query = new ListExternalIdentitiesQuery(UserId, CallerTenantId: null, IsPrivileged: true, 1, 10);

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(2);
        result.Value.Meta.Page.Should().Be(1);
        result.Value.Meta.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsEmptyList()
    {
        // Arrange
        var db = new FakeReadDbConnection();
        var sut = new ListExternalIdentitiesQueryHandler(db);
        var query = new ListExternalIdentitiesQuery(UserId, CallerTenantId: null, IsPrivileged: true, 1, 10);

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_PaginationCalculatesCorrectly()
    {
        // Arrange
        var db = new FakeReadDbConnection();
        // TotalCount = 15, PageSize = 5, so TotalPages = 3
        db.SetRows([
            [1L, UserId, (int)ExternalIdentityProvider.OAuth, "google-123",
             "User 1", null!, DateTime.UtcNow, null!, true, 15]
        ]);
        var sut = new ListExternalIdentitiesQueryHandler(db);
        var query = new ListExternalIdentitiesQuery(UserId, CallerTenantId: null, IsPrivileged: true, 2, 5);

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(15);
        result.Value.Meta.TotalPages.Should().Be(3);
        result.Value.Meta.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_MapsAllFields()
    {
        // Arrange
        var db = new FakeReadDbConnection();
        var linkedAt = DateTime.UtcNow.AddDays(-30);
        var lastSyncAt = DateTime.UtcNow.AddHours(-2);
        const long externalId = 99L;

        db.SetRows([
            [externalId, UserId, (int)ExternalIdentityProvider.OAuth, "google-user",
             "John Doe", "john@example.com", linkedAt, (DateTime?)lastSyncAt, true, 1]
        ]);
        var sut = new ListExternalIdentitiesQueryHandler(db);
        var query = new ListExternalIdentitiesQuery(UserId, CallerTenantId: null, IsPrivileged: true, 1, 10);

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value.Items.First();
        dto.UserId.Should().Be(UserId);
        dto.Provider.Should().Be(ExternalIdentityProvider.OAuth);
        dto.ExternalId.Should().Be("google-user");
        dto.DisplayName.Should().Be("John Doe");
        dto.Email.Should().Be("john@example.com");
        dto.LinkedAt.Should().Be(linkedAt);
        dto.LastSyncAt.Should().Be(lastSyncAt);
        dto.IsActive.Should().BeTrue();
    }
}
