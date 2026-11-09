using AqtCoreFW.Identity.Application.Commands.CreateJitProviderConfig;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using NSubstitute;
using FluentAssertions;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for CreateJitProviderConfigCommandHandler.
/// Verifies: unique scheme constraint, entity creation, repository persistence.
/// </summary>
public sealed class CreateJitProviderConfigCommandHandlerTests
{
    private readonly IJitProviderConfigRepository _repository;
    private readonly CreateJitProviderConfigCommandHandler _sut;

    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    public CreateJitProviderConfigCommandHandlerTests()
    {
        _repository = Substitute.For<IJitProviderConfigRepository>();
        _sut = new CreateJitProviderConfigCommandHandler(_repository);
    }

    [Fact]
    public async Task Handle_NewScheme_CreatesConfigAndReturnsId()
    {
        // Arrange
        var cmd = new CreateJitProviderConfigCommand(
            "NewSSO", "New SSO Provider", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TenantId, null, 100, ActorId);

        _repository.GetBySchemeAsync("NewSSO", Arg.Any<CancellationToken>()).Returns((JitProviderConfig?)null);
        _repository.AddAsync(Arg.Any<JitProviderConfig>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // T1: result.Value is long Id (0 in unit test since repo mock doesn't trigger DB IDENTITY)
        await _repository.Received(1).AddAsync(
            Arg.Is<JitProviderConfig>(c =>
                c.Scheme == "NewSSO" && c.DisplayName == "New SSO Provider"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateScheme_ReturnsConflictError()
    {
        // Arrange
        var existingConfig = JitProviderConfig.Create(
            "ExistingSSO", "Existing SSO", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TenantId, null, 0, ActorId);

        var cmd = new CreateJitProviderConfigCommand(
            "ExistingSSO", "Duplicate SSO", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TenantId, null, 0, ActorId);

        _repository.GetBySchemeAsync("ExistingSSO", Arg.Any<CancellationToken>()).Returns(existingConfig);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ConflictError && e.Message.Contains("already exists"));
        await _repository.DidNotReceive().AddAsync(Arg.Any<JitProviderConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CreatesConfigWithAllProperties()
    {
        // Arrange
        var allowedDomains = System.Text.Json.JsonSerializer.Serialize(new[] { "company.vn", "gov.vn" });
        var claimMapping = System.Text.Json.JsonSerializer.Serialize(new { sub = "Id", email = "Email" });

        var cmd = new CreateJitProviderConfigCommand(
            "FullConfig", "Full Config", ExternalIdentityProvider.SAML,
            true, "Editor", true, claimMapping, TenantId, allowedDomains, 50, ActorId);

        _repository.GetBySchemeAsync("FullConfig", Arg.Any<CancellationToken>()).Returns((JitProviderConfig?)null);
        _repository.AddAsync(Arg.Any<JitProviderConfig>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _repository.Received(1).AddAsync(
            Arg.Is<JitProviderConfig>(c =>
                c.Scheme == "FullConfig" &&
                c.ProviderType == ExternalIdentityProvider.SAML &&
                c.DefaultRoleName == "Editor" &&
                c.RequireApproval &&
                c.ClaimMappingJson == claimMapping &&
                c.AllowedDomainsJson == allowedDomains &&
                c.MaxProvisionsPerHour == 50),
            Arg.Any<CancellationToken>());
    }
}
