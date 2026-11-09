using AqtCoreFW.Api;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// FORMS-CONSENT-PRE-SPAN-ORPHAN: proves the PDPL consent write commits/rolls back ATOMICALLY with
/// the form submission. Before the fix, <c>ConsentRepository.SaveChangesAsync</c> ran pre-span on its
/// own IdentityDbContext transaction, so a rolled-back submit left an orphaned consent row. Now
/// ConsentRepository enlists IdentityDbContext onto the same <c>ICrossModuleUnitOfWork</c> the
/// submission uses, so consent + submission + outbox commit or roll back together.
///
/// Consent is exercised on BOTH submit paths. The atomicity (happy + rollback) proof runs through the
/// PUBLIC endpoint (<c>SubmitPublicFormCommand</c>, ITenantAgnostic + carries ConsentGiven). The
/// authenticated endpoint (<c>SubmitFormRequest</c> → <c>SubmitFormCommand</c>) now also carries
/// ConsentGiven (AUTH-SUBMIT-NO-CONSENTGIVEN fix — the FE modal always sent it but the DTO dropped it),
/// so its two tests here prove a consented PII submit succeeds and an unconsented one is still rejected
/// (GOV_FRM_020 gate preserved, no bypass). Both handlers run the identical ConsentRepository enlist.
///
/// Real SQL Server integration DB (<see cref="WebAppFixture"/>) — the cross-schema (identity + forms)
/// single-local-transaction behaviour cannot be exercised on InMemory/mocked EF. Each test uses its OWN
/// random TenantId so consent-row counts (ConsentRecord carries no template FK) are test-scoped.
///
/// PII fields cannot be created via the HTTP <c>/fields</c> endpoint (its request has no isPii), so the
/// template is built through the real create/field/publish endpoints and the field's IsPii flag is then
/// flipped by a targeted UPDATE — the submit path reads it fresh per request.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class FormSubmissionConsentAtomicityTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public FormSubmissionConsentAtomicityTests(WebAppFixture app) => _app = app;

    // --- Happy path (public): consent + PII → consent recorded, linked, committed cross-schema ---

    [Fact]
    public async Task PublicSubmit_WithConsentAndPii_RecordsAndLinksConsent()
    {
        var tenantId = Guid.NewGuid();
        var adminId  = Guid.NewGuid();
        var admin = CreateClientWithPermissions(_app, adminId, tenantId, PermCodes.Forms.Template.Manage);
        var code  = $"PB{Guid.NewGuid():N}"[..9];

        await CreateActivePiiTemplateAsync(admin, tenantId, adminId, code);

        var anon = _app.CreateClient();
        var submitResp = await anon.PostAsJsonAsync(
            $"/api/v1/public/forms/{code}/submit",
            new { consentGiven = true, data = new Dictionary<string, object?> { ["ssn"] = "987654321" } });
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var submissionId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        await using var scope = _app.Services.CreateAsyncScope();
        var formsDb    = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var submission = await formsDb.FormSubmissions.FirstOrDefaultAsync(s => s.PublicId == submissionId);
        submission.Should().NotBeNull("the submission must persist");
        submission!.ConsentRecordId.Should().NotBeNull("submission must be linked to the consent record");

        // ConsentRecordId holds the consent PublicId (external Guid AK returned by RecordConsentAsync).
        var consent = await identityDb.ConsentRecords
            .FirstOrDefaultAsync(c => c.PublicId == submission.ConsentRecordId);
        consent.Should().NotBeNull("the enlisted consent write must have committed with the submission");
        consent!.DataSubjectId.Should().Be(Guid.Empty, "anonymous public consent uses Guid.Empty subject");
        consent.TenantId.Should().Be(tenantId);

        var consentCount = await identityDb.ConsentRecords.CountAsync(c => c.TenantId == tenantId);
        consentCount.Should().Be(1, "exactly one consent row for this tenant/submit");
    }

    // --- Rollback (THE KEY PROOF): an in-span failure AFTER consent rolls BOTH back together ---

    [Fact]
    public async Task PublicSubmit_ConsentThenInSpanFailure_RollsBackConsentAndSubmission()
    {
        var tenantId = Guid.NewGuid();
        var adminId  = Guid.NewGuid();
        var admin = CreateClientWithPermissions(_app, adminId, tenantId, PermCodes.Forms.Template.Manage);
        var code  = $"RB{Guid.NewGuid():N}"[..9];

        var (templatePublicId, templateInternalId) = await CreateActivePiiTemplateAsync(admin, tenantId, adminId, code);

        // Force a failure INSIDE the atomic span, AFTER consent is recorded and the submission is added
        // (the public Json path calls outboxWriter.EnqueueAsync last, before CommitAsync). The throwing
        // writer only fails the "forms" enqueue and passes non-forms (audit) through, so it cannot cascade.
        var throwingApp = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOutboxWriter>();
                services.AddScoped<IOutboxWriter, ThrowingFormsOutboxWriter>();
            }));
        var anon = throwingApp.CreateClient();

        var submitResp = await anon.PostAsJsonAsync(
            $"/api/v1/public/forms/{code}/submit",
            new { consentGiven = true, data = new Dictionary<string, object?> { ["ssn"] = "111222333" } });
        submitResp.StatusCode.Should().Be(HttpStatusCode.InternalServerError,
            "the outbox enqueue throws unhandled inside the span → 500; the span rolls back atomically");

        await using var scope = _app.Services.CreateAsyncScope();
        var formsDb    = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var submissionCount = await formsDb.FormSubmissions
            .IgnoreQueryFilters()
            .CountAsync(s => s.FormTemplateId == templateInternalId);
        submissionCount.Should().Be(0, "a rolled-back submit must not leave an orphan FormSubmission");

        // THE PROOF: consent scoped to this test's tenant must be zero — before the fix it would be 1.
        var consentCount = await identityDb.ConsentRecords.CountAsync(c => c.TenantId == tenantId);
        consentCount.Should().Be(0,
            "FORMS-CONSENT-PRE-SPAN-ORPHAN: the consent write was enlisted onto the submit transaction, " +
            "so a rolled-back submit leaves NO orphaned consent record");

        var outboxCount = await formsDb.OutboxMessages
            .CountAsync(m => m.Payload.Contains(templatePublicId.ToString()));
        outboxCount.Should().Be(0, "no outbox event may survive a rolled-back submission");
    }

    // --- Guard: no PII field → consent path skipped even with ConsentGiven=true (no orphan possible) ---

    [Fact]
    public async Task PublicSubmit_NoPiiField_DoesNotRecordConsent()
    {
        var tenantId = Guid.NewGuid();
        var adminId  = Guid.NewGuid();
        var admin = CreateClientWithPermissions(_app, adminId, tenantId, PermCodes.Forms.Template.Manage);
        var code  = $"NP{Guid.NewGuid():N}"[..9];

        // Non-PII template (no IsPii flip) via the proven HTTP path.
        await CreateActiveJsonTemplateAsync(admin, tenantId, adminId, code, requiresConsent: false);

        var anon = _app.CreateClient();
        var submitResp = await anon.PostAsJsonAsync(
            $"/api/v1/public/forms/{code}/submit",
            new { consentGiven = true, data = new Dictionary<string, object?> { ["fullName"] = "John" } });
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var submissionId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        await using var scope = _app.Services.CreateAsyncScope();
        var formsDb    = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var submission = await formsDb.FormSubmissions.FirstOrDefaultAsync(s => s.PublicId == submissionId);
        submission!.ConsentRecordId.Should().BeNull("no PII fields → consent is never recorded");
        (await identityDb.ConsentRecords.CountAsync(c => c.TenantId == tenantId)).Should().Be(0);
    }

    // --- Guard: PII + RequiresConsent + ConsentGiven=false → GOV_FRM_020 rejection, no consent row ---

    [Fact]
    public async Task PublicSubmit_PiiRequiresConsent_ConsentGivenFalse_RejectsAndRecordsNoConsent()
    {
        var tenantId = Guid.NewGuid();
        var adminId  = Guid.NewGuid();
        var admin = CreateClientWithPermissions(_app, adminId, tenantId, PermCodes.Forms.Template.Manage);
        var code  = $"VG{Guid.NewGuid():N}"[..9];

        await CreateActivePiiTemplateAsync(admin, tenantId, adminId, code);

        var anon = _app.CreateClient();
        var submitResp = await anon.PostAsJsonAsync(
            $"/api/v1/public/forms/{code}/submit",
            new { consentGiven = false, data = new Dictionary<string, object?> { ["ssn"] = "123456789" } });

        submitResp.IsSuccessStatusCode.Should().BeFalse(
            "a PII field on a consent-requiring template must be rejected without consent (GOV_FRM_020)");
        var body = await submitResp.Content.ReadAsStringAsync();
        body.Should().Contain("GOV_FRM_020");

        await using var scope = _app.Services.CreateAsyncScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await identityDb.ConsentRecords.CountAsync(c => c.TenantId == tenantId)).Should().Be(0,
            "a validation-rejected submit records no consent");
    }

    // --- Authenticated path (AUTH-SUBMIT-NO-CONSENTGIVEN): the DTO now carries ConsentGiven ---

    // The fix proof: before SubmitFormRequest gained ConsentGiven, an authenticated consented PII submit
    // returned 422 (the FE-supplied consentGiven was silently dropped by JSON deserialization).
    [Fact]
    public async Task AuthenticatedSubmit_WithConsentAndPii_RecordsAndLinksConsent()
    {
        var tenantId = Guid.NewGuid();
        var userId   = Guid.NewGuid();
        var client = CreateClientWithPermissions(
            _app, userId, tenantId, PermCodes.Forms.Template.Manage, PermCodes.Forms.Submit.Create);
        var code = $"AC{Guid.NewGuid():N}"[..9];

        var (templatePublicId, _) = await CreateActivePiiTemplateAsync(client, tenantId, userId, code);

        var submitResp = await client.PostAsJsonAsync(
            "/api/v1/forms/submissions",
            new
            {
                formTemplatePublicId = templatePublicId,
                consentGiven = true,
                data = new Dictionary<string, object?> { ["ssn"] = "987654321" }
            });
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "the authenticated DTO now carries ConsentGiven, so a consented PII submit succeeds (was 422)");
        var submissionId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        await using var scope = _app.Services.CreateAsyncScope();
        var formsDb    = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var submission = await formsDb.FormSubmissions.FirstOrDefaultAsync(s => s.PublicId == submissionId);
        submission.Should().NotBeNull("the submission must persist");
        submission!.ConsentRecordId.Should().NotBeNull("submission must be linked to the consent record");

        var consent = await identityDb.ConsentRecords
            .FirstOrDefaultAsync(c => c.PublicId == submission.ConsentRecordId);
        consent.Should().NotBeNull("the enlisted consent write must have committed with the submission");
        consent!.DataSubjectId.Should().Be(userId,
            "authenticated consent records the submitting user as the data subject");
        consent.TenantId.Should().Be(tenantId);
        (await identityDb.ConsentRecords.CountAsync(c => c.TenantId == tenantId)).Should().Be(1,
            "exactly one consent row for this tenant/submit");
    }

    // Gate preserved: wiring the flag must NOT weaken the GOV_FRM_020 rejection when consent is withheld.
    [Fact]
    public async Task AuthenticatedSubmit_PiiRequiresConsent_ConsentGivenFalse_RejectsAndRecordsNoConsent()
    {
        var tenantId = Guid.NewGuid();
        var userId   = Guid.NewGuid();
        var client = CreateClientWithPermissions(
            _app, userId, tenantId, PermCodes.Forms.Template.Manage, PermCodes.Forms.Submit.Create);
        var code = $"AV{Guid.NewGuid():N}"[..9];

        var (templatePublicId, _) = await CreateActivePiiTemplateAsync(client, tenantId, userId, code);

        var submitResp = await client.PostAsJsonAsync(
            "/api/v1/forms/submissions",
            new
            {
                formTemplatePublicId = templatePublicId,
                consentGiven = false,
                data = new Dictionary<string, object?> { ["ssn"] = "123456789" }
            });

        submitResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "a PII+consent template without consent must be rejected 422 (GOV_FRM_020) on the authenticated path too");
        (await submitResp.Content.ReadAsStringAsync()).Should().Contain("GOV_FRM_020");

        await using var scope = _app.Services.CreateAsyncScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await identityDb.ConsentRecords.CountAsync(c => c.TenantId == tenantId)).Should().Be(0,
            "a validation-rejected submit records no consent");
    }

    // --- Helpers ---

    /// <summary>
    /// Creates a published (Active) Json template with a REQUIRED PII field ("ssn"), consent required.
    /// Uses the real create/field/publish endpoints (proven harness) then flips the field's IsPii flag —
    /// the HTTP /fields request has no isPii, so it is set by a targeted UPDATE the submit path reads fresh.
    /// Returns (publicId, internalId) so callers can scope submission counts by template.
    /// </summary>
    private async Task<(Guid publicId, long internalId)> CreateActivePiiTemplateAsync(
        HttpClient admin, Guid tenantId, Guid adminId, string code)
    {
        var publicId = await CreateActiveJsonTemplateAsync(admin, tenantId, adminId, code, requiresConsent: true, fieldKey: "ssn");

        await using var scope = _app.Services.CreateAsyncScope();
        var formsDb = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var internalId = await formsDb.FormTemplates
            .Where(t => t.PublicId == publicId).Select(t => t.Id).FirstAsync();

        // Field-add HTTP request cannot set isPii; flip it here so hasPiiFields is true on submit.
        // Also set IsPublic=1 for public bypass read (by-Code resolution requires IsPublic + public_forms_bypass flag).
        await formsDb.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE forms.FormFields SET IsPii = 1 WHERE FormTemplateId = {internalId} AND FieldKey = 'ssn'; UPDATE forms.FormTemplates SET IsPublic = 1 WHERE Id = {internalId}");

        return (publicId, internalId);
    }

    private async Task<Guid> CreateActiveJsonTemplateAsync(
        HttpClient admin, Guid tenantId, Guid adminId, string code,
        bool requiresConsent, string fieldKey = "fullName")
    {
        var createResp = await admin.PostAsJsonAsync("/api/v1/forms/templates", new
        {
            tenantId,
            name = $"Consent Atomicity {code}",
            nameVi = $"Consent Atomicity {code}",
            code,
            storageMode = 0, // StorageMode.Json
            createdBy = adminId,
            requiresConsent
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var addFieldResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId,
                fieldKey,
                type = 0, // FormFieldType.Text
                labelEn = "Field",
                labelVi = "Trường",
                displayOrder = 1,
                required = true,
                updatedBy = adminId
            });
        addFieldResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var publishResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/publish",
            new { tenantId, updatedBy = adminId });
        publishResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Set IsPublic=1 for public bypass read (by-Code resolution requires IsPublic + public_forms_bypass flag).
        await using var scope = _app.Services.CreateAsyncScope();
        var formsDb = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var internalId = await formsDb.FormTemplates
            .Where(t => t.PublicId == templatePublicId).Select(t => t.Id).FirstAsync();
        await formsDb.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE forms.FormTemplates SET IsPublic = 1 WHERE Id = {internalId}");

        return templatePublicId;
    }

    private static HttpClient CreateClientWithPermissions(
        WebApplicationFactory<Program> factory, Guid userId, Guid tenantId, params string[] permissions)
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

        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped(_ => permService)))
            .CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        return client;
    }

    /// <summary>
    /// Fails only the "forms" outbox enqueue (the submit span's last write before commit), forcing a
    /// rollback AFTER consent + submission were flushed onto the shared transaction. Passes non-forms
    /// schemas (e.g. "audit") through so a post-failure audit cannot cascade into a second throw.
    /// </summary>
    private sealed class ThrowingFormsOutboxWriter : IOutboxWriter
    {
        public Task EnqueueAsync(IExternalDomainEvent evt, string schemaName, CancellationToken ct = default) =>
            string.Equals(schemaName, "forms", StringComparison.OrdinalIgnoreCase)
                ? throw new InvalidOperationException(
                    "Consent atomicity test seam — simulated outbox enqueue failure inside the submit span.")
                : Task.CompletedTask;
    }
}
