using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Integration tests for WorkflowDefinitionsController — /api/v1/workflow/definitions
///
/// Routes:
///   GET  /api/v1/workflow/definitions?tenantId={string}            → List (paged, any authenticated)
///   GET  /api/v1/workflow/definitions/{id}?tenantId={string}       → GetById (any authenticated)
///   POST /api/v1/workflow/definitions                              → Create [Admin,SystemAdmin]
///         body: CreateWorkflowDefinitionCommand {
///           Name, Description, TenantId (string), CreatedBy (Guid),
///           States: [ { Name, DisplayNameVi, DisplayNameEn, IsInitial, IsFinal, Color, SortOrder } ],
///           Transitions: [ { FromStateName, ToStateName, ActionName, ActionLabelVi, ActionLabelEn,
///                             ConditionsJson?, SortOrder } ]
///                             (RequiredPermissionCode fully removed in S8b — was authz hook, replaced by WorkflowTransitionAssignment.)
///         }
///
/// Note: TenantId is string (not Guid) in workflow definitions — matches GetWorkflowDefinitionsQuery.
/// </summary>
[Collection("Integration")]
public class WorkflowDefinitionsApiTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string BaseUrl = "/api/v1/workflow/definitions";

    // -----------------------------------------------------------------
    // LIST
    // -----------------------------------------------------------------

    [Fact]
    public async Task ListWorkflowDefinitions_AsAuthenticatedUser_Returns200()
    {
        var tenantId = Guid.NewGuid().ToString();
        var response = await Client.GetAsync($"{BaseUrl}?tenantId={tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListWorkflowDefinitions_ResponseBody_WrappedInApiResponseEnvelope()
    {
        var tenantId = Guid.NewGuid().ToString();
        var response = await Client.GetAsync($"{BaseUrl}?tenantId={tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("data", out _).Should().BeTrue("response must be ApiResponse envelope");
    }

    [Fact]
    public async Task ListWorkflowDefinitions_Unauthenticated_Returns401Or403()
    {
        var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await anonClient.GetAsync($"{BaseUrl}?tenantId={Guid.NewGuid()}");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // -----------------------------------------------------------------
    // CREATE
    // -----------------------------------------------------------------

    [Fact]
    public async Task CreateWorkflowDefinition_WithValidData_Returns200()
    {
        var tenantId = Guid.NewGuid().ToString();
        var createdBy = Guid.NewGuid();
        var adminClient = CreateAuthenticatedClient(
            userId: createdBy.ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantId);

        var response = await adminClient.PostAsJsonAsync(BaseUrl, new
        {
            Name = "Building Permit Approval Workflow",
            Description = "Standard workflow for building permit approval",
            TenantId = tenantId,
            CreatedBy = createdBy,
            EntityType = "Generic",
            States = new[]
            {
                new
                {
                    Name = "Draft",
                    DisplayNameVi = "Nháp",
                    DisplayNameEn = "Draft",
                    IsInitial = true,
                    IsFinal = false,
                    Color = "#9E9E9E",
                    SortOrder = 1,
                },
                new
                {
                    Name = "Submitted",
                    DisplayNameVi = "Đã nộp",
                    DisplayNameEn = "Submitted",
                    IsInitial = false,
                    IsFinal = false,
                    Color = "#2196F3",
                    SortOrder = 2,
                },
                new
                {
                    Name = "Approved",
                    DisplayNameVi = "Đã duyệt",
                    DisplayNameEn = "Approved",
                    IsInitial = false,
                    IsFinal = true,
                    Color = "#4CAF50",
                    SortOrder = 3,
                },
            },
            Transitions = new[]
            {
                new
                {
                    FromStateName = "Draft",
                    ToStateName = "Submitted",
                    ActionName = "Submit",
                    ActionLabelVi = "Nộp hồ sơ",
                    ActionLabelEn = "Submit",
                    ConditionsJson = (string?)null,
                    SortOrder = 1,
                },
                new
                {
                    FromStateName = "Submitted",
                    ToStateName = "Approved",
                    ActionName = "Approve",
                    ActionLabelVi = "Phê duyệt",
                    ActionLabelEn = "Approve",
                    ConditionsJson = (string?)null,
                    SortOrder = 2,
                },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("data", out var data).Should().BeTrue();
        data.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateWorkflowDefinition_WithoutAdminRole_Returns403()
    {
        var tenantId = Guid.NewGuid().ToString();
        var restrictedClient = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(),
            roles: ["CaseOfficer"],
            tenantId: tenantId);

        var response = await restrictedClient.PostAsJsonAsync(BaseUrl, new
        {
            Name = "Unauthorized Workflow",
            Description = "Should be rejected",
            TenantId = tenantId,
            CreatedBy = Guid.NewGuid(),
            States = Array.Empty<object>(),
            Transitions = Array.Empty<object>(),
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // -----------------------------------------------------------------
    // GET BY ID
    // -----------------------------------------------------------------

    [Fact]
    public async Task GetWorkflowDefinitionById_ForNonExistentId_Returns404()
    {
        var tenantId = Guid.NewGuid().ToString();
        var nonExistentId = Guid.NewGuid();

        var response = await Client.GetAsync($"{BaseUrl}/{nonExistentId}?tenantId={tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetWorkflowDefinitionById_ForExistingDefinition_Returns200WithStatesAndTransitions()
    {
        var tenantId = Guid.NewGuid().ToString();
        var createdBy = Guid.NewGuid();
        var adminClient = CreateAuthenticatedClient(
            userId: createdBy.ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantId);

        // Create a definition with generic state names (no entity-type-specific validation)
        var createResp = await adminClient.PostAsJsonAsync(BaseUrl, new
        {
            Name = "Complaint Review Workflow",
            Description = "Workflow for reviewing citizen complaints",
            TenantId = tenantId,
            CreatedBy = createdBy,
            EntityType = "Generic",
            States = new[]
            {
                new
                {
                    Name = "Draft",
                    DisplayNameVi = "Nháp",
                    DisplayNameEn = "Draft",
                    IsInitial = true,
                    IsFinal = false,
                    Color = "#FF9800",
                    SortOrder = 1,
                },
                new
                {
                    Name = "Approved",
                    DisplayNameVi = "Đã duyệt",
                    DisplayNameEn = "Approved",
                    IsInitial = false,
                    IsFinal = true,
                    Color = "#607D8B",
                    SortOrder = 2,
                },
            },
            Transitions = new[]
            {
                new
                {
                    FromStateName = "Draft",
                    ToStateName = "Approved",
                    ActionName = "Approve",
                    ActionLabelVi = "Phê duyệt",
                    ActionLabelEn = "Approve",
                    ConditionsJson = (string?)null,
                    SortOrder = 1,
                },
            },
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "workflow definition creation must succeed before testing GetById");

        var createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var definitionId = createBody.GetProperty("data").GetProperty("id").GetGuid();

        // GetById
        var getResp = await adminClient.GetAsync($"{BaseUrl}/{definitionId}?tenantId={tenantId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        data.GetProperty("id").GetGuid().Should().Be(definitionId);
        data.TryGetProperty("states", out _).Should().BeTrue("definition must include states");
        data.TryGetProperty("transitions", out _).Should().BeTrue("definition must include transitions");
    }

    // -----------------------------------------------------------------
    // SAVE GRAPH — assignment rule read-back (regression)
    // -----------------------------------------------------------------

    /// <summary>
    /// Regression: the save path persists WorkflowTransitionAssignments + Clauses, but the GET
    /// query used to omit them — so clauses vanished on reload even though the save RESPONSE
    /// (projected from the in-memory spec) looked complete. This drives the real round-trip:
    /// save a graph carrying an assignment, then re-GET via the query handler (not the save
    /// response) and assert the assignment + clause survive. Asserts against GetById specifically
    /// because that is the read path the bug lived in.
    /// </summary>
    [Fact]
    public async Task SaveGraphWithAssignment_ThenGetById_ReturnsAssignmentClauses()
    {
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var adminClient = CreateAuthenticatedClient(
            userId: userId.ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantId);

        object[] graphStates =
        [
            new { Name = "Draft", DisplayNameVi = "Nháp", DisplayNameEn = "Draft", IsInitial = true, IsFinal = false, Color = "#9E9E9E", SortOrder = 1 },
            new { Name = "Approved", DisplayNameVi = "Đã duyệt", DisplayNameEn = "Approved", IsInitial = false, IsFinal = true, Color = "#4CAF50", SortOrder = 2 },
        ];

        // 1. Create a bare definition (no assignment — Create does not enforce R2) to obtain its id.
        var createResp = await adminClient.PostAsJsonAsync(BaseUrl, new
        {
            Name = "Assignment Read-Back Workflow",
            Description = "Regression: assignment clauses must survive a GET round-trip",
            TenantId = tenantId,
            CreatedBy = userId,
            EntityType = "Generic",
            States = graphStates,
            Transitions = new[]
            {
                new { FromStateName = "Draft", ToStateName = "Approved", ActionName = "Approve", ActionLabelVi = "Phê duyệt", ActionLabelEn = "Approve", ConditionsJson = (string?)null, SortOrder = 1 },
            },
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var definitionId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // 2. Save the graph with an assignment rule on the transition. Enums are sent as STRING
        //    members ("Union"/"Initiator"/"PreviousHandler") — the real FE wire format, since the
        //    API uses JsonStringEnumConverter both ways. This proves the BE accepts string-form
        //    enums on the save request (not just numeric). Initiator/PreviousHandler are ref-less,
        //    so they sidestep R3 (ByPermission existence) and the ByRole/Group/OrgUnit identity hops,
        //    while two clauses still exercise multi-clause read-back + the ORDER BY Id ordering claim.
        var saveResp = await adminClient.PutAsJsonAsync($"{BaseUrl}/{definitionId}/graph", new
        {
            States = graphStates,
            Transitions = new[]
            {
                new
                {
                    FromStateName = "Draft",
                    ToStateName = "Approved",
                    ActionName = "Approve",
                    ActionLabelVi = "Phê duyệt",
                    ActionLabelEn = "Approve",
                    ConditionsJson = (string?)null,
                    SortOrder = 1,
                    Assignment = new
                    {
                        CombineMode = "Union",
                        AllowAssignorOverride = true,
                        Clauses = new[]
                        {
                            new { Mode = "Initiator", RefsJson = (string?)null },
                            new { Mode = "PreviousHandler", RefsJson = (string?)null },
                        },
                    },
                },
            },
        });
        saveResp.StatusCode.Should().Be(HttpStatusCode.OK, "save-graph with a string-enum assignment must succeed");

        // 3. Re-GET via the query handler — the path that regressed (NOT the save response).
        var getResp = await adminClient.GetAsync($"{BaseUrl}/{definitionId}?tenantId={tenantId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var data = (await getResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var transition = data.GetProperty("transitions").EnumerateArray().Single();

        transition.TryGetProperty("assignment", out var assignment).Should().BeTrue(
            "the persisted assignment must be re-projected on GET (read-path round-trip)");
        assignment.ValueKind.Should().NotBe(JsonValueKind.Null,
            "assignment is non-null because the transition carries a rule");
        // Enums serialize as strings (JsonStringEnumConverter) — asserting the value, not just
        // presence, guards the Dapper int→enum mapping the read-back relies on.
        assignment.GetProperty("combineMode").GetString().Should().Be("Union",
            "CombineMode must round-trip through the int→enum map");
        assignment.GetProperty("allowAssignorOverride").GetBoolean().Should().BeTrue(
            "the assignment row fields must round-trip, not just exist");

        var clauses = assignment.GetProperty("clauses").EnumerateArray().ToList();
        clauses.Should().HaveCount(2,
            "both persisted clauses must be read back — this is what vanished before the fix");
        // ORDER BY Id preserves insertion order, so clauses come back in the saved sequence.
        clauses[0].GetProperty("mode").GetString().Should().Be("Initiator");
        clauses[1].GetProperty("mode").GetString().Should().Be("PreviousHandler");
        clauses[0].GetProperty("refsJson").ValueKind.Should().Be(JsonValueKind.Null,
            "a ref-less clause persists RefsJson as null");
    }

    // -----------------------------------------------------------------
    // FILTER: isActive query param
    // -----------------------------------------------------------------

    [Fact]
    public async Task ListWorkflowDefinitions_WithIsActiveFilter_Returns200()
    {
        var tenantId = Guid.NewGuid().ToString();
        var response = await Client.GetAsync($"{BaseUrl}?tenantId={tenantId}&isActive=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------
    // DELETE
    // -----------------------------------------------------------------

    [Fact]
    public async Task DeleteWorkflowDefinition_ByPublicId_Succeeds_AndGetByIdThen404()
    {
        var tenantId = Guid.NewGuid().ToString();
        var actorId = Guid.NewGuid();
        var admin = CreateAuthenticatedClient(
            userId: actorId.ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantId);

        // Create a minimal Draft definition to delete.
        var create = await admin.PostAsJsonAsync(BaseUrl, new
        {
            Name = "Deletable Workflow",
            Description = "delete-by-public-id test",
            TenantId = tenantId,
            CreatedBy = actorId,
            EntityType = "Generic",
            States = new[]
            {
                new
                {
                    Name = "Draft", DisplayNameVi = "Nháp", DisplayNameEn = "Draft",
                    IsInitial = true, IsFinal = false, Color = "#9E9E9E", SortOrder = 1,
                },
            },
            Transitions = Array.Empty<object>(),
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var publicId = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // DELETE keys off the Guid PublicId. The generic Find-based delete would 400 here
        // (EF.Find targets the long PK), so this asserts the PublicId-resolving override.
        var delete = await admin.DeleteAsync($"{BaseUrl}/{publicId}");
        delete.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Soft-deleted → no longer returned by GET.
        var get = await admin.GetAsync($"{BaseUrl}/{publicId}?tenantId={tenantId}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
