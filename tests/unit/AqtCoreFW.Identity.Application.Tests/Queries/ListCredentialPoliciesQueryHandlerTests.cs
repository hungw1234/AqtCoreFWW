using AqtCoreFW.Identity.Application.Queries.ListCredentialPolicies;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Queries;

/// <summary>
/// Tests for ListCredentialPoliciesQueryHandler.
/// Uses a reflection-based stub to work around the handler's internal CredentialPolicyRow type.
/// Verifies: returns paginated list, handles empty results, maps all fields.
/// </summary>
public sealed class ListCredentialPoliciesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    // Reflection-based stub: constructs instances of any internal row type T
    // using positional constructor arguments, bypassing access restrictions.
    private sealed class FakeReadDbConnection : IReadDbConnection
    {
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
            [1L, "Default Policy", TenantId,
             8, 256, true, true, true, true, 90, 5, 30, 3, true, 3, 24, true, 2],
            [2L, "Standard Policy", TenantId,
             10, 200, true, true, true, false, 60, 3, 15, 2, false, 3, 24, true, 2]
        ]);
        var sut = new ListCredentialPoliciesQueryHandler(db);
        var query = new ListCredentialPoliciesQuery(TenantId, 1, 10);

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
        var sut = new ListCredentialPoliciesQueryHandler(db);
        var query = new ListCredentialPoliciesQuery(TenantId, 1, 10);

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
        // TotalCount = 25, PageSize = 5, so TotalPages = 5
        db.SetRows([
            [1L, "Policy 6", TenantId,
             8, 256, true, true, true, true, 90, 5, 30, 3, false, 3, 24, true, 25]
        ]);
        var sut = new ListCredentialPoliciesQueryHandler(db);
        var query = new ListCredentialPoliciesQuery(TenantId, 2, 5);

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(25);
        result.Value.Meta.TotalPages.Should().Be(5);
        result.Value.Meta.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_MapsAllFields()
    {
        // Arrange
        var db = new FakeReadDbConnection();
        const long policyId = 99L;
        db.SetRows([
            [policyId, "Advanced Policy", TenantId,
             12, 256, false, true, true, true, 45, 7, 45, 5, true, 3, 24, true, 1]
        ]);
        var sut = new ListCredentialPoliciesQueryHandler(db);
        var query = new ListCredentialPoliciesQuery(TenantId, 1, 10);

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value.Items.First();
        dto.Id.Should().Be(policyId);
        dto.Name.Should().Be("Advanced Policy");
        dto.TenantId.Should().Be(TenantId);
        dto.MinLength.Should().Be(12);
        dto.MaxLength.Should().Be(256);
        dto.RequireUppercase.Should().BeFalse();
        dto.RequireLowercase.Should().BeTrue();
        dto.RequireDigit.Should().BeTrue();
        dto.RequireSpecialChar.Should().BeTrue();
        dto.RotationDays.Should().Be(45);
        dto.MaxFailedAttempts.Should().Be(7);
        dto.LockoutMinutes.Should().Be(45);
        dto.PasswordHistoryCount.Should().Be(5);
        dto.IsDefault.Should().BeTrue();
    }
}
