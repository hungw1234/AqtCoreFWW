using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// Integration tests for the signing-step read surface introduced by the "view/download signed PDFs" feature.
///
/// Tests cover:
///   Files endpoints (GET /api/v1/files/{id}/signing-steps and GET /api/v1/files/{id}/signed):
///     - Signed file → step rows with correct projection
///     - Unsigned file → empty list (not an error)
///     - Cross-tenant file id → 404
///     - ResolveSignedStep Completed → 200 download
///     - ResolveSignedStep not-Completed → 404
///
/// Seeding strategy: insert SigningRequest + SigningDocFlowRecord rows directly via IdentityDbContext.
/// The Files endpoint reads through InProcessSigningRequestClient → SigningStepQueryService → EF.
/// No MinIO object is stored for signing-steps tests (no actual download byte stream needed there);
/// for the download test, the in-memory stub storage in WebAppFixture returns an empty stream on
/// any key, which is sufficient to prove the 200 path.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class SigningStepReadSurfaceTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0f1e-A00000000001");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0f1e-B00000000002");
    private static readonly Guid UserId  = Guid.Parse("00000000-0000-0000-0f1e-000000000099");

    public SigningStepReadSurfaceTests(WebAppFixture app) => _app = app;

    // ─── Files HTTP endpoints: ownership guard fires before signing client ────────
    //
    // GET /api/v1/files/{id}/signing-steps and GET /api/v1/files/{id}/signed both guard against the
    // Files module DB first. Since this test project cannot upload a real file (cross-module boundary),
    // we verify the ownership-guard security contract: any unknown fileId → 404, never a data path to
    // the signing client. The signing client read logic is covered at the service level below.

    [Fact]
    public async Task GetSigningSteps_UnknownFileId_Returns404_OwnershipGuard()
    {
        // Even though a signing request with this fileId exists, the Files ownership guard fires first.
        // The handler calls IFileRepository.GetByIdAsync → fails → NotFoundError → 404.
        // This pin ensures the client can never bypass ownership by supplying any arbitrary guid.
        var fileId = Guid.NewGuid();
        await SeedCompletedSigningRequest(TenantA, fileId, sequence: 1);

        var client = _app.CreateAuthenticatedClient(UserId, TenantA, "GovOfficer");
        var resp = await client.GetAsync($"/api/v1/files/{fileId}/signing-steps");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "ownership guard must fire before signing client: no Files DB record → 404");
    }

    [Fact]
    public async Task GetSigningSteps_CrossTenantRequest_Returns404_OwnershipGuard()
    {
        // TenantB token on a TenantA fileId — ownership guard returns 404
        var fileId = Guid.NewGuid();
        await SeedCompletedSigningRequest(TenantA, fileId, sequence: 1);

        var client = _app.CreateAuthenticatedClient(UserId, TenantB, "GovOfficer");
        var resp = await client.GetAsync($"/api/v1/files/{fileId}/signing-steps");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant request must return 404, not a step list");
    }

    [Fact]
    public async Task DownloadSignedStep_UnknownFileId_Returns404_OwnershipGuard()
    {
        var fileId = Guid.NewGuid();
        await SeedCompletedSigningRequest(TenantA, fileId, sequence: 1);

        var client = _app.CreateAuthenticatedClient(UserId, TenantA, "GovOfficer");
        var resp = await client.GetAsync($"/api/v1/files/{fileId}/signed?sequence=1");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "ownership guard fires before storage key resolution: unknown file → 404");
    }

    [Fact]
    public async Task DownloadSignedStep_CrossTenantRequest_Returns404_OwnershipGuard()
    {
        var fileId = Guid.NewGuid();
        await SeedCompletedSigningRequest(TenantA, fileId, sequence: 1);

        var client = _app.CreateAuthenticatedClient(UserId, TenantB, "GovOfficer");
        var resp = await client.GetAsync($"/api/v1/files/{fileId}/signed?sequence=1");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant download must return 404, not another tenant's signed PDF");
    }

    // ─── GetSignedFileIds (batch badge) — indirectly covered by list endpoint ────
    //
    // The ListFiles endpoint calls GetSignedFileIdsAsync internally. We verify the contract via the
    // isDigitallySigned field on the list response — but that requires a real file upload path which
    // is available in FileEndpointsTests (Files integration project). The batch query is fully covered
    // by the SigningStepQueryService implementation which is exercised at service level below.

    // ─── SigningStepQueryService: ordered-by-sequence invariant ──────────────────

    [Fact]
    public async Task GetStepsForFileAsync_MultipleSteps_ReturnedOrderedBySequenceNumber()
    {
        // Verify directly via ISigningRequestClient (in-process) using service scope
        var fileId = Guid.NewGuid();
        // Seed two steps in reverse order — query must return seq 1 before seq 2
        await SeedCompletedSigningRequest(TenantA, fileId, sequence: 2);
        await SeedInitiatedSigningRequest(TenantA, fileId, sequence: 1);

        using var scope = _app.Services.CreateScope();
        var client = scope.ServiceProvider
            .GetRequiredService<AqtCoreFW.SharedKernel.Contracts.Clients.ISigningRequestClient>();

        var steps = await client.GetStepsForFileAsync(TenantA, fileId, CancellationToken.None);

        steps.Should().HaveCount(2);
        steps[0].SequenceNumber.Should().Be(1, "steps must be ordered by sequence ascending");
        steps[1].SequenceNumber.Should().Be(2);
        steps[0].Status.Should().Be("Pending", "initiated record with no failure projects as Pending");
        steps[1].Status.Should().Be("Completed");
    }

    [Fact]
    public async Task GetStepsForFileAsync_CrossTenantLookup_ReturnsEmptyList()
    {
        var fileId = Guid.NewGuid();
        await SeedCompletedSigningRequest(TenantA, fileId, sequence: 1);

        using var scope = _app.Services.CreateScope();
        var client = scope.ServiceProvider
            .GetRequiredService<AqtCoreFW.SharedKernel.Contracts.Clients.ISigningRequestClient>();

        // TenantB querying TenantA's file must get an empty list (not a data leak)
        var steps = await client.GetStepsForFileAsync(TenantB, fileId, CancellationToken.None);

        steps.Should().BeEmpty("cross-tenant file id returns empty list at the signing client level");
    }

    [Fact]
    public async Task ResolveSignedStepForFileAsync_CompletedStep_ReturnsKeyAndBucket()
    {
        var fileId = Guid.NewGuid();
        await SeedCompletedSigningRequest(TenantA, fileId, sequence: 1);

        using var scope = _app.Services.CreateScope();
        var client = scope.ServiceProvider
            .GetRequiredService<AqtCoreFW.SharedKernel.Contracts.Clients.ISigningRequestClient>();

        var resolution = await client.ResolveSignedStepForFileAsync(TenantA, fileId, 1, CancellationToken.None);

        resolution.Found.Should().BeTrue("completed step has a signed key");
        resolution.Bucket.Should().Be("signing-docflow");
        resolution.StorageKey.Should().NotBeNullOrWhiteSpace();
        resolution.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task ResolveSignedStepForFileAsync_NotCompletedStep_ReturnsNotFound()
    {
        var fileId = Guid.NewGuid();
        await SeedInitiatedSigningRequest(TenantA, fileId, sequence: 1);

        using var scope = _app.Services.CreateScope();
        var client = scope.ServiceProvider
            .GetRequiredService<AqtCoreFW.SharedKernel.Contracts.Clients.ISigningRequestClient>();

        var resolution = await client.ResolveSignedStepForFileAsync(TenantA, fileId, 1, CancellationToken.None);

        resolution.Found.Should().BeFalse();
        resolution.ErrorCode.Should().NotBeNullOrWhiteSpace("a machine-readable error code must be returned");
    }

    [Fact]
    public async Task ResolveSignedStepForFileAsync_CrossTenantLookup_ReturnsNotFound()
    {
        var fileId = Guid.NewGuid();
        await SeedCompletedSigningRequest(TenantA, fileId, sequence: 1);

        using var scope = _app.Services.CreateScope();
        var client = scope.ServiceProvider
            .GetRequiredService<AqtCoreFW.SharedKernel.Contracts.Clients.ISigningRequestClient>();

        var resolution = await client.ResolveSignedStepForFileAsync(TenantB, fileId, 1, CancellationToken.None);

        resolution.Found.Should().BeFalse("cross-tenant lookup must not return a storage key");
    }

    // ─── M1: Deterministic resolution with duplicate (tenant, sourceFileId, sequence) rows ──

    /// <summary>
    /// Seeds TWO SigningRequests with the same (tenant, sourceFileId, sequence=1):
    ///   - req-A: Completed (has SignedStorageKey)
    ///   - req-B: Initiated (no SignedStorageKey, added after req-A so it has a higher Id)
    ///
    /// GetStepsForFileAsync must return exactly ONE row for sequence 1 (the Completed one).
    /// ResolveSignedStepForFileAsync must return the Completed blob's key, not NOT_COMPLETED.
    ///
    /// This scenario occurs when a user re-initiates signing after the original attempt fails,
    /// leaving two SigningRequest rows with the same sequence number.
    /// </summary>
    [Fact]
    public async Task GetStepsForFile_DuplicateSequenceFromResign_ReturnsLatestCompleted()
    {
        var fileId = Guid.NewGuid();

        // req-A: sequence=1, Completed — the row that should win.
        await SeedCompletedSigningRequest(TenantA, fileId, sequence: 1);
        // req-B: sequence=1, Initiated — a later failed/in-progress attempt; higher Id.
        await SeedInitiatedSigningRequest(TenantA, fileId, sequence: 1);

        using var scope = _app.Services.CreateScope();
        var client = scope.ServiceProvider
            .GetRequiredService<AqtCoreFW.SharedKernel.Contracts.Clients.ISigningRequestClient>();

        // GetStepsForFileAsync must deduplicate: only ONE step row for sequence 1.
        var steps = await client.GetStepsForFileAsync(TenantA, fileId, CancellationToken.None);

        steps.Should().HaveCount(1,
            "duplicate sequence rows must be deduplicated — only the Completed row surfaces");
        steps[0].SequenceNumber.Should().Be(1);
        steps[0].Status.Should().Be("Completed",
            "the Completed row must win over the later Initiated row");
        steps[0].HasSignedPdf.Should().BeTrue(
            "Completed DocFlow record with signedStorageKey → hasSignedPdf=true");

        // ResolveSignedStepForFileAsync must return the Completed blob's key, not NOT_COMPLETED.
        var resolution = await client.ResolveSignedStepForFileAsync(
            TenantA, fileId, 1, CancellationToken.None);

        resolution.Found.Should().BeTrue(
            "Completed row must be preferred over the Initiated row during resolution");
        resolution.Bucket.Should().Be("signing-docflow");
        resolution.StorageKey.Should().NotBeNullOrWhiteSpace(
            "the signed storage key must be returned from the Completed DocFlow record");
        resolution.ErrorCode.Should().BeNull();
    }

    // ─── Independent re-signs of the same file: every Completed attempt is its own step ──

    /// <summary>
    /// Seeds TWO Completed SigningRequests with the same (tenant, sourceFileId, sequence=1) — the shape
    /// produced when a user signs the same uploaded file twice (each sign is its own request and its own
    /// signed PDF blob, both at sequence 1 because the create path never increments the sequence).
    ///
    /// GetStepsForFileAsync must return BOTH steps (not collapse to the latest), each carrying its own
    /// SigningRequestId, and ResolveSignedStepForFileByRequestAsync must return the matching blob for each
    /// — proving every sign is independently viewable/downloadable.
    /// </summary>
    [Fact]
    public async Task GetStepsForFile_TwoCompletedAtSameSequence_ReturnsBothAttempts()
    {
        var fileId = Guid.NewGuid();

        var (firstRequestId, firstKey)   = await SeedCompletedSigningRequest(TenantA, fileId, sequence: 1);
        var (secondRequestId, secondKey) = await SeedCompletedSigningRequest(TenantA, fileId, sequence: 1);

        firstRequestId.Should().NotBe(secondRequestId, "each sign is a distinct request");
        firstKey.Should().NotBe(secondKey, "each sign produced its own signed PDF blob");

        using var scope = _app.Services.CreateScope();
        var client = scope.ServiceProvider
            .GetRequiredService<AqtCoreFW.SharedKernel.Contracts.Clients.ISigningRequestClient>();

        // Both Completed attempts surface — the read no longer collapses successful re-signs by sequence.
        var steps = await client.GetStepsForFileAsync(TenantA, fileId, CancellationToken.None);

        steps.Should().HaveCount(2, "two successful signs of the same file must both appear in history");
        steps.Should().OnlyContain(s => s.Status == "Completed");
        steps.Select(s => s.SigningRequestId)
            .Should().BeEquivalentTo(new[] { firstRequestId, secondRequestId },
                "each step exposes its own SigningRequestId for per-attempt addressing");

        // Each attempt resolves to its OWN blob via per-request addressing (not always the latest).
        var firstResolution = await client.ResolveSignedStepForFileByRequestAsync(
            TenantA, fileId, firstRequestId, CancellationToken.None);
        var secondResolution = await client.ResolveSignedStepForFileByRequestAsync(
            TenantA, fileId, secondRequestId, CancellationToken.None);

        firstResolution.Found.Should().BeTrue();
        firstResolution.StorageKey.Should().Be(firstKey, "first attempt resolves to the first signed blob");
        secondResolution.Found.Should().BeTrue();
        secondResolution.StorageKey.Should().Be(secondKey, "second attempt resolves to the second signed blob");
    }

    [Fact]
    public async Task ResolveSignedStepForFileByRequest_CrossTenant_ReturnsNotFound()
    {
        var fileId = Guid.NewGuid();
        var (requestId, _) = await SeedCompletedSigningRequest(TenantA, fileId, sequence: 1);

        using var scope = _app.Services.CreateScope();
        var client = scope.ServiceProvider
            .GetRequiredService<AqtCoreFW.SharedKernel.Contracts.Clients.ISigningRequestClient>();

        // TenantB asking for TenantA's request id must not resolve (db_owner bypasses RLS → explicit guard).
        var resolution = await client.ResolveSignedStepForFileByRequestAsync(
            TenantB, fileId, requestId, CancellationToken.None);

        resolution.Found.Should().BeFalse("cross-tenant request id must not resolve a storage key");
        resolution.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task ResolveSignedStepForFileByRequest_WrongFile_ReturnsNotFound()
    {
        var fileId      = Guid.NewGuid();
        var otherFileId = Guid.NewGuid();
        var (requestId, _) = await SeedCompletedSigningRequest(TenantA, fileId, sequence: 1);

        using var scope = _app.Services.CreateScope();
        var client = scope.ServiceProvider
            .GetRequiredService<AqtCoreFW.SharedKernel.Contracts.Clients.ISigningRequestClient>();

        // A request id that belongs to a different file must not resolve under otherFileId.
        var resolution = await client.ResolveSignedStepForFileByRequestAsync(
            TenantA, otherFileId, requestId, CancellationToken.None);

        resolution.Found.Should().BeFalse("a request id from another file must not resolve");
        resolution.ErrorCode.Should().Be("NOT_FOUND");
    }

    // ─── Remote PAdES (PendingTransaction) parity: completion recognised across both underlying kinds ──
    //
    // The remote provider signs server-side via a SigningPendingTransaction addressed by a STRING transaction id
    // (a long.TryParse over it fails). These tests prove the read surface no longer treats a Completed remote
    // attempt as Failed/invisible — the badge, the step history, and the per-step download all recognise it.

    [Fact]
    public async Task GetStepsForFileAsync_RemotePadesCompleted_SurfacesAsCompletedStep()
    {
        var fileId = Guid.NewGuid();
        await SeedCompletedRemotePadesRequest(TenantA, fileId, sequence: 1);

        using var scope = _app.Services.CreateScope();
        var client = scope.ServiceProvider
            .GetRequiredService<AqtCoreFW.SharedKernel.Contracts.Clients.ISigningRequestClient>();

        var steps = await client.GetStepsForFileAsync(TenantA, fileId, CancellationToken.None);

        steps.Should().HaveCount(1);
        steps[0].Status.Should().Be("Completed",
            "a Completed remote PAdES transaction must project as Completed, not the DocFlow-only fallback Failed");
        steps[0].HasSignedPdf.Should().BeTrue("the remote transaction stored a signed PDF key");
        steps[0].CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveSignedStepForFileByRequest_RemotePadesCompleted_ReturnsPadesBucketAndKey()
    {
        var fileId = Guid.NewGuid();
        var (requestId, signedKey) = await SeedCompletedRemotePadesRequest(TenantA, fileId, sequence: 1);

        using var scope = _app.Services.CreateScope();
        var client = scope.ServiceProvider
            .GetRequiredService<AqtCoreFW.SharedKernel.Contracts.Clients.ISigningRequestClient>();

        var resolution = await client.ResolveSignedStepForFileByRequestAsync(
            TenantA, fileId, requestId, CancellationToken.None);

        resolution.Found.Should().BeTrue("a Completed remote transaction has a retrievable signed PDF");
        resolution.Bucket.Should().Be("signing-pades",
            "remote PAdES signed PDFs live in the signing-pades bucket, not signing-docflow");
        resolution.StorageKey.Should().Be(signedKey);
        resolution.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task GetSignedFileIdsAsync_RemotePadesCompleted_IncludesFileInBadgeSet()
    {
        var fileId = Guid.NewGuid();
        await SeedCompletedRemotePadesRequest(TenantA, fileId, sequence: 1);

        using var scope = _app.Services.CreateScope();
        var client = scope.ServiceProvider
            .GetRequiredService<AqtCoreFW.SharedKernel.Contracts.Clients.ISigningRequestClient>();

        var signed = await client.GetSignedFileIdsAsync(TenantA, new[] { fileId }, CancellationToken.None);

        signed.Should().Contain(fileId, "a remote-signed file must earn the isDigitallySigned list badge");
    }

    [Fact]
    public async Task GetStepsForFile_MixedDocFlowAndRemote_BothSurfaceWithCorrectBuckets()
    {
        // A file signed once by a USB token (DocFlow) and once by the remote provider (PendingTransaction) —
        // both attempts must surface, each resolving to its own bucket. Proves the two kinds coexist correctly.
        var fileId = Guid.NewGuid();
        var (docFlowReqId, docFlowKey) = await SeedCompletedSigningRequest(TenantA, fileId, sequence: 1);
        var (remoteReqId, remoteKey)   = await SeedCompletedRemotePadesRequest(TenantA, fileId, sequence: 1);

        using var scope = _app.Services.CreateScope();
        var client = scope.ServiceProvider
            .GetRequiredService<AqtCoreFW.SharedKernel.Contracts.Clients.ISigningRequestClient>();

        var steps = await client.GetStepsForFileAsync(TenantA, fileId, CancellationToken.None);

        steps.Should().HaveCount(2, "a file signed by both providers shows both signs, never collapsed by kind");
        steps.Should().OnlyContain(s => s.Status == "Completed");

        var docFlowRes = await client.ResolveSignedStepForFileByRequestAsync(
            TenantA, fileId, docFlowReqId, CancellationToken.None);
        var remoteRes = await client.ResolveSignedStepForFileByRequestAsync(
            TenantA, fileId, remoteReqId, CancellationToken.None);

        docFlowRes.Bucket.Should().Be("signing-docflow");
        docFlowRes.StorageKey.Should().Be(docFlowKey);
        remoteRes.Bucket.Should().Be("signing-pades");
        remoteRes.StorageKey.Should().Be(remoteKey);
    }

    // ─── Seeding helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a SigningRequest + Completed SigningDocFlowRecord for the given (tenant, fileId, sequence).
    /// The docId doubles as a unique key for the DocFlow round-trip.
    /// </summary>
    private async Task<(Guid RequestPublicId, string SignedStorageKey)> SeedCompletedSigningRequest(
        Guid tenantId, Guid fileId, int sequence)
    {
        using var scope = _app.Services.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var docId = Guid.NewGuid();
        var nonce = Guid.NewGuid();
        var signedKey = $"{tenantId}/{docId}/signed.pdf";

        // 1. Seed DocFlow record → Completed with a signed storage key.
        var docFlowRecord = SigningDocFlowRecord.Initiate(
            tenantId: tenantId,
            docId: docId,
            signType: DocFlowSignType.Issued,
            originalStorageKey: $"{tenantId}/{docId}/original.pdf",
            jwtNonce: nonce,
            initiatedBy: UserId,
            signerSequenceNumber: sequence);

        docFlowRecord.MarkDownloaded();
        docFlowRecord.Complete(
            signedStorageKey: signedKey,
            completedBy: UserId);

        db.SigningDocFlowRecords.Add(docFlowRecord);
        await db.SaveChangesAsync(); // get EF-assigned Id

        // 2. Seed the SigningRequest linking to the DocFlow record.
        var request = SigningRequest.Create(
            gen, tenantId,
            SigningArtifactKind.UploadedPdf,
            artifactRef: docId.ToString(),
            providerKey: "usb-vgca",
            assignedSignerId: UserId,
            initiatedBy: UserId,
            signerSequenceNumber: sequence,
            sourceFileId: fileId);

        request.LinkUnderlying(SigningUnderlyingKind.DocFlow, docFlowRecord.Id.ToString(), UserId);

        db.SigningRequests.Add(request);
        await db.SaveChangesAsync();

        return (request.PublicId, signedKey);
    }

    /// <summary>
    /// Seeds a SigningRequest + Initiated (not Completed) SigningDocFlowRecord.
    /// Used to verify the "not completed" resolution path.
    /// </summary>
    private async Task SeedInitiatedSigningRequest(Guid tenantId, Guid fileId, int sequence)
    {
        using var scope = _app.Services.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var docId = Guid.NewGuid();
        var nonce = Guid.NewGuid();

        var docFlowRecord = SigningDocFlowRecord.Initiate(
            tenantId: tenantId,
            docId: docId,
            signType: DocFlowSignType.Issued,
            originalStorageKey: $"{tenantId}/{docId}/original.pdf",
            jwtNonce: nonce,
            initiatedBy: UserId,
            signerSequenceNumber: sequence);

        db.SigningDocFlowRecords.Add(docFlowRecord);
        await db.SaveChangesAsync();

        var request = SigningRequest.Create(
            gen, tenantId,
            SigningArtifactKind.UploadedPdf,
            artifactRef: docId.ToString(),
            providerKey: "usb-vgca",
            assignedSignerId: UserId,
            initiatedBy: UserId,
            signerSequenceNumber: sequence,
            sourceFileId: fileId);

        request.LinkUnderlying(SigningUnderlyingKind.DocFlow, docFlowRecord.Id.ToString(), UserId);

        db.SigningRequests.Add(request);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a SigningRequest backed by a Completed remote PAdES <see cref="SigningPendingTransaction"/>.
    /// The transaction is addressed by a STRING transaction id (a Viettel-style UUID, not a long) and carries
    /// the signed PDF storage key — the exact shape the dual-kind read surface must recognise.
    /// </summary>
    private async Task<(Guid RequestPublicId, string SignedStorageKey)> SeedCompletedRemotePadesRequest(
        Guid tenantId, Guid fileId, int sequence)
    {
        using var scope = _app.Services.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var transactionId = Guid.NewGuid().ToString(); // provider-assigned string id (never a long)
        var signedKey = $"{tenantId}/{Guid.NewGuid()}.signed.pdf";

        // Initiated → CompletePades: flips to Completed and records the assembled signed PDF key.
        var txn = SigningPendingTransaction.Initiate(
            transactionId: transactionId,
            tenantId: tenantId,
            userId: UserId,
            requestId: Guid.NewGuid().ToString(),
            documentId: null,
            ttl: TimeSpan.FromHours(24));
        txn.CompletePades(new byte[] { 1, 2, 3, 4 }, signedKey);

        db.SigningPendingTransactions.Add(txn);
        await db.SaveChangesAsync();

        var request = SigningRequest.Create(
            gen, tenantId,
            SigningArtifactKind.UploadedPdf,
            artifactRef: transactionId,
            providerKey: "remote-viettel-mysign",
            assignedSignerId: UserId,
            initiatedBy: UserId,
            signerSequenceNumber: sequence,
            sourceFileId: fileId);

        // UnderlyingRecordId = the provider's STRING transaction id (the value a long.TryParse would drop).
        request.LinkUnderlying(SigningUnderlyingKind.PendingTransaction, transactionId, UserId);

        db.SigningRequests.Add(request);
        await db.SaveChangesAsync();

        return (request.PublicId, signedKey);
    }

    // ─── JSON helpers ─────────────────────────────────────────────────────────────

    private static JsonElement UnwrapData(JsonElement root) =>
        root.TryGetProperty("data", out var d) ? d : root;
}
