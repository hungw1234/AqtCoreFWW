using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Forms;

/// <summary>
/// Integration tests for WorkflowLinkPendingController — /api/v1/forms/workflow-link-pending
/// (P6-DEFER-1 phase 05 admin manual Retry-now / Shelve + list).
///
/// Gated entirely by PermCodes.Forms.WorkflowLink.Manage (LOCKED Q3 — dedicated least-privilege
/// perm, not a reuse of Submit.Approve). No production endpoint creates these rows — tests seed
/// via WorkflowLinkPendingSeedHelpers (direct FormsDbContext access), mirroring the
/// WorkflowSeedHelpers precedent for Workflow-module tests.
///
/// RLS-tier note: this harness runs against TestContainers SQL Server as the app's normal (non-RLS
/// bypass) connection role — the tenant-isolation assertions below prove the `TenantId = @TenantId`
/// predicate + tracked-getter behavior, not a true RLS BLOCK under a locked-down non-db_owner login
/// (see feedback-sa-dbowner-false-greens-rls-assertions in project memory). Treat GREEN here as
/// "the predicate works", not as "RLS BLOCK is independently proven".
/// </summary>
[Collection("Integration")]
public class WorkflowLinkPendingApiTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string BaseUrl = "/api/v1/forms/workflow-link-pending";

    // -----------------------------------------------------------------
    // RT6a — Retry-now permission gate + status branch
    // -----------------------------------------------------------------

    [Fact]
    public async Task RetryNow_WithoutManagePermission_Returns403()
    {
        var tenantId = Guid.NewGuid();
        var submissionId = await WorkflowLinkPendingSeedHelpers.SeedPendingRowAsync(Factory, tenantId);

        var restrictedClient = Factory.CreateClientWithPermissions(
            permissions: [], tenantId: tenantId.ToString());

        var response = await restrictedClient.PostAsync($"{BaseUrl}/{submissionId}/retry", content: null);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RetryNow_WithManagePermission_OnPendingRow_Returns202_AndRearmsNextRetryAt()
    {
        var tenantId = Guid.NewGuid();
        var submissionId = await WorkflowLinkPendingSeedHelpers.SeedPendingRowAsync(Factory, tenantId);

        var adminClient = Factory.CreateClientWithPermissions(
            permissions: [PermCodes.Forms.WorkflowLink.Manage], tenantId: tenantId.ToString());

        var response = await adminClient.PostAsync($"{BaseUrl}/{submissionId}/retry", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var row = await GetRow(adminClient, tenantId, statusFilter: null, submissionId);
        row.GetProperty("status").GetString().Should().Be("Pending");
        row.GetProperty("nextRetryAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        var nextRetryAt = row.GetProperty("nextRetryAt").GetDateTimeOffset();
        nextRetryAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task RetryNow_OnShelvedRow_UnshelvesToPending_ManualPathOnly()
    {
        var tenantId = Guid.NewGuid();
        var submissionId = await WorkflowLinkPendingSeedHelpers.SeedShelvedRowAsync(Factory, tenantId);

        var adminClient = Factory.CreateClientWithPermissions(
            permissions: [PermCodes.Forms.WorkflowLink.Manage], tenantId: tenantId.ToString());

        // Sanity: seeded row really is Shelved before the manual override.
        var before = await GetRow(adminClient, tenantId, statusFilter: null, submissionId);
        before.GetProperty("status").GetString().Should().Be("Shelved");

        var response = await adminClient.PostAsync($"{BaseUrl}/{submissionId}/retry", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var after = await GetRow(adminClient, tenantId, statusFilter: null, submissionId);
        after.GetProperty("status").GetString().Should().Be("Pending", "manual Retry-now un-shelves (LOCKED Q4)");
        after.GetProperty("resolvedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task RetryNow_ForNonExistentSubmission_Returns404()
    {
        var tenantId = Guid.NewGuid();
        var adminClient = Factory.CreateClientWithPermissions(
            permissions: [PermCodes.Forms.WorkflowLink.Manage], tenantId: tenantId.ToString());

        var response = await adminClient.PostAsync($"{BaseUrl}/{Guid.NewGuid()}/retry", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------
    // RT6b — Shelve
    // -----------------------------------------------------------------

    [Fact]
    public async Task Shelve_WithManagePermission_MarksRowShelved_WithResolvedByActor()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var submissionId = await WorkflowLinkPendingSeedHelpers.SeedPendingRowAsync(Factory, tenantId);

        var adminClient = Factory.CreateClientWithPermissions(
            permissions: [PermCodes.Forms.WorkflowLink.Manage], userId: actorId.ToString(), tenantId: tenantId.ToString());

        var response = await adminClient.PostAsync($"{BaseUrl}/{submissionId}/shelve", content: null);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var row = await GetRow(adminClient, tenantId, statusFilter: null, submissionId);
        row.GetProperty("status").GetString().Should().Be("Shelved");
        row.GetProperty("resolvedBy").GetGuid().Should().Be(actorId);
    }

    [Fact]
    public async Task Shelve_WithoutManagePermission_Returns403()
    {
        var tenantId = Guid.NewGuid();
        var submissionId = await WorkflowLinkPendingSeedHelpers.SeedPendingRowAsync(Factory, tenantId);

        var restrictedClient = Factory.CreateClientWithPermissions(
            permissions: [], tenantId: tenantId.ToString());

        var response = await restrictedClient.PostAsync($"{BaseUrl}/{submissionId}/shelve", content: null);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // -----------------------------------------------------------------
    // LIST — tenant isolation + Status filter + pagination
    // -----------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsOnlyCallersTenantRows()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var idA = await WorkflowLinkPendingSeedHelpers.SeedPendingRowAsync(Factory, tenantA);
        await WorkflowLinkPendingSeedHelpers.SeedPendingRowAsync(Factory, tenantB);

        var clientA = Factory.CreateClientWithPermissions(
            permissions: [PermCodes.Forms.WorkflowLink.Manage], tenantId: tenantA.ToString());

        var response = await clientA.GetAsync(BaseUrl);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").GetProperty("items").EnumerateArray().ToList();

        items.Should().OnlyContain(i => i.GetProperty("id").GetGuid() != Guid.Empty);
        items.Select(i => i.GetProperty("id").GetGuid()).Should().Contain(idA);
        items.Should().OnlyContain(i =>
            i.GetProperty("id").GetGuid() == idA); // tenantB's row must not leak into tenantA's list
    }

    [Fact]
    public async Task List_StatusFilter_NarrowsResults()
    {
        var tenantId = Guid.NewGuid();
        var pendingId = await WorkflowLinkPendingSeedHelpers.SeedPendingRowAsync(Factory, tenantId);
        var shelvedId = await WorkflowLinkPendingSeedHelpers.SeedShelvedRowAsync(Factory, tenantId);

        var adminClient = Factory.CreateClientWithPermissions(
            permissions: [PermCodes.Forms.WorkflowLink.Manage], tenantId: tenantId.ToString());

        var shelvedResponse = await adminClient.GetAsync($"{BaseUrl}?status=Shelved");
        var shelvedBody = await shelvedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var shelvedIds = shelvedBody.GetProperty("data").GetProperty("items")
            .EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        shelvedIds.Should().Contain(shelvedId);
        shelvedIds.Should().NotContain(pendingId);
    }

    [Fact]
    public async Task List_PaginationMeta_ReflectsRequestedPageAndSize()
    {
        var tenantId = Guid.NewGuid();
        await WorkflowLinkPendingSeedHelpers.SeedPendingRowAsync(Factory, tenantId);
        await WorkflowLinkPendingSeedHelpers.SeedPendingRowAsync(Factory, tenantId);

        var adminClient = Factory.CreateClientWithPermissions(
            permissions: [PermCodes.Forms.WorkflowLink.Manage], tenantId: tenantId.ToString());

        var response = await adminClient.GetAsync($"{BaseUrl}?page=1&pageSize=1");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");

        data.GetProperty("items").GetArrayLength().Should().Be(1);
        data.GetProperty("meta").GetProperty("page").GetInt32().Should().Be(1);
        data.GetProperty("meta").GetProperty("pageSize").GetInt32().Should().Be(1);
        data.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task List_WithoutManagePermission_Returns403()
    {
        var tenantId = Guid.NewGuid();
        var restrictedClient = Factory.CreateClientWithPermissions(
            permissions: [], tenantId: tenantId.ToString());

        var response = await restrictedClient.GetAsync(BaseUrl);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // -----------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------

    private static async Task<JsonElement> GetRow(
        HttpClient client, Guid tenantId, string? statusFilter, Guid submissionId)
    {
        _ = tenantId;
        var query = statusFilter is null ? "" : $"?status={statusFilter}";
        var response = await client.GetAsync($"{BaseUrl}{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").GetProperty("items").EnumerateArray();
        return items.First(i => i.GetProperty("id").GetGuid() == submissionId);
    }
}
