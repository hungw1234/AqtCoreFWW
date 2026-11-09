using System.Net;
using System.Net.Http.Json;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Phase 3 (RT3-M1 / RT3-H3) — FAILING-BY-DEFAULT 403 MERGE-GATE.
///
/// Coarse endpoint policy <c>PermCodes.Workflow.Task.Process</c> ("WORKFLOW.TASK.PROCESS",
/// auto-registered via Identity ModuleRegistration `foreach (PermCodes.All) AddPolicy`)
/// must gate ALL 4 state-mutating execute surfaces as an independent secondary
/// defense-in-depth layer (the fail-CLOSED resolver gate lands in a later P3 slice):
///   1. WorkflowInstancesController.ExecuteTransition  POST instances/{id}/transitions
///   2. WorkflowInstancesController.BatchTransition     POST instances/batch-transition
///   3. BusinessProcessesController.ExecuteTransition   POST business-processes/runs/{id}/transitions
///      (already [Authorize(Bp.View)] — multiple [Authorize] AND-stack; this proves the
///       ADDED Task.Process layer denies even when Bp.View is satisfied).
///   4. WorkflowInstancesController.ResolveBranchChild  POST instances/{id}/branch-children/{cs}/resolve
///      (RT3-M1-F1, user-ruled 3→4 — auto-transitions, dispatches ResolveBranchChildCommand).
///
/// Without the policy, an authenticated principal lacking WORKFLOW.TASK.PROCESS reaches
/// the handler (404/422 for a missing instance) — NOT 403. This suite is RED until the
/// attributes are added and is the merge-gate that makes any partial P3 merge fail green.
///
/// Positive control guards against false-green (everything 403s for an unrelated reason):
/// a principal WITH the permission must NOT get 403 on the same call.
/// </summary>
[Collection("Integration")]
public class WorkflowExecuteTransitionAuthzGateTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private static readonly Guid MissingInstance = Guid.NewGuid();

    [Fact]
    public async Task ExecuteTransition_PrincipalLacksProcessPermission_Returns403()
    {
        using var client = CreateClientWithPermissions([], tenantId: Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/workflow/instances/{MissingInstance}/transitions",
            new { TransitionId = 1L, Comment = "x" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "coarse Workflow.Task.Process policy must gate ExecuteTransition before the handler");
    }

    [Fact]
    public async Task BatchTransition_PrincipalLacksProcessPermission_Returns403()
    {
        using var client = CreateClientWithPermissions([], tenantId: Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(
            "/api/v1/workflow/instances/batch-transition",
            new { Items = new[] { new { InstanceId = MissingInstance, TransitionId = 1L } } });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "BatchTransition is the 3rd-surface authz hole (RT3-M1) — same coarse policy required");
    }

    [Fact]
    public async Task BpExecuteTransition_HasBpViewButLacksProcessPermission_Returns403()
    {
        // Has Bp.View (the pre-existing gate passes) but NOT Workflow.Task.Process —
        // proves the ADDED secondary layer denies independently.
        using var client = CreateClientWithPermissions(
            [PermCodes.BusinessProcess.Bp.View], tenantId: Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/business-processes/runs/{MissingInstance}/transitions",
            new { BpPublicId = Guid.NewGuid(), ActionName = "Submit", ConsentGiven = false });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "BP execute path must also carry the coarse Workflow.Task.Process layer (RT3-M1)");
    }

    [Fact]
    public async Task ResolveBranchChild_PrincipalLacksProcessPermission_Returns403()
    {
        // RT3-M1-F1 (user ruling a): ResolveBranchChild is the 4th state-mutating
        // execute surface (dispatches ResolveBranchChildCommand → auto-transition).
        // The coarse Workflow.Task.Process layer must gate it too.
        using var client = CreateClientWithPermissions([], tenantId: Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/workflow/instances/{MissingInstance}/branch-children/1/resolve",
            new { ResolutionType = 1, Comment = "x" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "ResolveBranchChild auto-transitions — same coarse policy required (RT3-M1 3→4)");
    }

    [Fact]
    public async Task ExecuteTransition_PrincipalHasProcessPermission_IsNot403()
    {
        // Positive control: with the permission the policy passes; a missing instance
        // yields 404/422 — anything but 403. Guards against false-green.
        using var client = CreateClientWithPermissions(
            [PermCodes.Workflow.Task.Process], tenantId: Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/workflow/instances/{MissingInstance}/transitions",
            new { TransitionId = 1L, Comment = "x" });

        resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "principal holding Workflow.Task.Process must clear the coarse policy");
    }
}
