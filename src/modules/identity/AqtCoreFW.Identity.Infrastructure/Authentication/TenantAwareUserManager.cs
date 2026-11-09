using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Infrastructure.Authentication;

/// <summary>
/// UserManager subclass that enforces per-tenant lockout thresholds (QĐ 742 1.4 / FU2).
/// Overrides <see cref="UserManager{TUser}.AccessFailedAsync"/> to read the tenant's
/// default <see cref="CredentialPolicy"/> and use its <c>MaxFailedAttempts</c> +
/// <c>LockoutMinutes</c> instead of the framework <see cref="IdentityOptions.Lockout"/>
/// values. Framework options remain the fallback when no policy exists or the user has
/// no tenant context (e.g. system users).
/// </summary>
public sealed class TenantAwareUserManager : UserManager<ApplicationUser>
{
    private readonly ICredentialPolicyRepository _policyRepository;
    private readonly ILogger<UserManager<ApplicationUser>> _logger;

    public TenantAwareUserManager(
        IUserStore<ApplicationUser> store,
        IOptions<IdentityOptions> optionsAccessor,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IEnumerable<IUserValidator<ApplicationUser>> userValidators,
        IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<ApplicationUser>> logger,
        ICredentialPolicyRepository policyRepository)
        : base(store, optionsAccessor, passwordHasher, userValidators, passwordValidators,
               keyNormalizer, errors, services, logger)
    {
        _policyRepository = policyRepository;
        _logger = logger;
    }

    public override async Task<IdentityResult> AccessFailedAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var lockoutStore = (IUserLockoutStore<ApplicationUser>)Store;

        var (maxAttempts, lockoutTimespan) = await ResolveLockoutSettingsAsync(user);

        var count = await lockoutStore.IncrementAccessFailedCountAsync(user, CancellationToken);

        if (count < maxAttempts)
        {
            return await UpdateUserAsync(user);
        }

        await lockoutStore.SetLockoutEndDateAsync(
            user, DateTimeOffset.UtcNow.Add(lockoutTimespan), CancellationToken);
        await lockoutStore.ResetAccessFailedCountAsync(user, CancellationToken);
        return await UpdateUserAsync(user);
    }

    private async Task<(int MaxAttempts, TimeSpan LockoutTimespan)> ResolveLockoutSettingsAsync(
        ApplicationUser user)
    {
        var fallbackMax = Options.Lockout.MaxFailedAccessAttempts;
        var fallbackTimespan = Options.Lockout.DefaultLockoutTimeSpan;

        if (user.TenantId is not { } tenantId || tenantId == Guid.Empty)
            return (fallbackMax, fallbackTimespan);

        // FU6: defense-in-depth — swallow repo exceptions so a transient DB hiccup or
        // schema drift cannot bypass lockout entirely (the framework fallback still locks).
        CredentialPolicy? policy;
        try
        {
            policy = await _policyRepository.GetDefaultForTenantAsync(tenantId, CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CredentialPolicy lookup failed for tenant {TenantId}; using framework IdentityOptions.Lockout fallback.",
                tenantId);
            return (fallbackMax, fallbackTimespan);
        }

        if (policy is null)
            return (fallbackMax, fallbackTimespan);

        var max = policy.MaxFailedAttempts > 0 ? policy.MaxFailedAttempts : fallbackMax;
        var span = policy.LockoutMinutes > 0 ? TimeSpan.FromMinutes(policy.LockoutMinutes) : fallbackTimespan;
        return (max, span);
    }
}
