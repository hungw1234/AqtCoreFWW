using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// Real-SQL invariants for the PAdES async-poll completion that the in-memory unit tests cannot prove:
/// <list type="number">
///   <item><b>RowVersion single-winner</b> — two concurrent completion sweeps loading the SAME row end with exactly
///     one Completed transition; the loser's save throws <see cref="DbUpdateConcurrencyException"/> (so the poll
///     job's per-row catch isolates it and never double-flips). This is the optimistic-concurrency guard the
///     in-memory provider does not enforce.</item>
///   <item><b>Completion filter</b> — the sweep query (<c>Status == Initiated &amp;&amp; PadesPlaceholderStorageKey
///     != null</c>) selects only PAdES rows and never touches async-callback (non-PAdES) PendingTransactions.</item>
/// </list>
/// Uses <see cref="WebAppFixture"/> (real API host + SQL Server via Testcontainers). The poll job itself is stripped
/// in the Testing environment, so these tests drive the persistence invariants directly.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class PadesCompletionConcurrencyTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public PadesCompletionConcurrencyTests(WebAppFixture app) => _app = app;

    /// <summary>Seeds an Initiated transaction carrying PAdES resume state (the rows the completion sweep targets).</summary>
    private async Task<string> SeedInitiatedPadesAsync()
    {
        string txId = $"pades-cmp-{Guid.NewGuid():N}";
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var tx = SigningPendingTransaction.Initiate(
            transactionId: txId, tenantId: Guid.NewGuid(), userId: Guid.NewGuid(),
            requestId: $"req-{txId}", documentId: $"doc-{txId}", ttl: TimeSpan.FromHours(24));
        // Resume-state values need only be well-formed (non-empty) for the persistence invariants under test;
        // the cryptographic splice is proven by PadesCompletionServiceTests, not here.
        tx.AttachPadesResumeState("tenant/k.placeholder.pdf", 790, 32768, [1, 2, 3], [4, 5, 6]);

        db.SigningPendingTransactions.Add(tx);
        await db.SaveChangesAsync();
        return txId;
    }

    [Fact]
    public async Task CompletePades_ConcurrentSaves_RowVersionElectsSingleWinner()
    {
        var txId = await SeedInitiatedPadesAsync();

        // Two independent scopes load the SAME row — each tracks its own copy with the same RowVersion.
        using var scopeA = _app.Services.CreateScope();
        using var scopeB = _app.Services.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var txA = await dbA.SigningPendingTransactions.FirstAsync(t => t.TransactionId == txId);
        var txB = await dbB.SigningPendingTransactions.FirstAsync(t => t.TransactionId == txId);

        // Both attempt to finalize, as two overlapping sweeps would. The keys here are arbitrary distinct sentinels
        // chosen to prove the ROW-level single-winner guard in isolation; the real completion path derives a
        // deterministic key from the transaction identity (so overlapping replica writes overwrite one idempotent
        // blob — proven in PadesCompletionServiceTests). RowVersion is the row guard, not the blob dedup.
        txA.CompletePades([7, 7], $"{txA.TenantId}/winner.signed.pdf");
        txB.CompletePades([9, 9], $"{txB.TenantId}/loser.signed.pdf");

        // First save wins and bumps RowVersion.
        await dbA.SaveChangesAsync();

        // Second save targets the stale RowVersion → optimistic-concurrency failure (the job swallows this per-row).
        var loserSave = async () => await dbB.SaveChangesAsync();
        await loserSave.Should().ThrowAsync<DbUpdateConcurrencyException>(
            because: "RowVersion must elect a single winner so the row is never double-completed");

        // The persisted row reflects exactly the winner — one Completed transition, the winner's key.
        using var verifyScope = _app.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var persisted = await verifyDb.SigningPendingTransactions.FirstAsync(t => t.TransactionId == txId);
        persisted.Status.Should().Be(PendingTransactionStatus.Completed);
        persisted.PadesSignedStorageKey.Should().EndWith("winner.signed.pdf");
    }

    [Fact]
    public async Task CompletionFilter_SelectsOnlyPadesRows_ExcludesAsyncCallbackTransactions()
    {
        // A PAdES row (has resume state) and an async-callback row (no resume state) in the same Initiated state.
        var padesTxId = await SeedInitiatedPadesAsync();

        string callbackTxId = $"async-cb-{Guid.NewGuid():N}";
        using (var seedScope = _app.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var callbackTx = SigningPendingTransaction.Initiate(
                transactionId: callbackTxId, tenantId: Guid.NewGuid(), userId: Guid.NewGuid(),
                requestId: "req-cb", documentId: "doc-cb", ttl: TimeSpan.FromHours(24));
            // No AttachPadesResumeState → PadesPlaceholderStorageKey stays null (the W6 async-callback shape).
            seedDb.SigningPendingTransactions.Add(callbackTx);
            await seedDb.SaveChangesAsync();
        }

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // Mirror PadesCompletionPollJob.RunOnceAsync's exact filter.
        var swept = await db.SigningPendingTransactions
            .Where(t => t.Status == PendingTransactionStatus.Initiated && t.PadesPlaceholderStorageKey != null)
            .Select(t => t.TransactionId)
            .ToListAsync();

        swept.Should().Contain(padesTxId, because: "an Initiated PAdES row carries resume state and must be swept");
        swept.Should().NotContain(callbackTxId,
            because: "an async-callback row has no PAdES resume state and must never be touched by the completion sweep");
    }
}
