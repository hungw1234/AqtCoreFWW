using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.Identity.Application.Queries.GetExternalIdentityById;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Queries;

/// <summary>
/// Tests for GetExternalIdentityByIdQueryHandler.
/// Verifies: found, not found scenarios.
/// </summary>
public sealed class GetExternalIdentityByIdQueryHandlerTests
{
    private readonly IExternalIdentityRepository _repo = Substitute.For<IExternalIdentityRepository>();
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly GetExternalIdentityByIdQueryHandler _sut;

    private const long ExternalIdentityId = 42L;
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public GetExternalIdentityByIdQueryHandlerTests()
    {
        _sut = new GetExternalIdentityByIdQueryHandler(_repo, _db);
    }

    [Fact]
    public async Task Handle_ExternalIdentityExists_ReturnsDto()
    {
        // Arrange
        var query = new GetExternalIdentityByIdQuery(ExternalIdentityId, CallerTenantId: null, IsPrivileged: true);

        var entity = ExternalIdentity.Create(
            UserId,
            ExternalIdentityProvider.OAuth,
            "google-user-123",
            "John Doe",
            "john@example.com",
            ActorId);

        _repo.GetByIdAsync(ExternalIdentityId, Arg.Any<CancellationToken>()).Returns(entity);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Id.Should().Be(entity.Id);
        dto.UserId.Should().Be(UserId);
        dto.Provider.Should().Be(ExternalIdentityProvider.OAuth);
        dto.ExternalId.Should().Be("google-user-123");
        dto.DisplayName.Should().Be("John Doe");
        dto.Email.Should().Be("john@example.com");
    }

    [Fact]
    public async Task Handle_ExternalIdentityNotFound_ReturnsFailResult()
    {
        // Arrange
        var query = new GetExternalIdentityByIdQuery(ExternalIdentityId, CallerTenantId: null, IsPrivileged: true);

        _repo.GetByIdAsync(ExternalIdentityId, Arg.Any<CancellationToken>()).Returns((ExternalIdentity?)null);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Handle_ReturnsMappedDto()
    {
        // Arrange
        var query = new GetExternalIdentityByIdQuery(ExternalIdentityId, CallerTenantId: null, IsPrivileged: true);

        var entity = ExternalIdentity.Create(
            UserId,
            ExternalIdentityProvider.SSO,
            "octocat",
            "The Octocat",
            null,
            ActorId);

        _repo.GetByIdAsync(ExternalIdentityId, Arg.Any<CancellationToken>()).Returns(entity);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Should().BeOfType<ExternalIdentityDto>();
        dto.Id.Should().Be(entity.Id);
        dto.Provider.Should().Be(ExternalIdentityProvider.SSO);
        dto.ExternalId.Should().Be("octocat");
        dto.IsActive.Should().Be(entity.IsActive);
    }

    [Fact]
    public async Task Handle_NonPrivilegedCrossTenant_ReturnsNotFound()
    {
        // Tenant isolation: non-privileged caller whose tenant does not own the link's user
        // gets NotFound (not Forbidden) so record existence is not leaked via the sequential Id.
        var query = new GetExternalIdentityByIdQuery(ExternalIdentityId, CallerTenantId: Guid.NewGuid(), IsPrivileged: false);
        var entity = ExternalIdentity.Create(
            UserId, ExternalIdentityProvider.OAuth, "google-123", "John", "john@example.com", ActorId);
        _repo.GetByIdAsync(ExternalIdentityId, Arg.Any<CancellationToken>()).Returns(entity);
        _db.QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(0); // owning user not in caller tenant

        var result = await _sut.Handle(query, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Handle_NonPrivilegedSameTenant_ReturnsDto()
    {
        var query = new GetExternalIdentityByIdQuery(ExternalIdentityId, CallerTenantId: Guid.NewGuid(), IsPrivileged: false);
        var entity = ExternalIdentity.Create(
            UserId, ExternalIdentityProvider.OAuth, "google-123", "John", "john@example.com", ActorId);
        _repo.GetByIdAsync(ExternalIdentityId, Arg.Any<CancellationToken>()).Returns(entity);
        _db.QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(1); // owning user belongs to caller tenant

        var result = await _sut.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(UserId);
    }
}
