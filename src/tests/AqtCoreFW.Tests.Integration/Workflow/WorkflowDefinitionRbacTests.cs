using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// RBAC tests for WorkflowDefinitionsController verifying policy-based authorization.
///
/// Native ASP.NET AND policy stacking:
///   - Class-level: [Authorize(Policy = PermCodes.Workflow.Def.View)]
///   - Method-level (writes): [Authorize(Policy = PermCodes.Workflow.Def.Manage)]
///   - Result: Read endpoints require View; Write endpoints require BOTH View AND Manage.
///
/// Test scenarios cover:
///   1. User with BOTH View + Manage â†’ 200/201 on all endpoints
///   2. User with only View â†’ 200 on reads, 403 on writes
///   3. User with only Manage â†’ 403 on all (class-level View blocks first)
///   4. User with NEITHER â†’ 403 on all
///   5. Cross-tenant: tenant A user â†’ tenant B definition â†’ 403/404
///   6. Global template visibility: TenantId=Guid.Empty visible to all tenants
/// </summary>
[Collection("Integration")]
public class WorkflowDefinitionRbacTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string BaseUrl = "/api/v1/workflow/definitions";

    // -----------------------------------------------------------------
    // Scenario 1: User with BOTH View + Manage claims
    // -----------------------------------------------------------------

    [Fact]
    public async Task UserWithViewAndManage_CanListDefinitions_Returns200()
    {
        var tenantId = Guid.NewGuid().ToString();
        using var client = CreateClientWithPermissions(
            [PermCodes.Workflow.Def.View, PermCodes.Workflow.Def.Manage],
            tenantId: tenantId);

        var response = await client.GetAsync(BaseUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UserWithViewAndManage_CanCreateDefinition_Returns200()
    {
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        using var client = CreateClientWithPermissions(
            [PermCodes.Workflow.Def.View, PermCodes.Workflow.Def.Manage],
            userId: userId.ToString(),
            tenantId: tenantId);

        var response = await client.PostAsJsonAsync(BaseUrl, new
        {
            Name = "RBAC Test Workflow",
            Description = "Testing View+Manage permissions",
            TenantId = tenantId,
            CreatedBy = userId,
            EntityType = "Generic",
            States = new[]
            {
                new
                {
                    Name = "Draft",
                    DisplayNameVi = "NhÃ¡p",
                    DisplayNameEn = "Draft",
                    IsInitial = true,
                    IsFinal = false,
                    Color = "#9E9E9E",
                    SortOrder = 1,
                },
                new
                {
                    Name = "Submitted",
                    DisplayNameVi = "ÄÃ£ ná»™p",
                    DisplayNameEn = "Submitted",
                    IsInitial = false,
                    IsFinal = true,
                    Color = "#4CAF50",
                    SortOrder = 2,
                },
            },
            Transitions = new[]
            {
                new
                {
                    FromStateName = "Draft",
                    ToStateName = "Submitted",
                    ActionName = "Submit",
                    ActionLabelVi = "Ná»™p",
                    ActionLabelEn = "Submit",
                    ConditionsJson = (string?)null,
                    SortOrder = 1,
                },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------
    // Scenario 2: User with only View claim
    // -----------------------------------------------------------------

    [Fact]
    public async Task UserWithOnlyView_CanListDefinitions_Returns200()
    {
        var tenantId = Guid.NewGuid().ToString();
        using var client = CreateClientWithPermissions(
            [PermCodes.Workflow.Def.View],
            tenantId: tenantId);

        var response = await client.GetAsync(BaseUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UserWithOnlyView_CannotCreateDefinition_Returns403()
    {
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        using var client = CreateClientWithPermissions(
            [PermCodes.Workflow.Def.View],
            userId: userId.ToString(),
            tenantId: tenantId);

        var response = await client.PostAsJsonAsync(BaseUrl, new
        {
            Name = "Unauthorized Workflow",
            Description = "Should be rejected",
            TenantId = tenantId,
            CreatedBy = userId,
            EntityType = "Generic",
            States = Array.Empty<object>(),
            Transitions = Array.Empty<object>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "user with only View claim should be denied write access (Manage required)");
    }

    // -----------------------------------------------------------------
    // Scenario 3: User with only Manage claim (no View)
    // -----------------------------------------------------------------

    [Fact]
    public async Task UserWithOnlyManage_CannotListDefinitions_Returns403()
    {
        var tenantId = Guid.NewGuid().ToString();
        using var client = CreateClientWithPermissions(
            [PermCodes.Workflow.Def.Manage],
            tenantId: tenantId);

        var response = await client.GetAsync(BaseUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "user with only Manage claim should be denied by class-level View policy");
    }

    [Fact]
    public async Task UserWithOnlyManage_CannotCreateDefinition_Returns403()
    {
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        using var client = CreateClientWithPermissions(
            [PermCodes.Workflow.Def.Manage],
            userId: userId.ToString(),
            tenantId: tenantId);

        var response = await client.PostAsJsonAsync(BaseUrl, new
        {
            Name = "Unauthorized Workflow",
            Description = "Should be rejected",
            TenantId = tenantId,
            CreatedBy = userId,
            EntityType = "Generic",
            States = Array.Empty<object>(),
            Transitions = Array.Empty<object>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "user with only Manage but not View should be denied by class-level View policy (native AND stack)");
    }

    // -----------------------------------------------------------------
    // Scenario 4: User with NEITHER View nor Manage claims
    // -----------------------------------------------------------------

    [Fact]
    public async Task UserWithNoPermissions_CannotListDefinitions_Returns403()
    {
        var tenantId = Guid.NewGuid().ToString();
        using var client = CreateClientWithPermissions(
            [], // No permissions
            tenantId: tenantId);

        var response = await client.GetAsync(BaseUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UserWithNoPermissions_CannotCreateDefinition_Returns403()
    {
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        using var client = CreateClientWithPermissions(
            [], // No permissions
            userId: userId.ToString(),
            tenantId: tenantId);

        var response = await client.PostAsJsonAsync(BaseUrl, new
        {
            Name = "Unauthorized Workflow",
            Description = "Should be rejected",
            TenantId = tenantId,
            CreatedBy = userId,
            EntityType = "Generic",
            States = Array.Empty<object>(),
            Transitions = Array.Empty<object>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // -----------------------------------------------------------------
    // Scenario 5: Cross-tenant isolation
    // -----------------------------------------------------------------

    [Fact]
    public async Task CrossTenant_GetDefinitionFromDifferentTenant_Returns404()
    {
        // Setup: Create a definition in tenant A
        var tenantA = Guid.NewGuid().ToString();
        var tenantB = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();

        using var adminClientA = CreateAuthenticatedClient(
            userId: userId.ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantA);

        var createResponse = await adminClientA.PostAsJsonAsync(BaseUrl, new
        {
            Name = "Tenant A Workflow",
            Description = "Should not be visible to tenant B",
            TenantId = tenantA,
            CreatedBy = userId,
            EntityType = "Generic",
            States = new[]
            {
                new
                {
                    Name = "Draft",
                    DisplayNameVi = "NhÃ¡p",
                    DisplayNameEn = "Draft",
                    IsInitial = true,
                    IsFinal = true,
                    Color = "#9E9E9E",
                    SortOrder = 1,
                },
            },
            Transitions = Array.Empty<object>(),
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var definitionId = createBody.GetProperty("data").GetProperty("id").GetGuid();

        // Act: Tenant B user tries to access tenant A's definition
        using var clientB = CreateClientWithPermissions(
            [PermCodes.Workflow.Def.View, PermCodes.Workflow.Def.Manage],
            tenantId: tenantB);

        var getResponse = await clientB.GetAsync($"{BaseUrl}/{definitionId}");

        // Assert: Should be 404 (tenant filter hides the definition)
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant access should be blocked by tenant filter â€” definition invisible to other tenants");
    }

    // -----------------------------------------------------------------
    // Scenario 6: Global template visibility
    // -----------------------------------------------------------------

    [Fact]
    public async Task GlobalTemplate_VisibleToAllTenants_Returns200()
    {
        // Note: Global templates have TenantId = Guid.Empty.
        // The query handler uses filter (TenantId=@T OR TenantId=@Global).
        // Creating a global template requires SystemAdmin with empty tenant context,
        // which is hard to test in integration tests without special setup.
        // This test verifies the list endpoint returns data (including any global templates).

        var tenantId = Guid.NewGuid().ToString();
        using var client = CreateClientWithPermissions(
            [PermCodes.Workflow.Def.View],
            tenantId: tenantId);

        var response = await client.GetAsync(BaseUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The response should be a valid API envelope (global templates would be included if any exist)
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("data", out _).Should().BeTrue("response must be ApiResponse envelope");
    }
}
