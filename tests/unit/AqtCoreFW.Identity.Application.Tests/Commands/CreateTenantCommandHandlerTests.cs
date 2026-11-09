using AqtCoreFW.Identity.Application.Commands.CreateTenant;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Events;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateTenantCommandHandler.
/// Verifies: success path, duplicate code conflict, event publishing.
/// </summary>
public sealed class CreateTenantCommandHandlerTests
{
    private readonly ITenantRepository _tenantRepository = Substitute.For<ITenantRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly CreateTenantCommandHandler _sut;

    private static readonly Guid ActorId = Guid.NewGuid();

    public CreateTenantCommandHandlerTests()
    {
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _sut = new CreateTenantCommandHandler(_tenantRepository, _guidGen, _publisher);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithNewTenantId()
    {
        // Arrange
        var cmd = new CreateTenantCommand(
            "Bộ Tài chính",
            "BTC",
            "contact@btc.gov.vn",
            ActorId);

        _tenantRepository.ExistsByCodeAsync(cmd.Code, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);

        // Verify repository was called
        await _tenantRepository.Received(1).AddAsync(
            Arg.Is<Tenant>(t =>
                t.Name == "Bộ Tài chính" &&
                t.Code == "BTC" &&
                t.ContactEmail == "contact@btc.gov.vn"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateCode_ReturnsConflictError()
    {
        // Arrange
        var cmd = new CreateTenantCommand(
            "Bộ Y tế",
            "BYT",
            null,
            ActorId);

        _tenantRepository.ExistsByCodeAsync(cmd.Code, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("đã tồn tại"));

        // Verify repository.AddAsync was not called
        await _tenantRepository.DidNotReceive().AddAsync(
            Arg.Any<Tenant>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PublishesTenantCreatedEvent()
    {
        // Arrange
        var cmd = new CreateTenantCommand(
            "Bộ Giáo dục",
            "BGDDT",
            "info@moet.gov.vn",
            ActorId);

        _tenantRepository.ExistsByCodeAsync(cmd.Code, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert — handler publishes via non-generic IPublisher.Publish(object, ct).
        // Use `as` cast (Expression-tree compatible — `is` pattern is not).
        await _publisher.Received(1).Publish(
            Arg.Is<object>(e => (e as TenantCreatedEvent) != null
                && ((TenantCreatedEvent)e).Code == "BGDDT"
                && ((TenantCreatedEvent)e).Name == "Bộ Giáo dục"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullContactEmail_AllowedByHandler()
    {
        // Arrange
        var cmd = new CreateTenantCommand(
            "Bộ Công an",
            "BCA",
            null,
            ActorId);

        _tenantRepository.ExistsByCodeAsync(cmd.Code, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _tenantRepository.Received(1).AddAsync(
            Arg.Is<Tenant>(t => t.ContactEmail == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CodeNormalized_ToUpperCase()
    {
        // Arrange
        var cmd = new CreateTenantCommand(
            "Bộ Ngoại giao",
            "bng-test",
            null,
            ActorId);

        _tenantRepository.ExistsByCodeAsync(cmd.Code, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert: Code should be normalized to uppercase in the entity
        await _tenantRepository.Received(1).AddAsync(
            Arg.Is<Tenant>(t => t.Code == "BNG-TEST"),
            Arg.Any<CancellationToken>());
    }
}
