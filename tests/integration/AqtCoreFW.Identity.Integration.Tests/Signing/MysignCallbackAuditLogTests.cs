using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// Asserts the Mysign async-callback completion is audited EXACTLY ONCE per signing event (H1).
///
/// The completion previously ran in a DbContext-direct controller outside the MediatR pipeline, so it
/// produced no audit row at all (ADR-004 gap). It now dispatches <c>CompleteMysignCallbackCommand</c>,
/// which is <c>[SuppressAutoAudit]</c> and emits a structured row ONLY on a real terminal transition.
/// This test proves: (a) a completion writes one row (count 0→1), and (b) an idempotent re-delivery
/// writes NO extra row — so Mysign retries cannot pollute the HMAC audit chain.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class MysignCallbackAuditLogTests : IClassFixture<WebAppFixture>
{
    private const string AuthPath   = "/api/signing/mysign-callback/auth";
    private const string ResultPath = "/api/signing/mysign-callback/result";
    private const string TestClientId     = "test-callback-client";
    private const string TestClientSecret = "test-callback-secret-min-16-chars-W6";

    private static readonly byte[] SigBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly string SigBase64 = Convert.ToBase64String(SigBytes);

    private readonly WebAppFixture _app;

    public MysignCallbackAuditLogTests(WebAppFixture app) => _app = app;

    [Fact]
    public async Task Completion_EmitsExactlyOneAuditRow_AndReplayEmitsNone()
    {
        var txId = $"audit-tx-{Guid.NewGuid():N}";
        var tenantId = Guid.NewGuid();
        await SeedPendingTransactionAsync(txId, tenantId);

        var auditDb = GetAuditDb();
        var countBefore = await CountAuditRowsAsync(auditDb, txId);
        countBefore.Should().Be(0, "no audit row should exist for a freshly-seeded transaction");

        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {await GetCallbackTokenAsync()}");
        var body = new
        {
            meta_data = new { request_id = "req-1", transaction_id = txId, status = "1" },
            data = new
            {
                action_timestamp    = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                signature_timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                results = new[] { new { document_id = "doc-1", signature = SigBase64 } },
            },
        };

        // First delivery — transitions Initiated → Completed → one audit row.
        (await client.PostAsJsonAsync(ResultPath, body)).StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await GetAuditRowsAsync(GetAuditDb(), txId);
        rows.Should().ContainSingle(
            "CompleteMysignCallbackCommand must emit exactly one audit row on the terminal transition");
        rows[0].TenantId.Should().Be(tenantId,
            "the anonymous callback must attribute the row to the signing tenant (from the transaction), " +
            "not the null/system tenant — so it lands on that tenant's HMAC chain");

        // Re-delivery (idempotent no-op) — must NOT add a second row (H1: exactly once per event).
        (await client.PostAsJsonAsync(ResultPath, body)).StatusCode.Should().Be(HttpStatusCode.OK);
        var countAfterReplay = await CountAuditRowsAsync(GetAuditDb(), txId);
        countAfterReplay.Should().Be(1,
            "an idempotent re-delivery is a 200 no-op and must emit NO additional audit row");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Task<int> CountAuditRowsAsync(AuditDbContext auditDb, string txId) =>
        auditDb.AuditLogEntries
            .Where(e => e.ResourceType == "SigningPendingTransaction" && e.ResourceId == txId)
            .CountAsync();

    private static Task<List<AuditLogEntry>> GetAuditRowsAsync(AuditDbContext auditDb, string txId) =>
        auditDb.AuditLogEntries
            .Where(e => e.ResourceType == "SigningPendingTransaction" && e.ResourceId == txId)
            .ToListAsync();

    private async Task SeedPendingTransactionAsync(string txId, Guid tenantId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.SigningPendingTransactions.Add(SigningPendingTransaction.Initiate(
            transactionId: txId,
            tenantId:      tenantId,
            userId:        Guid.NewGuid(),
            requestId:     $"req-{txId}",
            documentId:    $"doc-{txId}",
            ttl:           TimeSpan.FromHours(24)));
        await db.SaveChangesAsync();
    }

    private async Task<string> GetCallbackTokenAsync()
    {
        var resp = await _app.CreateClient().PostAsJsonAsync(AuthPath, new
        {
            grant_type    = "client_credentials",
            client_id     = TestClientId,
            client_secret = TestClientSecret,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return doc.GetProperty("data").GetProperty("access_token").GetString()!;
    }

    private AuditDbContext GetAuditDb()
    {
        var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    }
}
