using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Authorization;
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

namespace AqtCoreFW.Forms.Integration.Tests.Signing;

/// <summary>
/// HTTP-boundary integration tests for signed-PDF read endpoints on the Forms module.
/// Tests exercise the real MVC pipeline: auth guard → tenant guard → signing client → storage.
///
/// Covers:
///   GET /api/v1/forms/submissions/{publicId}/signing-steps
///     1. Signed submission (≥1 Completed step) → ordered step rows
///     2. Cross-tenant submission publicId → 404 (security-critical: no data leak)
///     3. Submission with null SigningRequestId → 200 empty array
///     4. Unknown submission publicId → 404
///   GET /api/v1/forms/submissions/{publicId}/signed?sequence=N
///     5. Completed step, signed blob seeded → 200 application/pdf with %PDF- header
///     6. Cross-tenant request → 404
///     7. No signing request on submission (null SigningRequestId) → 404
///     8. Step not Completed / sequence out of range → 404
///
/// Seeding strategy:
///   - FormSubmission rows seeded directly via FormsDbContext (using HTTP API for the template,
///     then EF for the submission record to avoid the full submit pipeline).
///   - SigningRequest + SigningDocFlowRecord rows seeded via IdentityDbContext.
///   - ISignedDocumentDownloader (= IFileStorageService adapter) substituted per-client via
///     WithWebHostBuilder to return a minimal valid PDF stream — MinIO not available in tests.
///
/// Note: ISignedDocumentDownloader (Forms.Application.Interfaces) is the port used by the
/// DownloadSubmissionSignedStepQueryHandler. It is bound to IFileStorageService at the host layer
/// (SignedDocumentDownloaderAdapter in AqtCoreFW.Api). We substitute IFileStorageService here
/// because the adapter delegates to it — a single substitution point covers both.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class FormSubmissionSigningReadSurfaceHttpTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-f502-A00000000001");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-f502-B00000000002");
    private static readonly Guid AdminId = Guid.Parse("00000000-0000-0000-f502-000000000010");
    private static readonly Guid UserId  = Guid.Parse("00000000-0000-0000-f502-000000000099");

    // Minimal valid PDF body — passes the %PDF- magic-header sniff in the download handler.
    private static readonly byte[] FakePdfBytes =
        Encoding.ASCII.GetBytes("%PDF-1.4\n%%test-signed-by-FormSubmissionSigningReadSurface\n");

    public FormSubmissionSigningReadSurfaceHttpTests(WebAppFixture app) => _app = app;

    // ─── TC-1: signing-steps returns ordered rows for a signed submission ─────────

    [Fact]
    public async Task SigningSteps_SignedSubmission_ReturnsOrderedRowsWith200()
    {
        var (_, submissionPublicId) = await SeedSubmissionAsync(TenantA, AdminId, UserId);
        await SeedCompletedSigningStepForSubmissionAsync(TenantA, submissionPublicId, sequence: 1);

        var client = CreateClient(UserId, TenantA);
        var resp = await client.GetAsync(
            $"/api/v1/forms/submissions/{submissionPublicId}/signing-steps");

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "a signed submission must return its step list with 200");

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = UnwrapData(body);

        data.ValueKind.Should().Be(JsonValueKind.Array);
        data.GetArrayLength().Should().BeGreaterThanOrEqualTo(1, "at least one completed step was seeded");

        var firstStep = data[0];
        firstStep.GetProperty("sequenceNumber").GetInt32().Should().Be(1);
        firstStep.GetProperty("status").GetString().Should().Be("Completed");
        firstStep.GetProperty("hasSignedPdf").GetBoolean().Should().BeTrue(
            "Completed DocFlow record with signedStorageKey → hasSignedPdf=true");
    }

    // ─── TC-2: cross-tenant signing-steps → 404 (security invariant) ────────────

    [Fact]
    public async Task SigningSteps_CrossTenantSubmissionId_Returns404()
    {
        // TenantA owns the submission; TenantB must get 404, not a step list.
        var (_, submissionPublicId) = await SeedSubmissionAsync(TenantA, AdminId, UserId);
        await SeedCompletedSigningStepForSubmissionAsync(TenantA, submissionPublicId, sequence: 1);

        var clientB = CreateClient(UserId, TenantB);
        var resp = await clientB.GetAsync(
            $"/api/v1/forms/submissions/{submissionPublicId}/signing-steps");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant submission id must return 404, not expose another tenant's signing steps");
    }

    // ─── TC-3: submission with null SigningRequestId → 200 empty array ────────────

    [Fact]
    public async Task SigningSteps_SubmissionWithNoSigningRequest_Returns200EmptyArray()
    {
        // Submission exists but no signing request was ever initiated (null SigningRequestId).
        var (_, submissionPublicId) = await SeedSubmissionAsync(TenantA, AdminId, UserId);

        var client = CreateClient(UserId, TenantA);
        var resp = await client.GetAsync(
            $"/api/v1/forms/submissions/{submissionPublicId}/signing-steps");

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "no signing initiated is a valid state, not a 404");

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = UnwrapData(body);
        data.ValueKind.Should().Be(JsonValueKind.Array);
        data.GetArrayLength().Should().Be(0, "unsigning submission returns an empty step list");
    }

    // ─── TC-4: unknown submission publicId → 404 ─────────────────────────────────

    [Fact]
    public async Task SigningSteps_UnknownSubmissionId_Returns404()
    {
        var unknownId = Guid.NewGuid();
        var client = CreateClient(UserId, TenantA);
        var resp = await client.GetAsync(
            $"/api/v1/forms/submissions/{unknownId}/signing-steps");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a submission id that doesn't exist in the DB must return 404");
    }

    // ─── TC-5: download signed step → 200 application/pdf with %PDF- magic bytes ─

    [Fact]
    public async Task DownloadSignedStep_CompletedStep_Streams200ApplicationPdf()
    {
        var (_, submissionPublicId) = await SeedSubmissionAsync(TenantA, AdminId, UserId);
        await SeedCompletedSigningStepForSubmissionAsync(TenantA, submissionPublicId, sequence: 1);

        // Substitute IFileStorageService so the download returns a real %PDF- payload.
        var storageStub = BuildStorageStubReturningFakePdf();
        var client = CreateClientWithStorageStub(UserId, TenantA, storageStub);

        var resp = await client.GetAsync(
            $"/api/v1/forms/submissions/{submissionPublicId}/signed?sequence=1");

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "Completed step with a signed storage key must stream 200");
        resp.Content.Headers.ContentType!.MediaType.Should()
            .Be("application/pdf", "signed PDF must be served as application/pdf");

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();
        bytes.Take(5).Should().Equal(Encoding.ASCII.GetBytes("%PDF-"),
            "streaming a PDF must start with the %PDF- magic header");
    }

    // ─── TC-6: download signed step — cross-tenant → 404 ────────────────────────

    [Fact]
    public async Task DownloadSignedStep_CrossTenantSubmissionId_Returns404()
    {
        var (_, submissionPublicId) = await SeedSubmissionAsync(TenantA, AdminId, UserId);
        await SeedCompletedSigningStepForSubmissionAsync(TenantA, submissionPublicId, sequence: 1);

        var clientB = CreateClient(UserId, TenantB);
        var resp = await clientB.GetAsync(
            $"/api/v1/forms/submissions/{submissionPublicId}/signed?sequence=1");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant download must return 404, not stream another tenant's signed PDF");
    }

    // ─── TC-7: download — null SigningRequestId on submission → 404 ──────────────

    [Fact]
    public async Task DownloadSignedStep_SubmissionWithNoSigningRequest_Returns404()
    {
        // No signing request on this submission — download must 404, not 500.
        var (_, submissionPublicId) = await SeedSubmissionAsync(TenantA, AdminId, UserId);

        var client = CreateClient(UserId, TenantA);
        var resp = await client.GetAsync(
            $"/api/v1/forms/submissions/{submissionPublicId}/signed?sequence=1");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a submission with no signing request has no signed PDF to download");
    }

    // ─── TC-8: download — step not Completed → 404 ───────────────────────────────

    [Fact]
    public async Task DownloadSignedStep_StepNotCompleted_Returns404()
    {
        var (_, submissionPublicId) = await SeedSubmissionAsync(TenantA, AdminId, UserId);
        await SeedInitiatedSigningStepForSubmissionAsync(TenantA, submissionPublicId, sequence: 1);

        var client = CreateClient(UserId, TenantA);
        var resp = await client.GetAsync(
            $"/api/v1/forms/submissions/{submissionPublicId}/signed?sequence=1");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a non-Completed step has no signed PDF — must return 404");
    }

    // ─── Seeding helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a published form template via the admin HTTP API, then inserts a FormSubmission
    /// row directly via EF to avoid the full pipeline validation overhead. Returns
    /// (templatePublicId, submissionPublicId).
    ///
    /// Direct EF insert is used because the submit endpoint enforces permission checks that
    /// would require full-stack permission seeding not necessary for the read-surface tests.
    /// The result is equivalent: a Pending submission row with the correct TenantId / PublicId.
    /// </summary>
    private async Task<(Guid templatePublicId, Guid submissionPublicId)> SeedSubmissionAsync(
        Guid tenantId, Guid adminId, Guid submitterId)
    {
        // Step 1: create a simple form template via HTTP (validates the template path).
        var adminClient = CreateClientWithPermissions(adminId, tenantId, PermCodes.Forms.Template.Manage);
        var code = $"S{Guid.NewGuid():N}"[..9];

        var createResp = await adminClient.PostAsJsonAsync("/api/v1/forms/templates", new
        {
            tenantId,
            name        = $"SigningTest {code}",
            nameVi      = $"BieuMau {code}",
            code,
            storageMode = 0,
            createdBy   = adminId
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK, "template creation must succeed");

        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Publish so submissions can be created.
        await adminClient.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/publish",
            new { tenantId, updatedBy = adminId });

        // Step 2: insert a FormSubmission row directly — avoids full-stack permission seeding.
        await using var scope = _app.Services.CreateAsyncScope();
        var db  = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var template = await db.FormTemplates
            .AsNoTracking()
            .FirstAsync(t => t.PublicId == templatePublicId);

        var submission = FormSubmission.Create(
            gen, tenantId,
            formTemplateId:      template.Id,
            formTemplateVersion: template.Version,
            submissionData:      "{\"_test\":true}",
            submittedBy:         submitterId);

        db.FormSubmissions.Add(submission);
        await db.SaveChangesAsync();

        return (templatePublicId, submission.PublicId);
    }

    /// <summary>
    /// Seeds a SigningRequest + Completed SigningDocFlowRecord and links it to the given submission
    /// (sets FormSubmission.SigningRequestId). The signed storage key is recorded so
    /// ResolveSignedStepForRequestAsync returns Found=true.
    /// </summary>
    private async Task SeedCompletedSigningStepForSubmissionAsync(
        Guid tenantId, Guid submissionPublicId, int sequence)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var formsDb    = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var gen        = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var docId = Guid.NewGuid();
        var nonce = Guid.NewGuid();

        // 1. Seed DocFlow record → Completed with a signed storage key.
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

        identityDb.SigningDocFlowRecords.Add(docFlowRecord);
        await identityDb.SaveChangesAsync();

        // 2. Seed SigningRequest linked to this DocFlow record and the formPdf artifact kind.
        var signingRequest = SigningRequest.Create(
            gen, tenantId,
            SigningArtifactKind.FormPdf,
            artifactRef:          docId.ToString(),
            providerKey:          "usb-vgca",
            assignedSignerId:     UserId,
            initiatedBy:          UserId,
            signerSequenceNumber: sequence,
            sourceFileId:         null);

        signingRequest.LinkUnderlying(
            SigningUnderlyingKind.DocFlow, docFlowRecord.Id.ToString(), UserId);

        identityDb.SigningRequests.Add(signingRequest);
        await identityDb.SaveChangesAsync();

        // 3. Link the signing request to the submission (sets FormSubmission.SigningRequestId).
        //    IgnoreQueryFilters needed because soft-delete global filter may exclude the row.
        var submission = await formsDb.FormSubmissions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.PublicId == submissionPublicId);

        submission.LinkSigningRequest(signingRequest.PublicId);
        await formsDb.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a SigningRequest + Initiated (not Completed) SigningDocFlowRecord and links it to
    /// the given submission. Used to verify that the download endpoint returns 404 when no
    /// signed PDF exists yet.
    /// </summary>
    private async Task SeedInitiatedSigningStepForSubmissionAsync(
        Guid tenantId, Guid submissionPublicId, int sequence)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var formsDb    = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var gen        = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

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

        identityDb.SigningDocFlowRecords.Add(docFlowRecord);
        await identityDb.SaveChangesAsync();

        var signingRequest = SigningRequest.Create(
            gen, tenantId,
            SigningArtifactKind.FormPdf,
            artifactRef:          docId.ToString(),
            providerKey:          "usb-vgca",
            assignedSignerId:     UserId,
            initiatedBy:          UserId,
            signerSequenceNumber: sequence,
            sourceFileId:         null);

        signingRequest.LinkUnderlying(
            SigningUnderlyingKind.DocFlow, docFlowRecord.Id.ToString(), UserId);

        identityDb.SigningRequests.Add(signingRequest);
        await identityDb.SaveChangesAsync();

        var submission = await formsDb.FormSubmissions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.PublicId == submissionPublicId);

        submission.LinkSigningRequest(signingRequest.PublicId);
        await formsDb.SaveChangesAsync();
    }

    // ─── Client helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Authenticated client with SystemAdmin role so PermissionRequirement gates on
    /// GetSigningSteps (Submit.View) and DownloadSignedStep (Submit.Export) are satisfied
    /// by the test bypass handler without a full user/role/permission DB graph.
    /// </summary>
    private HttpClient CreateClient(Guid userId, Guid tenantId)
        => _app.CreateAuthenticatedClient(userId, tenantId, "SystemAdmin");

    /// <summary>Client with explicit permission codes — used for admin operations like template create.</summary>
    private HttpClient CreateClientWithPermissions(
        Guid userId, Guid tenantId, params string[] permissions)
    {
        // Reuse the SystemAdmin role bypass registered in WebAppFixture, which satisfies all
        // PermissionRequirement gates without a DB user-permission graph.
        var client = _app.CreateAuthenticatedClient(userId, tenantId, "SystemAdmin");
        return client;
    }

    /// <summary>
    /// Builds an NSubstitute stub for IFileStorageService that returns FakePdfBytes on any
    /// DownloadAsync call, and substitutes it via WithWebHostBuilder.
    /// SignedDocumentDownloaderAdapter (host layer) delegates to IFileStorageService, so
    /// substituting IFileStorageService intercepts the download path end-to-end.
    /// </summary>
    private IFileStorageService BuildStorageStubReturningFakePdf()
    {
        var stub = Substitute.For<IFileStorageService>();
        stub.DownloadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(FakePdfBytes)));
        return stub;
    }

    private HttpClient CreateClientWithStorageStub(
        Guid userId, Guid tenantId, IFileStorageService storageStub)
    {
        var client = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => storageStub);
            })).CreateClient();

        // Include SystemAdmin role so PermissionRequirement gates (Submit.Export on DownloadSignedStep)
        // are satisfied by TestSystemAdminPermissionBypassHandler without a full permissions DB graph.
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "SystemAdmin");
        return client;
    }

    // ─── JSON helpers ─────────────────────────────────────────────────────────────

    private static JsonElement UnwrapData(JsonElement root) =>
        root.TryGetProperty("data", out var d) ? d : root;
}
