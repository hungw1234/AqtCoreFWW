using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Files.Infrastructure.Persistence;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace AqtCoreFW.Files.Integration.Tests.Signing;

/// <summary>
/// HTTP-boundary integration tests for signed-PDF read endpoints on the Files module.
/// Tests exercise the real MVC pipeline: auth guard → tenant guard → signing client → storage.
///
/// Covers:
///   GET /api/v1/files/{id}/signing-steps
///     1. Signed file (≥1 Completed step) → ordered step rows, isDigitallySigned badge
///     2. Cross-tenant file id → 404 (security-critical: no data leak)
///     3. Unsigned file (null SigningRequestId link) → empty array 200
///   GET /api/v1/files/{id}/signed?sequence=N
///     4. Completed step, signed blob seeded → 200 application/pdf with %PDF- header
///     5. Cross-tenant request → 404
///     6. Step not Completed / sequence out of range → 404
///   GET /api/v1/files (list) isDigitallySigned badge:
///     7. Available file with a Completed step → isDigitallySigned:true
///     8. Available file with no signing → isDigitallySigned:false
///
/// Seeding strategy:
///   - FileRecord rows seeded directly via FilesDbContext (mark Available so list/download work).
///   - SigningRequest + SigningDocFlowRecord rows seeded via IdentityDbContext (same shared DB).
///   - IFileStorageService substituted per-client via WithWebHostBuilder to return a minimal valid
///     PDF stream — MinIO is not available in the integration-test environment.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class FileSigningReadSurfaceHttpTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    // Fixed-across-test-class GUIDs with zero collision risk — unique prefix per class.
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-f501-A00000000001");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-f501-B00000000002");
    private static readonly Guid UserId  = Guid.Parse("00000000-0000-0000-f501-000000000099");

    // Minimal valid PDF body — passes the %PDF- magic-header sniff in the download handler.
    private static readonly byte[] FakePdfBytes =
        Encoding.ASCII.GetBytes("%PDF-1.4\n%%test-signed-by-FileSigningReadSurface\n");

    public FileSigningReadSurfaceHttpTests(WebAppFixture app) => _app = app;

    // ─── TC-1: signing-steps returns ordered rows for a signed file ──────────────

    [Fact]
    public async Task SigningSteps_SignedFile_ReturnsOrderedRowsWith200()
    {
        var fileId = await SeedAvailableFileAsync(TenantA);
        await SeedCompletedSigningStepForFileAsync(TenantA, fileId, sequence: 1);

        var client = _app.CreateAuthenticatedClient(UserId, TenantA, "GovOfficer");
        var resp = await client.GetAsync($"/api/v1/files/{fileId}/signing-steps");

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "a signed file must return its step list with 200");

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = UnwrapData(body);

        data.ValueKind.Should().Be(JsonValueKind.Array, "signing-steps returns an array");
        data.GetArrayLength().Should().BeGreaterThanOrEqualTo(1, "at least one completed step was seeded");

        // Verify sequenceNumber ordering: first element = sequence 1.
        var firstStep = data[0];
        firstStep.GetProperty("sequenceNumber").GetInt32().Should().Be(1);
        firstStep.GetProperty("status").GetString().Should().Be("Completed");
        firstStep.GetProperty("hasSignedPdf").GetBoolean().Should().BeTrue(
            "Completed DocFlow record with signedStorageKey → hasSignedPdf=true");
    }

    // ─── TC-2: cross-tenant signing-steps → 404 (security invariant) ────────────

    [Fact]
    public async Task SigningSteps_CrossTenantFileId_Returns404()
    {
        // TenantA owns the file; TenantB caller must get 404, not a step list.
        var fileId = await SeedAvailableFileAsync(TenantA);
        await SeedCompletedSigningStepForFileAsync(TenantA, fileId, sequence: 1);

        var clientB = _app.CreateAuthenticatedClient(UserId, TenantB, "GovOfficer");
        var resp = await clientB.GetAsync($"/api/v1/files/{fileId}/signing-steps");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant file id must return 404, not expose another tenant's signing data");
    }

    // ─── TC-3: unsigned file → 200 empty array (not an error) ───────────────────

    [Fact]
    public async Task SigningSteps_UnsignedFile_Returns200EmptyArray()
    {
        // File exists and is Available, but no signing request was ever created.
        var fileId = await SeedAvailableFileAsync(TenantA);

        var client = _app.CreateAuthenticatedClient(UserId, TenantA, "GovOfficer");
        var resp = await client.GetAsync($"/api/v1/files/{fileId}/signing-steps");

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "no signing initiated is a valid state, not a 404");

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = UnwrapData(body);

        data.ValueKind.Should().Be(JsonValueKind.Array);
        data.GetArrayLength().Should().Be(0, "unsigned file has no signing steps");
    }

    // ─── TC-4: download signed step → 200 application/pdf with %PDF- magic bytes ─

    [Fact]
    public async Task DownloadSignedStep_CompletedStep_Streams200ApplicationPdf()
    {
        var fileId = await SeedAvailableFileAsync(TenantA);
        await SeedCompletedSigningStepForFileAsync(TenantA, fileId, sequence: 1);

        // Substitute IFileStorageService so the download returns a real %PDF- payload
        // (MinIO not available in the integration-test environment).
        var storageStub = BuildStorageStubReturningFakePdf();
        var client = CreateClientWithStorageStub(UserId, TenantA, storageStub);

        var resp = await client.GetAsync($"/api/v1/files/{fileId}/signed?sequence=1");

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "Completed step with a signed storage key must stream 200");
        resp.Content.Headers.ContentType!.MediaType.Should()
            .Be("application/pdf", "signed PDF must be served as application/pdf");

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty("response must contain file bytes");

        // %PDF- magic header — guards against empty-body 200s.
        bytes.Take(5).Should().Equal(Encoding.ASCII.GetBytes("%PDF-"),
            "streaming a PDF must start with the %PDF- magic header");
    }

    // ─── TC-5: download signed step — cross-tenant → 404 ────────────────────────

    [Fact]
    public async Task DownloadSignedStep_CrossTenantFileId_Returns404()
    {
        var fileId = await SeedAvailableFileAsync(TenantA);
        await SeedCompletedSigningStepForFileAsync(TenantA, fileId, sequence: 1);

        var clientB = _app.CreateAuthenticatedClient(UserId, TenantB, "GovOfficer");
        var resp = await clientB.GetAsync($"/api/v1/files/{fileId}/signed?sequence=1");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant download must return 404, not stream another tenant's signed PDF");
    }

    // ─── TC-6: download signed step — not Completed → 404 ───────────────────────

    [Fact]
    public async Task DownloadSignedStep_StepNotCompleted_Returns404()
    {
        var fileId = await SeedAvailableFileAsync(TenantA);
        // Seed an Initiated (not Completed) signing step — no signed storage key.
        await SeedInitiatedSigningStepForFileAsync(TenantA, fileId, sequence: 1);

        var client = _app.CreateAuthenticatedClient(UserId, TenantA, "GovOfficer");
        var resp = await client.GetAsync($"/api/v1/files/{fileId}/signed?sequence=1");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a non-Completed step has no signed PDF — must return 404");
    }

    // ─── TC-7: list files — isDigitallySigned badge for signed file ──────────────

    [Fact]
    public async Task ListFiles_FileWithCompletedStep_HasIsDigitallySignedTrue()
    {
        var tenantId = Guid.NewGuid(); // isolated tenant to avoid row-count pollution
        var userId   = Guid.NewGuid();
        var fileId   = await SeedAvailableFileAsync(tenantId);
        await SeedCompletedSigningStepForFileAsync(tenantId, fileId, sequence: 1);

        var client = _app.CreateAuthenticatedClient(userId, tenantId, "GovOfficer");
        var resp = await client.GetAsync("/api/v1/files?page=1&pageSize=50");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body    = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items   = body.TryGetProperty("data", out var d)
            ? d.TryGetProperty("items", out var i) ? i : d
            : body.GetProperty("items");

        // Find the specific file we seeded.
        var found = false;
        foreach (var item in items.EnumerateArray())
        {
            // camelCase: the list handler uses anonymous objects serialized by STJ (camelCase policy).
            if (!item.TryGetProperty("id", out var idProp)) continue;
            if (idProp.GetGuid() != fileId) continue;

            found = true;
            item.GetProperty("isDigitallySigned").GetBoolean().Should().BeTrue(
                "a file with ≥1 Completed signing step must have isDigitallySigned=true");
            break;
        }

        found.Should().BeTrue($"file {fileId} must appear in the tenant list");
    }

    // ─── TC-8: list files — isDigitallySigned badge for unsigned file ────────────

    [Fact]
    public async Task ListFiles_FileWithNoSigning_HasIsDigitallySignedFalse()
    {
        var tenantId = Guid.NewGuid();
        var userId   = Guid.NewGuid();
        var fileId   = await SeedAvailableFileAsync(tenantId);
        // No signing step seeded — isDigitallySigned must be false.

        var client = _app.CreateAuthenticatedClient(userId, tenantId, "GovOfficer");
        var resp = await client.GetAsync("/api/v1/files?page=1&pageSize=50");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body  = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.TryGetProperty("data", out var d)
            ? d.TryGetProperty("items", out var i) ? i : d
            : body.GetProperty("items");

        var found = false;
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idProp)) continue;
            if (idProp.GetGuid() != fileId) continue;

            found = true;
            item.GetProperty("isDigitallySigned").GetBoolean().Should().BeFalse(
                "a file with no completed signing step must have isDigitallySigned=false");
            break;
        }

        found.Should().BeTrue($"file {fileId} must appear in the tenant list");
    }

    // ─── Seeding helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a FileRecord in "Available" status (virus-scan passed, downloadable) and returns
    /// its PublicId. The record must be Available for the signing-step endpoints to find it via
    /// the Files ownership guard (GetByIdAsync checks the record exists, not its status).
    /// </summary>
    private async Task<Guid> SeedAvailableFileAsync(Guid tenantId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db  = scope.ServiceProvider.GetRequiredService<FilesDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        // FileRecord.Create produces Quarantined status. We call MarkAvailable to advance to
        // Available so it appears in the list endpoint (which filters on all statuses, but
        // download requires Available). The signing-step endpoint does NOT filter on status —
        // only the ownership guard runs — so seeding as Available covers both paths.
        var record = FileRecord.Create(
            gen,
            tenantId,
            originalFileName:  "document-to-sign.pdf",
            storageKey:        $"{tenantId}/{Guid.NewGuid()}/document.pdf",
            contentType:       "application/pdf",
            sizeBytes:         1024,
            checksumSha256:    "fake-sha256-for-test",
            uploadedBy:        UserId,
            uploaderDisplayName: "Test User");

        // Advance from Quarantined → Available so it shows in list and is downloadable.
        record.MarkAvailable(gen);
        record.ClearDomainEvents(); // events not needed in tests; avoid outbox noise

        db.FileRecords.Add(record);
        await db.SaveChangesAsync();

        return record.PublicId;
    }

    /// <summary>
    /// Seeds a SigningRequest + Completed SigningDocFlowRecord linked to the given file.
    /// The signed storage key is recorded on the DocFlow record so ResolveSignedStepForFileAsync
    /// returns Found=true.
    /// </summary>
    private async Task SeedCompletedSigningStepForFileAsync(Guid tenantId, Guid fileId, int sequence)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db  = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var docId = Guid.NewGuid();
        var nonce = Guid.NewGuid();

        var docFlowRecord = SigningDocFlowRecord.Initiate(
            tenantId:           tenantId,
            docId:              docId,
            signType:           DocFlowSignType.Issued,
            originalStorageKey: $"{tenantId}/{docId}/original.pdf",
            jwtNonce:           nonce,
            initiatedBy:        UserId,
            signerSequenceNumber: sequence);

        docFlowRecord.MarkDownloaded();
        docFlowRecord.Complete(
            signedStorageKey: $"{tenantId}/{docId}/signed-seq{sequence}.pdf",
            completedBy: UserId);

        db.SigningDocFlowRecords.Add(docFlowRecord);
        await db.SaveChangesAsync();

        var request = SigningRequest.Create(
            gen, tenantId,
            SigningArtifactKind.UploadedPdf,
            artifactRef:          docId.ToString(),
            providerKey:          "usb-vgca",
            assignedSignerId:     UserId,
            initiatedBy:          UserId,
            signerSequenceNumber: sequence,
            sourceFileId:         fileId);

        request.LinkUnderlying(SigningUnderlyingKind.DocFlow, docFlowRecord.Id.ToString(), UserId);

        db.SigningRequests.Add(request);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a SigningRequest + Initiated (not Completed) SigningDocFlowRecord for the given file.
    /// Used to verify that the download endpoint returns 404 when no signed PDF exists yet.
    /// </summary>
    private async Task SeedInitiatedSigningStepForFileAsync(Guid tenantId, Guid fileId, int sequence)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db  = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var docId = Guid.NewGuid();
        var nonce = Guid.NewGuid();

        var docFlowRecord = SigningDocFlowRecord.Initiate(
            tenantId:           tenantId,
            docId:              docId,
            signType:           DocFlowSignType.Issued,
            originalStorageKey: $"{tenantId}/{docId}/original.pdf",
            jwtNonce:           nonce,
            initiatedBy:        UserId,
            signerSequenceNumber: sequence);

        db.SigningDocFlowRecords.Add(docFlowRecord);
        await db.SaveChangesAsync();

        var request = SigningRequest.Create(
            gen, tenantId,
            SigningArtifactKind.UploadedPdf,
            artifactRef:          docId.ToString(),
            providerKey:          "usb-vgca",
            assignedSignerId:     UserId,
            initiatedBy:          UserId,
            signerSequenceNumber: sequence,
            sourceFileId:         fileId);

        request.LinkUnderlying(SigningUnderlyingKind.DocFlow, docFlowRecord.Id.ToString(), UserId);

        db.SigningRequests.Add(request);
        await db.SaveChangesAsync();
    }

    // ─── Storage and client helpers ───────────────────────────────────────────────

    /// <summary>
    /// Builds an NSubstitute stub for IFileStorageService whose DownloadAsync always returns
    /// a MemoryStream wrapping FakePdfBytes — simulating a signed PDF blob in the
    /// "signing-docflow" bucket without requiring a real MinIO instance.
    /// The stub is injected at the WebApplicationFactory level (per-request scope), ensuring
    /// the actual MVC pipeline runs but the storage boundary is intercepted.
    /// </summary>
    private static IFileStorageService BuildStorageStubReturningFakePdf()
    {
        var stub = Substitute.For<IFileStorageService>();
        stub.DownloadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(FakePdfBytes)));
        return stub;
    }

    /// <summary>
    /// Creates an authenticated HttpClient with the given storage stub substituted in DI.
    /// Mirrors the pattern in FormDigitalSignatureSubmitTests.CreateClient.
    /// The WithWebHostBuilder clone shares the Testcontainers SQL database with _app,
    /// so rows seeded through _app.Services are visible here.
    /// </summary>
    private HttpClient CreateClientWithStorageStub(
        Guid userId, Guid tenantId, IFileStorageService storageStub, params string[] roles)
    {
        var client = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => storageStub);
            })).CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        if (roles.Length > 0)
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(",", roles));
        return client;
    }

    // ─── JSON helpers ─────────────────────────────────────────────────────────────

    private static JsonElement UnwrapData(JsonElement root) =>
        root.TryGetProperty("data", out var d) ? d : root;
}
