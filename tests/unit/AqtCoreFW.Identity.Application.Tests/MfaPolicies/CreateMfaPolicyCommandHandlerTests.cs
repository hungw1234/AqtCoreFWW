using AqtCoreFW.Identity.Application.MfaPolicies;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.MfaPolicies;

/// <summary>
/// Unit tests for CreateMfaPolicyCommandHandler.
/// Phase-07 T2 retier: verifies handler returns Guid PublicId (not long Id)
/// and that IGuidGenerator is used via the factory.
/// </summary>
public sealed class CreateMfaPolicyCommandHandlerTests
{
    private static readonly Guid FixedPublicId = Guid.Parse("018f5e80-0001-7001-8000-000000000099");
    private static readonly Guid ActorId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly IMfaPolicyRepository _repo;
    private readonly ICurrentUser _currentUser;
    private readonly IGuidGenerator _guidGenerator;
    private readonly CreateMfaPolicyCommandHandler _sut;

    public CreateMfaPolicyCommandHandlerTests()
    {
        _repo = Substitute.For<IMfaPolicyRepository>();
        _currentUser = Substitute.For<ICurrentUser>();
        _guidGenerator = Substitute.For<IGuidGenerator>();

        _currentUser.UserId.Returns(ActorId);
        _guidGenerator.NewIdMonotonic().Returns(FixedPublicId);

        _sut = new CreateMfaPolicyCommandHandler(_repo, _currentUser, _guidGenerator);
    }

    [Fact]
    public async Task Handle_NewRole_ReturnsOkWithPublicId()
    {
        // Arrange — no existing policy for this role
        _repo.GetByRoleAsync("Manager", Arg.Any<CancellationToken>()).Returns((MfaPolicy?)null);

        var cmd = new CreateMfaPolicyCommand("Manager", true, 7, MfaFactor.Totp);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert — result is OK and carries the Guid PublicId from the generator
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(FixedPublicId, "PublicId must match what IGuidGenerator issued");
        await _repo.Received(1).AddAsync(Arg.Any<MfaPolicy>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateRole_ReturnsConflictError()
    {
        // Arrange — role already has a policy
        var existing = MfaPolicy.Create(
            new FixedGuidGenerator(Guid.NewGuid()), "Manager", true, 7, MfaFactor.Totp, ActorId);
        _repo.GetByRoleAsync("Manager", Arg.Any<CancellationToken>()).Returns(existing);

        var cmd = new CreateMfaPolicyCommand("Manager", false, 0, MfaFactor.EmailOtp);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert — ConflictError returned, no AddAsync call
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("already exists"));
        await _repo.DidNotReceive().AddAsync(Arg.Any<MfaPolicy>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UsesGuidGeneratorNotDirectGuid()
    {
        // Verify IGuidGenerator.NewIdMonotonic is called (not Guid.NewGuid directly).
        _repo.GetByRoleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MfaPolicy?)null);

        await _sut.Handle(new CreateMfaPolicyCommand("NewRole", false, 7, MfaFactor.Totp), CancellationToken.None);

        _guidGenerator.Received(1).NewIdMonotonic();
    }
}

/// <summary>Deterministic IGuidGenerator for test-local use within this file.</summary>
file sealed class FixedGuidGenerator(Guid id) : IGuidGenerator
{
    public Guid NewIdMonotonic() => id;
    public Guid NewIdRandom() => id;
}
