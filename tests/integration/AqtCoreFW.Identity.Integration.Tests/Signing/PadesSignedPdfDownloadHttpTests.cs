using System.Net;
using System.Text;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// HTTP-boundary integration tests for the server-side PAdES signed-PDF download endpoint:
///   GET /api/v1/signing/request/{publicId}/signed-pdf  ([Authorize] + [RequiresMfa]).
///
/// The orchestrator logic is unit-covered (SignatureOrchestratorTests, 4 tests); these prove the
/// invariants only the real MVC pipeline can: auth/MFA guard → tenant resolution from claims →
/// orchestrator → typed-error/stream projection at the HTTP boundary. Cases:
///   1. Completed remote PAdES + signed blob → 200 application/pdf with the %PDF- magic header.
///   2. Cross-tenant publicId → 404 (no bucket access; tenant comes from the JWT, never the route).
///   3. Linked PendingTransaction still pending → typed 400 SIGNING_PADES_PDF_NOT_READY.
///   4. Non-PAdES (DocFlow-linked) request → typed 400 SIGNING_PADES_NO_SIGNED_PDF.
///
/// The two 400s assert the typed error CODE (not just the status) so a regression that collapses
/// both paths into one generic 400 cannot pass green — the same false-green guard the prior signing
/// slices adopted (pin the reason, not the status).
///
/// Storage: WebAppFixture's default storage stub returns an EMPTY stream on any key, which would
/// defeat the %PDF- assertion. The happy-path test therefore substitutes ISigningPlaceholderPdfReader
/// (the port the orchestrator reads the signed PDF through — directly in Identity.Application, so the
/// test stays within this project's reference set) to return real %PDF- bytes.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class PadesSignedPdfDownloadHttpTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    // Fixed GUIDs with a class-unique group so rows never collide with sibling suites sharing the DB.
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-f5d0-A00000000001");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-f5d0-B00000000002");
    private static readonly Guid UserId  = Guid.Parse("00000000-0000-0000-f5d0-000000000099");

    // Minimal valid PDF body — passes the %PDF- magic-header sniff at the download boundary.
    private static readonly byte[] FakeSignedPdfBytes =
        Encoding.ASCII.GetBytes("%PDF-1.7\n%%pades-signed-by-PadesSignedPdfDownload\n");

    public PadesSignedPdfDownloadHttpTests(WebAppFixture app) => _app = app;

    // ─── TC-1: completed remote PAdES → 200 application/pdf with %PDF- header ─────

    [Fact]
    public async Task GetSignedPdf_CompletedRemotePades_Streams200ApplicationPdfWithMagicBytes()
    {
        var (publicId, _) = await SeedCompletedRemotePadesRequestAsync(TenantA);

        // Substitute the signed-PDF reader so the read returns a real %PDF- payload (MinIO is not
        // available in the integration environment; the default stub returns an empty stream).
        var client = CreateClientWithPdfReaderStub(UserId, TenantA);

        var resp = await client.GetAsync($"/api/v1/signing/request/{publicId}/signed-pdf");

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "a Completed PAdES request with a stored signed PDF must stream 200");
        resp.Content.Headers.ContentType!.MediaType.Should()
            .Be("application/pdf", "the signed PDF must be served as application/pdf");

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty("the response must carry the signed PDF bytes");
        bytes.Take(5).Should().Equal(Encoding.ASCII.GetBytes("%PDF-"),
            "streaming a PDF must start with the %PDF- magic header (guards empty-body 200s)");
    }

    // ─── TC-2: cross-tenant publicId → 404 (tenant from JWT, never the route) ─────

    [Fact]
    public async Task GetSignedPdf_CrossTenantPublicId_Returns404()
    {
        // TenantA owns the completed PAdES request; a TenantB caller must get 404, never the PDF.
        var (publicId, _) = await SeedCompletedRemotePadesRequestAsync(TenantA);

        var clientB = _app.CreateAuthenticatedClient(UserId, TenantB, includeMfaClaim: true, "GovOfficer");
        var resp = await clientB.GetAsync($"/api/v1/signing/request/{publicId}/signed-pdf");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a request in another tenant must resolve to 404, never stream a cross-tenant signed PDF");
    }

    // ─── TC-3: linked PendingTransaction still pending → typed 400 ────────────────

    [Fact]
    public async Task GetSignedPdf_NotYetCompleted_Returns400_PdfNotReadyCode()
    {
        // PendingTransaction is Initiated (never CompletePades) → the signed PDF is not ready.
        var publicId = await SeedInitiatedRemotePadesRequestAsync(TenantA);

        var client = _app.CreateAuthenticatedClient(UserId, TenantA, includeMfaClaim: true, "GovOfficer");
        var resp = await client.GetAsync($"/api/v1/signing/request/{publicId}/signed-pdf");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a pending signature has no downloadable PDF yet");
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("SIGNING_PADES_PDF_NOT_READY",
            "the typed code must reach the client so the UI can distinguish 'not ready' from 'wrong kind'");
    }

    // ─── TC-4: non-PAdES (DocFlow) request → typed 400 ────────────────────────────

    [Fact]
    public async Task GetSignedPdf_NonPadesDocFlowRequest_Returns400_NoSignedPdfCode()
    {
        // A DocFlow-linked request has its signed PDF under a different surface — this endpoint rejects it.
        var publicId = await SeedCompletedDocFlowRequestAsync(TenantA);

        var client = _app.CreateAuthenticatedClient(UserId, TenantA, includeMfaClaim: true, "GovOfficer");
        var resp = await client.GetAsync($"/api/v1/signing/request/{publicId}/signed-pdf");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a DocFlow request has no BE-assembled PAdES PDF to download from this endpoint");
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("SIGNING_PADES_NO_SIGNED_PDF",
            "the typed code must distinguish 'wrong signing kind' from 'PAdES not ready yet'");
    }

    // ─── Seeding helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a SigningRequest backed by a Completed remote PAdES <see cref="SigningPendingTransaction"/>
    /// (string transaction id, signed-PDF key set) — the exact shape the download endpoint streams.
    /// </summary>
    private async Task<(Guid PublicId, string SignedKey)> SeedCompletedRemotePadesRequestAsync(Guid tenantId)
    {
        using var scope = _app.Services.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var transactionId = Guid.NewGuid().ToString(); // provider-assigned string id (never a long)
        var signedKey = $"{tenantId}/{Guid.NewGuid()}.signed.pdf";

        var txn = SigningPendingTransaction.Initiate(
            transactionId: transactionId, tenantId: tenantId, userId: UserId,
            requestId: Guid.NewGuid().ToString(), documentId: null, ttl: TimeSpan.FromHours(24));
        txn.CompletePades(new byte[] { 1, 2, 3, 4 }, signedKey);
        db.SigningPendingTransactions.Add(txn);
        await db.SaveChangesAsync();

        var request = SigningRequest.Create(
            gen, tenantId, SigningArtifactKind.UploadedPdf,
            artifactRef: transactionId, providerKey: "remote-viettel-mysign",
            assignedSignerId: UserId, initiatedBy: UserId, signerSequenceNumber: 1,
            sourceFileId: Guid.NewGuid());
        request.LinkUnderlying(SigningUnderlyingKind.PendingTransaction, transactionId, UserId);
        db.SigningRequests.Add(request);
        await db.SaveChangesAsync();

        return (request.PublicId, signedKey);
    }

    /// <summary>Seeds a SigningRequest linked to an Initiated (not Completed) remote PAdES transaction.</summary>
    private async Task<Guid> SeedInitiatedRemotePadesRequestAsync(Guid tenantId)
    {
        using var scope = _app.Services.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var transactionId = Guid.NewGuid().ToString();

        var txn = SigningPendingTransaction.Initiate(
            transactionId: transactionId, tenantId: tenantId, userId: UserId,
            requestId: Guid.NewGuid().ToString(), documentId: null, ttl: TimeSpan.FromHours(24));
        db.SigningPendingTransactions.Add(txn); // no CompletePades → still pending
        await db.SaveChangesAsync();

        var request = SigningRequest.Create(
            gen, tenantId, SigningArtifactKind.UploadedPdf,
            artifactRef: transactionId, providerKey: "remote-viettel-mysign",
            assignedSignerId: UserId, initiatedBy: UserId, signerSequenceNumber: 1,
            sourceFileId: Guid.NewGuid());
        request.LinkUnderlying(SigningUnderlyingKind.PendingTransaction, transactionId, UserId);
        db.SigningRequests.Add(request);
        await db.SaveChangesAsync();

        return request.PublicId;
    }

    /// <summary>Seeds a SigningRequest linked to a Completed DocFlow record (non-PAdES kind).</summary>
    private async Task<Guid> SeedCompletedDocFlowRequestAsync(Guid tenantId)
    {
        using var scope = _app.Services.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var docId = Guid.NewGuid();
        var docFlowRecord = SigningDocFlowRecord.Initiate(
            tenantId: tenantId, docId: docId, signType: DocFlowSignType.Issued,
            originalStorageKey: $"{tenantId}/{docId}/original.pdf",
            jwtNonce: Guid.NewGuid(), initiatedBy: UserId, signerSequenceNumber: 1);
        docFlowRecord.MarkDownloaded();
        docFlowRecord.Complete(signedStorageKey: $"{tenantId}/{docId}/signed.pdf", completedBy: UserId);
        db.SigningDocFlowRecords.Add(docFlowRecord);
        await db.SaveChangesAsync(); // EF-assigned long Id

        var request = SigningRequest.Create(
            gen, tenantId, SigningArtifactKind.UploadedPdf,
            artifactRef: docId.ToString(), providerKey: "usb-vgca",
            assignedSignerId: UserId, initiatedBy: UserId, signerSequenceNumber: 1,
            sourceFileId: Guid.NewGuid());
        request.LinkUnderlying(SigningUnderlyingKind.DocFlow, docFlowRecord.Id.ToString(), UserId);
        db.SigningRequests.Add(request);
        await db.SaveChangesAsync();

        return request.PublicId;
    }

    // ─── Client helper ──────────────────────────────────────────────────────────

    /// <summary>
    /// Authenticated + MFA'd client whose ISigningPlaceholderPdfReader is stubbed to return %PDF- bytes on
    /// any key — proving the 200 path without a MinIO object. The WithWebHostBuilder clone shares the
    /// Testcontainers SQL DB, so rows seeded via _app.Services are visible here.
    /// </summary>
    private HttpClient CreateClientWithPdfReaderStub(Guid userId, Guid tenantId)
    {
        var readerStub = Substitute.For<ISigningPlaceholderPdfReader>();
        readerStub.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(FakeSignedPdfBytes));

        var client = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddScoped(_ => readerStub))).CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "GovOfficer");
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.MfaAtHeader, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        return client;
    }
}
