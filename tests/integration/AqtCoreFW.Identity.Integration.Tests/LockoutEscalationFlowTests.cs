using AqtCoreFW.Identity.Application.Services;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Integration tests for lockout escalation flow (QĐ 742 1.4.d).
/// Tests consecutive lockouts within window, escalation threshold, and admin unlock.
/// Runs against Testcontainers SQL Server via WebAppFixture.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class LockoutEscalationFlowTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    // Instance fields: xUnit creates a fresh class instance per test method, so each test
    // gets its own unique user — no state leakage between tests sharing the SQL Server fixture.
    private readonly Guid _testTenantId = Guid.NewGuid();
    private readonly Guid _testUserId = Guid.NewGuid();

    public LockoutEscalationFlowTests(WebAppFixture app)
    {
        _app = app;
    }

    /// <summary>
    /// Seeds an ApplicationUser and executes <paramref name="action"/> inside a single DI scope.
    /// Both UserManager and ILockoutEscalationService are resolved from the same scope so
    /// their DbContext instances are shared — required for consistent reads after writes.
    /// </summary>
    private async Task ExecuteInScopeAsync(
        Func<ApplicationUser, UserManager<ApplicationUser>, ILockoutEscalationService, Task> action)
    {
        using var scope = _app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var lockoutService = sp.GetRequiredService<ILockoutEscalationService>();

        // Seed user if not present.
        var user = await userManager.FindByIdAsync(_testUserId.ToString());
        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = _testUserId,
                UserName = $"testescalation-{_testUserId:N}",
                Email = $"testescalation-{_testUserId:N}@test.vn",
                EmailConfirmed = true,
                FullName = "Lockout Escalation Test User",
                TenantId = _testTenantId,
                IsActive = true,
            };
            var result = await userManager.CreateAsync(user, "TestPass@12345!");
            result.Succeeded.Should().BeTrue(
                $"Failed to seed user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;
        }

        await action(user, userManager, lockoutService);
    }

    [Fact]
    public async Task TripleLockout_WithinWindow_SetsAutoDisabledAfterEscalation()
    {
        await ExecuteInScopeAsync(async (user, userManager, lockoutService) =>
        {
            // First lockout: counter=1
            var result1 = await lockoutService.OnLockoutAsync(user);
            result1.Should().BeFalse("not escalated yet");

            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;
            user.ConsecutiveLockoutCount.Should().Be(1);
            user.AutoDisabledAt.Should().BeNull();

            // Second lockout: counter=2
            var result2 = await lockoutService.OnLockoutAsync(user);
            result2.Should().BeFalse("not escalated yet");

            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;
            user.ConsecutiveLockoutCount.Should().Be(2);
            user.AutoDisabledAt.Should().BeNull();

            // Third lockout: counter=3 → escalation triggered (default threshold=3)
            var result3 = await lockoutService.OnLockoutAsync(user);
            result3.Should().BeTrue("escalation should be triggered at threshold=3");

            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;
            user.ConsecutiveLockoutCount.Should().Be(3);
            user.AutoDisabledAt.Should().NotBeNull();
            user.LockoutEnd.Should().Be(DateTimeOffset.MaxValue, "permanently locked after escalation");
        });
    }

    [Fact]
    public async Task FirstLockout_SetsCounterTo1()
    {
        await ExecuteInScopeAsync(async (user, userManager, lockoutService) =>
        {
            await lockoutService.OnLockoutAsync(user);

            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;
            user.ConsecutiveLockoutCount.Should().Be(1);
            user.AutoDisabledAt.Should().BeNull();
            user.LastLockoutAt.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsLockoutCounter()
    {
        await ExecuteInScopeAsync(async (user, userManager, lockoutService) =>
        {
            // Set up state: 2 lockouts
            await lockoutService.OnLockoutAsync(user);
            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;
            user.ConsecutiveLockoutCount.Should().Be(1);

            await lockoutService.OnLockoutAsync(user);
            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;
            user.ConsecutiveLockoutCount.Should().Be(2);

            // Act: successful login resets counter
            await lockoutService.ResetLockoutCounterAsync(user);

            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;
            user.ConsecutiveLockoutCount.Should().Be(0);
        });
    }

    [Fact]
    public async Task AdminUnlock_ClearsAutoDisabledAndCounter()
    {
        await ExecuteInScopeAsync(async (user, userManager, lockoutService) =>
        {
            // Trigger escalation (3 lockouts = threshold)
            await lockoutService.OnLockoutAsync(user);
            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;
            await lockoutService.OnLockoutAsync(user);
            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;
            await lockoutService.OnLockoutAsync(user);
            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;

            user.AutoDisabledAt.Should().NotBeNull();
            user.ConsecutiveLockoutCount.Should().Be(3);
            user.LockoutEnd.Should().Be(DateTimeOffset.MaxValue);

            // Act: admin unlock
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow);
            user.ConsecutiveLockoutCount = 0;
            user.AutoDisabledAt = null;
            await userManager.UpdateAsync(user);

            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;
            user.AutoDisabledAt.Should().BeNull();
            user.ConsecutiveLockoutCount.Should().Be(0);
            if (user.LockoutEnd.HasValue)
            {
                user.LockoutEnd.Value.UtcTicks
                    .Should().BeLessThan(DateTimeOffset.UtcNow.AddSeconds(1).UtcTicks);
            }
        });
    }

    [Fact]
    public async Task LockoutOutsideWindow_ResetsCounter()
    {
        await ExecuteInScopeAsync(async (user, userManager, lockoutService) =>
        {
            // First lockout: counter=1, LastLockoutAt=now
            await lockoutService.OnLockoutAsync(user);
            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;
            user.ConsecutiveLockoutCount.Should().Be(1);

            // Simulate 25h passing — outside 24h escalation window
            user.LastLockoutAt = DateTime.UtcNow.AddHours(-25);
            await userManager.UpdateAsync(user);

            // Act: second lockout after window expiry
            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;
            await lockoutService.OnLockoutAsync(user);

            // Counter should reset to 1, not increment to 2
            user = (await userManager.FindByIdAsync(_testUserId.ToString()))!;
            user.ConsecutiveLockoutCount.Should().Be(1);
        });
    }
}
