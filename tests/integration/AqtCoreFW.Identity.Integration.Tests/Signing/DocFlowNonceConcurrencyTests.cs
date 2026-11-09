using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// Persistence-level concurrency tests for the doc-flow nonce + record (W3, QĐ 742 mục 5.1).
///
/// <para>The replay/double-spend hole: <see cref="SigningDocFlowNonce.TryUse"/> is an in-memory
/// read-modify-write — two parallel requests carrying the SAME JWT both load the nonce at UseCount=0,
/// both pass the in-memory gate, and (before the rowversion token) both persisted = the nonce consumed
/// more than MaxUses, the doc-flow round-trip replayable.</para>
///
/// <para>These tests drive the race directly through two independent DbContext instances (two DI scopes,
/// separate change trackers) — no MinIO / HTTP needed — and assert the rowversion concurrency token makes
/// the consume atomic: the first SaveChanges wins, the loser throws <c>DbUpdateConcurrencyException</c>,
/// and exactly one mutation is persisted. This is the invariant the FileUploadHandlerController relies on
/// when it maps that exception to a 401.</para>
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class DocFlowNonceConcurrencyTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    private static readonly Guid _userId   = Guid.Parse("00000000-0000-0000-cafe-000000000001");
    private static readonly Guid _tenantId = Guid.Parse("00000000-0000-0000-cafe-000000000002");

    public DocFlowNonceConcurrencyTests(WebAppFixture app) => _app = app;

    private async Task<long> SeedRecordAndNonceAsync(Guid docId, Guid nonce)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var record = SigningDocFlowRecord.Initiate(
            tenantId:           _tenantId,
            docId:              docId,
            signType:           DocFlowSignType.Issued,
            originalStorageKey: $"test/{docId}/original.pdf",
            jwtNonce:           nonce,
            initiatedBy:        _userId);
        db.SigningDocFlowRecords.Add(record);
        await db.SaveChangesAsync();

        var nonceRow = SigningDocFlowNonce.Create(nonce, _tenantId, record.Id, ttlSeconds: 300);
        db.SigningDocFlowNonces.Add(nonceRow);
        await db.SaveChangesAsync();

        return record.Id;
    }

    [Fact]
    public async Task ParallelNonceConsume_SecondSaveThrowsConcurrency_AndOnlyOneIncrementPersists()
    {
        var docId = Guid.NewGuid();
        var nonce = Guid.NewGuid();
        await SeedRecordAndNonceAsync(docId, nonce);

        // Two independent contexts both load the nonce at the SAME rowversion (UseCount=0).
        using var scope1 = _app.Services.CreateScope();
        using var scope2 = _app.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var n1 = await db1.SigningDocFlowNonces.FirstAsync(x => x.Nonce == nonce);
        var n2 = await db2.SigningDocFlowNonces.FirstAsync(x => x.Nonce == nonce);

        // Both pass the in-memory gate — exactly the unguarded race the rowversion now closes.
        n1.TryUse().Should().BeTrue();
        n2.TryUse().Should().BeTrue();

        await db1.SaveChangesAsync(); // first writer wins, bumps the rowversion

        var second = async () => await db2.SaveChangesAsync();
        await second.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "the rowversion token must reject the second concurrent consume of the same nonce");

        using var verify = _app.Services.CreateScope();
        var vdb = verify.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var final = await vdb.SigningDocFlowNonces.AsNoTracking().FirstAsync(x => x.Nonce == nonce);
        final.UseCount.Should().Be(1, "only the winning transaction's increment may persist");
    }

    [Fact]
    public async Task ParallelStatusTransition_SecondSaveThrowsConcurrency()
    {
        var docId = Guid.NewGuid();
        var nonce = Guid.NewGuid();
        await SeedRecordAndNonceAsync(docId, nonce);

        using var scope1 = _app.Services.CreateScope();
        using var scope2 = _app.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var r1 = await db1.SigningDocFlowRecords.FirstAsync(x => x.DocId == docId && x.JwtNonce == nonce);
        var r2 = await db2.SigningDocFlowRecords.FirstAsync(x => x.DocId == docId && x.JwtNonce == nonce);

        // Both see Initiated and pass the in-memory pre-condition.
        r1.MarkDownloaded();
        r2.MarkDownloaded();

        await db1.SaveChangesAsync();

        var second = async () => await db2.SaveChangesAsync();
        await second.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "the rowversion token must reject a concurrent status transition (TOCTOU on Status==Initiated)");
    }

    [Fact]
    public async Task DbCheckConstraint_RejectsUseCountAboveMaxUses()
    {
        var docId = Guid.NewGuid();
        var nonce = Guid.NewGuid();
        await SeedRecordAndNonceAsync(docId, nonce);

        // Defence in depth: even a raw UPDATE that bypasses the entity must not push UseCount past MaxUses.
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // ExecuteSqlRawAsync surfaces the engine error directly as SqlException (no EF SaveChanges wrapper).
        var act = async () => await db.Database.ExecuteSqlRawAsync(
            "UPDATE [identity].[SigningDocFlowNonces] SET UseCount = MaxUses + 1 WHERE Nonce = {0}", nonce);

        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>(
            "CK_SigningDocFlowNonces_UseCount_LeMaxUses must block UseCount > MaxUses at the storage engine");
    }
}
