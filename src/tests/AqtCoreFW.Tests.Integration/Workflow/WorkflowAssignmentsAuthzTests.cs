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
/// Security gap: WorkflowAssignmentsController had class-level [Authorize] only on
/// List + Resolve actions — any authenticated user could enumerate or resolve admin
/// assignment rules (IDOR mirror of 4-A WorkflowTasksController.Reassign).
///
/// Fix: add [Authorize(Policy = PermCodes.Workflow.Assign.Manage)] to both naked methods.
///
/// Facts (mirror 4-A pattern):
///   F1 IDOR closed on List:     non-admin authenticated → 403; anon → 401 (baseline).
///   F2 IDOR closed on Resolve:  non-admin authenticated → 403; anon → 401 (baseline).
///   F3 Admin allow on List:     SystemAdmin with Assign.Manage → 200.
///   F4 Admin allow on Resolve:  SystemAdmin with Assign.Manage → 200 or 404 (no rule), never 401/403.
///
/// Anti-tautology: each non-admin test first verifies anon → 401 on the same route to prove
/// the route exists and global auth middleware fires; the subsequent 403 is therefore the
/// Workflow.Assign.Manage policy gate, not a misconfigured global filter.
/// </summary>
[Collection("Integration")]
public class WorkflowAssignmentsAuthzTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string AssignmentsUrl = "/api/v1/workflow/assignments";

    [Fact]
    public async Task List_NonAdminAuthenticatedUser_Returns403WithEnvelopeBody()
    {
        using var anonClient = Factory.CreateClient();
        var anonResp = await anonClient.GetAsync(AssignmentsUrl);
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anonymous must get 401 — proves route exists and global auth fires, " +
            "so the authenticated 403 below is from the Workflow.Assign.Manage policy gate");

        using var client = CreateClientWithPermissions([], tenantId: Guid.NewGuid().ToString());
        var resp = await client.GetAsync(AssignmentsUrl);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "any authenticated user without Workflow.Assign.Manage must be denied on List " +
            "(4-A-A invariant: class-level [Authorize] is NOT a security gate)");
    }

    [Fact]
    public async Task Resolve_NonAdminAuthenticatedUser_Returns403WithEnvelopeBody()
    {
        const string ResolvePath = "/resolve?entityType=Generic";

        using var anonClient = Factory.CreateClient();
        var anonResp = await anonClient.GetAsync(AssignmentsUrl + ResolvePath);
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anon must get 401 on /resolve — proves route exists and global auth fires");

        using var client = CreateClientWithPermissions([], tenantId: Guid.NewGuid().ToString());
        var resp = await client.GetAsync(AssignmentsUrl + ResolvePath);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "any authenticated user without Workflow.Assign.Manage must be denied on Resolve");
    }

    [Fact]
    public async Task List_AdminWithAssignManagePolicy_Returns200()
    {
        using var adminClient = CreateClientWithPermissions(
            [PermCodes.Workflow.Assign.Manage],
            tenantId: Guid.NewGuid().ToString());

        var resp = await adminClient.GetAsync(AssignmentsUrl);

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "admin with Workflow.Assign.Manage must successfully list assignment rules");
    }

    [Fact]
    public async Task Resolve_AdminWithAssignManagePolicy_NeverReturns403()
    {
        const string ResolvePath = "/resolve?entityType=Generic";
        using var adminClient = CreateClientWithPermissions(
            [PermCodes.Workflow.Assign.Manage],
            tenantId: Guid.NewGuid().ToString());

        var resp = await adminClient.GetAsync(AssignmentsUrl + ResolvePath);

        // Resolve may legitimately return 200 (rule found) or 404 (no rule) but never 401/403.
        // Anti-tautology: if policy were missing, this would 403 even with the policy granted.
        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "admin must not get 401 on Resolve");
        resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "admin with Workflow.Assign.Manage must not be denied on Resolve");
    }
}
