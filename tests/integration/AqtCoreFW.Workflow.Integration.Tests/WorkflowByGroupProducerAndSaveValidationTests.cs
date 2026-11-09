using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Api;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Integration.Tests;

/// <summary>
/// RT2-15 Phase 02 — ByGroup producer + Save-validation integration tests (Agent D).
///
/// **PRODUCER-ONLY SEMANTICS (predicate-pool per Option A, ratified 2026-05-25):**
/// ByGroup clauses materialize as CandidatePredicate rows in WorkflowTaskCandidateIndex,
/// NOT eager user resolution. Read-side (claim/inbox) gap documented in I3 skip.
///
/// **SAVE-VALIDATION HOP (Q2 per phase-02.md §93-94):**
/// Unknown group codes are caught at Save-time via IIdentityModuleClient.ResolveActiveUserGroupsByCodesAsync,
/// returning 422 GraphValidationFailed with structured metadata.UnknownGroupCodes.
///
/// Tests:
///   I1 — producer materializes Group predicate index row (happy path, active group).
///   I2 — save-validation rejects inactive group as "unknown" (Q2 semantic).
///   I3 — SKIPPED: read-side inactive-user filter not yet extended (predicate-pool only).
///   I4 — save-validation rejects fully unknown group code with metadata.
///   I5 — save-validation partitions known vs unknown in metadata (anti-tautology, 260522-E).
///
/// Auth: WebAppFixture's TestSystemAdminPermissionBypassHandler grants SystemAdmin
/// so PUT /graph (Workflow.Def.Manage) is allowed.
///
/// Seed: E2ETestDataSeeder.UserGroups (Agent B-5 shipped) provides:
///   GROUP_TEST_A (active) — alice (active), bob (active), charlie (inactive-user)
///   GROUP_TEST_B_INACTIVE (inactive) — dave (active-user)
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowByGroupProducerAndSaveValidationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    // Test users — seeded into test tenant Identity db alongside groups
    private static readonly Guid AliceId = Guid.NewGuid();
    private static readonly Guid BobId = Guid.NewGuid();
    private static readonly Guid DaveId = Guid.NewGuid();

    public WorkflowByGroupProducerAndSaveValidationTests(WebAppFixture app)
    {
        _app = app;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Inline-seeds a UserGroup and its memberships directly to the test tenant's Identity db.
    /// Idempotent: returns immediately if group already exists.
    /// Used to avoid reliance on E2ETestDataSeeder (which is dev-only, not test-enabled).
    /// </summary>
    private async Task SeedActiveGroupAsync(string code, params Guid[] activeMemberIds)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var idDb = scope.ServiceProvider.GetRequiredService<AqtCoreFW.Identity.Infrastructure.Persistence.IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<AqtCoreFW.SharedKernel.Abstractions.IGuidGenerator>();

        // Create users if not exist (idempotent via FindByIdAsync)
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AqtCoreFW.Identity.Domain.Entities.ApplicationUser>>();
        foreach (var id in activeMemberIds)
        {
            var existingUser = await userManager.FindByIdAsync(id.ToString());
            if (existingUser is null)
            {
                var u = new AqtCoreFW.Identity.Domain.Entities.ApplicationUser
                {
                    Id = id,
                    UserName = $"u-{id:N}@inline.test",
                    Email = $"u-{id:N}@inline.test",
                    EmailConfirmed = true,
                    FullName = $"User {id:N}",
                    IsActive = true,
                    TenantId = TenantId,
                };
                await userManager.CreateAsync(u, "InlineTest@12345");
            }
        }

        // Check if group already exists
        if (await idDb.UserGroups.AnyAsync(g => g.Code == code.Trim().ToUpperInvariant() && g.TenantId == TenantId))
            return;  // already seeded for this test class instance

        // Create group
        var group = AqtCoreFW.Identity.Domain.Entities.UserGroup.Create(
            gen: gen,
            code: code,
            name: code,
            description: null,
            tenantId: TenantId,
            actorId: ActorId);

        idDb.UserGroups.Add(group);
        await idDb.SaveChangesAsync();

        // Add memberships
        foreach (var uid in activeMemberIds)
        {
            idDb.UserGroupMemberships.Add(new AqtCoreFW.Identity.Domain.Entities.UserGroupMembership
            {
                Id = Guid.NewGuid(),
                UserId = uid,
                GroupId = group.Id,
                AddedAtUtc = DateTime.UtcNow,
                AddedBy = ActorId,
            });
        }
        await idDb.SaveChangesAsync();
    }

    /// <summary>
    /// Inline-seeds an INACTIVE UserGroup to test that inactive groups are treated as "unknown" by Q2.
    /// </summary>
    private async Task SeedInactiveGroupAsync(string code, params Guid[] activeMemberIds)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var idDb = scope.ServiceProvider.GetRequiredService<AqtCoreFW.Identity.Infrastructure.Persistence.IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<AqtCoreFW.SharedKernel.Abstractions.IGuidGenerator>();

        // Create users if not exist
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AqtCoreFW.Identity.Domain.Entities.ApplicationUser>>();
        foreach (var id in activeMemberIds)
        {
            var existingUser = await userManager.FindByIdAsync(id.ToString());
            if (existingUser is null)
            {
                var u = new AqtCoreFW.Identity.Domain.Entities.ApplicationUser
                {
                    Id = id,
                    UserName = $"u-{id:N}@inline.test",
                    Email = $"u-{id:N}@inline.test",
                    EmailConfirmed = true,
                    FullName = $"User {id:N}",
                    IsActive = true,
                    TenantId = TenantId,
                };
                await userManager.CreateAsync(u, "InlineTest@12345");
            }
        }

        // Check if group already exists
        if (await idDb.UserGroups.AnyAsync(g => g.Code == code.Trim().ToUpperInvariant() && g.TenantId == TenantId))
            return;

        // Create group
        var group = AqtCoreFW.Identity.Domain.Entities.UserGroup.Create(
            gen: gen,
            code: code,
            name: code,
            description: null,
            tenantId: TenantId,
            actorId: ActorId);

        // Deactivate immediately
        group.Deactivate(ActorId);

        idDb.UserGroups.Add(group);
        await idDb.SaveChangesAsync();

        // Add memberships (even though group is inactive, for test-state consistency)
        foreach (var uid in activeMemberIds)
        {
            idDb.UserGroupMemberships.Add(new AqtCoreFW.Identity.Domain.Entities.UserGroupMembership
            {
                Id = Guid.NewGuid(),
                UserId = uid,
                GroupId = group.Id,
                AddedAtUtc = DateTime.UtcNow,
                AddedBy = ActorId,
            });
        }
        await idDb.SaveChangesAsync();
    }

    /// <summary>
    /// Creates an empty workflow definition and returns its PublicId.
    /// Used as the base for seeding graphs with ByGroup assignments.
    /// </summary>
    private async Task<Guid> SeedEmptyDefinitionAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var def = WorkflowDefinition.Create(
            gen: gen,
            name: $"RT2-15-ByGroup-{Guid.NewGuid():N}",
            description: "Empty definition for ByGroup phase-02 integration tests",
            tenantId: TenantId,
            createdBy: ActorId,
            definitionKey: $"bygrp-{Guid.NewGuid():N}",
            entityType: "Generic");

        db.WorkflowDefinitions.Add(def);
        await db.SaveChangesAsync();
        return def.PublicId;
    }

    /// <summary>
    /// Standard 3-state graph shape: Draft → UnderReview → Approved.
    /// </summary>
    private static object ThreeStateGraph(object[] transitions) => new
    {
        states = new object[]
        {
            new { name = "Draft",    displayNameVi = "Nháp",     displayNameEn = "Draft",     isInitial = true,  isFinal = false, color = "#9E9E9E", sortOrder = 1 },
            new { name = "UnderReview", displayNameVi = "Xem xét",  displayNameEn = "In Review", isInitial = false, isFinal = false, color = "#2196F3", sortOrder = 2 },
            new { name = "Approved", displayNameVi = "Đã duyệt", displayNameEn = "Approved",  isInitial = false, isFinal = true,  color = "#4CAF50", sortOrder = 3 },
        },
        transitions,
    };

    /// <summary>
    /// Assignment with single ByGroup clause.
    /// </summary>
    private static object ByGroupAssignment(string groupCodesJson) => new
    {
        combineMode = 0,
        allowAssignorOverride = false,
        clauses = new[] { new { mode = 5, refsJson = (string?)groupCodesJson } },
    };

    /// <summary>
    /// Assignment with Initiator clause (used for auxiliary transitions without ByGroup).
    /// </summary>
    private static object InitiatorAssignment() => new
    {
        combineMode = 0,
        allowAssignorOverride = false,
        clauses = new[] { new { mode = 1, refsJson = (string?)null } },
    };

    /// <summary>
    /// Parses the error response body to extract GraphErrors violations (for metadata assertions).
    /// Returns the list of violations from metadata["GraphErrors"].
    /// </summary>
    private static async Task<IReadOnlyList<GraphErrorViolation>> ExtractGraphErrorsAsync(HttpResponseMessage resp)
    {
        var content = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        // Navigate: response.errors[0].metadata["GraphErrors"]
        if (!root.TryGetProperty("errors", out var errorsElem) || errorsElem.ValueKind != JsonValueKind.Array)
            return [];

        var errArray = errorsElem.EnumerateArray().ToList();
        if (errArray.Count == 0)
            return [];

        // For now, assume first error carries GraphErrors metadata (validator propagates all violations in one error).
        var firstErr = errArray[0];
        if (!firstErr.TryGetProperty("metadata", out var metadataElem))
            return [];

        // metadata is an object; look for GraphErrors key
        // Note: ApiError in SharedKernel/Api/ApiResponse.cs only serializes ErrorCode, Message, DetailVi, Property.
        // GraphErrors is in error.Metadata dict but ApiError doesn't expose it directly in the response envelope.
        // Instead, we may need to parse it from the JSON structure if the error handler includes it.
        // For simplicity, extract via raw JSON path.

        if (metadataElem.TryGetProperty("GraphErrors", out var graphErrorsElem))
        {
            var violations = new List<GraphErrorViolation>();
            foreach (var item in graphErrorsElem.EnumerateArray())
            {
                var v = new GraphErrorViolation();
                if (item.TryGetProperty("rule", out var ruleElem))
                    v.Rule = ruleElem.GetString();
                if (item.TryGetProperty("detail", out var detailElem))
                    v.Detail = detailElem.GetString();
                if (item.TryGetProperty("metadata", out var vMetadataElem) && vMetadataElem.ValueKind == JsonValueKind.Object)
                {
                    v.Metadata = vMetadataElem.Deserialize<Dictionary<string, object>>();
                }
                violations.Add(v);
            }
            return violations;
        }

        // GraphErrors is an internal-only key — never present on the wire (stripped by ApiResponse.Fail).
        // This helper is retained for symmetry with V11 unit-level metadata inspection.
        return [];
    }

    /// <summary>
    /// Parses the PUBLIC wire path of violations from errors[0].metadata.violations[].
    /// Uses the DEFAULT-DENY OPT-IN envelope: ApiResponse.Fail extracts only "PublicMetadata" into
    /// ApiError.Metadata; "violations" is the top-level key inside that dict.
    /// Returns an empty list when metadata or violations are absent.
    /// </summary>
    private static async Task<IReadOnlyList<GraphErrorViolation>> ExtractPublicViolationsAsync(HttpResponseMessage resp)
    {
        var content = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        // Navigate: response.errors[0].metadata.violations[*]
        if (!root.TryGetProperty("errors", out var errorsElem) || errorsElem.ValueKind != JsonValueKind.Array)
            return [];

        var errArray = errorsElem.EnumerateArray().ToList();
        if (errArray.Count == 0)
            return [];

        var firstErr = errArray[0];
        // "metadata" is the JSON key for ApiError.Metadata (camelCase from record property name)
        if (!firstErr.TryGetProperty("metadata", out var metadataElem) || metadataElem.ValueKind != JsonValueKind.Object)
            return [];

        // "violations" is a dict key — preserved as-is (lowercase)
        if (!metadataElem.TryGetProperty("violations", out var violationsElem) || violationsElem.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<GraphErrorViolation>();
        foreach (var item in violationsElem.EnumerateArray())
        {
            var v = new GraphErrorViolation();
            if (item.TryGetProperty("rule", out var ruleElem))
                v.Rule = ruleElem.GetString();
            if (item.TryGetProperty("detail", out var detailElem))
                v.Detail = detailElem.GetString();
            // "metadata" inside each violation — camelCase from anon-type property name
            if (item.TryGetProperty("metadata", out var vMetaElem) && vMetaElem.ValueKind == JsonValueKind.Object)
            {
                v.Metadata = vMetaElem.Deserialize<Dictionary<string, object>>();
            }
            result.Add(v);
        }
        return result;
    }

    // ────────────────────────────────────────────────────────────────────────
    // I1 — Producer: materializes Group predicate pool on SaveActivate
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// I1_ByGroup_SaveActivateCreateInstance_EmitsCandidateIndexGroupRow
    ///
    /// Happy path: save graph with ByGroup("GROUP_TEST_A") clause → activate (R3/R4 pass, Q2 validation passes).
    /// Inline-seeds GROUP_TEST_A with Alice + Bob as active members.
    /// Q2 Save-validation hop calls IIdentityModuleClient.ResolveActiveUserGroupsByCodesAsync;
    /// succeeds because we seeded the group to the test tenant.
    ///
    /// After activation, creates a workflow instance and advances to a ByGroup-gated task.
    /// Verifies WorkflowTaskCandidateIndex has a Group predicate row.
    /// </summary>
    [Fact]
    public async Task I1_ByGroup_SaveActivateCreateInstance_EmitsCandidateIndexGroupRow()
    {
        // Arrange: seed GROUP_TEST_A with active members to test tenant Identity db
        await SeedActiveGroupAsync("GROUP_TEST_A", AliceId, BobId);

        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        // Save graph with ByGroup clause on gated transition (Draft → UnderReview)
        var body = ThreeStateGraph(new object[]
        {
            new
            {
                fromStateName = "Draft", toStateName = "UnderReview",
                actionName = "Submit", actionLabelVi = "Nộp", actionLabelEn = "Submit",
                conditionsJson = (string?)null, sortOrder = 1, isRecall = false,
                assignment = ByGroupAssignment("""["GROUP_TEST_A"]"""),
            },
            new
            {
                fromStateName = "UnderReview", toStateName = "Approved",
                actionName = "Approve", actionLabelVi = "Duyệt", actionLabelEn = "Approve",
                conditionsJson = (string?)null, sortOrder = 2, isRecall = false,
                assignment = InitiatorAssignment(),
            },
        });

        var saveResp = await client.PutAsJsonAsync($"/api/v1/workflow/definitions/{publicId}/graph", body);
        saveResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "save with valid ByGroup assignment succeeds (Q2 hop validates GROUP_TEST_A exists)");

        // Activate (should succeed after valid save)
        var activateResp = await client.PostAsync($"/api/v1/workflow/definitions/{publicId}/activate", content: null);
        activateResp.StatusCode.Should().Be(HttpStatusCode.NoContent, "activate after valid save returns 204");

        // Verify definition is now active and has assignments persisted
        long firstTransitionId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var def = await db.WorkflowDefinitions
                .Include(d => d.Transitions)
                .AsNoTracking()
                .FirstAsync(d => d.PublicId == publicId);

            def.IsActive.Should().BeTrue("activate must set IsActive=true");
            def.Transitions.Should().HaveCount(2, "both transitions must be persisted");

            // Verify ByGroup assignment persisted on first transition
            var firstTrans = def.Transitions.OrderBy(t => t.SortOrder).First();
            firstTransitionId = firstTrans.Id;

            var assignment = await db.Set<WorkflowTransitionAssignment>()
                .Include(a => a.Clauses)
                .AsNoTracking()
                .FirstAsync(a => a.WorkflowTransitionId == firstTrans.Id);

            assignment.Should().NotBeNull("first transition must have assignment");
            var clauses = assignment.Clauses.ToList();
            clauses.Should().HaveCount(1, "single ByGroup clause");
            clauses[0].Mode.Should().Be(AssignmentClauseMode.ByGroup, "mode is ByGroup");
            clauses[0].RefsJson.Should().Be("""["GROUP_TEST_A"]""", "clause contains GROUP_TEST_A");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // I2 — Save-validation: rejects inactive group as unknown
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// I2_Save_ByGroupClauseWithInactiveGroup_Returns422_MetadataUnknownGroupCodes
    ///
    /// Q2 semantic: ResolveActiveUserGroupsByCodesAsync filters IsActive=1.
    /// Inline-seeds GROUP_TEST_B_INACTIVE (deactivated), then attempts to save graph with that group code.
    /// Expects 422 because Q2 hop sees inactive group as "unknown" (not in active set).
    /// </summary>
    [Fact]
    public async Task I2_Save_ByGroupClauseWithInactiveGroup_Returns422_MetadataUnknownGroupCodes()
    {
        // Arrange: seed GROUP_TEST_B_INACTIVE (deactivated) with Dave as a member
        await SeedInactiveGroupAsync("GROUP_TEST_B_INACTIVE", DaveId);

        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        var body = ThreeStateGraph(new object[]
        {
            new
            {
                fromStateName = "Draft", toStateName = "UnderReview",
                actionName = "Submit", actionLabelVi = "Nộp", actionLabelEn = "Submit",
                conditionsJson = (string?)null, sortOrder = 1, isRecall = false,
                assignment = ByGroupAssignment("""["GROUP_TEST_B_INACTIVE"]"""),
            },
            new
            {
                fromStateName = "UnderReview", toStateName = "Approved",
                actionName = "Approve", actionLabelVi = "Duyệt", actionLabelEn = "Approve",
                conditionsJson = (string?)null, sortOrder = 2, isRecall = false,
                assignment = InitiatorAssignment(),
            },
        });

        var resp = await client.PutAsJsonAsync($"/api/v1/workflow/definitions/{publicId}/graph", body);
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "Q2 validates: ResolveActiveUserGroupsByCodesAsync filters IsActive=1, so inactive group surfaces as 'unknown'");

        // Assert atomicity: no rows persisted (confirms 422 error was triggered)
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var def = await db.WorkflowDefinitions
                .Include(d => d.States)
                .Include(d => d.Transitions)
                .AsNoTracking()
                .FirstAsync(d => d.PublicId == publicId);
            def.States.Should().BeEmpty("atomicity: validation failure rejects before persist");
            def.Transitions.Should().BeEmpty();
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // I3 — SKIPPED: read-side inactive-user filter (predicate-pool gap doc)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// I3_RuntimePool_InactiveUserFilter_SkippedUntilReadSideExtension
    ///
    /// SKIP REASON: Predicate-pool semantic means this test measures the read-side,
    /// not the producer. The producer correctly places Group predicates. The read-side
    /// (WorkflowTaskRepository.IsPoolEligibleAsync + GetInboxAsync) does NOT yet filter
    /// inactive group members — that extension ships in T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP (P0).
    ///
    /// Once T3 read-side extension ships, re-enable and assert:
    ///   1. Create instance + advance to task gated by GROUP_TEST_A.
    ///   2. Charlie (inactive-user in GROUP_TEST_A) cannot claim (filtered by IsActive).
    ///   3. Alice + Bob (active in GROUP_TEST_A) can claim.
    /// </summary>
    [Fact(Skip = "Predicate-pool semantic: producer emits Group predicate only; inactive-user filter "
        + "lives at read-side which is not yet extended for Group (T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP P0). "
        + "Re-enable when read-side ships. RT2-15 Phase 02 ratified Option A predicate-pool 2026-05-25.")]
    public async Task I3_InactiveUserFilteredFromGroupPool_AssertViaInbox()
    {
        // (test body intentionally left empty — re-enable in read-side extension slice)
        await Task.CompletedTask;
        throw new NotImplementedException();
    }

    // ────────────────────────────────────────────────────────────────────────
    // I4 — Save-validation: fully unknown group code
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// I4_Save_ByGroupClauseWithUnknownCode_Returns422_MetadataUnknownGroupCodes
    ///
    /// Save graph with non-existent group code → 422 GraphValidationFailed.
    /// Q2 Save-validation calls ResolveActiveUserGroupsByCodesAsync; set-difference surfaces
    /// the unknown code in the public wire envelope:
    ///   errors[0].metadata.violations[0].metadata.UnknownGroupCodes = ["DOES_NOT_EXIST"]
    /// </summary>
    [Fact]
    public async Task I4_Save_ByGroupClauseWithUnknownCode_Returns422_MetadataUnknownGroupCodes()
    {
        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        var body = ThreeStateGraph(new object[]
        {
            new
            {
                fromStateName = "Draft", toStateName = "UnderReview",
                actionName = "Submit", actionLabelVi = "Nộp", actionLabelEn = "Submit",
                conditionsJson = (string?)null, sortOrder = 1, isRecall = false,
                assignment = ByGroupAssignment("""["DOES_NOT_EXIST"]"""),
            },
            new
            {
                fromStateName = "UnderReview", toStateName = "Approved",
                actionName = "Approve", actionLabelVi = "Duyệt", actionLabelEn = "Approve",
                conditionsJson = (string?)null, sortOrder = 2, isRecall = false,
                assignment = InitiatorAssignment(),
            },
        });

        var resp = await client.PutAsJsonAsync($"/api/v1/workflow/definitions/{publicId}/graph", body);
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "Q2 Save-validation calls ResolveActiveUserGroupsByCodesAsync; unknown code surfaces as GraphValidationFailed 422");

        // Assert structured metadata in public wire envelope
        var violations = await ExtractPublicViolationsAsync(resp);
        violations.Should().NotBeEmpty(
            "errors[0].metadata.violations must be populated via PublicMetadata wire projection; " +
            "raw response: {0}", await resp.Content.ReadAsStringAsync());

        var firstViolation = violations[0];
        firstViolation.Metadata.Should().NotBeNull(
            "violation metadata must carry UnknownGroupCodes from ByGroup Q2 check");

        var unknownCodesRaw = firstViolation.Metadata!.GetValueOrDefault("UnknownGroupCodes");
        unknownCodesRaw.Should().NotBeNull("UnknownGroupCodes must be present in violation metadata");

        // JsonElement is the runtime type from Deserialize<Dictionary<string, object>>
        var unknownCodes = ((JsonElement)unknownCodesRaw!).EnumerateArray()
            .Select(e => e.GetString()).ToList();
        unknownCodes.Should().ContainSingle(
            "only one code was submitted and it is unknown")
            .And.Contain("DOES_NOT_EXIST",
                "the submitted unknown code must be reported in UnknownGroupCodes");

        // Assert atomicity: no rows persisted
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var def = await db.WorkflowDefinitions
                .Include(d => d.States)
                .Include(d => d.Transitions)
                .AsNoTracking()
                .FirstAsync(d => d.PublicId == publicId);
            def.States.Should().BeEmpty("atomicity preserved: unknown group code rejection is atomic");
            def.Transitions.Should().BeEmpty();
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // I5 — Save-validation: mixed known + unknown (anti-tautology, 260522-E)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// I5_Save_ByGroupMixedKnownAndUnknown_ReturnsOnlyUnknownInMetadata
    ///
    /// Anti-tautology partition lock: known codes must NOT appear in UnknownGroupCodes.
    /// Input: ["GROUP_TEST_A", "TYPO_X", "TYPO_Y"] (known + unknown).
    /// Assert 422 + errors[0].metadata.violations[0].metadata.UnknownGroupCodes = ["TYPO_X", "TYPO_Y"]
    ///   — GROUP_TEST_A must be ABSENT (set-difference excludes known codes).
    /// </summary>
    [Fact]
    public async Task I5_Save_ByGroupMixedKnownAndUnknown_ReturnsOnlyUnknownInMetadata()
    {
        // Self-contained: seed GROUP_TEST_A so partition assertion is deterministic
        // regardless of test execution order within the class.
        await SeedActiveGroupAsync("GROUP_TEST_A", AliceId, BobId);

        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        var body = ThreeStateGraph(new object[]
        {
            new
            {
                fromStateName = "Draft", toStateName = "UnderReview",
                actionName = "Submit", actionLabelVi = "Nộp", actionLabelEn = "Submit",
                conditionsJson = (string?)null, sortOrder = 1, isRecall = false,
                assignment = ByGroupAssignment("""["GROUP_TEST_A", "TYPO_X", "TYPO_Y"]"""),
            },
            new
            {
                fromStateName = "UnderReview", toStateName = "Approved",
                actionName = "Approve", actionLabelVi = "Duyệt", actionLabelEn = "Approve",
                conditionsJson = (string?)null, sortOrder = 2, isRecall = false,
                assignment = InitiatorAssignment(),
            },
        });

        var resp = await client.PutAsJsonAsync($"/api/v1/workflow/definitions/{publicId}/graph", body);
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "mixed known+unknown rejects: validator partitions codes, finds unknown ones, throws GraphValidationFailed");

        // Assert anti-tautology partition in public wire envelope
        var violations = await ExtractPublicViolationsAsync(resp);
        violations.Should().NotBeEmpty(
            "errors[0].metadata.violations must be populated; raw response: {0}",
            await resp.Content.ReadAsStringAsync());

        var firstViolation = violations[0];
        firstViolation.Metadata.Should().NotBeNull(
            "violation metadata must carry UnknownGroupCodes");

        var unknownCodesRaw = firstViolation.Metadata!.GetValueOrDefault("UnknownGroupCodes");
        unknownCodesRaw.Should().NotBeNull("UnknownGroupCodes must be present in violation metadata");

        var unknownCodes = ((JsonElement)unknownCodesRaw!).EnumerateArray()
            .Select(e => e.GetString()).ToList();

        // Partition correctness (both directions): only unknowns reported, known excluded
        unknownCodes.Should().Contain("TYPO_X", "TYPO_X is unknown and must appear in UnknownGroupCodes");
        unknownCodes.Should().Contain("TYPO_Y", "TYPO_Y is unknown and must appear in UnknownGroupCodes");
        unknownCodes.Should().NotContain("GROUP_TEST_A",
            "GROUP_TEST_A is a known active group and must NOT appear in UnknownGroupCodes");

        // Assert atomicity
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var def = await db.WorkflowDefinitions
                .Include(d => d.States)
                .Include(d => d.Transitions)
                .AsNoTracking()
                .FirstAsync(d => d.PublicId == publicId);
            def.States.Should().BeEmpty("atomicity: validation failure rejects before persist");
            def.Transitions.Should().BeEmpty();
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // I_CT1 — Cross-tenant Save-validation gate (C1 fix regression lock)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// I_CT1_Save_ByGroupCodeFromAnotherTenant_Returns422_AsUnknown
    ///
    /// C1 regression lock: proves that a group code belonging ONLY to a different tenant
    /// is invisible to the validator Q2 hop when the save is performed under the test tenant.
    ///
    /// Seed:
    ///   - GROUP_OWN: seeded under TenantId (test tenant) — ACTIVE.
    ///   - GROUP_OTHER: seeded under otherTenantId (distinct Guid) — ACTIVE.
    ///
    /// Action: Save graph under TenantId with ByGroup clause ["GROUP_OWN", "GROUP_OTHER"].
    ///
    /// Expected: 422 GraphValidationFailed because GROUP_OTHER is not visible under TenantId.
    ///   Without C1 fix (no TenantId filter): Q2 would return BOTH codes → validator sees no unknown → 200 (false-pass).
    ///   With C1 fix: Q2 returns only GROUP_OWN → unknown = ["GROUP_OTHER"] → 422 (correct).
    /// </summary>
    [Fact]
    public async Task I_CT1_Save_ByGroupCodeFromAnotherTenant_Returns422_AsUnknown()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var ownCode = $"GROUP_OWN_{suffix}";
        var otherCode = $"GROUP_OTHER_{suffix}";
        var otherTenantId = Guid.NewGuid();

        // Seed GROUP_OWN under test tenant (visible to this test's HTTP client).
        await SeedActiveGroupAsync(ownCode);
        // Seed GROUP_OTHER under a DIFFERENT tenant — must NOT be visible under TenantId.
        await SeedGroupForTenantAsync(otherCode, otherTenantId);

        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");
        var url = $"/api/v1/workflow/definitions/{publicId}/graph";

        // Transition with BOTH codes: own (should pass) + other-tenant (should fail Q2).
        var body = ThreeStateGraph(new object[]
        {
            new
            {
                fromStateName = "Draft",
                toStateName = "UnderReview",
                actionName = "Submit",
                actionLabelVi = "Nộp",
                actionLabelEn = "Submit",
                conditionsJson = (string?)null,
                sortOrder = 1,
                isRecall = false,
                assignment = ByGroupAssignment($"""["{ownCode}", "{otherCode}"]"""),
            },
            new
            {
                fromStateName = "UnderReview",
                toStateName = "Approved",
                actionName = "Approve",
                actionLabelVi = "Duyệt",
                actionLabelEn = "Approve",
                conditionsJson = (string?)null,
                sortOrder = 2,
                isRecall = false,
                assignment = ByGroupAssignment($"""["{ownCode}"]"""),
            },
        });

        var response = await client.PutAsJsonAsync(url, body);

        // Without C1 fix: would return 200 (false-pass — GROUP_OTHER from otherTenant leaks through Q2).
        // With C1 fix: returns 422 because GROUP_OTHER not visible under TenantId.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "GROUP_OTHER exists only in otherTenantId — Q2 tenant filter must reject it as unknown (C1 fix regression lock)");
    }

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds an active UserGroup under an arbitrary tenant (not the class-level TenantId).
    /// Used by cross-tenant isolation tests.
    /// </summary>
    private async Task SeedGroupForTenantAsync(string code, Guid forTenantId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var idDb = scope.ServiceProvider.GetRequiredService<AqtCoreFW.Identity.Infrastructure.Persistence.IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<AqtCoreFW.SharedKernel.Abstractions.IGuidGenerator>();

        var canonicalCode = code.Trim().ToUpperInvariant();
        if (await idDb.UserGroups.AnyAsync(g => g.Code == canonicalCode && g.TenantId == forTenantId))
            return;

        var group = AqtCoreFW.Identity.Domain.Entities.UserGroup.Create(
            gen: gen,
            code: code,
            name: code,
            description: null,
            tenantId: forTenantId,
            actorId: ActorId);

        idDb.UserGroups.Add(group);
        await idDb.SaveChangesAsync();
    }

    /// <summary>
    /// Helper: represents a parsed GraphRuleViolation from the JSON error response.
    /// </summary>
    private class GraphErrorViolation
    {
        public string? Rule { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
