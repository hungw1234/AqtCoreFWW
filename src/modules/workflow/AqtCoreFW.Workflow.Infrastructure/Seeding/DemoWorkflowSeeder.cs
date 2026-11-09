using System.Text.Json;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Hosting;
using AqtCoreFW.SharedKernel.Application.Seeding;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Workflow.Infrastructure.Seeding;

/// <summary>
/// Tenant-agnostic demo workflow seeder: seeds one generic 6-state case-processing definition per
/// <see cref="TenantSeedProfiles"/> entry (BTC + BNV), each transition carrying a ByRole assignment
/// keyed by the per-tenant role CODE (profile.RolePrefix + suffix, e.g. "BTC_DIVISION_HEAD").
///
/// Definitions are seeded in <b>Draft</b> (IsActive=false): an operator activates them in the UI,
/// which re-runs the R1-R4 graph validator before they accept instances. The seeder never activates —
/// it only stages a structurally-valid graph whose assignments cover every human transition (so the
/// activate gate passes). No instances/tasks are seeded.
///
/// Config-gated (<c>Workflow:DemoWorkflowSeed</c>, default OFF) and idempotent per (TenantId,
/// DefinitionKey). Unlike OrgTreeSeeder there is no tenant-existence guard: a definition's TenantId is
/// an unenforced Guid stamp (no FK), so a tenant with no row yields a harmless never-routed definition
/// rather than an orphaned-FK row — not worth a cross-schema probe.
/// </summary>
public sealed class DemoWorkflowSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<DemoWorkflowSeederOptions> options,
    IGuidGenerator gen,
    ILogger<DemoWorkflowSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    private readonly DemoWorkflowSeederOptions _opts = options.Value;

    /// <summary>Stable definition key (shared across tenants; uniqueness is per TenantId).</summary>
    public const string DemoDefinitionKey = "demo-generic-process";

    // Seeder-authored rows are attributed to the system actor (all-zero Guid, mirrors E2E seeder).
    private static readonly Guid SystemActorId = Guid.Empty;

    /// <summary>
    /// Auto-hierarchy ByRole matrix (transition action → role-code suffixes). The full clause code is
    /// profile.RolePrefix + suffix. Both "Close" transitions (Approved→Closed, Rejected→Closed) share
    /// the action name and resolve to the same decider tier, so keying by action name is sufficient.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> RoleMatrix = new Dictionary<string, string[]>
    {
        ["Submit"]  = ["SPECIALIST", "OFFICER", "STAFF"], // author tier initiates
        ["Review"]  = ["DIVISION_HEAD"],                  // first-line reviewer
        ["Approve"] = ["DEPT_DIRECTOR"],                  // decider
        ["Reject"]  = ["DEPT_DIRECTOR"],                  // same decider can reject
        ["Close"]   = ["DEPT_DIRECTOR"],                  // decider closes the dossier
    };

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_opts.Enabled)
        {
            logger.LogDebug("[DemoWorkflowSeeder] Disabled — skipping workflow demo seed");
            return;
        }

        foreach (var profile in TenantSeedProfiles.All)
        {
            using var scope = scopeFactory.CreateScope();
            var dbOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<WorkflowDbContext>>();

            // Construct with the system seed context so the tenant global filter is bypassed — the
            // idempotency probe + inserts span both tenants from one startup scope (mirror OrgTreeSeeder).
            await using var ctx = new WorkflowDbContext(dbOptions, SystemSeederTenantContext.Instance);

            await SeedTenantDefinitionAsync(ctx, profile, gen, logger, ct);
        }

        logger.LogInformation("[DemoWorkflowSeeder] All tenants complete");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Seeds the demo definition for one tenant. Idempotent per (TenantId, DefinitionKey): a second run
    /// is a no-op. Three-phase save because the assignment is a separate entity keyed by the DB-assigned
    /// transition Id: (1) def + states → state Ids, (2) transitions → transition Ids, (3) per-transition
    /// ByRole assignment + clause. Static + ctx-injected so tests can drive it directly.
    /// </summary>
    public static async Task SeedTenantDefinitionAsync(
        WorkflowDbContext ctx,
        TenantSeedProfile profile,
        IGuidGenerator gen,
        ILogger logger,
        CancellationToken ct)
    {
        var exists = await ctx.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AnyAsync(w => w.TenantId == profile.TenantId && w.DefinitionKey == DemoDefinitionKey, ct);
        if (exists)
        {
            logger.LogInformation(
                "[DemoWorkflowSeeder] {Code}: demo definition already present — skipping", profile.Code);
            return;
        }

        // The def+states, transitions, and ByRole assignments span three SaveChanges (each phase keys on
        // IDENTITY ids the prior phase assigned). Wrap all three in one transaction so a mid-seed fault
        // cannot leave a committed definition with missing transitions/assignments: the idempotency probe
        // keys on the root (TenantId, DefinitionKey), so a half-wired survivor would be skipped (never
        // repaired) on a plain restart. reset-all nukes first and self-heals; a live-DB crash would not.
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        var def = WorkflowDefinition.Create(
            gen,
            name: "Quy trình xử lý hồ sơ (demo)",
            description: $"Quy trình demo 6 trạng thái cho {profile.Name} — định tuyến theo vai trò.",
            tenantId: profile.TenantId,
            createdBy: SystemActorId,
            definitionKey: DemoDefinitionKey,
            entityType: "Generic");

        // Phase 1: def + states. Exactly one IsInitial; Closed is the single final state.
        var draft       = MakeState(def.Id, "Draft",       "Nháp",          "Draft",        isInitial: true,  isFinal: false, "#808080", 1);
        var submitted   = MakeState(def.Id, "Submitted",   "Đã nộp",        "Submitted",    isInitial: false, isFinal: false, "#3B82F6", 2);
        var underReview = MakeState(def.Id, "UnderReview", "Đang xem xét",  "Under Review", isInitial: false, isFinal: false, "#F59E0B", 3);
        var approved    = MakeState(def.Id, "Approved",    "Đã phê duyệt",  "Approved",     isInitial: false, isFinal: false, "#10B981", 4);
        var rejected    = MakeState(def.Id, "Rejected",    "Đã từ chối",    "Rejected",     isInitial: false, isFinal: false, "#EF4444", 5);
        var closed      = MakeState(def.Id, "Closed",      "Đã đóng",       "Closed",       isInitial: false, isFinal: true,  "#6B7280", 6);

        def.AddState(draft);
        def.AddState(submitted);
        def.AddState(underReview);
        def.AddState(approved);
        def.AddState(rejected);
        def.AddState(closed);

        await ctx.WorkflowDefinitions.AddAsync(def, ct);
        await ctx.SaveChangesAsync(ct); // state Ids assigned by IDENTITY

        // Phase 2: transitions (now that state Ids exist).
        def.AddTransition(MakeTransition(def.Id, draft.Id,       submitted.Id,   "Submit",  "Nộp hồ sơ",   "Submit",  1));
        def.AddTransition(MakeTransition(def.Id, submitted.Id,   underReview.Id, "Review",  "Xem xét",     "Review",  2));
        def.AddTransition(MakeTransition(def.Id, underReview.Id, approved.Id,    "Approve", "Phê duyệt",   "Approve", 3));
        def.AddTransition(MakeTransition(def.Id, underReview.Id, rejected.Id,    "Reject",  "Từ chối",     "Reject",  4));
        def.AddTransition(MakeTransition(def.Id, approved.Id,    closed.Id,      "Close",   "Đóng hồ sơ", "Close",   5));
        def.AddTransition(MakeTransition(def.Id, rejected.Id,    closed.Id,      "Close",   "Đóng hồ sơ", "Close",   6));

        await ctx.SaveChangesAsync(ct); // transition Ids assigned by IDENTITY

        // Phase 3: per-transition ByRole assignment (separate entity keyed by transition Id).
        foreach (var transition in def.Transitions)
        {
            if (!RoleMatrix.TryGetValue(transition.ActionName, out var suffixes))
                continue; // defensive — every seeded action is in the matrix

            var codes = suffixes.Select(s => profile.RolePrefix + s).ToArray();
            var assignment = WorkflowTransitionAssignment.Create(transition.Id, AssignmentCombineMode.Union);
            assignment.AddClause(AssignmentClauseMode.ByRole, JsonSerializer.Serialize(codes));
            await ctx.WorkflowTransitionAssignments.AddAsync(assignment, ct);
        }

        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Intentionally NOT activated — left in Draft for operator activation (re-runs R1-R4 in UI).
        logger.LogInformation(
            "[DemoWorkflowSeeder] {Code}: seeded demo definition (Draft) with {States} states, {Transitions} transitions",
            profile.Code, def.States.Count, def.Transitions.Count);
    }

    private static WorkflowState MakeState(
        long defId, string name, string nameVi, string nameEn,
        bool isInitial, bool isFinal, string color, int order) =>
        WorkflowState.Create(defId, name, nameVi, nameEn, isInitial, isFinal, color, order);

    private static WorkflowTransition MakeTransition(
        long defId, long fromId, long toId,
        string action, string labelVi, string labelEn, int order) =>
        WorkflowTransition.Create(defId, fromId, toId, action, labelVi, labelEn, conditionsJson: null, order);
}
