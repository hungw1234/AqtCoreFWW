using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// Integration tests for Viettel Mysign async-callback (async=3) webhook endpoints (W6).
///
/// Uses <see cref="WebAppFixture"/> (real API host + SQL Server via Testcontainers).
/// Tests cover the full inbound flow:
///   /auth  — Mysign exchanges client credentials for a short-lived Bearer token
///   /result — Mysign delivers signing result; BE updates SigningPendingTransaction
///
/// Test callback credentials are seeded in WebAppFixture:
///   ClientId:     "test-callback-client"
///   ClientSecret: "test-callback-secret-min-16-chars-W6"
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class ViettelMysignAsyncCallbackTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    // Auth endpoint paths
    private const string AuthPath   = "/api/signing/mysign-callback/auth";
    private const string ResultPath = "/api/signing/mysign-callback/result";

    // Seeded test credentials (must match WebAppFixture InMemoryCollection)
    private const string TestClientId     = "test-callback-client";
    private const string TestClientSecret = "test-callback-secret-min-16-chars-W6";

    // A valid 32-byte signature in base64
    private static readonly byte[] FakeSigBytes  = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly string FakeSigBase64 = Convert.ToBase64String(FakeSigBytes);

    public ViettelMysignAsyncCallbackTests(WebAppFixture app) => _app = app;

    // ── Helper: anonymous HTTP client ────────────────────────────────────────

    private HttpClient AnonClient() => _app.CreateClient();

    // ── Helper: POST /auth and return the access_token ───────────────────────

    private async Task<string> GetCallbackTokenAsync()
    {
        var client = AnonClient();
        var resp = await client.PostAsJsonAsync(AuthPath, new
        {
            grant_type    = "client_credentials",
            client_id     = TestClientId,
            client_secret = TestClientSecret,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return doc.GetProperty("data").GetProperty("access_token").GetString()!;
    }

    // ── Helper: seed a SigningPendingTransaction row directly in DB ───────────

    private async Task<SigningPendingTransaction> SeedPendingTransactionAsync(
        string txId,
        PendingTransactionStatus? finalStatus = null)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var tx = SigningPendingTransaction.Initiate(
            transactionId: txId,
            tenantId:      Guid.NewGuid(),
            userId:        Guid.NewGuid(),
            requestId:     $"req-{txId}",
            documentId:    $"doc-{txId}",
            ttl:           TimeSpan.FromHours(24));

        if (finalStatus == PendingTransactionStatus.Completed)
            tx.Complete(FakeSigBytes);
        else if (finalStatus == PendingTransactionStatus.Failed)
            tx.Fail("Pre-seeded failure");

        db.SigningPendingTransactions.Add(tx);
        await db.SaveChangesAsync();
        return tx;
    }

    // ── Helper: fetch transaction status from DB ──────────────────────────────

    private async Task<SigningPendingTransaction?> GetTransactionAsync(string txId)
    {
        // Create fresh scope to avoid stale EF tracking cache
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.SigningPendingTransactions
            .FindAsync(txId);
    }

    // ── Test 1: Auth happy path ───────────────────────────────────────────────

    [Fact]
    public async Task Auth_ValidCredentials_Returns200WithAccessToken()
    {
        // Arrange
        var client = AnonClient();
        var body = new
        {
            grant_type    = "client_credentials",
            client_id     = TestClientId,
            client_secret = TestClientSecret,
        };

        // Act
        var resp = await client.PostAsJsonAsync(AuthPath, body);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("status_code").GetString().Should().Be("00");
        doc.GetProperty("message").GetString().Should().Be("OK");

        var data = doc.GetProperty("data");
        var token = data.GetProperty("access_token").GetString();
        token.Should().NotBeNullOrEmpty("a raw base64 token should be returned");

        var expiresIn = data.GetProperty("expires_in").GetInt32();
        expiresIn.Should().Be(3600, "matches CallbackTokenTtlSeconds seeded in WebAppFixture");
    }

    // ── Test 2: Auth bad credentials ─────────────────────────────────────────

    [Fact]
    public async Task Auth_BadCredentials_Returns401()
    {
        // Arrange
        var client = AnonClient();
        var body = new
        {
            grant_type    = "client_credentials",
            client_id     = TestClientId,
            client_secret = "WRONG-SECRET",
        };

        // Act
        var resp = await client.PostAsJsonAsync(AuthPath, body);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("status_code").GetString().Should().Be("01");
    }

    // ── Test 3: Result happy path ─────────────────────────────────────────────

    [Fact]
    public async Task Result_ValidTokenAndSignature_CompletesTransaction()
    {
        // Arrange
        var txId = $"callback-tx-happy-{Guid.NewGuid():N}";

        // Step 1: Seed a pending transaction
        await SeedPendingTransactionAsync(txId);

        // Step 2: Obtain callback token
        var token = await GetCallbackTokenAsync();

        // Step 3: POST result
        var client = AnonClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var body = new
        {
            meta_data = new { request_id = "req-1", transaction_id = txId, description = "OK", status = "1" },
            data = new
            {
                action_timestamp    = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                signature_timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                results = new[] { new { document_id = "doc-1", signature = FakeSigBase64 } },
            },
        };

        var resp = await client.PostAsJsonAsync(ResultPath, body);

        // Assert HTTP
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("status_code").GetString().Should().Be("00");

        // Step 4: Verify DB
        var tx = await GetTransactionAsync(txId);
        tx.Should().NotBeNull();
        tx!.Status.Should().Be(PendingTransactionStatus.Completed);
        tx.Signature.Should().NotBeNull();
        tx.Signature.Should().Equal(FakeSigBytes, "stored signature should match what Mysign sent");
        tx.CompletedAt.Should().NotBeNull();
    }

    // ── Test 4: Idempotency — same callback twice ─────────────────────────────

    [Fact]
    public async Task Result_ReplayCallback_IsIdempotent()
    {
        // Arrange
        var txId = $"callback-tx-idempotent-{Guid.NewGuid():N}";
        await SeedPendingTransactionAsync(txId);

        var token = await GetCallbackTokenAsync();
        var client = AnonClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var body = new
        {
            meta_data = new { request_id = "req-1", transaction_id = txId, status = "1" },
            data = new
            {
                action_timestamp    = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                signature_timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                results = new[] { new { document_id = "doc-1", signature = FakeSigBase64 } },
            },
        };

        // Act — first delivery
        var resp1 = await client.PostAsJsonAsync(ResultPath, body);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);

        // We need a new token for the second call (each token is valid for 1h — reuse same)
        // The same Bearer token is still valid (not single-use); idempotency is at the tx level.
        var resp2 = await client.PostAsJsonAsync(ResultPath, body);

        // Assert — second delivery returns 200 "Already processed"
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await resp2.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("message").GetString().Should().Contain("processed");

        // DB row should still be Completed, not double-processed
        var tx = await GetTransactionAsync(txId);
        tx!.Status.Should().Be(PendingTransactionStatus.Completed);
    }

    // ── Test 5: Result with no/bad token → 401 ───────────────────────────────

    [Fact]
    public async Task Result_MissingOrBadToken_Returns401()
    {
        // No Authorization header
        var client = AnonClient();
        var body = new
        {
            meta_data = new { transaction_id = "some-tx", status = "1" },
            data = new { results = new[] { new { document_id = "d", signature = FakeSigBase64 } } },
        };

        var resp1 = await client.PostAsJsonAsync(ResultPath, body);
        resp1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Bad token
        client.DefaultRequestHeaders.Add("Authorization", "Bearer invalid-token-that-is-not-in-db");
        var resp2 = await client.PostAsJsonAsync(ResultPath, body);
        resp2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Test 6: Result with unknown txId → 200 "Already processed" ───────────

    [Fact]
    public async Task Result_UnknownTransactionId_ReturnsSilentOk()
    {
        // Arrange — no DB row seeded
        var txId   = $"unknown-tx-{Guid.NewGuid():N}";
        var token  = await GetCallbackTokenAsync();
        var client = AnonClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var body = new
        {
            meta_data = new { transaction_id = txId, status = "1" },
            data = new
            {
                results = new[] { new { document_id = "d", signature = FakeSigBase64 } },
            },
        };

        // Act
        var resp = await client.PostAsJsonAsync(ResultPath, body);

        // Assert — silent OK, no info leak about internal state
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("status_code").GetString().Should().Be("00");
        doc.GetProperty("message").GetString().Should().Contain("processed");
    }
}
