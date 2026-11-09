using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// End-to-end integration coverage for the authed DigitalSignature submit path:
/// HTTP base64 signature → command mapping → verify-at-ingest → cert-bind → SQL persistence.
///
/// The unit suite (SubmitFormCommandHandlerTests) already exhausts the handler decision logic
/// against a mocked signing client. These tests add what unit-with-mocks cannot: the real DI
/// wiring, the controller's base64→SignaturePayload mapping, the six evidence columns landing in
/// SQL Server, and the hard-block producing a real 422 with zero persisted rows.
///
/// IIdentitySigningClient is substituted at the Forms boundary on purpose — the cryptographic
/// verify + binding-guard verdict is Identity's concern and is covered by Identity's own tests
/// (InProcessIdentitySigningClientTests / SignerBindingGuard). Here we control the verdict and
/// assert Forms' use of the contract: server-resolved SignerUserId, evidence persistence, and the
/// all-or-nothing block.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class FormDigitalSignatureSubmitTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId  = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();

    // FormFieldType.DigitalSignature = 8 (no JsonStringEnumConverter registered on the wire).
    private const int DigitalSignatureFieldType = 8;
    private const string SigFieldKey = "signature";

    public FormDigitalSignatureSubmitTests(WebAppFixture app) => _app = app;

    // ── Happy path: valid signature persists with all six evidence columns ──────────

    [Fact]
    public async Task Submit_WithValidSignature_Returns200AndPersistsAllEvidenceColumns()
    {
        var templatePublicId = await SeedRequiredSignatureTemplateAsync();

        // Controlled "valid" verdict + capture of the verify request to assert the binding E2E.
        var verified = new SignatureVerifyResult
        {
            IsValid           = true,
            SignerSubject     = "CN=Nguyen Van A, OID.2.5.4.5=079201001234",
            SignerSerial      = "SERIAL-INT-001",
            SignerCertDer     = [0xAB, 0xCD, 0xEF],
            ChainEvidenceJson = "{\"chain\":[\"root\",\"leaf\"]}",
            VerifiedAt        = DateTimeOffset.UtcNow
        };
        var signing = Substitute.For<IIdentitySigningClient>();
        signing.VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(verified));
        SignatureVerifyRequest? capturedReq = null;
        await signing.VerifySignatureAsync(Arg.Do<SignatureVerifyRequest>(r => capturedReq = r), Arg.Any<CancellationToken>());

        var user = CreateClient(UserId, TenantId, signing, PermCodes.Forms.Submit.Create, PermCodes.Forms.Submit.View);

        var submitResp = await user.PostAsJsonAsync("/api/v1/forms/submissions", new
        {
            formTemplatePublicId = templatePublicId,
            // Required-field validation is NOT skipped for DigitalSignature → the field key must
            // carry a non-empty value to pass validation; the crypto proof rides in `signature`.
            data      = new Dictionary<string, object?> { [SigFieldKey] = "signed" },
            signature = new
            {
                signatureBase64   = Convert.ToBase64String([0x01, 0x02, 0x03]),
                signerCertBase64  = Convert.ToBase64String([0xAB, 0xCD, 0xEF]),
                format            = "Raw",
                hashAlg           = "SHA256",
                encode            = "RSA",
                providerKey       = "usb-vgca"
            }
        });

        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var submissionId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        // Anti-impersonation: the server must bind the cert to the authenticated submitter.
        capturedReq.Should().NotBeNull();
        capturedReq!.SignerUserId.Should().Be(UserId, "the verify request must bind to the authenticated submitter");
        capturedReq.TenantId.Should().Be(TenantId, "TenantId is server-resolved from the JWT, never the client bundle");

        // The six evidence columns must land in SQL (not merely AddAsync being invoked).
        var row = await ReadSubmissionAsync(submissionId);
        row.Should().NotBeNull();
        row!.SignatureVerdict.Should().Be(true);
        row.SignerSubject.Should().Be(verified.SignerSubject);
        row.SignerSerial.Should().Be(verified.SignerSerial);
        row.SignerCertDer.Should().BeEquivalentTo(verified.SignerCertDer);
        row.SignatureChainEvidence.Should().Be(verified.ChainEvidenceJson);
        row.SignatureVerifiedAt.Should().NotBeNull();
    }

    // ── Hard-block: verify failure → 422 GOV_FRM_022, zero persisted rows ───────────

    [Fact]
    public async Task Submit_VerifyFails_Returns422Gov022AndPersistsNoRow()
    {
        var templatePublicId = await SeedRequiredSignatureTemplateAsync();

        var signing = Substitute.For<IIdentitySigningClient>();
        signing.VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SignatureVerifyResult
            {
                IsValid = false, FailureReason = "cert does not bind to the assigned signer"
            }));

        var user = CreateClient(UserId, TenantId, signing, PermCodes.Forms.Submit.Create);

        var submitResp = await user.PostAsJsonAsync("/api/v1/forms/submissions", new
        {
            formTemplatePublicId = templatePublicId,
            data      = new Dictionary<string, object?> { [SigFieldKey] = "signed" },
            signature = new
            {
                signatureBase64  = Convert.ToBase64String([0x01, 0x02, 0x03]),
                signerCertBase64 = Convert.ToBase64String([0xAB, 0xCD])
            }
        });

        submitResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await submitResp.Content.ReadAsStringAsync()).Should().Contain("GOV_FRM_022");

        // All-or-nothing: a failed verify must leave no submission row behind.
        (await CountSubmissionsAsync(templatePublicId)).Should().Be(0);
    }

    // ── Hard-block: required signature absent → 422 GOV_FRM_021, zero persisted rows ─

    [Fact]
    public async Task Submit_RequiredSignatureMissing_Returns422Gov021AndPersistsNoRow()
    {
        var templatePublicId = await SeedRequiredSignatureTemplateAsync();

        // Substitute the signing client so the "verify is never called" claim is self-enforcing:
        // the required-missing gate must short-circuit before any verify call. If a future refactor
        // reordered the gates, the DidNotReceive assertion below would fail loudly.
        var signing = Substitute.For<IIdentitySigningClient>();
        var user = CreateClient(UserId, TenantId, signing, PermCodes.Forms.Submit.Create);

        var submitResp = await user.PostAsJsonAsync("/api/v1/forms/submissions", new
        {
            formTemplatePublicId = templatePublicId,
            // Field value present (passes data validation) but the signature proof bundle is omitted.
            data = new Dictionary<string, object?> { [SigFieldKey] = "signed" }
        });

        submitResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await submitResp.Content.ReadAsStringAsync()).Should().Contain("GOV_FRM_021");

        (await CountSubmissionsAsync(templatePublicId)).Should().Be(0);
        await signing.DidNotReceive().VerifySignatureAsync(
            Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates → adds a required DigitalSignature field → publishes a Json template via the admin
    /// API, returning the template's T2 PublicId (Guid AK).
    /// </summary>
    private async Task<Guid> SeedRequiredSignatureTemplateAsync()
    {
        var admin = CreateClient(AdminId, TenantId, signing: null, PermCodes.Forms.Template.Manage);
        var code  = $"S{Guid.NewGuid():N}"[..9];

        var createResp = await admin.PostAsJsonAsync("/api/v1/forms/templates", new
        {
            tenantId    = TenantId,
            name        = $"Signature Form {code}",
            nameVi      = $"Biểu mẫu chữ ký {code}",
            code,
            storageMode = 0,
            createdBy   = AdminId
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var addFieldResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields", new
            {
                tenantId     = TenantId,
                fieldKey     = SigFieldKey,
                type         = DigitalSignatureFieldType,
                labelEn      = "Digital Signature",
                labelVi      = "Chữ ký số",
                displayOrder = 1,
                required     = true,
                updatedBy    = AdminId
            });
        addFieldResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var publishResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/publish",
            new { tenantId = TenantId, updatedBy = AdminId });
        publishResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return templatePublicId;
    }

    /// <summary>
    /// Builds an authenticated client that grants exactly <paramref name="permissions"/> to
    /// <paramref name="userId"/> and, when supplied, substitutes <see cref="IIdentitySigningClient"/>
    /// on that server instance. WithWebHostBuilder clones the server per call but shares the
    /// Testcontainers SQL database, so writes are visible through <see cref="WebAppFixture.Services"/>.
    /// </summary>
    private HttpClient CreateClient(
        Guid userId, Guid tenantId, IIdentitySigningClient? signing, params string[] permissions)
    {
        var permService = Substitute.For<IEffectivePermissionService>();
        permService
            .GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string>(permissions)
            });
        permService
            .GetSummaryAsync(Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = Guid.Empty,
                PermissionCodes = new HashSet<string>()
            });

        var client = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => permService);
                if (signing is not null)
                    services.AddScoped(_ => signing);
            })).CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        return client;
    }

    /// <summary>Reads a persisted submission by its Guid PublicId, bypassing soft-delete filters.</summary>
    private async Task<FormSubmission?> ReadSubmissionAsync(Guid submissionPublicId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        return await db.FormSubmissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.PublicId == submissionPublicId);
    }

    /// <summary>Counts persisted (including soft-deleted) submissions for a template's PublicId.</summary>
    private async Task<int> CountSubmissionsAsync(Guid templatePublicId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var template = await db.FormTemplates
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(t => t.PublicId == templatePublicId);
        return await db.FormSubmissions
            .IgnoreQueryFilters().AsNoTracking()
            .CountAsync(s => s.FormTemplateId == template.Id);
    }
}
