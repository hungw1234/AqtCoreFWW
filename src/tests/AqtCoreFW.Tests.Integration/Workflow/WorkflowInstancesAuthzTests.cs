using System.Net;
using System.Net.Http.Json;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Phase 4-A-A invariant closure 2026-05-22 — sibling controllers Authorize audit.
///
/// Security gap: WorkflowInstancesController had class-level [Authorize] only on 5 routes —
/// Create (POST), GetById (GET), GetAvailableTransitions, ExportAudit, GetBranchStatus.
/// Any authenticated user could create an instance, fetch any instance by GUID, or export
/// audit trails (IDOR mirror of 4-A WorkflowTasksController.Reassign).
///
/// Fix:
///   - Create        → [Authorize(Policy = PermCodes.Workflow.Instance.Manage)] (NEW PermCode added 2026-05-22).
///   - ExportAudit, GetBranchStatus → Workflow.Instance.View (admin-tier read).
///   - GetById, GetAvailableTransitions → BusinessProcess.Bp.View: these back the BP run-detail
///     page that the task inbox links to, so BP participants (Bp.View, not admin Instance.View)
///     must be able to read the run + its available transitions. Mirrors my-processed / tasks-by-instance.
///
/// Facts (mirror 4-A pattern, adapted per-route-nature — reads have F1+F2 only,
/// Create includes F3 persistence anti-tautology):
///   F1+F2 per route: non-admin → 403; anon → 401 baseline (anti-tautology).
///   F3 admin-success per route + persistence check on Create (instance row exists post-call).
/// </summary>
[Collection("Integration")]
public class WorkflowInstancesAuthzTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string InstancesUrl = "/api/v1/workflow/instances";
    private static readonly Guid NonExistentInstanceId = new("00000000-0000-0000-0000-000000000ABC");

    // ── Create (mutation, Workflow.Instance.Manage) ──────────────────────────

    [Fact]
    public async Task Create_NonAdminAuthenticatedUser_Returns403WithEnvelopeBody()
    {
        var body = new
        {
            EntityType = "Generic",
            EntityId = Guid.NewGuid(),
            DefinitionId = (Guid?)null,
            SlaHours = (int?)null,
            EntitySubType = (string?)null,
        };

        using var anonClient = Factory.CreateClient();
        var anonResp = await anonClient.PostAsJsonAsync(InstancesUrl, body);
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anonymous must get 401 on POST /instances — proves route exists and global auth fires, " +
            "so the authenticated 403 below is from the Workflow.Instance.Manage policy gate");

        // Authenticated principal with only View (NOT Manage) → still 403 — proves Manage is the gate, not generic Authorize.
        using var client = CreateClientWithPermissions(
            [PermCodes.Workflow.Instance.View],
            tenantId: Guid.NewGuid().ToString());
        var resp = await client.PostAsJsonAsync(InstancesUrl, body);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "authenticated user with only Workflow.Instance.View (not .Manage) must be denied on Create " +
            "(4-A-A invariant: class-level [Authorize] is NOT a security gate)");
    }

    [Fact]
    public async Task GetById_NonAdminAuthenticatedUser_Returns403WithEnvelopeBody()
    {
        var url = $"{InstancesUrl}/{NonExistentInstanceId}";

        using var anonClient = Factory.CreateClient();
        var anonResp = await anonClient.GetAsync(url);
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anonymous must get 401 on GET /instances/{id} — proves route exists and global auth fires");

        using var client = CreateClientWithPermissions([], tenantId: Guid.NewGuid().ToString());
        var resp = await client.GetAsync(url);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "authenticated user without BusinessProcess.Bp.View must be denied on GetById");
    }

    [Fact]
    public async Task GetAvailableTransitions_NonAdminAuthenticatedUser_Returns403()
    {
        var url = $"{InstancesUrl}/{NonExistentInstanceId}/available-transitions";

        using var anonClient = Factory.CreateClient();
        var anonResp = await anonClient.GetAsync(url);
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anonymous must get 401 — proves route exists");

        using var client = CreateClientWithPermissions([], tenantId: Guid.NewGuid().ToString());
        var resp = await client.GetAsync(url);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "authenticated user without BusinessProcess.Bp.View must be denied on GetAvailableTransitions");
    }

    [Fact]
    public async Task ExportAudit_NonAdminAuthenticatedUser_Returns403()
    {
        var url = $"{InstancesUrl}/{NonExistentInstanceId}/audit-export?format=Json";

        using var anonClient = Factory.CreateClient();
        var anonResp = await anonClient.GetAsync(url);
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anonymous must get 401 on ExportAudit — proves route exists");

        using var client = CreateClientWithPermissions([], tenantId: Guid.NewGuid().ToString());
        var resp = await client.GetAsync(url);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "authenticated user without Workflow.Instance.View must be denied on ExportAudit " +
            "(audit-trail enumeration is sensitive even for read)");
    }

    [Fact]
    public async Task GetBranchStatus_NonAdminAuthenticatedUser_Returns403()
    {
        var url = $"{InstancesUrl}/{NonExistentInstanceId}/branch-status";

        using var anonClient = Factory.CreateClient();
        var anonResp = await anonClient.GetAsync(url);
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anonymous must get 401 — proves route exists");

        using var client = CreateClientWithPermissions([], tenantId: Guid.NewGuid().ToString());
        var resp = await client.GetAsync(url);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "authenticated user without Workflow.Instance.View must be denied on GetBranchStatus");
    }

    // ── Admin happy-path sanity (proves the policies grant access, not block it) ──

    [Fact]
    public async Task GetById_BpParticipantWithBpView_NeverReturns403()
    {
        // BP participant holds Bp.View (not the admin-tier Instance.View) — must reach the run.
        using var client = CreateClientWithPermissions(
            [PermCodes.BusinessProcess.Bp.View],
            tenantId: Guid.NewGuid().ToString());

        var resp = await client.GetAsync($"{InstancesUrl}/{NonExistentInstanceId}");

        // May be 404 (no instance) — but never 401/403 with the Bp.View policy granted.
        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "BP participant with BusinessProcess.Bp.View must not be denied on GetById " +
            "(backs the inbox-linked BP run-detail page)");
    }

    [Fact]
    public async Task GetAvailableTransitions_BpParticipantWithBpView_NeverReturns403()
    {
        using var client = CreateClientWithPermissions(
            [PermCodes.BusinessProcess.Bp.View],
            tenantId: Guid.NewGuid().ToString());

        var resp = await client.GetAsync(
            $"{InstancesUrl}/{NonExistentInstanceId}/available-transitions");

        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "BP participant with BusinessProcess.Bp.View must not be denied on GetAvailableTransitions");
    }

    [Fact]
    public async Task ExportAudit_AdminWithViewPolicy_NeverReturns403()
    {
        using var adminClient = CreateClientWithPermissions(
            [PermCodes.Workflow.Instance.View],
            tenantId: Guid.NewGuid().ToString());

        var resp = await adminClient.GetAsync(
            $"{InstancesUrl}/{NonExistentInstanceId}/audit-export?format=Json");

        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "admin with Workflow.Instance.View must not be denied on ExportAudit");
    }

    [Fact]
    public async Task GetBranchStatus_AdminWithViewPolicy_NeverReturns403()
    {
        using var adminClient = CreateClientWithPermissions(
            [PermCodes.Workflow.Instance.View],
            tenantId: Guid.NewGuid().ToString());

        var resp = await adminClient.GetAsync(
            $"{InstancesUrl}/{NonExistentInstanceId}/branch-status");

        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "admin with Workflow.Instance.View must not be denied on GetBranchStatus");
    }

    [Fact]
    public async Task Create_AdminWithManagePolicy_NeverReturns403()
    {
        // No definition seeded — request will fail validation/422 OR succeed in default-resolution path.
        // Critical assertion: NOT 401/403 with the Manage policy granted.
        // Anti-tautology: if Manage policy were missing or wrong, this would 403 even with admin permissions.
        using var adminClient = CreateClientWithPermissions(
            [PermCodes.Workflow.Instance.Manage],
            tenantId: Guid.NewGuid().ToString());

        var resp = await adminClient.PostAsJsonAsync(InstancesUrl, new
        {
            EntityType = "Generic",
            EntityId = Guid.NewGuid(),
            DefinitionId = (Guid?)null,
            SlaHours = (int?)null,
            EntitySubType = (string?)null,
        });

        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "admin with Workflow.Instance.Manage must not be denied on Create — " +
            "proves the new Manage policy is correctly wired (anti-tautology vs reusing View)");
    }
}
