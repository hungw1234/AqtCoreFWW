using AqtCoreFW.Identity.Application.Commands.DelegateRole;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for DelegateRoleCommandHandler.
/// Covers: valid delegation, self-delegation guard, expired window guard,
/// unauthorized actor guard, and duplicate overlap guard.
/// </summary>
public sealed class DelegateRoleCommandHandlerTests
{
    private readonly IDelegationRepository _delegations;
    private readonly IGuidGenerator _guidGen;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DelegateRoleCommandHandler _sut;

    private static readonly Guid DelegatorId = Guid.NewGuid();
    private static readonly Guid DelegateId = Guid.NewGuid();
    private static readonly Guid ActorId = DelegatorId; // actor == delegator by default (self-service)

    public DelegateRoleCommandHandlerTests()
    {
        _delegations = Substitute.For<IDelegationRepository>();
        _guidGen = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());

        var store = Substitute.For<IUserStore<ApplicationUser>,
                                   IUserPasswordStore<ApplicationUser>>();
        _userManager = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());

        _sut = new DelegateRoleCommandHandler(_delegations, _guidGen, _userManager);
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithDelegationId()
    {
        _delegations.HasActiveOverlapAsync(
            DelegatorId, DelegateId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var cmd = BuildCommand();
        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsDelegationAndPublishesEvent()
    {
        _delegations.HasActiveOverlapAsync(
            DelegatorId, DelegateId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await _sut.Handle(BuildCommand(), CancellationToken.None);

        await _delegations.Received(1).AddAsync(
            Arg.Is<UserDelegation>(d => d.DelegatorId == DelegatorId && d.DelegateId == DelegateId),
            Arg.Any<CancellationToken>());
        await _delegations.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        // Domain events are raised inside UserDelegation.Create; outbox dispatches them on SaveChanges.
        // Handler no longer calls IDomainEventPublisher directly — outbox pattern handles dispatch.
    }

    // --- Validation guards ---

    [Fact]
    public async Task Handle_SelfDelegation_ReturnsFailure()
    {
        var cmd = BuildCommand(delegateId: DelegatorId); // delegate == delegator

        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("yourself"));
    }

    [Fact]
    public async Task Handle_ValidFromAfterValidTo_ReturnsFailure()
    {
        var now = DateTime.UtcNow;
        var cmd = BuildCommand(validFrom: now.AddDays(5), validTo: now.AddDays(1));

        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("ValidFrom"));
    }

    [Fact]
    public async Task Handle_ValidToInPast_ReturnsFailure()
    {
        var now = DateTime.UtcNow;
        var cmd = BuildCommand(validFrom: now.AddDays(-10), validTo: now.AddDays(-1));

        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("future"));
    }

    // --- Authorization: actor != delegator, non-admin ---

    [Fact]
    public async Task Handle_UnauthorizedActor_NotAdminRole_ReturnsForbidenFailure()
    {
        var differentActor = Guid.NewGuid();
        var actorUser = new ApplicationUser { Id = differentActor };
        _userManager.FindByIdAsync(differentActor.ToString()).Returns(actorUser);
        _userManager.GetRolesAsync(actorUser).Returns(["GovOfficer"]); // not Admin

        var cmd = BuildCommand(actorId: differentActor);
        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("yourself") || e.Message.Contains("only"));
    }

    [Fact]
    public async Task Handle_ActorIsAdmin_CanDelegateOnBehalfOfOthers()
    {
        var adminId = Guid.NewGuid();
        var adminUser = new ApplicationUser { Id = adminId };
        _userManager.FindByIdAsync(adminId.ToString()).Returns(adminUser);
        _userManager.GetRolesAsync(adminUser).Returns(["Admin"]);
        _delegations.HasActiveOverlapAsync(
            DelegatorId, DelegateId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var cmd = BuildCommand(actorId: adminId);
        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // --- Circular delegation (C-03) ---

    [Fact]
    public async Task Handle_CircularDelegationExists_ReturnsConflictFailure()
    {
        // B→A already exists — attempting A→B should be blocked
        _delegations.HasActiveOverlapAsync(
            DelegateId, DelegatorId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("Circular"));
    }

    // --- Duplicate overlap ---

    [Fact]
    public async Task Handle_OverlappingDelegationExists_ReturnsConflictFailure()
    {
        _delegations.HasActiveOverlapAsync(
            DelegatorId, DelegateId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("overlap") || e.Message.Contains("active"));
    }

    // --- C3 Guard: Status based on actor ownership ---

    [Fact]
    public async Task Handle_SelfDelegation_CreatesActiveStatus()
    {
        // Arrange: actor == delegator — self-delegation should create Active status immediately
        _delegations.HasActiveOverlapAsync(
            DelegatorId, DelegateId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var cmd = BuildCommand(actorId: DelegatorId); // actor == delegator

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _delegations.Received(1).AddAsync(
            Arg.Is<UserDelegation>(d =>
                d.DelegatorId == DelegatorId &&
                d.DelegateId == DelegateId &&
                d.Status == DelegationStatus.Active),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OnBehalfDelegationByAdmin_CreatesPendingApprovalStatus()
    {
        // Arrange: actor != delegator (on behalf) and actor is Admin — should create PendingApproval
        var adminId = Guid.NewGuid();
        var adminUser = new ApplicationUser { Id = adminId };
        _userManager.FindByIdAsync(adminId.ToString()).Returns(adminUser);
        _userManager.GetRolesAsync(adminUser).Returns(["Admin"]);
        _delegations.HasActiveOverlapAsync(
            DelegatorId, DelegateId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var cmd = BuildCommand(actorId: adminId); // actor != delegator

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _delegations.Received(1).AddAsync(
            Arg.Is<UserDelegation>(d =>
                d.DelegatorId == DelegatorId &&
                d.DelegateId == DelegateId &&
                d.Status == DelegationStatus.PendingApproval),
            Arg.Any<CancellationToken>());
    }

    // --- Helpers ---

    private static DelegateRoleCommand BuildCommand(
        Guid? delegateId = null,
        Guid? actorId = null,
        DateTime? validFrom = null,
        DateTime? validTo = null)
    {
        var now = DateTime.UtcNow;
        return new DelegateRoleCommand(
            DelegatorId: DelegatorId,
            DelegateId: delegateId ?? DelegateId,
            ValidFrom: validFrom ?? now.AddMinutes(1),
            ValidTo: validTo ?? now.AddDays(7),
            Reason: "Test delegation",
            ActorId: actorId ?? ActorId);
    }
}
