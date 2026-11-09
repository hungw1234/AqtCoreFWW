using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Phase-3 Slice-5 — Fine-grained resolver fail-CLOSED gate (HTTP layer).
///
/// Tests the authz-of-T gate wired in ExecuteTransitionCommandHandler (corrected D1 semantic):
///   A transition T is authorized iff the actor satisfies T's OWN assignment rule
///   (T as outgoing of the CURRENT state — two separate resolver invocations, never conflated).
///
///   Case A: T's own rule satisfied (ByPermission matches actor)                  → 200
///   Case B: T's own rule not satisfied (actor lacks required permission code)    → 403
///   Case C: T's own rule passes but targetState has empty outgoing pool          → 422 GOV_WFL_014
///   Case D: Resolver throws on T's own rule (PreviousHandler clause)            → 403, no stack leak
///
/// Seeding strategy (corrected D1):
///   - Definition + instance created via SystemAdmin API client.
///   - WorkflowTransitionAssignment for the EXECUTED transition (T) gates Cases A/B/C/D.
///   - FinalApprove assignment seeds the WF_NO_HANDLER pre-check for Case A/B (non-final path).
///   - Cases C and D require specific seeding so authz-of-T resolves/throws correctly.
///
/// All actors carry PermCodes.Workflow.Task.Process to clear the coarse G1 gate;
/// the fine-grained authz-of-T gate is the only differentiator.
///
/// IMPORTANT: G1 (WorkflowExecuteTransitionAuthzGateTests) MUST stay 5/5 GREEN;
/// this suite tests a different (finer-grained) layer on top of G1.
/// </summary>
[Collection("Integration")]
public class WorkflowFineResolverGateTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string DefinitionsUrl = "/api/v1/workflow/definitions";
    private const string InstancesUrl = "/api/v1/workflow/instances";

    // Unique permission code for this test class to avoid cross-test pollution
    private const string ApprovePermission = "CASES.WORKFLOW.FINE_APPROVE";

    // ─────────────────────────────────────────────────────────────────────
    // Shared seeding helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a 3-state workflow (Draft [initial] → UnderReview [non-final] → Approved [final]).
    ///
    /// Authz-of-T seeding (corrected D1 semantic):
    ///   - Submit (Draft→UnderReview): ByPermission(ApprovePermission) — actor executing Submit
    ///     must satisfy THIS transition's own rule. Case A actor has ApprovePermission → 200.
    ///     Case B actor lacks it → 403.
    ///
    /// WF_NO_HANDLER seeding (non-final UnderReview):
    ///   - FinalApprove (UnderReview→Approved): ByPermission(ApprovePermission) — ensures UnderReview
    ///     has a non-empty outgoing pool so the WF_NO_HANDLER pre-check does not fire when
    ///     the instance lands in UnderReview (pool resolved separately, NOT for authz-of-T on Submit).
    ///
    /// Uses SystemAdmin for all API calls.
    /// Returns (instanceId, submitTransitionId).
    /// </summary>
    private async Task<(Guid InstanceId, long SubmitTransitionId)> SeedThreeStateWorkflowAsync(
        string tenantId,
        string instanceCreatorId)
    {
        using var adminClient = CreateAuthenticatedClient(
            roles: ["SystemAdmin"],
            tenantId: tenantId,
            userId: instanceCreatorId);

        var createResp = await adminClient.PostAsJsonAsync(DefinitionsUrl, new
        {
            Name = "Fine Resolver Gate Test Workflow",
            Description = "3-state definition for Slice-5 integration tests",
            TenantId = tenantId,
            CreatedBy = Guid.Parse(instanceCreatorId),
            EntityType = "Generic",
            States = new[]
            {
                new { Name = "Draft", DisplayNameVi = "Nháp", DisplayNameEn = "Draft",
                      IsInitial = true, IsFinal = false, Color = "#808080", SortOrder = 1 },
                new { Name = "UnderReview", DisplayNameVi = "Đang xét duyệt", DisplayNameEn = "UnderReview",
                      IsInitial = false, IsFinal = false, Color = "#1890ff", SortOrder = 2 },
                new { Name = "Approved", DisplayNameVi = "Đã duyệt", DisplayNameEn = "Approved",
                      IsInitial = false, IsFinal = true, Color = "#52c41a", SortOrder = 3 },
            },
            Transitions = new[]
            {
                new { FromStateName = "Draft", ToStateName = "UnderReview",
                      ActionName = "Submit", ActionLabelVi = "Nộp", ActionLabelEn = "Submit",
                      ConditionsJson = (string?)null, SortOrder = 1 },
                new { FromStateName = "UnderReview", ToStateName = "Approved",
                      ActionName = "FinalApprove", ActionLabelVi = "Phê duyệt cuối", ActionLabelEn = "FinalApprove",
                      ConditionsJson = (string?)null, SortOrder = 2 },
            },
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK, "definition seed must succeed");
        var defBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var definitionPublicId = defBody.GetProperty("data").GetProperty("id").GetGuid();

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var defEntity = await ctx.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.PublicId == definitionPublicId);
        defEntity.Should().NotBeNull("definition must be persisted");

        var submitTransition = await ctx.Set<WorkflowTransition>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.DefinitionId == defEntity!.Id && t.ActionName == "Submit");
        submitTransition.Should().NotBeNull("Submit transition must exist");

        var finalApproveTransition = await ctx.Set<WorkflowTransition>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.DefinitionId == defEntity!.Id && t.ActionName == "FinalApprove");
        finalApproveTransition.Should().NotBeNull("FinalApprove transition must exist");

        // Authz-of-T assignment for Submit (Draft→UnderReview):
        //   Actor executing Submit must satisfy THIS transition's own ByPermission rule.
        //   Resolver is called: resolver.Resolve(draftState, [OA(Submit, this-assignment)], ctx).
        //   Case A actor has ApprovePermission → authorized. Case B actor lacks it → 403.
        var submitAssignment = WorkflowTransitionAssignment.Create(
            workflowTransitionId: submitTransition!.Id,
            combineMode: AssignmentCombineMode.Union);
        submitAssignment.AddClause(AssignmentClauseMode.ByPermission,
            $"""["{ApprovePermission}"]""");
        ctx.Set<WorkflowTransitionAssignment>().Add(submitAssignment);

        // WF_NO_HANDLER pre-check assignment for FinalApprove (UnderReview→Approved):
        //   When Submit succeeds and instance enters UnderReview, the WF_NO_HANDLER check resolves
        //   UnderReview's outgoing pool (FinalApprove). FinalApprove must have a non-empty assignment
        //   so UnderReview is not flagged as misconfigured. FinalApprove's own authz-of-T is checked
        //   separately when that transition is executed (out of scope for Cases A/B).
        var finalApproveAssignment = WorkflowTransitionAssignment.Create(
            workflowTransitionId: finalApproveTransition!.Id,
            combineMode: AssignmentCombineMode.Union);
        finalApproveAssignment.AddClause(AssignmentClauseMode.ByPermission,
            $"""["{ApprovePermission}"]""");
        ctx.Set<WorkflowTransitionAssignment>().Add(finalApproveAssignment);

        await ctx.SaveChangesAsync();

        var instanceResp = await adminClient.PostAsJsonAsync(InstancesUrl, new
        {
            EntityType = "Generic",
            EntityId = Guid.NewGuid(),
            DefinitionId = definitionPublicId,
        });
        instanceResp.StatusCode.Should().Be(HttpStatusCode.OK, "instance seed must succeed");
        var instanceBody = await instanceResp.Content.ReadFromJsonAsync<JsonElement>();
        var instanceId = instanceBody.GetProperty("data").GetProperty("id").GetGuid();

        return (instanceId, submitTransition.Id);
    }

    /// <summary>
    /// Seeds a 2-state workflow (Draft [initial] → Submitted [non-final]) for Case C (WF_NO_HANDLER).
    ///
    /// Authz-of-T for Submit (Draft→Submitted): ByPermission(ApprovePermission) seeded so the
    /// authz-of-T gate PASSES (actor has ApprovePermission). This is required by D1: authz-of-T
    /// must pass before the WF_NO_HANDLER pre-check executes.
    ///
    /// WF_NO_HANDLER for Submitted: NO outgoing assignment seeded on Submitted (no FinalX transition).
    /// Submitted is non-final with empty outgoing pool → WF_NO_HANDLER → 422.
    ///
    /// State names from Generic EntityType allowed list.
    /// Returns (instanceId, submitTransitionId).
    /// </summary>
    private async Task<(Guid InstanceId, long SubmitTransitionId)> SeedEmptyPoolWorkflowAsync(
        string tenantId,
        string actorId)
    {
        using var adminClient = CreateAuthenticatedClient(
            roles: ["SystemAdmin"],
            tenantId: tenantId,
            userId: actorId);

        var createResp = await adminClient.PostAsJsonAsync(DefinitionsUrl, new
        {
            Name = "Empty Pool Test Workflow",
            Description = "Submitted has empty outgoing pool — Slice-5 WF_NO_HANDLER test (Case C)",
            TenantId = tenantId,
            CreatedBy = Guid.Parse(actorId),
            EntityType = "Generic",
            States = new[]
            {
                new { Name = "Draft", DisplayNameVi = "Nháp", DisplayNameEn = "Draft",
                      IsInitial = true, IsFinal = false, Color = "#808080", SortOrder = 1 },
                new { Name = "Submitted", DisplayNameVi = "Đã nộp", DisplayNameEn = "Submitted",
                      IsInitial = false, IsFinal = false, Color = "#faad14", SortOrder = 2 },
            },
            Transitions = new[]
            {
                new { FromStateName = "Draft", ToStateName = "Submitted",
                      ActionName = "Submit", ActionLabelVi = "Nộp", ActionLabelEn = "Submit",
                      ConditionsJson = (string?)null, SortOrder = 1 },
            },
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK, "empty-pool definition seed must succeed");
        var defBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var definitionPublicId = defBody.GetProperty("data").GetProperty("id").GetGuid();

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var defEntity = await ctx.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.PublicId == definitionPublicId);
        var submitTransition = await ctx.Set<WorkflowTransition>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.DefinitionId == defEntity!.Id && t.ActionName == "Submit");
        submitTransition.Should().NotBeNull("Submit transition must exist");

        // Authz-of-T assignment for Submit (Draft→Submitted):
        //   Actor must satisfy THIS transition's own rule (D1 authz-of-T gate).
        //   Seeded with ApprovePermission so the authz-of-T gate PASSES for the test actor
        //   (who has ApprovePermission + Task.Process). Without this, authz-of-T fires 403
        //   before WF_NO_HANDLER can trigger → test would assert 403 instead of 422.
        var submitAssignment = WorkflowTransitionAssignment.Create(
            workflowTransitionId: submitTransition!.Id,
            combineMode: AssignmentCombineMode.Union);
        submitAssignment.AddClause(AssignmentClauseMode.ByPermission,
            $"""["{ApprovePermission}"]""");
        ctx.Set<WorkflowTransitionAssignment>().Add(submitAssignment);

        // NO assignment seeded on Submitted's outgoing transitions (none exist here).
        // Submitted is non-final → WF_NO_HANDLER pre-check fires → empty outgoing pool → 422.

        await ctx.SaveChangesAsync();

        var instanceResp = await adminClient.PostAsJsonAsync(InstancesUrl, new
        {
            EntityType = "Generic",
            EntityId = Guid.NewGuid(),
            DefinitionId = definitionPublicId,
        });
        instanceResp.StatusCode.Should().Be(HttpStatusCode.OK, "instance seed must succeed");
        var instanceBody = await instanceResp.Content.ReadFromJsonAsync<JsonElement>();
        var instanceId = instanceBody.GetProperty("data").GetProperty("id").GetGuid();

        return (instanceId, submitTransition.Id);
    }

    /// <summary>
    /// Seeds a 2-state workflow (Draft [initial] → UnderReview [non-final]) for Case D (resolver throw).
    ///
    /// Authz-of-T for Advance (Draft→UnderReview): PreviousHandler clause seeded on the Advance transition
    /// itself. When the actor executes Advance, authz-of-T calls:
    ///   resolver.Resolve(draftState, [OA(Advance, PreviousHandler-assignment)], ctx)
    /// WorkflowHandlerResolver.ApplyClause throws NotSupportedException on PreviousHandler (RT2-15).
    /// Fail-CLOSED catch returns ForbiddenError → 403 (not 500). Exception text must not leak.
    ///
    /// State names from Generic EntityType allowed list.
    /// Returns (instanceId, advanceTransitionId).
    /// </summary>
    /// <summary>
    /// Phase 5b: instance creation now hard-blocks PreviousHandler-clause configurations at
    /// the initial state's outgoing pool (resolver throws → Fail(GOV_WFL_014) → 422).
    /// Returns the definition PublicId so callers can exercise creation directly.
    /// </summary>
    private async Task<Guid> SeedPreviousHandlerWorkflowDefinitionAsync(
        string tenantId,
        string actorId)
    {
        using var adminClient = CreateAuthenticatedClient(
            roles: ["SystemAdmin"],
            tenantId: tenantId,
            userId: actorId);

        var createResp = await adminClient.PostAsJsonAsync(DefinitionsUrl, new
        {
            Name = "PreviousHandler Resolver Throws Test Workflow",
            Description = "Advance transition has PreviousHandler clause → resolver throws → fail-CLOSED 403",
            TenantId = tenantId,
            CreatedBy = Guid.Parse(actorId),
            EntityType = "Generic",
            States = new[]
            {
                new { Name = "Draft", DisplayNameVi = "Nháp", DisplayNameEn = "Draft",
                      IsInitial = true, IsFinal = false, Color = "#808080", SortOrder = 1 },
                new { Name = "UnderReview", DisplayNameVi = "Đang xét duyệt", DisplayNameEn = "UnderReview",
                      IsInitial = false, IsFinal = false, Color = "#faad14", SortOrder = 2 },
            },
            Transitions = new[]
            {
                // Actor executes this; authz-of-T checks Advance's OWN assignment (PreviousHandler)
                // → resolver.Resolve(draftState, [OA(Advance, PreviousHandler-assignment)], ctx) throws.
                new { FromStateName = "Draft", ToStateName = "UnderReview",
                      ActionName = "Advance", ActionLabelVi = "Chuyển", ActionLabelEn = "Advance",
                      ConditionsJson = (string?)null, SortOrder = 1 },
            },
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK, "PreviousHandler definition seed must succeed");
        var defBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var definitionPublicId = defBody.GetProperty("data").GetProperty("id").GetGuid();

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var defEntity = await ctx.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.PublicId == definitionPublicId);
        var advanceTransition = await ctx.Set<WorkflowTransition>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.DefinitionId == defEntity!.Id && t.ActionName == "Advance");
        advanceTransition.Should().NotBeNull("Advance transition must exist");

        // PreviousHandler clause on Advance (Draft→UnderReview) itself:
        //   Authz-of-T invokes resolver.Resolve(draftState, [OA(Advance, this-assignment)], ctx).
        //   WorkflowHandlerResolver.ApplyClause throws NotSupportedException for PreviousHandler (RT2-15).
        //   Fail-CLOSED catch in ExecuteTransitionCommandHandler returns ForbiddenError → 403.
        var advanceAssignment = WorkflowTransitionAssignment.Create(
            workflowTransitionId: advanceTransition!.Id,
            combineMode: AssignmentCombineMode.Union);
        advanceAssignment.AddClause(AssignmentClauseMode.PreviousHandler, null);
        ctx.Set<WorkflowTransitionAssignment>().Add(advanceAssignment);
        await ctx.SaveChangesAsync();

        // Phase 5b: creation is now the surface where PreviousHandler clause at initial-state-outgoing
        // is rejected (resolver throws → Fail(GOV_WFL_014) → 422). Callers exercise that endpoint
        // directly — no instance row exists for this misconfigured definition.
        return definitionPublicId;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Case A: T's own rule satisfied → 200
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteTransition_ResolverPoolSatisfied_Returns2xx()
    {
        var tenantId = Guid.NewGuid().ToString();
        var actorId = Guid.NewGuid().ToString();

        var (instanceId, submitTransitionId) = await SeedThreeStateWorkflowAsync(tenantId, actorId);

        // Actor has coarse gate permission AND ApprovePermission for authz-of-T (Submit's own rule)
        using var actor = CreateClientWithPermissions(
            [PermCodes.Workflow.Task.Process, ApprovePermission],
            userId: actorId,
            tenantId: tenantId);

        var resp = await actor.PostAsJsonAsync(
            $"{InstancesUrl}/{instanceId}/transitions",
            new { TransitionId = submitTransitionId, Comment = "submitting" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "actor with Task.Process + ApprovePermission satisfies Submit's own ByPermission rule → 200");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Case B: T's own rule not satisfied → 403
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteTransition_ResolverPoolUnsatisfied_Returns403()
    {
        var tenantId = Guid.NewGuid().ToString();
        var seederId = Guid.NewGuid().ToString();
        var unauthorizedId = Guid.NewGuid().ToString();

        var (instanceId, submitTransitionId) = await SeedThreeStateWorkflowAsync(tenantId, seederId);

        // Actor has Process (passes coarse G1 gate) but lacks ApprovePermission for Submit's own rule
        using var unauthorized = CreateClientWithPermissions(
            [PermCodes.Workflow.Task.Process],
            userId: unauthorizedId,
            tenantId: tenantId);

        var resp = await unauthorized.PostAsJsonAsync(
            $"{InstancesUrl}/{instanceId}/transitions",
            new { TransitionId = submitTransitionId, Comment = "unauthorized" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "actor lacks ApprovePermission for Submit's own rule → authz-of-T gate denies → 403");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Case C: authz-of-T passes, targetState empty outgoing pool → 422 GOV_WFL_014
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteTransition_EmptyPool_NonFinalTarget_Returns422WithNoHandlerCode()
    {
        var tenantId = Guid.NewGuid().ToString();
        var actorId = Guid.NewGuid().ToString();

        // Submit has ByPermission(ApprovePermission) seeded so authz-of-T passes.
        // Submitted has no outgoing assignments → WF_NO_HANDLER pre-check fires → 422.
        var (instanceId, submitTransitionId) = await SeedEmptyPoolWorkflowAsync(tenantId, actorId);

        // Actor has Task.Process (G1) + ApprovePermission (authz-of-T for Submit's own rule)
        using var actor = CreateClientWithPermissions(
            [PermCodes.Workflow.Task.Process, ApprovePermission],
            userId: actorId,
            tenantId: tenantId);

        var resp = await actor.PostAsJsonAsync(
            $"{InstancesUrl}/{instanceId}/transitions",
            new { TransitionId = submitTransitionId, Comment = "no-handler" });

        // 422 Unprocessable Entity — WF_NO_HANDLER pre-check fires after authz-of-T passes
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "authz-of-T passes (ApprovePermission satisfies Submit's rule) but Submitted has empty " +
            "outgoing pool → WF_NO_HANDLER pre-check → 422 (ValidationError)");

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.ToString().Should().Contain("No eligible handler resolved",
            "response body must include the NoHandlerResolved error message for empty handler pool");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Case D (Phase 5b): PreviousHandler clause on initial-state-outgoing is now rejected at
    // INSTANCE CREATION (resolver throws → Fail-CLOSED → 422 GOV_WFL_014), no leak.
    // Pre-5b: rejected at first ETR call to that transition (403). Hardened forward to creation.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateInstance_PreviousHandlerAtInitialState_FailClosed_Returns422_NoExceptionLeak()
    {
        var tenantId = Guid.NewGuid().ToString();
        var actorId = Guid.NewGuid().ToString();

        var definitionPublicId = await SeedPreviousHandlerWorkflowDefinitionAsync(tenantId, actorId);

        // Phase-04-A follow-up 2026-05-22: Create now requires Workflow.Instance.Manage (4-A-A invariant
        // closure on WorkflowInstancesController). Add to actor perms so the request reaches the handler
        // where the resolver fail-CLOSED behavior under test actually fires (vs. being short-circuited 403).
        using var actor = CreateClientWithPermissions(
            [PermCodes.Workflow.Task.Process, PermCodes.Workflow.Instance.Manage],
            userId: actorId,
            tenantId: tenantId);

        var resp = await actor.PostAsJsonAsync(InstancesUrl, new
        {
            EntityType = "Generic",
            EntityId = Guid.NewGuid(),
            DefinitionId = definitionPublicId,
        });

        // 422 UnprocessableEntity — resolver fail-CLOSED at initial-state pool resolution
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "Phase 5b: PreviousHandler clause at initial-state outgoing throws NotSupportedException " +
            "→ fail-CLOSED catch → Fail(NoHandlerResolved) → 422 (ValidationError)");

        // Body must surface GOV_WFL_014 not stale GOV_SYS_002 (4-B-A dual-metadata invariant)
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain(WorkflowErrorCodes.NoHandlerResolved,
            "response body code must surface GOV_WFL_014 (dual-metadata fix)");

        // No internal exception/clause-mode leak
        body.Should().NotContain("NotSupportedException",
            "internal exception type must not leak to response body");
        body.Should().NotContain("PreviousHandler",
            "internal clause mode name must not leak to response body");
        body.Should().NotContain("RT2-15",
            "internal plan reference must not leak to response body");
    }

    // ─────────────────────────────────────────────────────────────────────
    // A7 (Slice 8a TDD): RequiredPermissionCode stripped from wire format
    //
    // Spec-lock (S5 META-L (c) anti-tautology preconditions):
    //   (a) GET /available-transitions returns 200 — proves endpoint reachable.
    //   (b) Response body is non-empty — proves payload returned.
    //   (c) Response contains the seeded transition (ActionName "Submit") —
    //       proves resolver path healthy (assignment-based authz still works).
    //   (d) Response body does NOT contain "requiredPermissionCode" (case-insensitive) —
    //       SPEC-LOCK: DTO strip propagated through JSON serialization to wire format.
    //   Without (a)+(b)+(c), assert (d) reduces to "empty body has no RPC key" — tautology.
    //
    // RED→GREEN: at baseline (RPC field still in DTO), (d) FAILS because handler maps
    // RPC into DTO and JSON serializer emits the key. After Slice 8a strip (DTO field
    // deleted + SQL SELECT no longer selects column), (d) passes.
    // </summary>
    [Fact]
    public async Task GetAvailableTransitions_ResponseBody_DoesNotContain_RequiredPermissionCode()
    {
        var tenantId = Guid.NewGuid().ToString();
        var actorId = Guid.NewGuid().ToString();

        var (instanceId, _submitTransitionId) = await SeedThreeStateWorkflowAsync(tenantId, actorId);

        // Actor must have ApprovePermission (custom code) so the assignment-based resolver
        // surfaces the Submit transition (resolver filters by ByPermission rule).
        // GET /available-transitions is gated on BusinessProcess.Bp.View (backs the BP run-detail
        // page the inbox links to); add it to the actor's perms.
        using var actor = CreateClientWithPermissions(
            [PermCodes.Workflow.Task.Process, PermCodes.BusinessProcess.Bp.View, ApprovePermission],
            userId: actorId,
            tenantId: tenantId);

        var resp = await actor.GetAsync($"{InstancesUrl}/{instanceId}/available-transitions");

        // (a) Endpoint reachable
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "GET /available-transitions must succeed for SystemAdmin");

        // (b) Body non-empty
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "response body must be present (anti-tautology — proves endpoint produced output)");

        // (c) Response contains expected resolver-authorized transition (Submit/UnderReview seed)
        body.Should().Contain("Submit",
            "resolver path must surface the assignment-authorized Submit transition — proves resolver still healthy after RPC strip");

        // (d) SPEC-LOCK: RequiredPermissionCode key MUST be absent from wire format
        //     (case-insensitive: catches "requiredPermissionCode", "RequiredPermissionCode",
        //      "REQUIREDPERMISSIONCODE", etc.)
        body.ToLowerInvariant().Should().NotContain("requiredpermissioncode",
            "Slice 8a: DTO field removed + SQL SELECT no longer projects column → wire format must not carry RPC key");
    }
}
