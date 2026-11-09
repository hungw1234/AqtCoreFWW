using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Version-lifecycle invariants around clone + delete, distinct from clone deep-copy fidelity
/// (WorkflowDefinitionCloneTests). Guards two defects on the seeded demo-parallel-process:
///   1. cloning after a prior version was soft-deleted threw 500 — the next-version calc reused the
///      deleted version's number, colliding with the unfiltered (Tenant, Key, Version) unique index.
///   2. deleting the latest version left the key with no latest row — the surviving version did not
///      reclaim IsLatest, so the seed's v1 was stuck not-latest after its broken v2 clone was deleted.
/// Both drive the real clone + delete endpoints end-to-end.
/// </summary>
[Collection("Integration")]
public sealed class WorkflowDefinitionVersionLifecycleTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string DefinitionsUrl = "/api/v1/workflow/definitions";

    [Fact]
    public async Task Clone_AfterPriorVersionSoftDeleted_AssignsMonotonicVersion_NoCollision()
    {
        var (admin, v1PublicId) = await CreateDraftAsync();

        // v1 → clone v2 (latest), then delete v2 (soft-delete). The number 2 is now occupied by a
        // soft-deleted row in the unfiltered unique index.
        var v2PublicId = await CloneAsync(admin, v1PublicId);
        (await admin.DeleteAsync($"{DefinitionsUrl}/{v2PublicId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Cloning v1 again must NOT reuse 2 (would collide → 500); it must jump to 3.
        var v3PublicId = await CloneAsync(admin, v1PublicId);

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var v3 = await ctx.WorkflowDefinitions.AsNoTracking().FirstAsync(d => d.PublicId == v3PublicId);
        v3.Version.Should().Be(3, "next version clears every used number including the soft-deleted v2");
        v3.IsLatest.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_LatestVersion_PromotesHighestSurvivingToLatest()
    {
        var (admin, v1PublicId) = await CreateDraftAsync();
        var v2PublicId = await CloneAsync(admin, v1PublicId);

        // Precondition: clone made v2 latest, v1 not.
        await using (var pre = Factory.Services.CreateAsyncScope())
        {
            var ctx = pre.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            (await ctx.WorkflowDefinitions.AsNoTracking().FirstAsync(d => d.PublicId == v1PublicId))
                .IsLatest.Should().BeFalse();
        }

        // Delete the latest (v2) → v1 must reclaim IsLatest.
        (await admin.DeleteAsync($"{DefinitionsUrl}/{v2PublicId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = Factory.Services.CreateAsyncScope();
        var post = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        (await post.WorkflowDefinitions.AsNoTracking().FirstAsync(d => d.PublicId == v1PublicId))
            .IsLatest.Should().BeTrue("deleting the latest version hands the flag to the surviving newest");
    }

    [Fact]
    public async Task Delete_NonLatestVersion_LeavesLatestUntouched()
    {
        var (admin, v1PublicId) = await CreateDraftAsync();
        var v2PublicId = await CloneAsync(admin, v1PublicId);

        // Delete the NON-latest v1 → v2 stays latest, no spurious promotion.
        (await admin.DeleteAsync($"{DefinitionsUrl}/{v1PublicId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        (await ctx.WorkflowDefinitions.AsNoTracking().FirstAsync(d => d.PublicId == v2PublicId))
            .IsLatest.Should().BeTrue("deleting a non-latest version must not disturb the current latest");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Creates a minimal single-state Draft definition (v1) via the real create endpoint.
    /// Version lifecycle needs no graph/activation, so this stays deliberately small.</summary>
    private async Task<(HttpClient Admin, Guid PublicId)> CreateDraftAsync()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var admin = Factory.CreateAuthenticatedClient(
            userId: userId.ToString(), roles: ["SystemAdmin"], tenantId: tenantId.ToString());

        var createResp = await admin.PostAsJsonAsync(DefinitionsUrl, new
        {
            Name = $"Version Lifecycle {Guid.NewGuid():N}",
            Description = "Version-lifecycle clone/delete coverage",
            TenantId = tenantId.ToString(),
            CreatedBy = userId,
            EntityType = "Generic",
            States = new object[]
            {
                new { Name = "Draft", DisplayNameVi = "Nháp", DisplayNameEn = "Draft", IsInitial = true, IsFinal = false, Color = "#9E9E9E", SortOrder = 1 },
            },
            Transitions = Array.Empty<object>(),
        });
        var raw = await createResp.Content.ReadAsStringAsync();
        createResp.StatusCode.Should().Be(HttpStatusCode.OK, $"create must succeed. Body: {raw}");
        var publicId = JsonSerializer.Deserialize<JsonElement>(raw).GetProperty("data").GetProperty("id").GetGuid();
        return (admin, publicId);
    }

    private static async Task<Guid> CloneAsync(HttpClient admin, Guid sourcePublicId)
    {
        var resp = await admin.PostAsync($"{DefinitionsUrl}/{sourcePublicId}/clone", null);
        var raw = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"clone must succeed (no version collision). Body: {raw}");
        return JsonSerializer.Deserialize<JsonElement>(raw).GetProperty("data").GetProperty("id").GetGuid();
    }
}
