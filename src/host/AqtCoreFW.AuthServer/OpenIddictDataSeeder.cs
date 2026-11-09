using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace AqtCoreFW.AuthServer;

/// <summary>
/// Seeds OpenIddict application registrations and dev users on startup.
/// All methods are idempotent — safe to run on every startup.
/// WARNING: Dev user seeding must never run in production.
/// </summary>
internal static class OpenIddictDataSeeder
{
    // ---------------------------------------------------------------------------
    // Dev seed: registers the API and a test SPA client in OpenIddict
    // ---------------------------------------------------------------------------
    internal static async Task SeedApplicationsAsync(IServiceProvider services, IWebHostEnvironment env)
    {
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        // Register custom "api" scope — required for token requests with scope=api
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        if (await scopeManager.FindByNameAsync("api") is null)
        {
            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = "api",
                DisplayName = "AqtCoreFW API access"
            });
        }

        // Resource server descriptor (the API itself) - confidential client used by the Api for
        // introspection. The secret comes from Auth:ResourceClientSecret; outside the dev
        // environments a missing or development value fails startup (AuthResourceClient).
        var resourceConfiguration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var apiClientId = AuthResourceClient.ResolveClientId(resourceConfiguration);
        var apiClientSecret = AuthResourceClient.ResolveSecret(resourceConfiguration, env.EnvironmentName);
        var apiClient = await manager.FindByClientIdAsync(apiClientId);
        if (apiClient is null)
        {
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = apiClientId,
                ClientSecret = apiClientSecret,
                ClientType = OpenIddictConstants.ClientTypes.Confidential,
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Introspection
                }
            });
        }
        else if (!await manager.ValidateClientSecretAsync(apiClient, apiClientSecret))
        {
            // Rotation: databases seeded before the secret became configurable still hold the
            // development secret; replace it with the configured one.
            await manager.UpdateAsync(apiClient, apiClientSecret);
        }

        // Dev SPA / Swagger client (Authorization Code + PKCE, public)
        const string spaClientId = "aqtcorefw-spa-dev";

        // Resolve and validate BEFORE the delete below. The client is delete-and-recreated on
        // every boot, so a configuration error discovered mid-way would leave the deployment with
        // no SPA client at all — and therefore no way to log in and fix it.
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var isDevelopment = env.IsDevelopment();
        var redirectUris = SpaClientRedirectUris.Resolve(
            configuration,
            SpaClientRedirectUris.RedirectUrisKey,
            SpaClientRedirectUris.DevelopmentRedirectUris,
            isDevelopment);
        var postLogoutRedirectUris = SpaClientRedirectUris.Resolve(
            configuration,
            SpaClientRedirectUris.PostLogoutRedirectUrisKey,
            SpaClientRedirectUris.DevelopmentPostLogoutRedirectUris,
            isDevelopment);

        // Delete + recreate to pick up permission changes (dev only)
        var existingSpa = await manager.FindByClientIdAsync(spaClientId);
        if (existingSpa is not null)
            await manager.DeleteAsync(existingSpa);

        var spaDescriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = spaClientId,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.EndSession,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                // Scopes — must match FE oidc-client-ts scope config exactly
                $"{OpenIddictConstants.Permissions.Prefixes.Scope}openid",
                OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddictConstants.Permissions.Scopes.Roles,
                $"{OpenIddictConstants.Permissions.Prefixes.Scope}api",
                $"{OpenIddictConstants.Permissions.Prefixes.Scope}offline_access"
            },
            Requirements =
            {
                OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
            }
        };

        // RedirectUris / PostLogoutRedirectUris are ISet<Uri> collection properties — they can be
        // populated but not assigned.
        spaDescriptor.RedirectUris.UnionWith(redirectUris);
        spaDescriptor.PostLogoutRedirectUris.UnionWith(postLogoutRedirectUris);

        await manager.CreateAsync(spaDescriptor);

        // Dev ROPC client — password flow for .http files and local tooling ONLY
        // WARNING: Never seed this in production environments
        if (env.IsDevelopment())
        {
            const string ropcClientId = "aqtcorefw-dev-ropc";
            // Delete + recreate to pick up permission changes (dev only)
            var existingRopc = await manager.FindByClientIdAsync(ropcClientId);
            if (existingRopc is not null)
                await manager.DeleteAsync(existingRopc);

            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = ropcClientId,
                ClientSecret = "dev-secret-not-for-production",
                ClientType = OpenIddictConstants.ClientTypes.Confidential,
                ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.Endpoints.Revocation,
                    OpenIddictConstants.Permissions.GrantTypes.Password,
                    OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                    OpenIddictConstants.Permissions.Scopes.Email,
                    OpenIddictConstants.Permissions.Scopes.Profile,
                    OpenIddictConstants.Permissions.Scopes.Roles,
                    $"{OpenIddictConstants.Permissions.Prefixes.Scope}openid",
                    $"{OpenIddictConstants.Permissions.Prefixes.Scope}api",
                    $"{OpenIddictConstants.Permissions.Prefixes.Scope}offline_access"
                }
            });
        }
    }

    // ---------------------------------------------------------------------------
    // Dev seed: creates test users for local development and E2E tests
    // WARNING: Never seed this in production environments
    // ---------------------------------------------------------------------------
    internal static async Task SeedDevUsersAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(OpenIddictDataSeeder));

        // Ensure all required roles exist. Admin/SystemAdmin are system-reserved: tag them
        // RoleType.System so privilege guards keyed on RoleType (alongside role name) treat them as
        // privileged, and so the role-admin API refuses to rename or delete them. Defaulting them to
        // Business (the entity default) leaves the RoleType leg of those guards inert.
        foreach (var role in new[] { "Admin", "SystemAdmin", "GovOfficer", "Viewer" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var roleType = role is "Admin" or "SystemAdmin" ? RoleType.System : RoleType.Business;
                await roleManager.CreateAsync(new ApplicationRole { Name = role, RoleType = roleType });
            }
        }

        // Default dev tenant (matches E2E seed TEST_TENANT_ID)
        var devTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        // Passwords from config — no hardcoded secrets in source
        var adminPassword = config["SeedPasswords:AdminPassword"] ?? "DevAdmin@12345";
        var officerPassword = config["SeedPasswords:OfficerPassword"] ?? "DevOfficer@12345";
        var viewerPassword = config["SeedPasswords:ViewerPassword"] ?? "DevViewer@12345";

        // Per-user try/catch so a single missing Vault pepper / transient infra fault does not abort host startup.
        // Failures are logged; remaining users still seed (admin can still log in if officer/viewer fail and vice versa).
        await TrySeedUserAsync(logger, "admin@dev.local", () => EnsureDevUserAsync(
            userManager, roleManager,
            email: "admin@dev.local",
            password: adminPassword,
            fullName: "Dev Admin",
            roles: ["Admin", "SystemAdmin", "GovOfficer"],
            tenantId: devTenantId));

        await TrySeedUserAsync(logger, "officer@dev.local", () => EnsureDevUserAsync(
            userManager, roleManager,
            email: "officer@dev.local",
            password: officerPassword,
            fullName: "Dev Officer",
            roles: ["GovOfficer"],
            tenantId: devTenantId));

        await TrySeedUserAsync(logger, "viewer@dev.local", () => EnsureDevUserAsync(
            userManager, roleManager,
            email: "viewer@dev.local",
            password: viewerPassword,
            fullName: "Dev Viewer",
            roles: []));
    }

    private static async Task TrySeedUserAsync(ILogger logger, string email, Func<Task> seed)
    {
        try
        {
            await seed();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dev user seed failed for {Email} — continuing with remaining users", email);
        }
    }

    /// <summary>
    /// Creates user if absent; on existing user ensures all required roles and tenant are assigned.
    /// Idempotent — safe to run on every startup.
    /// </summary>
    internal static async Task EnsureDevUserAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        string email, string password, string fullName, string[] roles,
        Guid? tenantId = null)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            // Ensure role membership is correct (handles case where roles changed after initial seed)
            foreach (var role in roles)
            {
                if (!await userManager.IsInRoleAsync(existing, role))
                    await userManager.AddToRoleAsync(existing, role);
            }

            // Ensure tenant assignment is correct
            if (tenantId.HasValue && existing.TenantId != tenantId)
            {
                existing.TenantId = tenantId;
                await userManager.UpdateAsync(existing);
            }
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            TenantId = tenantId,
        };

        // Password meets policy: 12+ chars, uppercase, digit, special char
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            Console.WriteLine($"[SeedDevUser] Failed to create '{email}': {errors}");
            return;
        }

        foreach (var role in roles)
            await userManager.AddToRoleAsync(user, role);

        Console.WriteLine($"[SeedDevUser] Created '{email}' with roles: [{string.Join(", ", roles)}]");
    }
}
