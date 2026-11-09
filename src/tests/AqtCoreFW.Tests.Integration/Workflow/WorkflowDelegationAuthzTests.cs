using System.Net;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Phase 4-A-A invariant closure 2026-05-22 — sibling controllers Authorize audit.
///
/// Security gap: WorkflowDelegationController.List (GET /delegation-rules) had class-level
/// [Authorize] only — any authenticated user could enumerate tenant delegation rules
/// (IDOR mirror of 4-A WorkflowTasksController.Reassign).
///
/// NOTE: DelegateTask + ReturnDelegation use runtime IAuthorizationService check
/// (line 43-44, 65-66) — INTENTIONALLY scoped per delegation contract (task assignee
/// OR admin via Delegation.Manage). NOT changed in this slice. Create/Update/Delete on
/// delegation-rules already have explicit [Authorize(Policy = Delegation.Manage)].
///
/// Fix scope: List action only.
///
/// Facts (mirror 4-A pattern):
///   F1 IDOR closed:    non-admin authenticated → 403; anon → 401 (baseline).
///   F2 Admin allow:    SystemAdmin with Delegation.Manage → 200.
/// </summary>
[Collection("Integration")]
public class WorkflowDelegationAuthzTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string DelegationRulesUrl = "/api/v1/workflow/delegation-rules";

    [Fact]
    public async Task ListRules_NonAdminAuthenticatedUser_Returns403WithEnvelopeBody()
    {
        using var anonClient = Factory.CreateClient();
        var anonResp = await anonClient.GetAsync(DelegationRulesUrl);
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anonymous must get 401 — proves route exists and global auth fires, " +
            "so the authenticated 403 below is from the Workflow.Delegation.Manage policy gate");

        using var client = CreateClientWithPermissions([], tenantId: Guid.NewGuid().ToString());
        var resp = await client.GetAsync(DelegationRulesUrl);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "any authenticated user without Workflow.Delegation.Manage must be denied on " +
            "ListRules (4-A-A invariant: class-level [Authorize] is NOT a security gate)");
    }

    [Fact]
    public async Task ListRules_AdminWithDelegationManagePolicy_Returns200()
    {
        using var adminClient = CreateClientWithPermissions(
            [PermCodes.Workflow.Delegation.Manage],
            tenantId: Guid.NewGuid().ToString());

        var resp = await adminClient.GetAsync(DelegationRulesUrl);

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "admin with Workflow.Delegation.Manage must successfully list delegation rules");
    }
}
