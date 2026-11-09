using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Integration tests for GET /api/v1/workflow/instances/my-processed ("dossiers I have processed").
///
/// The endpoint is driven by a raw Dapper query over WorkflowInstanceHistories (TransitionedBy index)
/// joined to WorkflowInstances. These facts exercise the SQL semantics against real rows:
///   - participation (only instances the caller has transitioned appear)
///   - DISTINCT (one row per instance no matter how many transitions)
///   - tenant isolation enforced via the instance JOIN (history has no TenantId column)
///   - EntityType = 'BusinessProcess' scoping
///   - soft-delete predicates (instance excluded when deleted; current-state deletion only nulls the name)
///   - keyset pagination over InstanceId DESC (no duplicate/skip across pages, hasNextPage boundary)
///   - malformed cursor → 400, authz gate (401/403/200)
///
/// Seeding strategy: definition + instance created via the API (FK-valid rows + initial state), then
/// history rows / soft-delete flips / state moves applied EF-direct so each variant is controlled
/// precisely. Every test uses a fresh (tenantId, userId) pair so the caller's participation set is
/// exactly what the test seeds — shared-DB rows from other tests cannot pollute exact-count assertions.
///
/// View 1 (inbox) coverage is intentionally NOT duplicated here: WorkflowTasksInboxTests F1-F5 already
/// cover the assignee/pool arms, terminal exclusion, cross-tenant isolation and the 401/403/200 gate.
/// </summary>
[Collection("Integration")]
public class WorkflowInstancesMyProcessedTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string WorkflowDefinitionsUrl = "/api/v1/workflow/definitions";
    private const string InstancesUrl = "/api/v1/workflow/instances";
    private const string MyProcessedUrl = "/api/v1/workflow/instances/my-processed";

    private const string BpViewPerm = PermCodes.BusinessProcess.Bp.View;
    private const string FirstStatePerm = PermCodes.Workflow.Task.Process;

    // ─────────────────────────────────────────────────────────────────────
    // Seeding helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a Draft→Approved workflow definition under <paramref name="tenantId"/> and seeds a
    /// first-state assignment (so instance creation is not blocked by the empty-pool hard-block).
    /// Returns the definition PublicId.
    /// </summary>
    private async Task<Guid> SeedDefinitionAsync(HttpClient adminClient, Guid tenantId)
    {
        // Name must be unique per (TenantId, Name) — the definition table has a unique index there
        // (WorkflowDefinitionConfiguration), so tests that seed several definitions under one tenant
        // (keyset/boundary) would otherwise collide and the create endpoint would surface a 500.
        var defResp = await adminClient.PostAsJsonAsync(WorkflowDefinitionsUrl, new
        {
            Name = $"My-Processed Test Workflow {Guid.NewGuid():N}",
            Description = "Seeded for WorkflowInstancesMyProcessedTests",
            TenantId = tenantId.ToString(),
            CreatedBy = Guid.NewGuid(),
            EntityType = "Generic",
            States = new[]
            {
                new { Name = "Draft", DisplayNameVi = "Nháp", DisplayNameEn = "Draft",
                      IsInitial = true, IsFinal = false, Color = "#808080", SortOrder = 1 },
                new { Name = "Approved", DisplayNameVi = "Đã duyệt", DisplayNameEn = "Approved",
                      IsInitial = false, IsFinal = true, Color = "#52c41a", SortOrder = 2 },
            },
            Transitions = new[]
            {
                new { FromStateName = "Draft", ToStateName = "Approved",
                      ActionName = "Approve", ActionLabelVi = "Phê duyệt", ActionLabelEn = "Approve",
                      ConditionsJson = (string?)null, SortOrder = 1 },
            },
        });

        if (defResp.StatusCode != HttpStatusCode.OK)
        {
            var body = await defResp.Content.ReadAsStringAsync();
            defResp.StatusCode.Should().Be(HttpStatusCode.OK, $"definition seed must succeed. body={body}");
        }

        var definitionPublicId = (await defResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        await WorkflowSeedHelpers.SeedFirstStateAssignmentByPermissionAsync(
            Factory, definitionPublicId, FirstStatePerm);

        return definitionPublicId;
    }

    /// <summary>
    /// Creates one instance for the definition with the given <paramref name="entityType"/>.
    /// Returns the instance PublicId, internal Id, and the Draft/Approved state ids of its definition.
    /// </summary>
    private async Task<(Guid PublicId, long Id, long DraftStateId, long ApprovedStateId)> SeedInstanceAsync(
        HttpClient adminClient, Guid definitionPublicId, string entityType)
    {
        var instResp = await adminClient.PostAsJsonAsync(InstancesUrl, new
        {
            EntityType = entityType,
            EntityId = Guid.NewGuid(),
            DefinitionId = definitionPublicId,
        });
        instResp.StatusCode.Should().Be(HttpStatusCode.OK, "instance seed must succeed");

        var instancePublicId = (await instResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var instance = await ctx.WorkflowInstances.AsNoTracking()
            .FirstAsync(i => i.PublicId == instancePublicId);

        var states = await ctx.WorkflowStates.AsNoTracking()
            .Where(s => s.DefinitionId == instance.DefinitionId)
            .ToListAsync();

        var draftId = states.First(s => s.IsInitial).Id;
        var approvedId = states.First(s => s.IsFinal).Id;

        return (instancePublicId, instance.Id, draftId, approvedId);
    }

    /// <summary>
    /// Appends <paramref name="count"/> history rows for the instance, all transitioned by
    /// <paramref name="transitionedBy"/>. The my-processed query reads only InstanceId + TransitionedBy
    /// from history, so the from/to state ids only need to be FK-valid.
    /// </summary>
    private async Task SeedHistoryAsync(
        long instanceId, long fromStateId, long toStateId, Guid transitionedBy, int count)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        for (var i = 0; i < count; i++)
        {
            var history = WorkflowInstanceHistory.Create(
                gen,
                instanceId: instanceId,
                fromStateId: fromStateId,
                toStateId: toStateId,
                actionName: "Approve",
                transitionedBy: transitionedBy,
                comment: null,
                transitionedAt: DateTimeOffset.UtcNow);

            ctx.WorkflowInstanceHistories.Add(history);
        }

        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a fully-formed "processed" BP instance: definition + instance (EntityType=BusinessProcess)
    /// + <paramref name="transitionCount"/> history rows transitioned by <paramref name="userId"/>.
    /// </summary>
    private async Task<(Guid PublicId, long Id, long DraftStateId, long ApprovedStateId)>
        SeedProcessedBpInstanceAsync(Guid userId, Guid tenantId, int transitionCount)
    {
        using var adminClient = CreateAuthenticatedClient(roles: ["SystemAdmin"], tenantId: tenantId.ToString());
        var definitionPublicId = await SeedDefinitionAsync(adminClient, tenantId);
        var instance = await SeedInstanceAsync(adminClient, definitionPublicId, "BusinessProcess");

        if (transitionCount > 0)
            await SeedHistoryAsync(instance.Id, instance.DraftStateId, instance.ApprovedStateId, userId, transitionCount);

        return instance;
    }

    private async Task ExecuteRawAsync(string sql, params object[] parameters)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        await ctx.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    private static List<JsonElement> ReadItems(JsonElement body)
    {
        var data = body.GetProperty("data");
        return data.GetProperty("items").EnumerateArray().ToList();
    }

    private static List<Guid> ReadPublicIds(JsonElement body) =>
        ReadItems(body)
            .Select(i => i.GetProperty("workflowInstancePublicId").GetGuid())
            .ToList();

    // ─────────────────────────────────────────────────────────────────────
    // Authorization gate
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyProcessed_Anonymous_Returns401()
    {
        using var anonClient = Factory.CreateClient();

        var resp = await anonClient.GetAsync(MyProcessedUrl);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anonymous access to my-processed must be rejected with 401");
    }

    [Fact]
    public async Task GetMyProcessed_WithoutBpViewPermission_Returns403()
    {
        // An unrelated permission passes [Authorize] but fails [Authorize(Policy = BUSINESSPROCESS.BP.VIEW)].
        using var client = CreateClientWithPermissions(
            [PermCodes.Workflow.Assign.Manage], userId: Guid.NewGuid().ToString(),
            tenantId: Guid.NewGuid().ToString());

        var resp = await client.GetAsync(MyProcessedUrl);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "caller lacking BUSINESSPROCESS.BP.VIEW must get 403 — proves explicit policy gate");
    }

    [Fact]
    public async Task GetMyProcessed_WithBpViewPermission_Returns200()
    {
        using var client = CreateClientWithPermissions(
            [BpViewPerm], userId: Guid.NewGuid().ToString(), tenantId: Guid.NewGuid().ToString());

        var resp = await client.GetAsync(MyProcessedUrl);

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "caller with BUSINESSPROCESS.BP.VIEW must reach the handler (empty list is fine)");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Inclusion / exclusion semantics
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyProcessed_StartedButNoTransition_Excluded()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        // Instance with ZERO history — the caller started it but never transitioned it.
        await SeedProcessedBpInstanceAsync(userId, tenantId, transitionCount: 0);

        using var client = CreateClientWithPermissions([BpViewPerm], userId.ToString(), tenantId.ToString());
        var body = await (await client.GetAsync(MyProcessedUrl)).Content.ReadFromJsonAsync<JsonElement>();

        ReadItems(body).Should().BeEmpty(
            "an instance with no history rows for the caller must not appear (history-driven query)");
    }

    [Fact]
    public async Task GetMyProcessed_MultipleTransitionsSameUser_AppearsExactlyOnce()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        // 3 transitions by the same user on ONE instance → DISTINCT must collapse to a single row.
        var instance = await SeedProcessedBpInstanceAsync(userId, tenantId, transitionCount: 3);

        using var client = CreateClientWithPermissions([BpViewPerm], userId.ToString(), tenantId.ToString());
        var body = await (await client.GetAsync(MyProcessedUrl)).Content.ReadFromJsonAsync<JsonElement>();

        var ids = ReadPublicIds(body);
        ids.Should().ContainSingle(id => id == instance.PublicId,
            "an instance the caller transitioned N times must appear exactly once (DISTINCT InstanceId)");
    }

    [Fact]
    public async Task GetMyProcessed_ReturnsOnlyCallerTransitioned_ExcludingSystemActorInstances()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        // Instance A — transitioned by the caller; must appear.
        var mine = await SeedProcessedBpInstanceAsync(userId, tenantId, transitionCount: 1);

        // Instance B — same tenant, transitioned ONLY by the system actor (Guid.Empty — used by
        // DelegationExpiryJob/EscalationCheckJob/migrations), never by the caller. The query has no
        // special Guid.Empty predicate; exclusion is purely the TransitionedBy = @UserId filter.
        // Seeding A alongside B makes the result set non-empty, so NotContain(B) genuinely
        // discriminates participation rather than passing on an empty list.
        var systemOnly = await SeedProcessedBpInstanceAsync(Guid.Empty, tenantId, transitionCount: 1);

        using var client = CreateClientWithPermissions([BpViewPerm], userId.ToString(), tenantId.ToString());
        var body = await (await client.GetAsync(MyProcessedUrl)).Content.ReadFromJsonAsync<JsonElement>();

        var ids = ReadPublicIds(body);
        ids.Should().Contain(mine.PublicId, "the caller's own transitioned instance must appear");
        ids.Should().NotContain(systemOnly.PublicId,
            "an instance transitioned only by the system actor (Guid.Empty) must not surface for the caller");
    }

    [Fact]
    public async Task GetMyProcessed_CrossTenant_Excluded()
    {
        var userId = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Instance + history live under tenantA, transitioned by userId.
        var instance = await SeedProcessedBpInstanceAsync(userId, tenantA, transitionCount: 1);

        // SAME user, but authenticated under a DIFFERENT tenant. History has no TenantId column —
        // isolation relies entirely on the JOIN to WorkflowInstances.TenantId.
        using var client = CreateClientWithPermissions([BpViewPerm], userId.ToString(), tenantB.ToString());
        var body = await (await client.GetAsync(MyProcessedUrl)).Content.ReadFromJsonAsync<JsonElement>();

        ReadPublicIds(body).Should().NotContain(instance.PublicId,
            "tenant isolation via the instance JOIN must hide another tenant's dossier");
    }

    [Fact]
    public async Task GetMyProcessed_NonBusinessProcessEntityType_Excluded()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        using var adminClient = CreateAuthenticatedClient(roles: ["SystemAdmin"], tenantId: tenantId.ToString());
        var definitionPublicId = await SeedDefinitionAsync(adminClient, tenantId);

        // A non-BP instance (EntityType="Generic") transitioned by the caller — must be filtered out.
        var generic = await SeedInstanceAsync(adminClient, definitionPublicId, "Generic");
        await SeedHistoryAsync(generic.Id, generic.DraftStateId, generic.ApprovedStateId, userId, 1);

        using var client = CreateClientWithPermissions([BpViewPerm], userId.ToString(), tenantId.ToString());
        var body = await (await client.GetAsync(MyProcessedUrl)).Content.ReadFromJsonAsync<JsonElement>();

        ReadPublicIds(body).Should().NotContain(generic.PublicId,
            "only EntityType='BusinessProcess' instances are returned by the BP-Run list");
    }

    [Fact]
    public async Task GetMyProcessed_SoftDeletedInstance_Excluded()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var instance = await SeedProcessedBpInstanceAsync(userId, tenantId, transitionCount: 1);
        await ExecuteRawAsync("UPDATE workflow.WorkflowInstances SET IsDeleted = 1 WHERE Id = {0}", instance.Id);

        using var client = CreateClientWithPermissions([BpViewPerm], userId.ToString(), tenantId.ToString());
        var body = await (await client.GetAsync(MyProcessedUrl)).Content.ReadFromJsonAsync<JsonElement>();

        ReadPublicIds(body).Should().NotContain(instance.PublicId,
            "soft-deleted instance must be excluded (i.IsDeleted = 0 predicate)");
    }

    [Fact]
    public async Task GetMyProcessed_SoftDeletedCurrentState_StillAppearsWithEmptyName()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var instance = await SeedProcessedBpInstanceAsync(userId, tenantId, transitionCount: 1);

        // Soft-delete the instance's CURRENT state row. The LEFT JOIN must keep the dossier the caller
        // demonstrably processed; the name coalesces to '' rather than dropping the row.
        await ExecuteRawAsync(
            "UPDATE workflow.WorkflowStates SET IsDeleted = 1 WHERE Id = {0}", instance.DraftStateId);

        using var client = CreateClientWithPermissions([BpViewPerm], userId.ToString(), tenantId.ToString());
        var body = await (await client.GetAsync(MyProcessedUrl)).Content.ReadFromJsonAsync<JsonElement>();

        var item = ReadItems(body).SingleOrDefault(i =>
            i.GetProperty("workflowInstancePublicId").GetGuid() == instance.PublicId);

        item.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "instance must still appear even though its current state is soft-deleted (LEFT JOIN)");
        item.GetProperty("currentStateName").GetString().Should().BeEmpty(
            "deleted current-state name must coalesce to '' (COALESCE(s.DisplayNameVi, ''))");
        item.GetProperty("isFinal").GetBoolean().Should().BeFalse(
            "an absent (soft-deleted) current state must coalesce IsFinal to false (COALESCE(s.IsFinal, 0))");
    }

    [Fact]
    public async Task GetMyProcessed_FinalStateInstance_StillAppears()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var instance = await SeedProcessedBpInstanceAsync(userId, tenantId, transitionCount: 1);

        // Move the instance to its final state + stamp CompletedAt — the query has no status filter,
        // so a completed/final dossier must still be listed.
        await ExecuteRawAsync(
            "UPDATE workflow.WorkflowInstances SET CurrentStateId = {0}, CompletedAt = SYSUTCDATETIME() WHERE Id = {1}",
            instance.ApprovedStateId, instance.Id);

        using var client = CreateClientWithPermissions([BpViewPerm], userId.ToString(), tenantId.ToString());
        var body = await (await client.GetAsync(MyProcessedUrl)).Content.ReadFromJsonAsync<JsonElement>();

        var item = ReadItems(body).SingleOrDefault(i =>
            i.GetProperty("workflowInstancePublicId").GetGuid() == instance.PublicId);

        item.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "a final/completed instance must still appear (no status filter on the list)");
        item.GetProperty("isFinal").GetBoolean().Should().BeTrue(
            "isFinal must reflect the current WorkflowState.IsFinal flag");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Keyset pagination
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyProcessed_Keyset_WalksPagesNoDuplicateOrSkip()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        // Seed 3 processed instances for one caller. Sort is InstanceId DESC (newest first).
        // WorkflowInstance.Id is a SQL IDENTITY column and these calls are awaited sequentially,
        // so the ids strictly increase i1 < i2 < i3 → DESC order is [i3, i2, i1].
        var i1 = await SeedProcessedBpInstanceAsync(userId, tenantId, 1);
        var i2 = await SeedProcessedBpInstanceAsync(userId, tenantId, 1);
        var i3 = await SeedProcessedBpInstanceAsync(userId, tenantId, 1);
        var expectedDescending = new[] { i3.PublicId, i2.PublicId, i1.PublicId };

        using var client = CreateClientWithPermissions([BpViewPerm], userId.ToString(), tenantId.ToString());

        // Page 1 — pageSize 2 → first two newest, hasNextPage true.
        var page1 = await (await client.GetAsync($"{MyProcessedUrl}?pageSize=2"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var page1Ids = ReadPublicIds(page1);
        page1Ids.Should().Equal(expectedDescending[0], expectedDescending[1]);
        page1.GetProperty("data").GetProperty("meta").GetProperty("hasNextPage").GetBoolean()
            .Should().BeTrue("3 rows with pageSize 2 must report a next page");
        var nextCursor = page1.GetProperty("data").GetProperty("meta").GetProperty("nextCursor").GetString();
        nextCursor.Should().NotBeNullOrEmpty("page 1 must hand back a cursor for page 2");

        // Page 2 — remaining row, no duplicate of page 1, no skip, hasNextPage false.
        var page2 = await (await client.GetAsync($"{MyProcessedUrl}?pageSize=2&cursor={Uri.EscapeDataString(nextCursor!)}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var page2Ids = ReadPublicIds(page2);
        page2Ids.Should().Equal(expectedDescending[2]);
        page2Ids.Should().NotIntersectWith(page1Ids, "keyset paging must not repeat a row across pages");
        page2.GetProperty("data").GetProperty("meta").GetProperty("hasNextPage").GetBoolean()
            .Should().BeFalse("the last page must report no next page");
    }

    [Fact]
    public async Task GetMyProcessed_HasNextPage_FalseAtExactPageSizeBoundary()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        // Exactly pageSize rows → the N+1 peek finds nothing extra → hasNextPage must be false.
        await SeedProcessedBpInstanceAsync(userId, tenantId, 1);
        await SeedProcessedBpInstanceAsync(userId, tenantId, 1);

        using var client = CreateClientWithPermissions([BpViewPerm], userId.ToString(), tenantId.ToString());
        var body = await (await client.GetAsync($"{MyProcessedUrl}?pageSize=2"))
            .Content.ReadFromJsonAsync<JsonElement>();

        ReadItems(body).Should().HaveCount(2);
        body.GetProperty("data").GetProperty("meta").GetProperty("hasNextPage").GetBoolean()
            .Should().BeFalse("with exactly pageSize results the next-page peek must be empty");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Cursor validation
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyProcessed_MalformedCursor_Returns400()
    {
        using var client = CreateClientWithPermissions(
            [BpViewPerm], userId: Guid.NewGuid().ToString(), tenantId: Guid.NewGuid().ToString());

        // Not valid base64 of a long → codec TryDecode fails → BadRequestError (GOV_WORKFLOW_040).
        var resp = await client.GetAsync($"{MyProcessedUrl}?cursor={Uri.EscapeDataString("!!!not-a-cursor!!!")}");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a malformed cursor token must return 400, never 500");
    }
}
