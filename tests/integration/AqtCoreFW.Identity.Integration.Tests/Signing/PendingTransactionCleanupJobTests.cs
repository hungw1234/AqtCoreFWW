using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.BackgroundJobs;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// Real-SQL invariants for <see cref="PendingTransactionCleanupJob"/> — the destructive sweep that flips overdue
/// <see cref="SigningPendingTransaction"/> rows to Expired. The job had no tests; its filter is purely time-based
/// (<c>Status == Initiated AND ExpiresAt &lt; UtcNow - graceMinutes</c>) and never inspects the Mysign poll
/// sub-status (e.g. <c>4003</c> "CA processing"), so the ONLY thing protecting a long-running in-flight sign from
/// premature expiry is its <c>ExpiresAt</c> (TTL, ~24h). These tests pin exactly that boundary by driving the real
/// <see cref="PendingTransactionCleanupJob.RunOnceAsync"/> (internal, reached via InternalsVisibleTo) rather than
/// re-implementing the filter — re-implementation would let the test pass while the job drifts (false-green guard).
///
/// Grace period is 60 minutes in the Testing config (<see cref="WebAppFixture"/>:
/// <c>Signing:Providers:ViettelMysign:PendingTxCleanupGraceMinutes = 60</c>).
///
/// <para>The factory <see cref="SigningPendingTransaction.Initiate"/> always sets <c>ExpiresAt</c> in the future
/// (ttl must be positive), so the seed helper backdates <c>ExpiresAt</c> through the EF change tracker to place a
/// row before/after the grace cutoff.</para>
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class PendingTransactionCleanupJobTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public PendingTransactionCleanupJobTests(WebAppFixture app) => _app = app;

    private PendingTransactionCleanupJob NewJob() => new(
        _app.Services,
        _app.Services.GetRequiredService<IOptionsMonitor<ViettelMysignOptions>>(),
        NullLogger<PendingTransactionCleanupJob>.Instance);

    // ─── TC-1: overdue Initiated row IS expired (guards against a vacuous "nothing ever expires" test) ───

    [Fact]
    public async Task RunOnce_OverdueInitiated_MarksExpired()
    {
        // ExpiresAt 2h in the past — well beyond the 60-minute grace cutoff.
        var tenantId = await SeedTenantAsync();
        var txId = await SeedTransactionAsync(tenantId, expiresAt: DateTime.UtcNow.AddHours(-2));

        await NewJob().RunOnceAsync();

        var status = await ReadStatusAsync(txId);
        status.Should().Be(PendingTransactionStatus.Expired,
            "an Initiated transaction past ExpiresAt + grace must be swept to Expired");
    }

    // ─── TC-2: in-flight sign within TTL is NEVER expired early (the row's real "4003 not premature" invariant) ─

    [Fact]
    public async Task RunOnce_FutureExpiry_LeavesInitiated()
    {
        // A long-running CA-processing sign: still Initiated, ExpiresAt 24h ahead (the normal TTL).
        var tenantId = await SeedTenantAsync();
        var txId = await SeedTransactionAsync(tenantId, expiresAt: DateTime.UtcNow.AddHours(24));

        await NewJob().RunOnceAsync();

        var status = await ReadStatusAsync(txId);
        status.Should().Be(PendingTransactionStatus.Initiated,
            "a transaction still within its TTL must never be expired early, regardless of provider poll sub-status");
    }

    // ─── TC-3: a row just past ExpiresAt but inside the grace window is NOT yet expired ───

    [Fact]
    public async Task RunOnce_WithinGracePeriod_LeavesInitiated()
    {
        // Expired 10 minutes ago — inside the 60-minute grace, so the sweep must not touch it yet.
        var tenantId = await SeedTenantAsync();
        var txId = await SeedTransactionAsync(tenantId, expiresAt: DateTime.UtcNow.AddMinutes(-10));

        await NewJob().RunOnceAsync();

        var status = await ReadStatusAsync(txId);
        status.Should().Be(PendingTransactionStatus.Initiated,
            "the grace period absorbs a callback that arrives just after ExpiresAt — no expiry inside the window");
    }

    // ─── TC-4: a terminal (Completed) row is never clobbered, even past expiry ───

    [Fact]
    public async Task RunOnce_CompletedRowPastExpiry_StaysCompleted()
    {
        // Completed + ExpiresAt long past: the filter selects only Initiated rows, so this must be left intact.
        var tenantId = await SeedTenantAsync();
        var txId = await SeedTransactionAsync(tenantId, expiresAt: DateTime.UtcNow.AddHours(-2), completed: true);

        await NewJob().RunOnceAsync();

        var status = await ReadStatusAsync(txId);
        status.Should().Be(PendingTransactionStatus.Completed,
            "a finished signature must never be flipped to Expired by the cleanup sweep");
    }

    // ─── TC-5: the per-tenant sweep covers EVERY tenant, not just the first (least-priv iteration guard) ───

    [Fact]
    public async Task RunOnce_MultipleTenants_ExpiresEachTenantsOverdueRow()
    {
        // Two distinct tenants, each with an overdue Initiated row. The per-tenant sweep (pinning
        // SESSION_CONTEXT per tenant) must expire BOTH — proving it iterates the whole tenant set rather
        // than relying on a single db_owner all-tenant pass.
        var tenantA = await SeedTenantAsync();
        var tenantB = await SeedTenantAsync();
        var txA = await SeedTransactionAsync(tenantA, expiresAt: DateTime.UtcNow.AddHours(-2));
        var txB = await SeedTransactionAsync(tenantB, expiresAt: DateTime.UtcNow.AddHours(-2));

        await NewJob().RunOnceAsync();

        (await ReadStatusAsync(txA)).Should().Be(PendingTransactionStatus.Expired);
        (await ReadStatusAsync(txB)).Should().Be(PendingTransactionStatus.Expired,
            "the per-tenant sweep must reach every tenant's overdue rows, not only the first");
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
            name: $"Cleanup Test Tenant {Guid.NewGuid():N}",
            code: $"CLN{Guid.NewGuid():N}"[..12],
            contactEmail: null,
            actorId: Guid.Empty);
        tenant.ClearDomainEvents();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        return tenant.PublicId;
    }

    /// <summary>
    /// Seeds a transaction with a back-dated (or future) ExpiresAt. When <paramref name="completed"/> is true the
    /// row is finalized via CompletePades. ExpiresAt is set through the EF change tracker because the domain factory
    /// only ever produces a future expiry.
    /// </summary>
    private async Task<string> SeedTransactionAsync(Guid tenantId, DateTime expiresAt, bool completed = false)
    {
        var txId = $"cleanup-{Guid.NewGuid():N}";
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var tx = SigningPendingTransaction.Initiate(
            transactionId: txId, tenantId: tenantId, userId: Guid.NewGuid(),
            requestId: $"req-{txId}", documentId: $"doc-{txId}", ttl: TimeSpan.FromHours(24));
        if (completed)
            tx.CompletePades(new byte[] { 1, 2, 3, 4 }, $"{tx.TenantId}/cleanup.signed.pdf");

        db.SigningPendingTransactions.Add(tx);
        await db.SaveChangesAsync();

        // Backdate (or push out) ExpiresAt — the factory only ever sets a future value.
        db.Entry(tx).Property(t => t.ExpiresAt).CurrentValue = expiresAt;
        await db.SaveChangesAsync();

        return txId;
    }

    private async Task<PendingTransactionStatus> ReadStatusAsync(string txId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var tx = await db.SigningPendingTransactions.AsNoTracking()
            .FirstAsync(t => t.TransactionId == txId);
        return tx.Status;
    }
}
