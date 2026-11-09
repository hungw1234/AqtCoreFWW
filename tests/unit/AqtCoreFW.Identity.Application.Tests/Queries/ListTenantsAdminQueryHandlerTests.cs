using AqtCoreFW.Identity.Application.Queries.ListTenantsAdmin;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Queries;

/// <summary>
/// Unit tests for ListTenantsAdminQueryHandler.
/// Verifies: pagination, status filter, user count enrichment.
/// </summary>
public sealed class ListTenantsAdminQueryHandlerTests
{
    private readonly ITenantRepository _tenantRepository = Substitute.For<ITenantRepository>();
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly ListTenantsAdminQueryHandler _sut;

    private static readonly Guid ActorId = Guid.NewGuid();

    public ListTenantsAdminQueryHandlerTests()
    {
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _sut = new ListTenantsAdminQueryHandler(_tenantRepository, _db);
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsEmptyPagedResult()
    {
        // Arrange
        _tenantRepository.ListAsync(
            Arg.Any<string?>(),
            Arg.Any<TenantStatus?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns((new List<Tenant>(), 0));

        var query = new ListTenantsAdminQuery(null, null, 1, 20);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithTenants_ReturnsPagedResult()
    {
        // Arrange
        var tenant1 = Tenant.Create(_guidGen, "Tenant A", "A", null, ActorId);
        var tenant2 = Tenant.Create(_guidGen, "Tenant B", "B", null, ActorId);

        _tenantRepository.ListAsync(
            Arg.Any<string?>(),
            Arg.Any<TenantStatus?>(),
            1, 20,
            Arg.Any<CancellationToken>())
            .Returns(([tenant1, tenant2], 2));

        // Mock empty user counts (Dapper returns no rows)
        _db.QueryAsync<object>(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<object>>([]));

        var query = new ListTenantsAdminQuery(null, null, 1, 20);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_WithStatusFilter_PassesFilterToRepository()
    {
        // Arrange
        _tenantRepository.ListAsync(
            Arg.Any<string?>(),
            TenantStatus.Active,
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns((new List<Tenant>(), 0));

        var query = new ListTenantsAdminQuery(null, TenantStatus.Active, 1, 20);

        // Act
        await _sut.Handle(query, CancellationToken.None);

        // Assert
        await _tenantRepository.Received(1).ListAsync(
            null,
            TenantStatus.Active,
            1, 20,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithSearch_PassesSearchToRepository()
    {
        // Arrange
        _tenantRepository.ListAsync(
            "test",
            Arg.Any<TenantStatus?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns((new List<Tenant>(), 0));

        var query = new ListTenantsAdminQuery("test", null, 1, 20);

        // Act
        await _sut.Handle(query, CancellationToken.None);

        // Assert
        await _tenantRepository.Received(1).ListAsync(
            "test",
            null,
            1, 20,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Pagination_CalculatesTotalPages()
    {
        // Arrange
        var tenant = Tenant.Create(_guidGen, "Tenant", "T", null, ActorId);

        _tenantRepository.ListAsync(
            Arg.Any<string?>(),
            Arg.Any<TenantStatus?>(),
            1, 10,
            Arg.Any<CancellationToken>())
            .Returns(([tenant], 25)); // 25 total, 10 per page = 3 pages

        _db.QueryAsync<object>(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<object>>([]));

        var query = new ListTenantsAdminQuery(null, null, 1, 10);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Meta.TotalPages.Should().Be(3);
        result.Value.Meta.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_LastPage_HasNextPageFalse()
    {
        // Arrange
        var tenant = Tenant.Create(_guidGen, "Tenant", "T", null, ActorId);

        _tenantRepository.ListAsync(
            Arg.Any<string?>(),
            Arg.Any<TenantStatus?>(),
            3, 10,
            Arg.Any<CancellationToken>())
            .Returns(([tenant], 25)); // Page 3 of 3

        _db.QueryAsync<object>(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<object>>([]));

        var query = new ListTenantsAdminQuery(null, null, 3, 10);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Meta.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_DtoMapping_IncludesAllFields()
    {
        // Arrange
        var tenant = Tenant.Create(_guidGen, "Test Name", "TESTCODE", "test@example.com", ActorId);

        _tenantRepository.ListAsync(
            Arg.Any<string?>(),
            Arg.Any<TenantStatus?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(([tenant], 1));

        _db.QueryAsync<object>(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<object>>([]));

        var query = new ListTenantsAdminQuery(null, null, 1, 20);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value.Items.First();
        dto.Name.Should().Be("Test Name");
        dto.Code.Should().Be("TESTCODE");
        dto.ContactEmail.Should().Be("test@example.com");
        dto.Status.Should().Be(TenantStatus.Active);
    }
}
