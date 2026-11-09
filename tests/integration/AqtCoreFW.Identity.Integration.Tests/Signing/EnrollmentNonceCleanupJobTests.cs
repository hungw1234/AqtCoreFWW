using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.BackgroundJobs;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// Real-SQL invariants for <see cref="EnrollmentNonceCleanupJob"/> — the destructive sweep that hard-deletes
/// spent <see cref="SignerEnrollmentNonce"/> rows. Its filter is <c>ConsumedAt != null OR ExpiresAt &lt; now</c>
/// (no grace period — a spent nonce can never authorise enrollment again, so prompt deletion is correct).
/// These tests drive the real <see cref="EnrollmentNonceCleanupJob.RunOnceAsync"/> (internal, reached via
/// InternalsVisibleTo) rather than re-implementing the predicate — re-implementation would let the test pass
/// while the job drifts (false-green guard). TC-3 pins the "never delete a live nonce" boundary so the suite
/// cannot pass vacuously by deleting everything.
///
/// <para>The factory <see cref="SignerEnrollmentNonce.Issue"/> only ever produces a future, unconsumed nonce,
/// so the seed helper back-dates <c>ExpiresAt</c> through the EF change tracker and consumes via the domain
/// method to place a row on either side of the filter.</para>
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class EnrollmentNonceCleanupJobTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public EnrollmentNonceCleanupJobTests(WebAppFixture app) => _app = app;

    private EnrollmentNonceCleanupJob NewJob() => new(
        _app.Services,
        NullLogger<EnrollmentNonceCleanupJob>.Instance);

    // ─── TC-1: a consumed nonce is deleted even though its ExpiresAt is still in the future ───

    [Fact]
    public async Task RunOnce_ConsumedNonce_IsDeleted()
    {
        // Consumed (single-use done) but ExpiresAt 5 min ahead — the ConsumedAt branch must still delete it.
        var tenantId = await SeedTenantAsync();
        var id = await SeedNonceAsync(tenantId, expiresAt: DateTime.UtcNow.AddMinutes(5), consumed: true);

        await NewJob().RunOnceAsync();

        (await NonceExistsAsync(id)).Should().BeFalse(
            "a consumed (single-use spent) nonce can never authorise enrollment again and must be swept");
    }

    // ─── TC-2: an expired, unconsumed nonce is deleted ───

    [Fact]
    public async Task RunOnce_ExpiredNonce_IsDeleted()
    {
        // Past ExpiresAt, never consumed — already rejected on read, so the sweep removes it.
        var tenantId = await SeedTenantAsync();
        var id = await SeedNonceAsync(tenantId, expiresAt: DateTime.UtcNow.AddMinutes(-5), consumed: false);

        await NewJob().RunOnceAsync();

        (await NonceExistsAsync(id)).Should().BeFalse(
            "a nonce past its TTL is dead weight and must be swept");
    }

    // ─── TC-3: a live (unconsumed, future-expiry) nonce is NEVER deleted (anti-vacuous guard) ───

    [Fact]
    public async Task RunOnce_ActiveNonce_IsRetained()
    {
        // Within TTL and unconsumed — an in-flight enrollment challenge must survive the sweep.
        var tenantId = await SeedTenantAsync();
        var id = await SeedNonceAsync(tenantId, expiresAt: DateTime.UtcNow.AddMinutes(5), consumed: false);

        await NewJob().RunOnceAsync();

        (await NonceExistsAsync(id)).Should().BeTrue(
            "a live, unconsumed nonce within its TTL must never be deleted by the cleanup sweep");
    }

    // ─── TC-4: the per-tenant sweep deletes spent nonces across EVERY tenant (least-priv iteration guard) ───

    [Fact]
    public async Task RunOnce_MultipleTenants_DeletesEachTenantsSpentNonce()
    {
        // Two tenants, each with a spent (consumed) nonce. The per-tenant sweep must delete BOTH — proving
        // it pins SESSION_CONTEXT and iterates the whole tenant set, not just a single db_owner pass.
        var tenantA = await SeedTenantAsync();
        var tenantB = await SeedTenantAsync();
        var idA = await SeedNonceAsync(tenantA, expiresAt: DateTime.UtcNow.AddMinutes(5), consumed: true);
        var idB = await SeedNonceAsync(tenantB, expiresAt: DateTime.UtcNow.AddMinutes(5), consumed: true);

        await NewJob().RunOnceAsync();

        (await NonceExistsAsync(idA)).Should().BeFalse();
        (await NonceExistsAsync(idB)).Should().BeFalse(
            "the per-tenant sweep must reach every tenant's spent nonces, not only the first");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a real Tenant row (via the domain factory) and returns its PublicId — the discriminator the
    /// per-tenant sweep enumerates from the non-RLS Tenants registry. Domain events are cleared before save
    /// so seeding does not trigger tenant-provisioning side-effects in the test.
    /// </summary>
    private async Task<Guid> SeedTenantAsync()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var tenant = Tenant.Create(
            gen,
            name: $"Nonce Cleanup Tenant {Guid.NewGuid():N}",
            code: $"NCL{Guid.NewGuid():N}"[..12],
            contactEmail: null,
            actorId: Guid.Empty);
        tenant.ClearDomainEvents();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        return tenant.PublicId;
    }

    /// <summary>
    /// Seeds an enrollment nonce, optionally consumed, with a back-dated/future ExpiresAt. ExpiresAt is set
    /// through the EF change tracker because the domain factory only ever produces a future value.
    /// </summary>
    private async Task<Guid> SeedNonceAsync(Guid tenantId, DateTime expiresAt, bool consumed)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var nonce = SignerEnrollmentNonce.Issue(
            gen: gen, tenantId: tenantId, userId: Guid.NewGuid(), challenge: new byte[16]);
        if (consumed)
            nonce.Consume(DateTime.UtcNow);

        db.SignerEnrollmentNonces.Add(nonce);
        await db.SaveChangesAsync();

        // Override ExpiresAt — the factory only ever sets a future value.
        db.Entry(nonce).Property(n => n.ExpiresAt).CurrentValue = expiresAt;
        await db.SaveChangesAsync();

        return nonce.Id;
    }

    private async Task<bool> NonceExistsAsync(Guid id)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.SignerEnrollmentNonces.AsNoTracking().AnyAsync(n => n.Id == id);
    }
}
