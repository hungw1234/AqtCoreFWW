using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Forms;

/// <summary>
/// Integration tests for FormTemplatesController — /api/v1/forms/templates
///
/// Routes (FormTemplate is T2 — addressed by Guid PublicId, not numeric PK):
///   GET  /api/v1/forms/templates                          → List (paged, any authenticated)
///   GET  /api/v1/forms/templates/{publicId:guid}          → GetById (any authenticated)
///   POST /api/v1/forms/templates                          → Create [Forms.Template.Manage]
///   POST /api/v1/forms/templates/{publicId:guid}/publish  → Publish [Forms.Template.Manage]
///
/// Tenant + identity resolved from JWT claims, not request body.
/// Response `data.id` is the Guid PublicId.
/// SchemaJson must be a valid JSON array of field objects with fieldId + type.
/// Templates start inactive (IsActive=false) until published.
/// </summary>
[Collection("Integration")]
public class FormTemplatesApiTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string BaseUrl = "/api/v1/forms/templates";

    // Minimal valid schema JSON required by FormValidationService
    private static readonly string ValidSchemaJson =
        """[{"fieldId":"applicantName","type":"text","label":"Applicant Name","required":true}]""";

    // -----------------------------------------------------------------
    // LIST
    // -----------------------------------------------------------------

    [Fact]
    public async Task ListFormTemplates_AsAuthenticatedUser_Returns200()
    {
        var tenantId = Guid.NewGuid();
        var response = await Client.GetAsync($"{BaseUrl}?tenantId={tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListFormTemplates_ResponseBody_WrappedInApiResponseEnvelope()
    {
        var tenantId = Guid.NewGuid();
        var response = await Client.GetAsync($"{BaseUrl}?tenantId={tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("data", out _).Should().BeTrue("response must be ApiResponse envelope");
    }

    [Fact]
    public async Task ListFormTemplates_Unauthenticated_Returns401Or403()
    {
        var tenantId = Guid.NewGuid();
        var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await anonClient.GetAsync($"{BaseUrl}?tenantId={tenantId}");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // -----------------------------------------------------------------
    // CREATE
    // -----------------------------------------------------------------

    [Fact]
    public async Task CreateFormTemplate_WithValidData_Returns200()
    {
        var tenantId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var client = CreateAuthenticatedClient(
            userId: createdBy.ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantId.ToString());

        var response = await client.PostAsJsonAsync(BaseUrl, new
        {
            TenantId = tenantId,
            Name = "Building Permit Application Form",
            NameVi = "Đơn xin cấp phép xây dựng",
            Code = "building_permit_form",
            StorageMode = 0, // Json
            SchemaJson = ValidSchemaJson,
            CreatedBy = createdBy,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("data", out var data).Should().BeTrue();
        data.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateFormTemplate_WithoutAdminRole_Returns403()
    {
        var tenantId = Guid.NewGuid();
        // CaseOfficer is not in [Authorize(Roles = "Admin,SystemAdmin")]
        var client = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(),
            roles: ["CaseOfficer"],
            tenantId: tenantId.ToString());

        var response = await client.PostAsJsonAsync(BaseUrl, new
        {
            TenantId = tenantId,
            Name = "Unauthorized Form",
            NameVi = "Form không có quyền",
            SchemaJson = ValidSchemaJson,
            CreatedBy = Guid.NewGuid(),
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // -----------------------------------------------------------------
    // GET BY ID
    // -----------------------------------------------------------------

    [Fact]
    public async Task GetFormTemplateById_ForNonExistentId_Returns404()
    {
        var tenantId = Guid.NewGuid();
        var nonExistentId = Guid.NewGuid();

        var response = await Client.GetAsync($"{BaseUrl}/{nonExistentId}?tenantId={tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetFormTemplateById_ForExistingTemplate_Returns200WithData()
    {
        var tenantId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var adminClient = CreateAuthenticatedClient(
            userId: createdBy.ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantId.ToString());

        // Create template first
        var createResp = await adminClient.PostAsJsonAsync(BaseUrl, new
        {
            TenantId = tenantId,
            Name = "Complaint Form Template",
            NameVi = "Mẫu đơn khiếu nại",
            Code = "complaint_form_template",
            StorageMode = 0, // Json
            SchemaJson = ValidSchemaJson,
            CreatedBy = createdBy,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = createBody.GetProperty("data").GetProperty("id").GetGuid();

        // Publish it so GetById returns it (templates start inactive — GetById may require IsActive=true)
        // If GetById returns inactive too, this publish step still ensures the template is accessible
        await adminClient.PostAsJsonAsync(
            $"{BaseUrl}/{templateId}/publish",
            new { TenantId = tenantId, UpdatedBy = createdBy });

        // GetById — any authenticated user can read
        var getResp = await adminClient.GetAsync($"{BaseUrl}/{templateId}?tenantId={tenantId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("id").GetGuid().Should().Be(templateId);
    }

    // -----------------------------------------------------------------
    // PUBLISH
    // -----------------------------------------------------------------

    [Fact]
    public async Task PublishFormTemplate_WithValidData_Returns200Or204()
    {
        var tenantId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var adminClient = CreateAuthenticatedClient(
            userId: createdBy.ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantId.ToString());

        // Create
        var createResp = await adminClient.PostAsJsonAsync(BaseUrl, new
        {
            TenantId = tenantId,
            Name = "Report Submission Form",
            NameVi = "Mẫu báo cáo",
            Code = "report_submission_form",
            StorageMode = 0, // Json
            SchemaJson = ValidSchemaJson,
            CreatedBy = createdBy,
        });
        var createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = createBody.GetProperty("data").GetProperty("id").GetGuid();

        // Add a field first — Publish requires at least one active field (GOV_FRM_003)
        var addFieldResp = await adminClient.PostAsJsonAsync(
            $"{BaseUrl}/{templateId}/fields",
            new
            {
                TenantId = tenantId,
                FieldKey = "applicantName",
                Type = 0, // FormFieldType.Text
                LabelVi = "Tên người nộp đơn",
                LabelEn = "Applicant Name",
                DisplayOrder = 1,
                Required = true,
                ValidationRulesJson = (string?)null,
                Options = (object?)null,
                UpdatedBy = createdBy,
            });
        addFieldResp.StatusCode.Should().BeOneOf(
            [HttpStatusCode.OK, HttpStatusCode.NoContent],
            "AddField must succeed before publish");

        // Publish
        var publishResp = await adminClient.PostAsJsonAsync(
            $"{BaseUrl}/{templateId}/publish",
            new { TenantId = tenantId, UpdatedBy = createdBy });

        // Controller ProducesResponseType(204) for publish, but ToApiResponse wraps it — accept 200 or 204
        publishResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PublishFormTemplate_WithoutAdminRole_Returns403()
    {
        var tenantId = Guid.NewGuid();
        var restrictedClient = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(),
            roles: ["CaseOfficer"],
            tenantId: tenantId.ToString());

        // Well-formed Guid so the {publicId:guid} route matches and authorization runs.
        // Policy denies before the handler executes, so a non-existent id still yields 403, not 404.
        var response = await restrictedClient.PostAsJsonAsync(
            $"{BaseUrl}/{Guid.NewGuid()}/publish",
            new { TenantId = tenantId, UpdatedBy = Guid.NewGuid() });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // -----------------------------------------------------------------
    // PUBLIC ACCESS (PATCH .../public-access) — IsPublic toggle
    // -----------------------------------------------------------------

    [Fact]
    public async Task SetPublicAccess_TogglesIsPublic_AndGetReflectsState()
    {
        var tenantId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var adminClient = CreateAuthenticatedClient(
            userId: createdBy.ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantId.ToString());

        // Create a Draft template — defaults IsPublic=false. GetById has no Status filter,
        // so a Draft is readable and its isPublic can be asserted without publishing.
        var createResp = await adminClient.PostAsJsonAsync(BaseUrl, new
        {
            TenantId = tenantId,
            Name = "Public Toggle Form",
            NameVi = "Mẫu bật công khai",
            Code = "public_toggle_form",
            StorageMode = 0, // Json
            SchemaJson = ValidSchemaJson,
            CreatedBy = createdBy,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templateId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Baseline: new template is not public
        await AssertIsPublic(adminClient, templateId, tenantId, expected: false);

        // Enable
        var enableResp = await adminClient.PatchAsJsonAsync(
            $"{BaseUrl}/{templateId}/public-access", new { IsPublic = true });
        enableResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        await AssertIsPublic(adminClient, templateId, tenantId, expected: true);

        // Disable — proves the toggle is symmetric, not a one-way latch
        var disableResp = await adminClient.PatchAsJsonAsync(
            $"{BaseUrl}/{templateId}/public-access", new { IsPublic = false });
        disableResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        await AssertIsPublic(adminClient, templateId, tenantId, expected: false);
    }

    [Fact]
    public async Task SetPublicAccess_WithoutManagePermission_Returns403()
    {
        var tenantId = Guid.NewGuid();
        var restrictedClient = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(),
            roles: ["CaseOfficer"],
            tenantId: tenantId.ToString());

        // Policy denies before the handler runs, so a non-existent id still yields 403, not 404.
        var response = await restrictedClient.PatchAsJsonAsync(
            $"{BaseUrl}/{Guid.NewGuid()}/public-access", new { IsPublic = true });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SetPublicAccess_ForNonExistentTemplate_Returns404()
    {
        var tenantId = Guid.NewGuid();
        var adminClient = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantId.ToString());

        var response = await adminClient.PatchAsJsonAsync(
            $"{BaseUrl}/{Guid.NewGuid()}/public-access", new { IsPublic = true });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetPublicAccess_ForTemplateInAnotherTenant_Returns404()
    {
        // Tenant A owns the template; tenant B (also a Manage-holding admin) must not reach it.
        // The tenant-scoped load collapses cross-tenant access to the same not-found path — the
        // permission alone is not enough, proving tenant isolation on this write path.
        var tenantA = Guid.NewGuid();
        var ownerAdmin = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(), roles: ["SystemAdmin"], tenantId: tenantA.ToString());

        var createResp = await ownerAdmin.PostAsJsonAsync(BaseUrl, new
        {
            TenantId = tenantA,
            Name = "Tenant A Private Form",
            NameVi = "Mẫu riêng của tenant A",
            Code = "tenant_a_private_form",
            StorageMode = 0, // Json
            SchemaJson = ValidSchemaJson,
            CreatedBy = Guid.NewGuid(),
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templateId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var otherTenantAdmin = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(), roles: ["SystemAdmin"], tenantId: Guid.NewGuid().ToString());

        var response = await otherTenantAdmin.PatchAsJsonAsync(
            $"{BaseUrl}/{templateId}/public-access", new { IsPublic = true });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetPublicAccess_Enable_WhenPublicCodeTakenByAnotherTenant_Returns409()
    {
        // Two tenants may hold the same Code (uniqueness is per-tenant), but only ONE may be PUBLIC:
        // the anonymous by-Code resolution runs cross-tenant, so a shared public Code is ambiguous.
        var sharedCode = $"pcc_{Guid.NewGuid():N}"[..20];
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var adminA = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(), roles: ["SystemAdmin"], tenantId: tenantA.ToString());
        var adminB = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(), roles: ["SystemAdmin"], tenantId: tenantB.ToString());

        var idA = await CreatePublicToggleTemplate(adminA, tenantA, sharedCode);
        var idB = await CreatePublicToggleTemplate(adminB, tenantB, sharedCode);

        // Tenant A claims the public Code first.
        var enableA = await adminA.PatchAsJsonAsync($"{BaseUrl}/{idA}/public-access", new { IsPublic = true });
        enableA.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Tenant B enabling the SAME Code is rejected with 409 — before the flip (B stays private).
        var enableB = await adminB.PatchAsJsonAsync($"{BaseUrl}/{idB}/public-access", new { IsPublic = true });
        enableB.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertIsPublic(adminB, idB, tenantB, expected: false);

        // Control (non-vacuous): a DIFFERENT, free Code enables fine — the 409 is Code-specific, not a
        // blanket block on tenant B.
        var freeCode = $"pcc_{Guid.NewGuid():N}"[..20];
        var idBFree = await CreatePublicToggleTemplate(adminB, tenantB, freeCode);
        var enableFree = await adminB.PatchAsJsonAsync($"{BaseUrl}/{idBFree}/public-access", new { IsPublic = true });
        enableFree.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        await AssertIsPublic(adminB, idBFree, tenantB, expected: true);
    }

    private static async Task<Guid> CreatePublicToggleTemplate(HttpClient client, Guid tenantId, string code)
    {
        var resp = await client.PostAsJsonAsync(BaseUrl, new
        {
            TenantId = tenantId,
            Name = "Public Code Conflict Form",
            NameVi = "Mẫu kiểm tra trùng Code công khai",
            Code = code,
            StorageMode = 0, // Json
            SchemaJson = ValidSchemaJson,
            CreatedBy = Guid.NewGuid(),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();
    }

    // -----------------------------------------------------------------
    // CONSENT + APPROVAL-WORKFLOW PROJECTION (single-template GET round-trip)
    // -----------------------------------------------------------------

    [Fact]
    public async Task GetFormTemplateById_ProjectsConsentAndApprovalWorkflow_AfterUpdate()
    {
        var tenantId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var adminClient = CreateAuthenticatedClient(
            userId: createdBy.ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantId.ToString());

        // Create a Draft template — consent off, no consent text, no approval workflow by default.
        // GetById has no Status filter, so a Draft is readable without publishing.
        var createResp = await adminClient.PostAsJsonAsync(BaseUrl, new
        {
            TenantId = tenantId,
            Name = "Consent Projection Form",
            NameVi = "Mẫu kiểm tra đồng ý",
            Code = "consent_projection_form",
            StorageMode = 0, // Json
            SchemaJson = ValidSchemaJson,
            CreatedBy = createdBy,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templateId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Baseline: the projection returns the persisted defaults (not stale nulls masking a gap).
        var baseline = await GetTemplateData(adminClient, templateId, tenantId);
        baseline.GetProperty("requiresConsent").GetBoolean().Should().BeFalse();
        baseline.GetProperty("consentText").ValueKind.Should().Be(JsonValueKind.Null);
        baseline.GetProperty("approvalWorkflowDefinitionId").ValueKind.Should().Be(JsonValueKind.Null);

        // Persist consent + approval-workflow via PUT — the write path the FE Settings tab uses.
        var workflowId = Guid.NewGuid();
        const string consentText = "Tôi đồng ý cho phép xử lý dữ liệu cá nhân theo PDPL.";
        var putResp = await adminClient.PutAsJsonAsync($"{BaseUrl}/{templateId}", new
        {
            Name = "Consent Projection Form",
            NameVi = "Mẫu kiểm tra đồng ý",
            RequiresConsent = true,
            ConsentText = consentText,
            ApprovalWorkflowDefinitionId = workflowId,
        });
        putResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Reload — the projection must reflect the persisted state, proving it is no longer
        // write-only (the pre-fix bug: GET omitted these columns → UI showed defaults on reload).
        var reloaded = await GetTemplateData(adminClient, templateId, tenantId);
        reloaded.GetProperty("requiresConsent").GetBoolean().Should().BeTrue();
        reloaded.GetProperty("consentText").GetString().Should().Be(consentText);
        reloaded.GetProperty("approvalWorkflowDefinitionId").GetGuid().Should().Be(workflowId);
    }

    private static async Task<JsonElement> GetTemplateData(
        HttpClient client, Guid templateId, Guid tenantId)
    {
        var getResp = await client.GetAsync($"{BaseUrl}/{templateId}?tenantId={tenantId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data");
    }

    private static async Task AssertIsPublic(
        HttpClient client, Guid templateId, Guid tenantId, bool expected)
    {
        var getResp = await client.GetAsync($"{BaseUrl}/{templateId}?tenantId={tenantId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("isPublic").GetBoolean().Should().Be(expected);
    }
}
