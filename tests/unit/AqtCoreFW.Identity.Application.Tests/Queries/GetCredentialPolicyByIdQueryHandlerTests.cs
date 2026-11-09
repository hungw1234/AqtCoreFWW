using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.Identity.Application.Queries.GetCredentialPolicyById;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Queries;

/// <summary>
/// Tests for GetCredentialPolicyByIdQueryHandler.
/// Verifies: found, not found scenarios.
/// </summary>
public sealed class GetCredentialPolicyByIdQueryHandlerTests
{
    private readonly ICredentialPolicyRepository _repo = Substitute.For<ICredentialPolicyRepository>();
    private readonly GetCredentialPolicyByIdQueryHandler _sut;

    private const long PolicyId = 42L;
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public GetCredentialPolicyByIdQueryHandlerTests()
    {
        _sut = new GetCredentialPolicyByIdQueryHandler(_repo);
    }

    [Fact]
    public async Task Handle_PolicyExists_ReturnsDto()
    {
        // Arrange
        var query = new GetCredentialPolicyByIdQuery(PolicyId);

        var policy = CredentialPolicy.Create(
            "Standard Policy",
            TenantId,
            8,
            256,
            true,
            true,
            true,
            true,
            90,
            5,
            30,
            3,
            false,
            ActorId);

        _repo.GetByIdAsync(PolicyId, Arg.Any<CancellationToken>()).Returns(policy);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Id.Should().Be(policy.Id);
        dto.Name.Should().Be("Standard Policy");
        dto.TenantId.Should().Be(TenantId);
        dto.MinLength.Should().Be(8);
        dto.MaxLength.Should().Be(256);
        dto.RequireUppercase.Should().BeTrue();
        dto.RequireLowercase.Should().BeTrue();
        dto.RequireDigit.Should().BeTrue();
        dto.RequireSpecialChar.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_PolicyNotFound_ReturnsFailResult()
    {
        // Arrange
        var query = new GetCredentialPolicyByIdQuery(PolicyId);

        _repo.GetByIdAsync(PolicyId, Arg.Any<CancellationToken>()).Returns((CredentialPolicy?)null);

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
        var query = new GetCredentialPolicyByIdQuery(PolicyId);

        var policy = CredentialPolicy.Create(
            "Custom Policy",
            TenantId,
            10,
            200,
            true,
            false,
            true,
            false,
            60,
            3,
            15,
            2,
            true,
            ActorId);

        _repo.GetByIdAsync(PolicyId, Arg.Any<CancellationToken>()).Returns(policy);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Should().BeOfType<CredentialPolicyDto>();
        dto.RotationDays.Should().Be(60);
        dto.MaxFailedAttempts.Should().Be(3);
        dto.LockoutMinutes.Should().Be(15);
        dto.PasswordHistoryCount.Should().Be(2);
        dto.IsDefault.Should().BeTrue();
    }
}
