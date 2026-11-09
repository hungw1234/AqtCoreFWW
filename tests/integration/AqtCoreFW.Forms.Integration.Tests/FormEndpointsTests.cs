using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// Integration tests for Forms REST API endpoints.
/// Uses WebApplicationFactory + Testcontainers SQL Server.
/// Covers: form template lifecycle (create â†’ add field â†’ publish â†’ submit â†’ query).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class FormEndpointsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId  = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();

    public FormEndpointsTests(WebAppFixture app)
    {
        _app = app;
    }

    // --- Auth ---

    [Fact]
    public async Task GetTemplates_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();
        var response = await client.GetAsync($"/api/v1/forms/templates?tenantId={TenantId}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetTemplates_Authenticated_Returns200()
    {
        var client = _app.CreateAuthenticatedClient(UserId, TenantId);
        var response = await client.GetAsync($"/api/v1/forms/templates?tenantId={TenantId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- Create template ---

    [Fact]
    public async Task CreateTemplate_NonAdminRole_Returns403()
    {
        var client = _app.CreateAuthenticatedClient(UserId, TenantId, "GovOfficer");
        var payload = BuildCreateTemplatePayload("FORBIDDEN-FORM");

        var response = await client.PostAsJsonAsync("/api/v1/forms/templates", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateTemplate_AdminRole_Returns200WithDraftStatus()
    {
        var client = CreateClientWithPermissions(AdminId, TenantId, PermCodes.Forms.Template.Manage);
        var payload = BuildCreateTemplatePayload($"FORM-{Guid.NewGuid():N}"[..10]);

        var response = await client.PostAsJsonAsync("/api/v1/forms/templates", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetProperty("status").GetString().Should().Be("Draft");
    }

    [Fact]
    public async Task CreateTemplate_MissingCode_Returns422()
    {
        var client = CreateClientWithPermissions(AdminId, TenantId, PermCodes.Forms.Template.Manage);
        var payload = new
        {
            tenantId = TenantId,
            name = "Test Form",
            nameVi = "Biá»ƒu máº«u test",
            code = "",          // invalid â€” empty code
            storageMode = 0,    // StorageMode.Json = 0 (no JsonStringEnumConverter registered)
            createdBy = AdminId
        };

        var response = await client.PostAsJsonAsync("/api/v1/forms/templates", payload);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // --- Full lifecycle: create â†’ add field â†’ publish â†’ submit â†’ query ---

    [Fact]
    public async Task FormLifecycle_CreatePublishSubmitQuery_Succeeds()
    {
        var admin = CreateClientWithPermissions(AdminId, TenantId, PermCodes.Forms.Template.Manage);
        var user  = CreateClientWithPermissions(UserId, TenantId, PermCodes.Forms.Submit.Create, PermCodes.Forms.Submit.View);
        var code  = $"F{Guid.NewGuid():N}"[..9]; // unique code per test run

        // 1. Create draft template
        var createResp = await admin.PostAsJsonAsync("/api/v1/forms/templates",
            BuildCreateTemplatePayload(code));
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        // Phase-07: FormTemplate retier T1->T2 - Id is now Guid PublicId (AK), not long IDENTITY.
        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // 2. Add a field (fullName, not PII)
        var addFieldResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId  = TenantId,
                fieldKey  = "fullName",
                type      = 0,          // FormFieldType.Text = 0 (no JsonStringEnumConverter registered)
                labelEn   = "Full Name",
                labelVi   = "Há» tÃªn",
                displayOrder = 1,       // matches AddFormFieldRequest.DisplayOrder
                required  = true,
                updatedBy = AdminId
            });
        addFieldResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Publish template â†’ status becomes Active (Json mode)
        var publishResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/publish",
            new { tenantId = TenantId, updatedBy = AdminId });
        publishResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 4. Submit form data
        var submitResp = await user.PostAsJsonAsync("/api/v1/forms/submissions", new
        {
            formTemplatePublicId = templatePublicId,
            data                 = new Dictionary<string, string> { ["fullName"] = "Nguyen Van A" }
        });
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        // SubmitFormCommand returns Result<Guid> â€” data is a raw Guid string, not an object
        var submissionId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        // 5. Query the submission back
        var getResp = await user.GetAsync(
            $"/api/v1/forms/submissions/{submissionId}?tenantId={TenantId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var subBody = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        subBody.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    // --- BP MVP P1: FormTemplateVersion snapshot lifecycle ---

    [Fact]
    public async Task Publish_CreatesVersionSnapshot_VisibleViaListAndGetEndpoints()
    {
        var admin = CreateClientWithPermissions(AdminId, TenantId, PermCodes.Forms.Template.Manage);
        var code = $"V{Guid.NewGuid():N}"[..9];

        // 1. Create + add field + publish
        var createResp = await admin.PostAsJsonAsync("/api/v1/forms/templates",
            BuildCreateTemplatePayload(code));
        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                fieldKey = "snapshotField",
                type = 0,
                labelEn = "Snapshot Field",
                labelVi = "Trường snapshot",
                displayOrder = 1,
                required = true,
                updatedBy = AdminId
            });

        var publishResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/publish",
            new { });
        publishResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 2. List versions — must contain VersionNumber=1
        var listResp = await admin.GetAsync($"/api/v1/forms/templates/{templatePublicId}/versions");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var versions = listBody.GetProperty("data").EnumerateArray().ToList();
        versions.Should().NotBeEmpty();
        versions.Select(v => v.GetProperty("versionNumber").GetInt32()).Should().Contain(1);

        // 3. Get the snapshot — SchemaJson must contain the field key
        var getResp = await admin.GetAsync($"/api/v1/forms/templates/{templatePublicId}/versions/1");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = (await getResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        detail.GetProperty("versionNumber").GetInt32().Should().Be(1);
        detail.GetProperty("schemaJson").GetString().Should().Contain("snapshotField");
    }

    // --- Cross-tenant isolation ---

    [Fact]
    public async Task GetTemplates_TenantA_DoesNotSeeTemplatesFromTenantB()
    {
        var tenantB = Guid.NewGuid();
        var adminBId = Guid.NewGuid();
        var adminB  = CreateClientWithPermissions(adminBId, tenantB, PermCodes.Forms.Template.Manage);

        // Create template in TenantB
        var code = $"B{Guid.NewGuid():N}"[..9];
        await adminB.PostAsJsonAsync("/api/v1/forms/templates",
            new
            {
                tenantId    = tenantB,
                name        = "TenantB Form",
                nameVi      = "TenantB Form",
                code,
                storageMode = 0,    // StorageMode.Json = 0 (no JsonStringEnumConverter registered)
                createdBy   = Guid.NewGuid()
            });

        // TenantA list must not contain TenantB template â€” no policy needed for GET
        var adminA = _app.CreateAuthenticatedClient(AdminId, TenantId);
        var response = await adminA.GetAsync($"/api/v1/forms/templates?tenantId={TenantId}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // ApiResponse<PagedResult<T>> â†’ { "success": true, "data": { "items": [...], "totalCount": N, ... } }
        var items = body.GetProperty("data").GetProperty("items").EnumerateArray().ToList();

        items.Should().NotContain(t =>
            t.GetProperty("name").GetString() == "TenantB Form");
    }

    // --- Helpers ---

    private object BuildCreateTemplatePayload(string code) => new
    {
        tenantId    = TenantId,
        name        = $"Test Form {code}",
        nameVi      = $"Biá»ƒu máº«u {code}",
        code,
        storageMode = 0,        // StorageMode.Json = 0 (no JsonStringEnumConverter registered)
        createdBy   = AdminId
    };

    /// <summary>
    /// Creates an authenticated client whose <see cref="IEffectivePermissionService"/> grants only the
    /// specified permissions. Other users get an empty set (â†’ 403). Other tests in the same
    /// fixture are unaffected because <c>WithWebHostBuilder</c> clones the server per call.
    /// Pattern proven by <c>WorkflowTaxonomyAdminEndpointsTests.CreateClientWithPermissions</c>.
    /// </summary>
    private HttpClient CreateClientWithPermissions(Guid userId, Guid tenantId, params string[] permissions)
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
                services.AddScoped(_ => permService)))
            .CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        return client;
    }
}
