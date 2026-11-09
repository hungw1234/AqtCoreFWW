using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>
/// Custom UserStore that overrides <see cref="FindByEmailAsync"/> to query by
/// <see cref="ApplicationUser.NormalizedEmailHash"/> instead of the encrypted
/// NormalizedEmail column.
///
/// Once Email is AES-GCM encrypted, any WHERE-clause comparison against the ciphertext
/// column is useless (non-deterministic cipher → every ciphertext is unique even for the
/// same plaintext). Hash-based lookup is the only correct approach.
///
/// All other UserStore methods are inherited from
/// <see cref="UserStore{TUser,TRole,TContext,TKey}"/> unchanged — they do not query by email.
///
/// Registration: <c>.AddIdentity(...).AddEntityFrameworkStores&lt;IdentityDbContext&gt;()
///               .AddUserStore&lt;EmailHashedUserStore&gt;()</c>
/// The explicit <c>AddUserStore</c> call AFTER <c>AddEntityFrameworkStores</c> overrides the
/// default store registration (last registration wins in ASP.NET Core DI).
/// </summary>
public sealed class EmailHashedUserStore(
    IdentityDbContext context,
    EmailHashService emailHashService,
    IdentityErrorDescriber? describer = null)
    : UserStore<ApplicationUser, ApplicationRole, IdentityDbContext, Guid>(context, describer)
{
    /// <summary>
    /// Finds a user by normalized email using the hash column rather than the encrypted column.
    /// <paramref name="normalizedEmail"/> is the already-normalized (uppercase) value produced
    /// by ASP.NET Core Identity's <c>ILookupNormalizer</c>.
    /// </summary>
    public override async Task<ApplicationUser?> FindByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(normalizedEmail))
            return null;

        // Compute hash with Guid.Empty pepper — matches the global column written by EmailHashInterceptor.
        // Cross-tenant login path: caller doesn't know tenant upfront; global hash is the correct key.
        // Tenant-scoped lookup is a separate concern handled at the service layer (NormalizedEmailHashTenant).
        var hash = emailHashService.ComputeHash(normalizedEmail, Guid.Empty);

        return await Users
            .FirstOrDefaultAsync(u => u.NormalizedEmailHashGlobal == hash, cancellationToken);
    }
}
