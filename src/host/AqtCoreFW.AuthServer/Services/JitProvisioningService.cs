using AqtCoreFW.Audit.Domain.Services;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.AuthServer.Services;

/// <summary>
/// Core JIT (Just-In-Time) provisioning logic for external SSO login.
/// On first external login: auto-create ApplicationUser + ExternalIdentity link.
/// Security: never auto-links by email (RT-01), validates domain whitelist (RT-02),
/// requires TenantId (RT-03), rate limits (RT-04).
/// </summary>
public sealed class JitProvisioningService(
    UserManager<ApplicationUser> userManager,
    IExternalIdentityRepository externalIdentityRepo,
    IJitProviderConfigRepository jitConfigRepo,
    IAuditService auditService,
    IdentityDbContext dbContext,
    ILogger<JitProvisioningService> logger)
{
    // In-memory rate limit tracking per provider scheme — reset hourly
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _rateLimits = new();

    public sealed record JitResult(
        bool Success,
        ApplicationUser? User,
        string? ErrorCode); // "deactivated", "pending_approval", "provision_failed", "jit_disabled", "domain_not_allowed", "rate_limited", "no_tenant"

    public async Task<JitResult> ProvisionOrLinkAsync(
        string loginProvider,
        string providerKey,
        string? email,
        string? fullName,
        CancellationToken ct = default)
    {
        var provider = MapProvider(loginProvider);

        // 1. Look up existing ExternalIdentity by (Provider, ExternalId)
        var existing = await externalIdentityRepo
            .GetByProviderAndExternalIdAsync(provider, providerKey, ct);

        if (existing is not null)
            return await HandleExistingLink(existing, fullName, email, ct);

        // 2. No existing link — check JIT provider config
        var config = await jitConfigRepo.GetBySchemeAsync(loginProvider, ct);
        if (config is null || !config.JitEnabled || !config.IsActive)
        {
            logger.LogInformation("JIT disabled for provider {Provider}", loginProvider);
            return new(false, null, "jit_disabled");
        }

        // [RT-03] Require TenantId
        if (config.DefaultTenantId is null)
        {
            logger.LogWarning("JIT provider {Provider} has no TenantId configured", loginProvider);
            return new(false, null, "no_tenant");
        }

        // [RT-02] Validate email domain
        if (!IsEmailDomainAllowed(email, config.AllowedDomainsJson))
        {
            logger.LogWarning("JIT: Email {Email} domain not in AllowedDomains for {Provider}", email, loginProvider);
            return new(false, null, "domain_not_allowed");
        }

        // [RT-04] Rate limit — max provisions per hour per provider
        if (config.MaxProvisionsPerHour > 0 && IsRateLimitExceeded(loginProvider, config.MaxProvisionsPerHour))
        {
            logger.LogWarning("JIT: Rate limit exceeded for {Provider} ({Max}/hr)", loginProvider, config.MaxProvisionsPerHour);
            return new(false, null, "rate_limited");
        }

        // [RT-01] Check if email matches existing user — NEVER auto-link
        if (!string.IsNullOrEmpty(email))
        {
            var existingUser = await userManager.FindByEmailAsync(email);
            if (existingUser is not null)
            {
                logger.LogWarning("JIT: Email {Email} matches existing user {UserId}. Pending admin approval",
                    email, existingUser.Id);
                await auditService.LogLoginAttemptAsync(
                    existingUser.Id, email, "JIT", null, false,
                    $"JIT: email match — pending admin approval for {provider} link");
                return new(false, null, "pending_approval");
            }
        }

        // 3. Create new user + ExternalIdentity in a single transaction
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            var user = new ApplicationUser
            {
                UserName = email ?? $"{providerKey}@{loginProvider}",
                Email = email,
                EmailConfirmed = true, // IdP already verified
                FullName = fullName ?? "Unknown",
                AuthSource = MapAuthSource(provider),
                IsActive = !config.RequireApproval,
                TenantId = config.DefaultTenantId,
                CreatedAtUtc = DateTime.UtcNow,
            };

            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                logger.LogError("JIT: Failed to create user for {Provider}/{ExternalId}: {Errors}",
                    provider, providerKey,
                    string.Join(", ", createResult.Errors.Select(e => e.Description)));
                return new(false, null, "provision_failed");
            }

            // Assign default role from config
            await userManager.AddToRoleAsync(user, config.DefaultRoleName);

            // 4. Create ExternalIdentity link
            var extIdentity = ExternalIdentity.Create(
                user.Id, provider, providerKey, fullName, email, user.Id);
            await externalIdentityRepo.AddAsync(extIdentity, ct);

            await transaction.CommitAsync(ct);

            await auditService.LogLoginAttemptAsync(
                user.Id, user.Email ?? providerKey, "JIT", null, true,
                $"JIT provisioned: new user via {provider}");

            // If approval required, user created but inactive
            if (config.RequireApproval)
                return new(false, null, "pending_approval");

            return new(true, user, null);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            logger.LogError(ex, "JIT: Transaction failed for {Provider}/{ExternalId}", provider, providerKey);
            return new(false, null, "provision_failed");
        }
    }

    private async Task<JitResult> HandleExistingLink(
        ExternalIdentity existing, string? fullName, string? email, CancellationToken ct)
    {
        if (!existing.IsActive)
            return new(false, null, "deactivated");

        var linkedUser = await userManager.FindByIdAsync(existing.UserId.ToString());
        if (linkedUser is null || !linkedUser.IsActive)
            return new(false, null, "deactivated");

        // Update sync timestamp
        existing.UpdateSync(fullName, email, null, linkedUser.Id);
        await externalIdentityRepo.UpdateAsync(existing, ct);

        return new(true, linkedUser, null);
    }

    /// <summary>Simple in-memory rate limit check — resets every hour per provider scheme.</summary>
    private static bool IsRateLimitExceeded(string scheme, int maxPerHour)
    {
        var now = DateTime.UtcNow;
        var entry = _rateLimits.AddOrUpdate(
            scheme,
            _ => (1, now),
            (_, existing) =>
            {
                // Reset window if over 1 hour
                if ((now - existing.WindowStart).TotalHours >= 1)
                    return (1, now);
                return (existing.Count + 1, existing.WindowStart);
            });
        return entry.Count > maxPerHour;
    }

    /// <summary>Validates email domain against provider AllowedDomains JSON whitelist.</summary>
    private static bool IsEmailDomainAllowed(string? email, string? allowedDomainsJson)
    {
        if (string.IsNullOrEmpty(allowedDomainsJson))
            return true; // No whitelist = allow all

        var domains = System.Text.Json.JsonSerializer.Deserialize<string[]>(allowedDomainsJson);
        if (domains is null || domains.Length == 0)
            return true;

        if (string.IsNullOrEmpty(email))
            return false; // No email but whitelist exists = reject

        var emailDomain = email.Split('@').LastOrDefault()?.ToLowerInvariant();
        return emailDomain is not null &&
               domains.Any(d => d.Equals(emailDomain, StringComparison.OrdinalIgnoreCase));
    }

    private static ExternalIdentityProvider MapProvider(string loginProvider) =>
        loginProvider.ToUpperInvariant() switch
        {
            var s when s.Contains("VNEID") => ExternalIdentityProvider.VNeID,
            var s when s.Contains("LDAP") => ExternalIdentityProvider.LDAP,
            var s when s.Contains("SAML") => ExternalIdentityProvider.SAML,
            _ => ExternalIdentityProvider.SSO
        };

    private static string MapAuthSource(ExternalIdentityProvider provider) =>
        provider switch
        {
            ExternalIdentityProvider.VNeID => "VNEID",
            ExternalIdentityProvider.LDAP => "LDAP",
            _ => "SSO"
        };
}
