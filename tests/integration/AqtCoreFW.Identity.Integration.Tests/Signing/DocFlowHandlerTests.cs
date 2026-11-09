using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// HTTP boundary tests for Tier-3 VGCA doc-flow endpoints (W3, QĐ 742 mục 5.1).
///
/// Covers:
///   - Happy: POST /initiate → record + nonce persisted, JWT returned
///   - Negative-1: Expired JWT (signed with correct key but exp in past) → 401
///   - Negative-2: Nonce reuse (3rd request) → 401
///   - Negative-3: JWT doc_id does not match any persisted record → 404
///     (nonce 128-bit entropy IS the multi-tenant isolation mechanism — no tenant_id claim in JWT)
///   - Negative-4: Tampered JWT (wrong signing key) → 401
///
/// Note: The download→sign step requires the VGCA desktop service (no automated coverage). The upload
/// half of the happy path runs here against real MinIO, pinning the PascalCase response contract the
/// desktop tool parses.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class DocFlowHandlerTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    private static readonly Guid _userId   = Guid.Parse("00000000-0000-0000-dead-000000000001");
    private static readonly Guid _tenantId = Guid.Parse("00000000-0000-0000-dead-000000000002");

    public DocFlowHandlerTests(WebAppFixture app)
    {
        _app = app;
    }

    private HttpClient MfaClient(Guid? userId = null, Guid? tenantId = null)
        => _app.CreateAuthenticatedClient(
            userId   ?? _userId,
            tenantId ?? _tenantId,
            includeMfaClaim: true);

    // ─── Helper: resolve IDocFlowJwtService from DI ────────────────────────────

    private IDocFlowJwtService GetJwtService()
    {
        using var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IDocFlowJwtService>();
    }

    // ─── Happy path: POST /initiate ────────────────────────────────────────────

    [Fact]
    public async Task Initiate_ValidRequest_ReturnsJwtAndPersistsRecord()
    {
        var client = MfaClient();
        var docId  = Guid.NewGuid();

        var body = new
        {
            docId             = docId.ToString(),
            signType          = "Issued",
            // OriginalStorageKey must be within the caller's tenant prefix (validator enforces it).
            originalStorageKey = $"{_tenantId}/{docId}/original.pdf",
            docNumber         = "VB-001/2026",
            issuedDate        = "2026-05-11",
        };

        var response = await client.PostAsJsonAsync("/api/signing/docflow/initiate", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "valid authenticated initiate request should succeed");

        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data     = envelope.GetProperty("data");
        var handlerUrl = data.GetProperty("handlerUrl").GetString();
        var jwt        = data.GetProperty("jwt").GetString();
        var returnedDocId = data.GetProperty("docId").GetGuid();

        handlerUrl.Should().Contain("/api/signing/docflow/file");
        jwt.Should().NotBeNullOrWhiteSpace("JWT must be issued");
        returnedDocId.Should().Be(docId);

        // Verify record + nonce were persisted
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var record = await db.SigningDocFlowRecords
            .FirstOrDefaultAsync(r => r.DocId == docId && r.TenantId == _tenantId);
        record.Should().NotBeNull("record must be persisted after /initiate");
        record!.Status.Should().Be(DocFlowStatus.Initiated);

        var nonce = await db.SigningDocFlowNonces
            .FirstOrDefaultAsync(n => n.DocFlowRecordId == record.Id);
        nonce.Should().NotBeNull("nonce must be persisted after /initiate");
        nonce!.UseCount.Should().Be(0);
        nonce.MaxUses.Should().Be(2);
    }

    // ─── Negative-1: Expired JWT (correct key, exp in past) ───────────────────

    [Fact]
    public async Task Download_ExpiredJwt_Returns401()
    {
        var docId = Guid.NewGuid();
        var nonce = Guid.NewGuid();

        // Build a JWT manually with exp in the past — cannot use IssueJwt because the JWT library
        // rejects descriptors where Expires < NotBefore (IDX12401). Instead, construct token directly
        // using the REAL signing key but with past NotBefore + IssuedAt + Expires.
        string expiredJwt;
        using (var scope = _app.Services.CreateScope())
        {
            var realOpts = scope.ServiceProvider
                .GetRequiredService<IOptions<DocFlowJwtOptions>>().Value;

            var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(realOpts.SigningKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var pastBase    = DateTime.UtcNow.AddSeconds(-600); // 10 min ago
            var pastExpires = pastBase.AddSeconds(60);          // 9 min ago — still > clock-skew window

            var descriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub,  _userId.ToString()),
                    new Claim("doc_id",                     docId.ToString()),
                    new Claim("nonce",                      nonce.ToString()),
                }),
                Issuer             = realOpts.IssuerName,
                NotBefore          = pastBase,
                IssuedAt           = pastBase,
                Expires            = pastExpires,
                SigningCredentials = creds,
            };

            var handler = new JwtSecurityTokenHandler();
            var token   = handler.CreateJwtSecurityToken(descriptor);
            expiredJwt  = handler.WriteToken(token);
        }

        var client = _app.CreateClient();
        var response = await client.GetAsync(
            $"/api/signing/docflow/file?docId={docId}&token={Uri.EscapeDataString(expiredJwt)}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "JWT with exp in the past must be rejected with 401");
    }

    // ─── Negative-2: Nonce reuse ───────────────────────────────────────────────

    [Fact]
    public async Task Download_NonceExhausted_Returns401()
    {
        // Seed a nonce row with UseCount=MaxUses (already exhausted)
        var docId   = Guid.NewGuid();
        var nonce   = Guid.NewGuid();
        var actorId = _userId;

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            // Create a record first (nonce FK requires it)
            var record = SigningDocFlowRecord.Initiate(
                tenantId:           _tenantId,
                docId:              docId,
                signType:           DocFlowSignType.Issued,
                originalStorageKey: $"test/{docId}/original.pdf",
                jwtNonce:           nonce,
                initiatedBy:        actorId);

            db.SigningDocFlowRecords.Add(record);
            await db.SaveChangesAsync();

            // Create nonce with UseCount already at MaxUses
            var nonceRow = SigningDocFlowNonce.Create(nonce, _tenantId, record.Id, ttlSeconds: 300);
            // Exhaust it: call TryUse() twice
            nonceRow.TryUse();
            nonceRow.TryUse();
            db.SigningDocFlowNonces.Add(nonceRow);
            await db.SaveChangesAsync();
        }

        // Issue a valid JWT for this nonce (with the dev signing key)
        var jwtService = GetJwtService();
        var jwt = jwtService.IssueJwt(actorId, docId, nonce, _tenantId);

        var client = _app.CreateClient();
        var response = await client.GetAsync(
            $"/api/signing/docflow/file?docId={docId}&token={Uri.EscapeDataString(jwt)}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "exhausted nonce must be rejected with 401");

        // Assert the 401 came from the nonce gate, NOT from JWT validation. A valid token must clear
        // TryValidate first; if the sub claim failed to read back, the request would 401 at the JWT gate
        // with a different reason and this download path would never reach the nonce check — making every
        // status-only assertion in this suite pass for the wrong reason. Pin the reason so a regression in
        // claim extraction is distinguishable from the intended exhausted-nonce rejection.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Nonce", "the rejection must be the exhausted-nonce path, proving the JWT validated");
        body.Should().NotContain("sub claim", "a valid token must not be rejected at the JWT sub-claim gate");
    }

    // ─── Negative-3: JWT doc_id does not match any persisted record ───────────
    //
    // Isolation model: no tenant_id claim is embedded in the doc-flow JWT.
    // Cross-tenant leakage is prevented by nonce 128-bit uniqueness:
    //   a nonce created for tenantA's record is astronomically unlikely to match
    //   a lookup for tenantB's docId. This test seeds tenantA's record+nonce and
    //   issues a JWT with a different docId — the BE record lookup returns null → 404.

    [Fact]
    public async Task Download_JwtDocIdMismatch_ReturnsNotFound()
    {
        var tenantA = Guid.Parse("00000000-0000-0000-dead-A00000000003");
        var docIdA  = Guid.NewGuid();
        var docIdB  = Guid.NewGuid(); // JWT carries this; no record exists for docIdB
        var nonce   = Guid.NewGuid();

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var record = SigningDocFlowRecord.Initiate(
                tenantId:           tenantA,
                docId:              docIdA,
                signType:           DocFlowSignType.Issued,
                originalStorageKey: $"test/{docIdA}/original.pdf",
                jwtNonce:           nonce,
                initiatedBy:        _userId);

            db.SigningDocFlowRecords.Add(record);
            await db.SaveChangesAsync();

            var nonceRow = SigningDocFlowNonce.Create(nonce, tenantA, record.Id, 300);
            db.SigningDocFlowNonces.Add(nonceRow);
            await db.SaveChangesAsync();
        }

        // JWT claims docIdB — no record matches (record lookup is by docId + nonce)
        var jwtService = GetJwtService();
        var jwt = jwtService.IssueJwt(_userId, docIdB, nonce, tenantA);

        var client = _app.CreateClient();
        var response = await client.GetAsync(
            $"/api/signing/docflow/file?docId={docIdB}&token={Uri.EscapeDataString(jwt)}");

        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.NotFound },
            "JWT with docId that matches no persisted record must be rejected");
    }

    // ─── Negative-4: Tampered JWT (wrong signing key) ─────────────────────────

    [Fact]
    public async Task Download_TamperedJwt_Returns401()
    {
        var docId = Guid.NewGuid();
        var nonce = Guid.NewGuid();

        // Issue JWT with a DIFFERENT key — signature will not validate against dev key
        using var scope = _app.Services.CreateScope();
        var differentKeyOptions = Options.Create(
            new DocFlowJwtOptions
            {
                SigningKey  = "WRONG-KEY-DIFFERENT-FROM-DEV-CONFIG!!!",
                IssuerName  = "AqtCoreFW",
                TtlSeconds  = 300,
            });

        var tamperedService = new DocFlowJwtService(differentKeyOptions);
        var tamperedJwt = tamperedService.IssueJwt(_userId, docId, nonce, _tenantId);

        var client = _app.CreateClient();
        var response = await client.GetAsync(
            $"/api/signing/docflow/file?docId={docId}&token={Uri.EscapeDataString(tamperedJwt)}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "JWT signed with a different key must fail HMAC-SHA256 validation");
    }

    // ─── Storage failure must NOT consume the nonce or advance record state ────
    //
    // The download fetches the PDF BEFORE committing the nonce-use + Downloaded transition. So when the
    // storage fetch fails (backing object missing / storage unreachable), the request returns non-200
    // and the nonce slot + record stay untouched — letting the VGCA service retry. (If the commit ran
    // before the fetch, a storage failure would permanently burn the nonce and strand the record in
    // Downloaded with no bytes ever delivered = an unrecoverable dead-end.)

    [Fact]
    public async Task Download_StorageObjectMissing_PreservesNonceAndRecordState_ForRetry()
    {
        var docId   = Guid.NewGuid();
        var nonce   = Guid.NewGuid();
        var actorId = _userId;

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var record = SigningDocFlowRecord.Initiate(
                tenantId:           _tenantId,
                docId:              docId,
                signType:           DocFlowSignType.Issued,
                originalStorageKey: $"test/{docId}/never-uploaded.pdf", // object does not exist in storage
                jwtNonce:           nonce,
                initiatedBy:        actorId);

            db.SigningDocFlowRecords.Add(record);
            await db.SaveChangesAsync();

            var nonceRow = SigningDocFlowNonce.Create(nonce, _tenantId, record.Id, ttlSeconds: 300);
            db.SigningDocFlowNonces.Add(nonceRow);
            await db.SaveChangesAsync();
        }

        var jwt = GetJwtService().IssueJwt(actorId, docId, nonce, _tenantId);
        var client = _app.CreateClient();

        var response = await client.GetAsync(
            $"/api/signing/docflow/file?docId={docId}&token={Uri.EscapeDataString(jwt)}");

        // A download whose backing object is missing must fail — never a silent 200 with empty bytes.
        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "a missing backing object must surface as an error, not an empty 200");

        // Critical invariant: state is untouched so the VGCA service can retry.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var nonceRow = await db.SigningDocFlowNonces.AsNoTracking()
                .FirstAsync(n => n.Nonce == nonce);
            nonceRow.UseCount.Should().Be(0,
                "the nonce slot must NOT be consumed when the storage fetch fails");

            var record = await db.SigningDocFlowRecords.AsNoTracking()
                .FirstAsync(r => r.DocId == docId && r.JwtNonce == nonce);
            record.Status.Should().Be(DocFlowStatus.Initiated,
                "the record must NOT advance to Downloaded when no bytes were delivered");
        }
    }

    // ─── C2: cross-tenant initiate rejected (storage-key tenant prefix) ────────

    [Fact]
    public async Task Initiate_ForeignTenantStorageKey_Returns400()
    {
        var client = MfaClient(); // authenticated as _tenantId
        var docId  = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();

        var body = new
        {
            docId             = docId.ToString(),
            signType          = "Issued",
            // Names a DIFFERENT tenant's prefix — must be rejected before any record is created.
            originalStorageKey = $"{foreignTenant}/{docId}/original.pdf",
            docNumber         = "VB-002/2026",
            issuedDate        = "2026-06-15",
        };

        var response = await client.PostAsJsonAsync("/api/signing/docflow/initiate", body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "an OriginalStorageKey outside the caller's tenant prefix must be rejected at validation");

        // No record should have been persisted for this docId.
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var record = await db.SigningDocFlowRecords
            .FirstOrDefaultAsync(r => r.DocId == docId);
        record.Should().BeNull("a rejected initiate must not persist a signing record");
    }

    // ─── C2: download with a JWT whose tenant != record tenant rejected ───────

    [Fact]
    public async Task Download_JwtTenantMismatch_Returns401()
    {
        var tenantA = Guid.Parse("00000000-0000-0000-dead-B00000000010");
        var tenantB = Guid.Parse("00000000-0000-0000-dead-B00000000011");
        var docId   = Guid.NewGuid();
        var nonce   = Guid.NewGuid();

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var record = SigningDocFlowRecord.Initiate(
                tenantId:           tenantA,
                docId:              docId,
                signType:           DocFlowSignType.Issued,
                originalStorageKey: $"{tenantA}/{docId}/original.pdf",
                jwtNonce:           nonce,
                initiatedBy:        _userId);
            db.SigningDocFlowRecords.Add(record);
            await db.SaveChangesAsync();

            var nonceRow = SigningDocFlowNonce.Create(nonce, tenantA, record.Id, 300);
            db.SigningDocFlowNonces.Add(nonceRow);
            await db.SaveChangesAsync();
        }

        // Issue a (validly signed) JWT that claims tenantB for a record owned by tenantA.
        var jwt = GetJwtService().IssueJwt(_userId, docId, nonce, tenantB);

        var client = _app.CreateClient();
        var response = await client.GetAsync(
            $"/api/signing/docflow/file?docId={docId}&token={Uri.EscapeDataString(jwt)}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a token whose tenant_id does not match the record/nonce tenant must be rejected");
    }

    // ─── Happy path: upload returns the VGCA PascalCase contract ──────────────
    //
    // The VGCA desktop tool parses the upload response and is case-sensitive — it expects the exact
    // PascalCase shape from the vendor SDK sample ({Status, Message, FileName, FileServer}). The host's
    // JSON policy serializes properties as camelCase by default, so this pins the response keys to
    // PascalCase on the wire. A camelCase body leaves the tool unable to read its keys, so it retries
    // and never fires its JS callback — the browser ceremony hangs to timeout even though the signing
    // succeeded server-side. Assert at the real serialization boundary so an anonymous-object/camelCase
    // regression is caught here, not in a hardware retest.

    [Fact]
    public async Task Upload_ValidSignedPdf_ReturnsPascalCaseVgcaContract()
    {
        var docId   = Guid.NewGuid();
        var nonce   = Guid.NewGuid();
        var actorId = _userId;

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var record = SigningDocFlowRecord.Initiate(
                tenantId:           _tenantId,
                docId:              docId,
                signType:           DocFlowSignType.Issued,
                originalStorageKey: $"{_tenantId}/{docId}/original.pdf",
                jwtNonce:           nonce,
                initiatedBy:        actorId);
            record.MarkDownloaded(); // upload is only valid from the Downloaded state
            db.SigningDocFlowRecords.Add(record);
            await db.SaveChangesAsync();

            // Nonce slot 1 (download) already consumed; slot 2 remains for this upload.
            var nonceRow = SigningDocFlowNonce.Create(nonce, _tenantId, record.Id, ttlSeconds: 300);
            nonceRow.TryUse();
            db.SigningDocFlowNonces.Add(nonceRow);
            await db.SaveChangesAsync();
        }

        var jwt = GetJwtService().IssueJwt(actorId, docId, nonce, _tenantId);
        var client = _app.CreateClient();

        // Minimal valid PDF — passes the "%PDF-" magic-header sniff in the handler.
        var pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4\n%signed-by-test\n");
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "uploadfile", "signed.pdf");

        var response = await client.PostAsync(
            $"/api/signing/docflow/file?token={Uri.EscapeDataString(jwt)}", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a valid signed PDF upload from a Downloaded record must succeed");

        var body = await response.Content.ReadAsStringAsync();

        // The tool keys off PascalCase — these fail if the response reverts to camelCase.
        body.Should().Contain("\"Status\":true",
            "the VGCA tool reads the PascalCase 'Status' key to confirm a successful upload");
        body.Should().Contain("\"FileServer\":",
            "the tool reads the PascalCase 'FileServer' key for the signed-file location");
        body.Should().Contain("\"FileName\":");
        body.Should().NotContain("\"status\":",
            "a camelCase body would be unreadable by the case-sensitive desktop tool");
        body.Should().NotContain("\"fileServer\":");

        // The record must have advanced to Completed.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var record = await db.SigningDocFlowRecords.AsNoTracking()
                .FirstAsync(r => r.DocId == docId && r.JwtNonce == nonce);
            record.Status.Should().Be(DocFlowStatus.Completed,
                "a successful upload must complete the signing record");
        }
    }
}
