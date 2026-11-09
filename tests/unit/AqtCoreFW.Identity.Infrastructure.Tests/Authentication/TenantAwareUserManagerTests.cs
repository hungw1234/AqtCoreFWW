using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Infrastructure.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Authentication;

/// <summary>
/// Unit tests for <see cref="TenantAwareUserManager"/> (QĐ 742 1.4 / FU2).
/// Verifies AccessFailedAsync resolves lockout threshold + duration from per-tenant
/// CredentialPolicy with framework IdentityOptions as fallback.
/// </summary>
public sealed class TenantAwareUserManagerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const int FrameworkMaxAttempts = 5;
    private static readonly TimeSpan FrameworkLockoutSpan = TimeSpan.FromMinutes(15);

    private readonly IUserStore<ApplicationUser> _store;
    private readonly IUserLockoutStore<ApplicationUser> _lockoutStore;
    private readonly ICredentialPolicyRepository _policyRepository;
    private readonly TenantAwareUserManager _sut;

    public TenantAwareUserManagerTests()
    {
        // Multi-interface substitute so the cast inside AccessFailedAsync succeeds
        var multi = Substitute.For<IUserStore<ApplicationUser>, IUserLockoutStore<ApplicationUser>>();
        _store = multi;
        _lockoutStore = (IUserLockoutStore<ApplicationUser>)multi;

        var optionsAccessor = Substitute.For<IOptions<IdentityOptions>>();
        optionsAccessor.Value.Returns(new IdentityOptions
        {
            Lockout = new LockoutOptions
            {
                MaxFailedAccessAttempts = FrameworkMaxAttempts,
                DefaultLockoutTimeSpan = FrameworkLockoutSpan
            }
        });

        _policyRepository = Substitute.For<ICredentialPolicyRepository>();

        // Default UpdateAsync to success so AccessFailedAsync's UpdateUserAsync resolves
        _store.UpdateAsync(Arg.Any<ApplicationUser>(), Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Success);

        _sut = new TenantAwareUserManager(
            _store,
            optionsAccessor,
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>(),
            _policyRepository);
    }

    [Fact]
    public async Task AccessFailedAsync_PolicyMaxAttempts10_DoesNotLockoutAtFifthAttempt()
    {
        var user = NewUser(TenantId);
        SetupPolicy(maxAttempts: 10, lockoutMinutes: 30);
        _lockoutStore.IncrementAccessFailedCountAsync(user, Arg.Any<CancellationToken>())
            .Returns(5);

        var result = await _sut.AccessFailedAsync(user);

        result.Succeeded.Should().BeTrue();
        await _lockoutStore.DidNotReceive().SetLockoutEndDateAsync(
            Arg.Any<ApplicationUser>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _lockoutStore.DidNotReceive().ResetAccessFailedCountAsync(
            Arg.Any<ApplicationUser>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccessFailedAsync_PolicyMaxAttempts10_LockoutAtTenthAttempt()
    {
        var user = NewUser(TenantId);
        SetupPolicy(maxAttempts: 10, lockoutMinutes: 30);
        _lockoutStore.IncrementAccessFailedCountAsync(user, Arg.Any<CancellationToken>())
            .Returns(10);

        var result = await _sut.AccessFailedAsync(user);

        result.Succeeded.Should().BeTrue();
        await _lockoutStore.Received(1).SetLockoutEndDateAsync(
            user,
            Arg.Is<DateTimeOffset?>(d => d.HasValue && IsApprox(d.Value, TimeSpan.FromMinutes(30))),
            Arg.Any<CancellationToken>());
        await _lockoutStore.Received(1).ResetAccessFailedCountAsync(
            user, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccessFailedAsync_NoTenant_FallsBackToFrameworkOptions()
    {
        var user = NewUser(tenantId: null);
        _lockoutStore.IncrementAccessFailedCountAsync(user, Arg.Any<CancellationToken>())
            .Returns(FrameworkMaxAttempts);

        var result = await _sut.AccessFailedAsync(user);

        result.Succeeded.Should().BeTrue();
        await _policyRepository.DidNotReceive().GetDefaultForTenantAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _lockoutStore.Received(1).SetLockoutEndDateAsync(
            user,
            Arg.Is<DateTimeOffset?>(d => d.HasValue && IsApprox(d.Value, FrameworkLockoutSpan)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccessFailedAsync_PolicyNullForTenant_FallsBackToFrameworkOptions()
    {
        var user = NewUser(TenantId);
        _policyRepository.GetDefaultForTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns((CredentialPolicy?)null);
        _lockoutStore.IncrementAccessFailedCountAsync(user, Arg.Any<CancellationToken>())
            .Returns(FrameworkMaxAttempts);

        var result = await _sut.AccessFailedAsync(user);

        result.Succeeded.Should().BeTrue();
        await _lockoutStore.Received(1).SetLockoutEndDateAsync(
            user,
            Arg.Is<DateTimeOffset?>(d => d.HasValue && IsApprox(d.Value, FrameworkLockoutSpan)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccessFailedAsync_PolicyMaxAttemptsZero_FallsBackToFrameworkMax()
    {
        var user = NewUser(TenantId);
        SetupPolicy(maxAttempts: 0, lockoutMinutes: 30);
        _lockoutStore.IncrementAccessFailedCountAsync(user, Arg.Any<CancellationToken>())
            .Returns(FrameworkMaxAttempts);

        var result = await _sut.AccessFailedAsync(user);

        result.Succeeded.Should().BeTrue();
        await _lockoutStore.Received(1).SetLockoutEndDateAsync(
            user,
            Arg.Is<DateTimeOffset?>(d => d.HasValue && IsApprox(d.Value, TimeSpan.FromMinutes(30))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccessFailedAsync_PolicyLockoutMinutesZero_FallsBackToFrameworkTimespan()
    {
        var user = NewUser(TenantId);
        SetupPolicy(maxAttempts: 10, lockoutMinutes: 0);
        _lockoutStore.IncrementAccessFailedCountAsync(user, Arg.Any<CancellationToken>())
            .Returns(10);

        var result = await _sut.AccessFailedAsync(user);

        result.Succeeded.Should().BeTrue();
        await _lockoutStore.Received(1).SetLockoutEndDateAsync(
            user,
            Arg.Is<DateTimeOffset?>(d => d.HasValue && IsApprox(d.Value, FrameworkLockoutSpan)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccessFailedAsync_PolicyRepoThrows_FallsBackToFrameworkOptions()
    {
        // FU6: defense-in-depth — repo exception must not bypass lockout.
        var user = NewUser(TenantId);
        _policyRepository.GetDefaultForTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns<CredentialPolicy?>(_ => throw new InvalidOperationException("DB unreachable"));
        _lockoutStore.IncrementAccessFailedCountAsync(user, Arg.Any<CancellationToken>())
            .Returns(FrameworkMaxAttempts);

        var result = await _sut.AccessFailedAsync(user);

        result.Succeeded.Should().BeTrue();
        await _lockoutStore.Received(1).SetLockoutEndDateAsync(
            user,
            Arg.Is<DateTimeOffset?>(d => d.HasValue && IsApprox(d.Value, FrameworkLockoutSpan)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccessFailedAsync_TenantIdEmptyGuid_SkipsPolicyLookupAndUsesFramework()
    {
        var user = NewUser(Guid.Empty);
        _lockoutStore.IncrementAccessFailedCountAsync(user, Arg.Any<CancellationToken>())
            .Returns(FrameworkMaxAttempts);

        var result = await _sut.AccessFailedAsync(user);

        result.Succeeded.Should().BeTrue();
        await _policyRepository.DidNotReceive().GetDefaultForTenantAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _lockoutStore.Received(1).SetLockoutEndDateAsync(
            user,
            Arg.Is<DateTimeOffset?>(d => d.HasValue && IsApprox(d.Value, FrameworkLockoutSpan)),
            Arg.Any<CancellationToken>());
    }

    private static ApplicationUser NewUser(Guid? tenantId) =>
        new() { Id = Guid.NewGuid(), UserName = "u@test", Email = "u@test", TenantId = tenantId };

    private void SetupPolicy(int maxAttempts, int lockoutMinutes)
    {
        var policy = CredentialPolicy.Create(
            name: "tenant-policy",
            tenantId: TenantId,
            minLength: 12,
            maxLength: 64,
            requireUppercase: true,
            requireLowercase: true,
            requireDigit: true,
            requireSpecialChar: true,
            rotationDays: 90,
            maxFailedAttempts: maxAttempts,
            lockoutMinutes: lockoutMinutes,
            passwordHistoryCount: 5,
            isDefault: true,
            createdBy: Guid.Empty);
        _policyRepository.GetDefaultForTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(policy);
    }

    private static bool IsApprox(DateTimeOffset actual, TimeSpan expectedDelta)
    {
        var delta = actual - DateTimeOffset.UtcNow;
        return Math.Abs((delta - expectedDelta).TotalSeconds) < 5;
    }
}
