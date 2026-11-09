using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.Tests.SharedFixtures;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Organization.Integration.Tests;

/// <summary>
/// Integration tests for Organization (OrgUnit + Staff) REST API endpoints.
/// Uses WebApplicationFactory + Testcontainers SQL Server.
/// All write operations require Admin or SystemAdmin role.
/// Routes: /api/v1/admin/org/units, /api/v1/admin/org/staff
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class OrgUnitEndpointsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    // Fixed tenant IDs â€” deterministic across tests in the collection
    private static readonly Guid TenantA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid AdminId = Guid.Parse("AD000000-0000-0000-0000-000000000001");
    private static readonly Guid StaffId = Guid.Parse("5F000000-0000-0000-0000-000000000001");

    public OrgUnitEndpointsTests(WebAppFixture app) => _app = app;

    // â”€â”€ Helper â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Creates a root org unit and returns its ID. Fails the test if creation fails.</summary>
    private async Task<Guid> CreateOrgUnitAsync(
        HttpClient client,
        Guid tenantId,
        string name,
        string code,
        Guid? parentId = null)
    {
        var payload = new { name, nameEn = name + " EN", code, parentId };
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/admin/org/units?tenantId={tenantId}", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"CreateOrgUnit '{name}' must succeed (status={resp.StatusCode})");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

    // â”€â”€ TC-ORG-INT-001: Create org unit via API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task CreateOrgUnit_ValidPayload_Returns200AndCanBeRetrievedById()
    {
        var client = CreateClientWithPermissions(AdminId, TenantA, PermCodes.Org.Unit.Manage);
        var code = $"TC001-{Guid.NewGuid():N}"[..20]; // keep unique + within DB length

        var payload = new
        {
            name = "Ministry of Integration Tests",
            nameEn = "Ministry of Integration Tests EN",
            code,
            parentId = (Guid?)null
        };

        var createResp = await client.PostAsJsonAsync(
            $"/api/v1/admin/org/units?tenantId={TenantA}", payload);

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var orgId = createBody.GetProperty("data").GetProperty("id").GetGuid();
        orgId.Should().NotBeEmpty();

        // GET by id
        var getResp = await client.GetAsync(
            $"/api/v1/admin/org/units/{orgId}?tenantId={TenantA}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var getData = (await getResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data");
        getData.GetProperty("id").GetGuid().Should().Be(orgId);
        getData.GetProperty("code").GetString().Should().Be(code);
        getData.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    // â”€â”€ TC-ORG-INT-002: Child org unit level computed from parent â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task CreateOrgUnit_WithParent_LevelIsParentLevelPlusOne()
    {
        var client = CreateClientWithPermissions(AdminId, TenantA, PermCodes.Org.Unit.Manage);

        // Create root (level 1)
        var rootId = await CreateOrgUnitAsync(client, TenantA,
            "TC002 Root", $"TC002-ROOT-{Guid.NewGuid():N}"[..20]);

        // Create child under root
        var childCode = $"TC002-CHILD-{Guid.NewGuid():N}"[..20];
        var childResp = await client.PostAsJsonAsync(
            $"/api/v1/admin/org/units?tenantId={TenantA}",
            new { name = "TC002 Child", nameEn = "TC002 Child EN", code = childCode, parentId = rootId });

        childResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var childData = (await childResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data");

        childData.GetProperty("level").GetInt32().Should().Be(2,
            "child of a level-1 root must be level 2");
        childData.GetProperty("parentId").GetGuid().Should().Be(rootId);
    }

    // â”€â”€ TC-ORG-INT-003: Duplicate code within tenant returns 409 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task CreateOrgUnit_DuplicateCodeSameTenant_Returns409Conflict()
    {
        var client = CreateClientWithPermissions(AdminId, TenantA, PermCodes.Org.Unit.Manage);
        var code = $"TC003-DUP-{Guid.NewGuid():N}"[..20];

        // First creation â€” must succeed
        await CreateOrgUnitAsync(client, TenantA, "TC003 Original", code);

        // Second creation with same code â€” must fail with 409
        var duplicateResp = await client.PostAsJsonAsync(
            $"/api/v1/admin/org/units?tenantId={TenantA}",
            new { name = "TC003 Duplicate", nameEn = "TC003 Duplicate EN", code, parentId = (Guid?)null });

        duplicateResp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "duplicate code within the same tenant must return 409");
    }

    // â”€â”€ TC-ORG-INT-004: Deactivate with active children returns 422 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task DeleteOrgUnit_WithActiveChildren_Returns422()
    {
        var client = CreateClientWithPermissions(AdminId, TenantA, PermCodes.Org.Unit.Manage);

        // Create parent
        var parentId = await CreateOrgUnitAsync(client, TenantA,
            "TC004 Parent", $"TC004-PAR-{Guid.NewGuid():N}"[..20]);

        // Create child under parent
        await CreateOrgUnitAsync(client, TenantA,
            "TC004 Child", $"TC004-CHD-{Guid.NewGuid():N}"[..20], parentId);

        // Attempt to deactivate parent while child is still active
        var deleteResp = await client.DeleteAsync(
            $"/api/v1/admin/org/units/{parentId}?tenantId={TenantA}");

        deleteResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "deactivating a unit with active children must return 422");
    }

    // â”€â”€ TC-ORG-INT-005: Get tree returns hierarchical structure â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task GetTree_AfterCreatingHierarchy_ReturnsAllUnitsForTenant()
    {
        var client = CreateClientWithPermissions(AdminId, TenantA, PermCodes.Org.Unit.Manage);

        // Build a small 2-level hierarchy unique to this test run
        var rootCode  = $"TC005-R-{Guid.NewGuid():N}"[..20];
        var childCode = $"TC005-C-{Guid.NewGuid():N}"[..20];

        var rootId = await CreateOrgUnitAsync(client, TenantA, "TC005 Root", rootCode);
        var childId = await CreateOrgUnitAsync(client, TenantA, "TC005 Child", childCode, rootId);

        // GET tree
        var treeResp = await client.GetAsync(
            $"/api/v1/admin/org/units?tenantId={TenantA}");
        treeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var treeItems = (await treeResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data")
            .EnumerateArray()
            .ToList();

        treeItems.Should().Contain(u => u.GetProperty("id").GetGuid() == rootId,
            "root unit must appear in tree");
        treeItems.Should().Contain(u => u.GetProperty("id").GetGuid() == childId,
            "child unit must appear in tree");

        var childNode = treeItems.First(u => u.GetProperty("id").GetGuid() == childId);
        childNode.GetProperty("parentId").GetGuid().Should().Be(rootId);
    }

    // â”€â”€ TC-ORG-INT-006: Cross-tenant isolation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task GetTree_TenantB_DoesNotSeeTenantAOrgUnits()
    {
        // Create an org unit under TenantA
        var clientA = CreateClientWithPermissions(AdminId, TenantA, PermCodes.Org.Unit.Manage);
        var code = $"TC006-A-{Guid.NewGuid():N}"[..20];
        var orgIdA = await CreateOrgUnitAsync(clientA, TenantA, "TC006 TenantA Unit", code);

        // Query tree as TenantB — no permission policy needed for GET, but the controller
        // carries [RequiresMfa] so the mfa_at claim must be present.
        var clientB = _app.CreateAuthenticatedClient(AdminId, TenantB, includeMfaClaim: true);
        var treeResp = await clientB.GetAsync(
            $"/api/v1/admin/org/units?tenantId={TenantB}");
        treeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var treeItems = (await treeResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data")
            .EnumerateArray()
            .ToList();

        treeItems.Should().NotContain(u => u.GetProperty("id").GetGuid() == orgIdA,
            "TenantB must not see org units belonging to TenantA");
    }

    // â”€â”€ TC-ORG-INT-007: Assign staff creates assignment + history â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task AssignStaff_ToActiveOrgUnit_CreatesAssignmentAndPositionHistory()
    {
        var client = CreateClientWithPermissions(AdminId, TenantA, PermCodes.Org.Unit.Manage, PermCodes.Org.Staff.Manage);

        // Create an org unit to assign to
        var unitId = await CreateOrgUnitAsync(client, TenantA,
            "TC007 Assignment Unit", $"TC007-U-{Guid.NewGuid():N}"[..20]);

        var staffId = Guid.NewGuid(); // unique staff per test run

        var assignPayload = new
        {
            orgUnitId = unitId,
            roleInOrg = "Officer",
            positionTitle = "Senior Integration Officer"
        };

        var assignResp = await client.PostAsJsonAsync(
            $"/api/v1/admin/org/staff/{staffId}/assign?tenantId={TenantA}", assignPayload);

        assignResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var assignData = (await assignResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data");

        assignData.GetProperty("userId").GetGuid().Should().Be(staffId);
        // T1 child DTO exposes the long FK to the T2 OrgUnit parent (UserOrgUnitAssignmentDto,
        // arch:suppress R_T2FK) — the request-side Guid maps to this internal surrogate key.
        var assignedOrgUnitDbId = assignData.GetProperty("orgUnitId").GetInt64();
        assignedOrgUnitDbId.Should().Be(await GetOrgUnitDbIdAsync(unitId),
            "the assignment must reference exactly the unit named in the request");
        assignData.GetProperty("isActive").GetBoolean().Should().BeTrue();

        // Verify history is recorded
        var historyResp = await client.GetAsync(
            $"/api/v1/admin/org/staff/{staffId}/history?tenantId={TenantA}");
        historyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var historyData = (await historyResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data");

        // Same long key as the assignment response — proves history rows reference the same unit.
        historyData.GetProperty("assignments").EnumerateArray()
            .Should().Contain(a => a.GetProperty("orgUnitId").GetInt64() == assignedOrgUnitDbId,
                "assignment must appear in staff history");

        historyData.GetProperty("positionHistory").EnumerateArray()
            .Should().Contain(p => p.GetProperty("positionTitle").GetString() == "Senior Integration Officer",
                "position history must be recorded on assignment");
    }

    // â”€â”€ TC-ORG-INT-008: Transfer staff closes old, creates new â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task TransferStaff_FromOldUnit_ClosesOldAssignmentAndOpensNew()
    {
        var client = CreateClientWithPermissions(AdminId, TenantA, PermCodes.Org.Unit.Manage, PermCodes.Org.Staff.Manage);

        // Create two org units
        var unitFrom = await CreateOrgUnitAsync(client, TenantA,
            "TC008 From Unit", $"TC008-FROM-{Guid.NewGuid():N}"[..20]);
        var unitTo = await CreateOrgUnitAsync(client, TenantA,
            "TC008 To Unit", $"TC008-TO-{Guid.NewGuid():N}"[..20]);

        var staffId = Guid.NewGuid(); // unique per test run

        // Assign staff to 'from' unit first
        var assignPayload = new
        {
            orgUnitId = unitFrom,
            roleInOrg = "Officer",
            positionTitle = "Original Position"
        };
        var assignResp = await client.PostAsJsonAsync(
            $"/api/v1/admin/org/staff/{staffId}/assign?tenantId={TenantA}", assignPayload);
        assignResp.StatusCode.Should().Be(HttpStatusCode.OK);
        // T1 child DTO exposes the long FK to the T2 OrgUnit parent — capture the 'from'
        // unit's surrogate key so the transfer/history asserts can compare long-with-long.
        var fromOrgUnitDbId = (await assignResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("orgUnitId").GetInt64();

        // Transfer to 'to' unit
        var transferPayload = new
        {
            toOrgUnitId = unitTo,
            roleInOrg = "SeniorOfficer",
            positionTitle = "New Position After Transfer"
        };
        var transferResp = await client.PostAsJsonAsync(
            $"/api/v1/admin/org/staff/{staffId}/transfer?tenantId={TenantA}", transferPayload);

        transferResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var transferData = (await transferResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data");

        // New assignment targets exactly the 'to' unit and is active.
        var toOrgUnitDbId = transferData.GetProperty("orgUnitId").GetInt64();
        fromOrgUnitDbId.Should().Be(await GetOrgUnitDbIdAsync(unitFrom),
            "the original assignment must reference exactly the 'from' unit");
        toOrgUnitDbId.Should().Be(await GetOrgUnitDbIdAsync(unitTo),
            "the transfer must reference exactly the 'to' unit");
        transferData.GetProperty("isActive").GetBoolean().Should().BeTrue();

        // Verify history: 2 assignments, old one closed
        var historyResp = await client.GetAsync(
            $"/api/v1/admin/org/staff/{staffId}/history?tenantId={TenantA}");
        historyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var assignments = (await historyResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data")
            .GetProperty("assignments")
            .EnumerateArray()
            .ToList();

        assignments.Should().HaveCount(2, "one original + one new after transfer");

        var closedAssignment = assignments.FirstOrDefault(
            a => a.GetProperty("orgUnitId").GetInt64() == fromOrgUnitDbId);
        closedAssignment.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        closedAssignment.GetProperty("isActive").GetBoolean().Should().BeFalse(
            "old assignment must be closed (isActive=false) after transfer");

        var newAssignment = assignments.FirstOrDefault(
            a => a.GetProperty("orgUnitId").GetInt64() == toOrgUnitDbId);
        newAssignment.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        newAssignment.GetProperty("isActive").GetBoolean().Should().BeTrue(
            "new assignment must be open (isActive=true) after transfer");
    }

    /// <summary>
    /// Resolves a T2 OrgUnit's internal long surrogate key from its public Guid, so asserts on
    /// T1-child DTO fields (long OrgUnitId FK) can compare against the unit a request named.
    /// IgnoreQueryFilters: the root-scope OrgDbContext has no tenant context, and the tenant
    /// filter would otherwise hide every row.
    /// </summary>
    private async Task<long> GetOrgUnitDbIdAsync(Guid publicId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrgDbContext>();
        return await db.OrgUnits.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.PublicId == publicId)
            .Select(u => u.Id)
            .SingleAsync();
    }

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
        // OrgUnitsController carries [RequiresMfa] — supply a fresh mfa_at claim so the
        // MfaRequirement passes and assertions exercise the permission layer, not MFA.
        client.DefaultRequestHeaders.Add(TestAuthHandler.MfaAtHeader,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        return client;
    }
}
