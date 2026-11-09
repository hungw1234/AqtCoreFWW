using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Infrastructure.Authentication;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Events;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Authentication;

/// <summary>
/// Unit tests for LockoutEscalationService (QĐ 742 1.4.d).
/// Tests lockout counter tracking, escalation window logic, and auto-disable thresholds.
/// Uses mocked UserManager, CredentialPolicyRepository, IMessageBus — no real DB.
/// </summary>
public sealed class LockoutEscalationServiceTests
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICredentialPolicyRepository _policyRepository;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<LockoutEscalationService> _logger;
    private readonly LockoutEscalationService _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    public LockoutEscalationServiceTests()
    {
        var store = Substitute.For<IUserStore<ApplicationUser>,
            IUserEmailStore<ApplicationUser>>();

        _userManager = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<Microsoft.Extensions.Options.IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());

        _policyRepository = Substitute.For<ICredentialPolicyRepository>();
        _messageBus = Substitute.For<IMessageBus>();
        _logger = Substitute.For<ILogger<LockoutEscalationService>>();

        _sut = new LockoutEscalationService(_userManager, _policyRepository, _messageBus, _logger);
    }

    [Fact]
    public async Task OnLockoutAsync_FirstLockoutInWindow_SetsCounterTo1AndReturnsfalse()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = UserId,
            UserName = "testuser",
            TenantId = TenantId,
            LastLockoutAt = null,
            ConsecutiveLockoutCount = 0,
            AutoDisabledAt = null,
        };

        var policy = CredentialPolicy.Create(
            "Default", TenantId, 8, 128, true, true, true, true, 90, 5, 30, 3, true, Guid.NewGuid(),
            escalationLockoutCount: 3, escalationWindowHours: 24);
        _policyRepository.GetDefaultForTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(policy);

        // Act
        var result = await _sut.OnLockoutAsync(user);

        // Assert
        result.Should().BeFalse();
        user.ConsecutiveLockoutCount.Should().Be(1);
        user.AutoDisabledAt.Should().BeNull();
        user.LastLockoutAt.Should().NotBeNull();
        await _userManager.Received(1).UpdateAsync(user);
        await _messageBus.DidNotReceive().PublishAsync(Arg.Any<AccountAutoDisabledIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnLockoutAsync_SecondLockoutInWindow_IncrementsCounterAndReturnsfalse()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var user = new ApplicationUser
        {
            Id = UserId,
            UserName = "testuser",
            TenantId = TenantId,
            LastLockoutAt = now.AddHours(-1), // 1 hour ago (within 24h window)
            ConsecutiveLockoutCount = 1,
            AutoDisabledAt = null,
        };

        var policy = CredentialPolicy.Create(
            "Default", TenantId, 8, 128, true, true, true, true, 90, 5, 30, 3, true, Guid.NewGuid(),
            escalationLockoutCount: 3, escalationWindowHours: 24);
        _policyRepository.GetDefaultForTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(policy);

        // Act
        var result = await _sut.OnLockoutAsync(user);

        // Assert
        result.Should().BeFalse();
        user.ConsecutiveLockoutCount.Should().Be(2);
        user.AutoDisabledAt.Should().BeNull();
        await _userManager.Received(1).UpdateAsync(user);
        await _messageBus.DidNotReceive().PublishAsync(Arg.Any<AccountAutoDisabledIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnLockoutAsync_LockoutOutsideWindow_ResetsCounterToAndReturnsfalse()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var user = new ApplicationUser
        {
            Id = UserId,
            UserName = "testuser",
            TenantId = TenantId,
            LastLockoutAt = now.AddHours(-25), // 25 hours ago (outside 24h window)
            ConsecutiveLockoutCount = 2,
            AutoDisabledAt = null,
        };

        var policy = CredentialPolicy.Create(
            "Default", TenantId, 8, 128, true, true, true, true, 90, 5, 30, 3, true, Guid.NewGuid(),
            escalationLockoutCount: 3, escalationWindowHours: 24);
        _policyRepository.GetDefaultForTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(policy);

        // Act
        var result = await _sut.OnLockoutAsync(user);

        // Assert
        result.Should().BeFalse();
        user.ConsecutiveLockoutCount.Should().Be(1); // Reset, not incremented
        user.AutoDisabledAt.Should().BeNull();
        await _userManager.Received(1).UpdateAsync(user);
        await _messageBus.DidNotReceive().PublishAsync(Arg.Any<AccountAutoDisabledIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnLockoutAsync_HitEscalationThreshold_SetsAutoDisabledAndPublishesEvent()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var user = new ApplicationUser
        {
            Id = UserId,
            UserName = "testuser",
            Email = "test@example.com",
            TenantId = TenantId,
            LastLockoutAt = now.AddHours(-1), // Within window
            ConsecutiveLockoutCount = 2,
            AutoDisabledAt = null,
        };

        var policy = CredentialPolicy.Create(
            "Default", TenantId, 8, 128, true, true, true, true, 90, 5, 30, 3, true, Guid.NewGuid(),
            escalationLockoutCount: 3, escalationWindowHours: 24);
        _policyRepository.GetDefaultForTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(policy);

        _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue)
            .Returns(IdentityResult.Success);

        // Act
        var result = await _sut.OnLockoutAsync(user);

        // Assert
        result.Should().BeTrue();
        user.ConsecutiveLockoutCount.Should().Be(3);
        user.AutoDisabledAt.Should().NotBeNull();
        await _userManager.Received(1).SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await _messageBus.Received(1).PublishAsync(
            Arg.Is<AccountAutoDisabledIntegrationEvent>(e =>
                e.UserId == UserId && e.TenantId == TenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnLockoutAsync_AlreadyAutoDisabled_DoesNotRepublishEvent()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var user = new ApplicationUser
        {
            Id = UserId,
            UserName = "testuser",
            Email = "test@example.com",
            TenantId = TenantId,
            LastLockoutAt = now.AddHours(-1),
            ConsecutiveLockoutCount = 3,
            AutoDisabledAt = now.AddHours(-5), // Already auto-disabled
        };

        var policy = CredentialPolicy.Create(
            "Default", TenantId, 8, 128, true, true, true, true, 90, 5, 30, 3, true, Guid.NewGuid(),
            escalationLockoutCount: 3, escalationWindowHours: 24);
        _policyRepository.GetDefaultForTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(policy);

        // Act
        var result = await _sut.OnLockoutAsync(user);

        // Assert
        result.Should().BeFalse(); // Already auto-disabled, so no new escalation
        user.ConsecutiveLockoutCount.Should().Be(4); // Still increments counter
        await _messageBus.DidNotReceive().PublishAsync(Arg.Any<AccountAutoDisabledIntegrationEvent>(), Arg.Any<CancellationToken>());
        await _userManager.Received(1).UpdateAsync(user);
    }

    [Fact]
    public async Task OnLockoutAsync_NullPolicy_UsesHardcodedDefaults()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var user = new ApplicationUser
        {
            Id = UserId,
            UserName = "testuser",
            Email = "test@example.com",
            TenantId = TenantId,
            LastLockoutAt = now.AddHours(-1),
            ConsecutiveLockoutCount = 2,
            AutoDisabledAt = null,
        };

        _policyRepository.GetDefaultForTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .ReturnsNull(); // No policy found

        _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue)
            .Returns(IdentityResult.Success);

        // Act
        var result = await _sut.OnLockoutAsync(user);

        // Assert
        // Hardcoded defaults: EscalationLockoutCount=3, EscalationWindowHours=24
        // counter=2+1=3 → hits threshold
        result.Should().BeTrue();
        user.ConsecutiveLockoutCount.Should().Be(3);
        user.AutoDisabledAt.Should().NotBeNull();
        await _messageBus.Received(1).PublishAsync(
            Arg.Is<AccountAutoDisabledIntegrationEvent>(e => e.UserId == UserId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnLockoutAsync_PolicyRepoThrows_UsesHardcodedDefaults()
    {
        // FU6: defense-in-depth — repo exception must not bypass escalation accounting.
        var now = DateTime.UtcNow;
        var user = new ApplicationUser
        {
            Id = UserId,
            UserName = "testuser",
            Email = "test@example.com",
            TenantId = TenantId,
            LastLockoutAt = now.AddHours(-1),
            ConsecutiveLockoutCount = 2,
            AutoDisabledAt = null,
        };

        _policyRepository.GetDefaultForTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns<CredentialPolicy?>(_ => throw new InvalidOperationException("DB unreachable"));

        _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue)
            .Returns(IdentityResult.Success);

        var result = await _sut.OnLockoutAsync(user);

        // Hardcoded fallback: count=3 / window=24h → 2+1=3 hits threshold.
        result.Should().BeTrue();
        user.ConsecutiveLockoutCount.Should().Be(3);
        user.AutoDisabledAt.Should().NotBeNull();
        await _messageBus.Received(1).PublishAsync(
            Arg.Is<AccountAutoDisabledIntegrationEvent>(e => e.UserId == UserId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnLockoutAsync_TwoConcurrentCalls_BothCompleteWithoutException()
    {
        // FU3: concurrency stress — documents the read-modify-write race on
        // ConsecutiveLockoutCount. Both calls must complete without throwing; the
        // observed counter is racy (1 or 2). A CAS-based fix is tracked in QD742-FU5.
        var user = new ApplicationUser
        {
            Id = UserId,
            UserName = "testuser",
            TenantId = TenantId,
            LastLockoutAt = null,
            ConsecutiveLockoutCount = 0,
            AutoDisabledAt = null,
        };

        var policy = CredentialPolicy.Create(
            "Default", TenantId, 8, 128, true, true, true, true, 90, 5, 30, 3, true, Guid.NewGuid(),
            escalationLockoutCount: 5, escalationWindowHours: 24);
        _policyRepository.GetDefaultForTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(policy);

        var t1 = Task.Run(() => _sut.OnLockoutAsync(user));
        var t2 = Task.Run(() => _sut.OnLockoutAsync(user));

        var results = await Task.WhenAll(t1, t2);

        results.Should().AllSatisfy(r => r.Should().BeFalse(),
            "threshold is 5 so neither call should auto-disable");
        user.ConsecutiveLockoutCount.Should().BeInRange(1, 2,
            "race may overwrite — documenting pre-existing behavior, FU5 tracks CAS fix");
        user.AutoDisabledAt.Should().BeNull();
        await _messageBus.DidNotReceive().PublishAsync(
            Arg.Any<AccountAutoDisabledIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetLockoutCounterAsync_NonZeroCounter_ResetsAndCalls()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = UserId,
            UserName = "testuser",
            ConsecutiveLockoutCount = 2,
        };

        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        // Act
        await _sut.ResetLockoutCounterAsync(user);

        // Assert
        user.ConsecutiveLockoutCount.Should().Be(0);
        await _userManager.Received(1).UpdateAsync(user);
    }

    [Fact]
    public async Task ResetLockoutCounterAsync_ZeroCounter_SkipsUpdateAsync()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = UserId,
            UserName = "testuser",
            ConsecutiveLockoutCount = 0,
        };

        // Act
        await _sut.ResetLockoutCounterAsync(user);

        // Assert
        user.ConsecutiveLockoutCount.Should().Be(0);
        await _userManager.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
    }
}
