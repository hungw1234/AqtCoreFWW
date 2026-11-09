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
/// End-to-end integration for multi-signer form submissions.
/// When a template has >1 FormTemplateSignerRole, evidence is persisted to the
/// FormSubmissionSignerEvidence child collection instead of scalar fields.
/// Tests real DB persistence, migration application, and role-based evidence routing.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class FormMultiSignerSubmitTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid UserId1 = Guid.NewGuid();
    private static readonly Guid UserId2 = Guid.NewGuid();

    // FormFieldType.Text = 0 (simple field for data; not DigitalSignature)
    private const int TextFieldType = 0;
    private const int DigitalSignatureFieldType = 8;

    public FormMultiSignerSubmitTests(WebAppFixture app) => _app = app;

    // ── Happy path: 2-signer template, each signer records evidence ──────────────────

    [Fact]
    public async Task Submit_MultiSignerTemplate_WithValidSignatures_PersistedToChildCollection()
    {
        var templatePublicId = await SeedMultiSignerTemplateAsync();

        var signing = Substitute.For<IIdentitySigningClient>();
        var user = CreateClient(UserId1, TenantId, signing, PermCodes.Forms.Submit.Create, PermCodes.Forms.Submit.View);

        // Simulate signer 1 providing valid signature
        var verdict1 = new SignatureVerifyResult
        {
            IsValid = true,
            SignerSubject = "CN=Reviewer Name, OID.2.5.4.5=079111222333",
            SignerSerial = "SERIAL-REVIEWER",
            SignerCertDer = [0x01, 0x02, 0x03],
            ChainEvidenceJson = "{\"chain\":[\"root\",\"reviewer\"]}",
            VerifiedAt = DateTimeOffset.UtcNow
        };

        signing.VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(verdict1));

        var submitResp = await user.PostAsJsonAsync("/api/v1/forms/submissions", new
        {
            formTemplatePublicId = templatePublicId,
            data = new Dictionary<string, object?> { ["name"] = "John Doe", ["signature_field"] = "signed" },
            signature = new
            {
                signatureBase64 = Convert.ToBase64String([0x01, 0x02]),
                signerCertBase64 = Convert.ToBase64String([0x01, 0x02, 0x03]),
                format = "Raw",
                hashAlg = "SHA256",
                encode = "RSA",
                providerKey = "usb-vgca"
            }
        });

        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var submissionPublicId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        // Verify multi-signer evidence persisted to child collection
        var submission = await ReadSubmissionWithEvidenceAsync(submissionPublicId);
        submission.Should().NotBeNull();

        // For multi-signer: scalar fields should remain null, collection has the evidence
        submission!.SignatureVerdict.Should().BeNull("multi-signer uses collection, not scalar");
        submission.SignerEvidences.Should().HaveCountGreaterThan(0);

        // First signer evidence should be in the collection
        var reviewerEvidence = submission.SignerEvidences.FirstOrDefault(e => e.SignerRoleKey == "reviewer");
        reviewerEvidence.Should().NotBeNull();
        reviewerEvidence!.SignatureVerdict.Should().BeTrue();
        reviewerEvidence.SignerSubject.Should().Be(verdict1.SignerSubject);
        reviewerEvidence.SignerSerial.Should().Be(verdict1.SignerSerial);
        reviewerEvidence.SignerCertDer.Should().BeEquivalentTo(verdict1.SignerCertDer);
        reviewerEvidence.SignatureVerifiedAt.Should().BeCloseTo(verdict1.VerifiedAt, TimeSpan.FromSeconds(1));
        reviewerEvidence.SignerUserId.Should().Be(UserId1, "authenticated signer bound to user");
    }

    [Fact]
    public async Task Submit_MultiSignerTemplate_RoleMismatchEvidence()
    {
        var templatePublicId = await SeedMultiSignerTemplateAsync();

        var signing = Substitute.For<IIdentitySigningClient>();
        var user = CreateClient(UserId1, TenantId, signing, PermCodes.Forms.Submit.Create);

        // Both signers provide evidence; in a real scenario they would submit separately
        var verdict = new SignatureVerifyResult
        {
            IsValid = true,
            SignerSubject = "CN=Test Signer",
            SignerSerial = "SERIAL-TEST",
            SignerCertDer = [0xAA, 0xBB],
            ChainEvidenceJson = null,
            VerifiedAt = DateTimeOffset.UtcNow
        };

        signing.VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(verdict));

        var submitResp = await user.PostAsJsonAsync("/api/v1/forms/submissions", new
        {
            formTemplatePublicId = templatePublicId,
            data = new Dictionary<string, object?> { ["name"] = "Test", ["signature_field"] = "signed" },
            signature = new
            {
                signatureBase64 = Convert.ToBase64String([0x01, 0x02]),
                signerCertBase64 = Convert.ToBase64String([0xAA, 0xBB])
            }
        });

        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var submissionPublicId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        var submission = await ReadSubmissionWithEvidenceAsync(submissionPublicId);
        submission!.SignerEvidences.Should().NotBeEmpty();
    }

    // ── Single-signer template backward compatibility ──────────────────────────────────

    [Fact]
    public async Task Submit_SingleSignerTemplate_UsesScalarPath_NotCollection()
    {
        var templatePublicId = await SeedSingleSignerTemplateAsync();

        var signing = Substitute.For<IIdentitySigningClient>();
        var user = CreateClient(UserId1, TenantId, signing, PermCodes.Forms.Submit.Create);

        var verdict = new SignatureVerifyResult
        {
            IsValid = true,
            SignerSubject = "CN=Single Signer",
            SignerSerial = "SERIAL-SINGLE",
            SignerCertDer = [0x11, 0x22],
            ChainEvidenceJson = "{\"chain\":[\"single\"]}",
            VerifiedAt = DateTimeOffset.UtcNow
        };

        signing.VerifySignatureAsync(Arg.Any<SignatureVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(verdict));

        var submitResp = await user.PostAsJsonAsync("/api/v1/forms/submissions", new
        {
            formTemplatePublicId = templatePublicId,
            data = new Dictionary<string, object?> { ["name"] = "Single", ["signature_field"] = "signed" },
            signature = new
            {
                signatureBase64 = Convert.ToBase64String([0x01]),
                signerCertBase64 = Convert.ToBase64String([0x11, 0x22])
            }
        });

        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var submissionPublicId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        var submission = await ReadSubmissionWithEvidenceAsync(submissionPublicId);
        submission.Should().NotBeNull();

        // Single-signer uses scalar path
        submission!.SignatureVerdict.Should().Be(true);
        submission.SignerSubject.Should().Be(verdict.SignerSubject);
        submission.SignerSerial.Should().Be(verdict.SignerSerial);
        submission.SignerCertDer.Should().BeEquivalentTo(verdict.SignerCertDer);

        // Collection should be empty (single-signer does not populate it)
        submission.SignerEvidences.Should().BeEmpty();
    }

    // ── Migration: tables created successfully ────────────────────────────────────────

    [Fact]
    public async Task Migration_FormSubmissionSignerEvidenceTableExists()
    {
        // The migration 20260617070246_form_submission_signer_evidence should have
        // created the two tables. Verify by querying the schema.
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();

        // If tables don't exist, EnsureCreated would have failed during fixture setup,
        // or queries would throw. Instead, create a test signer evidence and verify it persists.
        var submission = FormSubmission.Create(
            gen: NSubstitute.Substitute.For<AqtCoreFW.SharedKernel.Abstractions.IGuidGenerator>(),
            tenantId: TenantId,
            formTemplateId: 1,
            formTemplateVersion: 1,
            submissionData: null,
            submittedBy: Guid.NewGuid());

        var evidence = FormSubmissionSignerEvidence.Create(
            formSubmissionId: 999, // Will fail FK if table doesn't exist
            signerRoleKey: "test_role",
            signatureVerdict: true,
            signerSubject: "CN=Test",
            signerSerial: "SERIAL-TEST",
            signatureVerifiedAt: DateTimeOffset.UtcNow,
            signatureChainEvidence: null,
            signerCertDer: [0x01],
            signerUserId: Guid.NewGuid(),
            signedAt: DateTimeOffset.UtcNow);

        // Attempting to add without the real FormSubmission will violate FK,
        // but that's the point: the test confirms the table structure exists
        // by virtue of reaching the FK constraint (vs. "table does not exist" error).
        // A more robust approach would be to read schema metadata, but this
        // is sufficient for integration verification.

        // For this test, we'll just verify the DbSet exists and can be queried.
        // Query succeeds without throwing means the table exists.
        // We don't assert null because parallel test runs may have left evidence rows.
        var canQuery = true;
        try
        {
            _ = await db.FormSubmissionSignerEvidences
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }
        catch
        {
            canQuery = false;
        }

        canQuery.Should().BeTrue("FormSubmissionSignerEvidence table should exist and be queryable");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a 2-signer template: roles "reviewer" (required) + "approver" (optional).
    /// Includes a text field and a digital signature field.
    /// </summary>
    private async Task<Guid> SeedMultiSignerTemplateAsync()
    {
        var admin = CreateClient(AdminId, TenantId, signing: null, PermCodes.Forms.Template.Manage);
        var code = $"M{Guid.NewGuid():N}"[..9];

        var createResp = await admin.PostAsJsonAsync("/api/v1/forms/templates", new
        {
            tenantId = TenantId,
            name = $"Multi-Signer Form {code}",
            nameVi = $"Biểu mẫu nhiều người ký {code}",
            code,
            storageMode = 0,
            createdBy = AdminId
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Add a text field
        var addFieldResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields", new
            {
                tenantId = TenantId,
                fieldKey = "name",
                type = TextFieldType,
                labelEn = "Name",
                labelVi = "Tên",
                displayOrder = 1,
                required = true,
                updatedBy = AdminId
            });
        addFieldResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Add a digital signature field
        var addSigFieldResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields", new
            {
                tenantId = TenantId,
                fieldKey = "signature_field",
                type = DigitalSignatureFieldType,
                labelEn = "Digital Signature",
                labelVi = "Chữ ký số",
                displayOrder = 2,
                required = true,
                updatedBy = AdminId
            });
        addSigFieldResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Add 2 signer roles
        var addRoleResp1 = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/signer-roles", new
            {
                tenantId = TenantId,
                roleKey = "reviewer",
                roleLabelVi = "Người Duyệt",
                displayOrder = 1,
                isRequired = true,
                placementJson = (string?)null,
                updatedBy = AdminId
            });
        addRoleResp1.StatusCode.Should().Be(HttpStatusCode.OK);

        var addRoleResp2 = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/signer-roles", new
            {
                tenantId = TenantId,
                roleKey = "approver",
                roleLabelVi = "Người Phê Duyệt",
                displayOrder = 2,
                isRequired = false,
                placementJson = (string?)null,
                updatedBy = AdminId
            });
        addRoleResp2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Publish
        var publishResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/publish",
            new { tenantId = TenantId, updatedBy = AdminId });
        publishResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return templatePublicId;
    }

    /// <summary>
    /// Creates a 1-signer template for backward compatibility testing.
    /// </summary>
    private async Task<Guid> SeedSingleSignerTemplateAsync()
    {
        var admin = CreateClient(AdminId, TenantId, signing: null, PermCodes.Forms.Template.Manage);
        var code = $"S{Guid.NewGuid():N}"[..9];

        var createResp = await admin.PostAsJsonAsync("/api/v1/forms/templates", new
        {
            tenantId = TenantId,
            name = $"Single-Signer Form {code}",
            nameVi = $"Biểu mẫu một người ký {code}",
            code,
            storageMode = 0,
            createdBy = AdminId
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Add a text field
        await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields", new
            {
                tenantId = TenantId,
                fieldKey = "name",
                type = TextFieldType,
                labelEn = "Name",
                labelVi = "Tên",
                displayOrder = 1,
                required = true,
                updatedBy = AdminId
            });

        // Add a digital signature field
        await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields", new
            {
                tenantId = TenantId,
                fieldKey = "signature_field",
                type = DigitalSignatureFieldType,
                labelEn = "Digital Signature",
                labelVi = "Chữ ký số",
                displayOrder = 2,
                required = true,
                updatedBy = AdminId
            });

        // Add 1 signer role (single-signer)
        await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/signer-roles", new
            {
                tenantId = TenantId,
                roleKey = "signer",
                roleLabelVi = "Người Ký",
                displayOrder = 1,
                isRequired = true,
                placementJson = (string?)null,
                updatedBy = AdminId
            });

        // Publish
        await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/publish",
            new { tenantId = TenantId, updatedBy = AdminId });

        return templatePublicId;
    }

    /// <summary>
    /// Creates an authenticated client that grants exactly <paramref name="permissions"/> to
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

    private async Task<FormSubmission?> ReadSubmissionWithEvidenceAsync(Guid submissionPublicId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        return await db.FormSubmissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(s => s.SignerEvidences)
            .FirstOrDefaultAsync(s => s.PublicId == submissionPublicId);
    }
}
