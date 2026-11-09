using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.BusinessProcess.Infrastructure.Persistence;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Forms.Infrastructure.Seeding;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Hosting;
using AqtCoreFW.SharedKernel.Application.Seeding;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Api;

/// <summary>
/// Tenant-agnostic demo BusinessProcess seeder: seeds one Draft BP definition per
/// <see cref="TenantSeedProfiles"/> entry (BTC + BNV), pairing the demo Workflow definition
/// (<see cref="DemoWorkflowSeeder"/>) with the demo intake Form template
/// (<see cref="DemoFormTemplateSeeder"/>) via intake + per-state/per-transition form bindings.
///
/// Left in <b>Draft</b> (IsActive=false): an operator activates it in the UI, which re-runs the
/// R1-R4 graph validator. The seeder never activates — it only stages the configuration. No BP runs
/// (WorkflowInstances) are seeded.
///
/// <para>Host-resident, unlike the per-module demo seeders. BP is the first seeder that must read
/// across module boundaries (Workflow def + states + transitions, Form template + version). Module
/// assemblies may not reference each other (ModuleIsolationTests), and the cross-module client
/// contracts expose only PublicId-keyed resolution — not the natural-key (DefinitionKey / Code)
/// lookups a seeder holds. The composition root already references all module Infrastructure and is
/// the sanctioned place for cross-module seeding (mirrors E2ETestDataSeeder).</para>
///
/// <para>Config-gated (<c>BusinessProcess:DemoBusinessProcessSeed</c>, default OFF) and idempotent
/// per (TenantId, Code) — the unique (TenantId, Code) index makes the existence probe load-bearing.
/// Dependency resolution is fail-loud: a missing Workflow def / Form template throws rather than
/// seeding a half-wired BP — startup order (Forms + Workflow demo seeders before this) is a hard
/// precondition, not a soft skip.</para>
///
/// <para>Cross-tenant reads/writes from a single startup scope rely on the dev/seed connection being
/// db_owner (the RLS predicate bypasses for db_owner; <see cref="SystemSeederTenantContext"/> leaves
/// SESSION_CONTEXT unset and reports IsSystemAdmin so the EF global filter is bypassed too). Mirrors
/// the Organization / Workflow / Forms demo seeders.</para>
/// </summary>
public sealed class DemoBusinessProcessSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<DemoBusinessProcessSeederOptions> options,
    IGuidGenerator gen,
    ILogger<DemoBusinessProcessSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    private readonly DemoBusinessProcessSeederOptions _opts = options.Value;

    /// <summary>Stable BP code (shared across tenants; uniqueness is per TenantId).</summary>
    public const string DemoBpCode = "demo-business-process";

    /// <summary>Workflow state the intake form is bound to (the initial state).</summary>
    public const string IntakeStateName = "Draft";

    /// <summary>Workflow transition the intake form is bound to (first human action).</summary>
    public const string IntakeTransitionAction = "Submit";

    // Seeder-authored rows are attributed to the system actor (all-zero Guid, mirrors E2E seeder).
    private static readonly Guid SystemActorId = Guid.Empty;

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_opts.Enabled)
        {
            logger.LogDebug("[DemoBusinessProcessSeeder] Disabled — skipping BP demo seed");
            return;
        }

        foreach (var profile in TenantSeedProfiles.All)
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            // Construct each context with the system seed context so the tenant global filter is
            // bypassed — reads + inserts span both tenants from one startup scope (mirror the other
            // demo seeders). Workflow + Forms are read-only here; BusinessProcess is written.
            await using var wfCtx = new WorkflowDbContext(
                sp.GetRequiredService<DbContextOptions<WorkflowDbContext>>(), SystemSeederTenantContext.Instance);
            await using var formsCtx = new FormsDbContext(
                sp.GetRequiredService<DbContextOptions<FormsDbContext>>(), SystemSeederTenantContext.Instance);
            await using var bpCtx = new BusinessProcessDbContext(
                sp.GetRequiredService<DbContextOptions<BusinessProcessDbContext>>(), SystemSeederTenantContext.Instance);

            await SeedTenantBusinessProcessAsync(bpCtx, wfCtx, formsCtx, profile, gen, logger, ct);
        }

        logger.LogInformation("[DemoBusinessProcessSeeder] All tenants complete");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Seeds the demo BP for one tenant. Idempotent per (TenantId, Code): a second run is a no-op.
    /// Two-phase save (EF IDENTITY-before-children): (1) the BP root is inserted so its Id is assigned,
    /// then (2) the state/transition form bindings — keyed by the now-assigned bp.Id — cascade in one
    /// SaveChanges. Static + ctx-injected so tests can drive it directly with the three contexts.
    ///
    /// Dependency resolution is fail-loud: the demo Workflow definition (+ its Draft state and Submit
    /// transition) and the demo Form template (+ its published version) MUST already be seeded. A null
    /// resolve throws — it signals a startup-ordering regression, and a silent skip would ship an
    /// empty/half-wired BP that a weak test could miss.
    /// </summary>
    public static async Task SeedTenantBusinessProcessAsync(
        BusinessProcessDbContext bpCtx,
        WorkflowDbContext wfCtx,
        FormsDbContext formsCtx,
        TenantSeedProfile profile,
        IGuidGenerator gen,
        ILogger logger,
        CancellationToken ct)
    {
        var exists = await bpCtx.BusinessProcessDefinitions
            .IgnoreQueryFilters()
            .AnyAsync(d => d.TenantId == profile.TenantId && d.Code == DemoBpCode, ct);
        if (exists)
        {
            logger.LogInformation(
                "[DemoBusinessProcessSeeder] {Code}: demo BP already present — skipping", profile.Code);
            return;
        }

        // ── Resolve cross-module dependencies (fail-loud) ───────────────────────────────────────────
        var workflowDef = await wfCtx.WorkflowDefinitions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                w => w.TenantId == profile.TenantId && w.DefinitionKey == DemoWorkflowSeeder.DemoDefinitionKey, ct)
            ?? throw new InvalidOperationException(
                $"[DemoBusinessProcessSeeder] {profile.Code}: demo workflow definition " +
                $"'{DemoWorkflowSeeder.DemoDefinitionKey}' not found — DemoWorkflowSeeder must run first.");

        var draftStateId = await wfCtx.WorkflowStates
            .IgnoreQueryFilters()
            .Where(s => s.DefinitionId == workflowDef.Id && s.Name == IntakeStateName)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(ct);
        if (draftStateId <= 0)
            throw new InvalidOperationException(
                $"[DemoBusinessProcessSeeder] {profile.Code}: workflow state '{IntakeStateName}' not found on demo definition.");

        var submitTransitionId = await wfCtx.WorkflowTransitions
            .IgnoreQueryFilters()
            .Where(t => t.DefinitionId == workflowDef.Id && t.ActionName == IntakeTransitionAction)
            .Select(t => t.Id)
            .FirstOrDefaultAsync(ct);
        if (submitTransitionId <= 0)
            throw new InvalidOperationException(
                $"[DemoBusinessProcessSeeder] {profile.Code}: workflow transition '{IntakeTransitionAction}' not found on demo definition.");

        var formTemplate = await formsCtx.FormTemplates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                t => t.TenantId == profile.TenantId && t.Code == DemoFormTemplateSeeder.DemoTemplateCode, ct)
            ?? throw new InvalidOperationException(
                $"[DemoBusinessProcessSeeder] {profile.Code}: demo form template " +
                $"'{DemoFormTemplateSeeder.DemoTemplateCode}' not found — DemoFormTemplateSeeder must run first.");

        // Pin the latest published snapshot version. BP references (FormTemplateId, VersionNumber),
        // so resolve from the snapshot rows rather than the mutable template.Version field.
        var formVersion = await formsCtx.FormTemplateVersions
            .IgnoreQueryFilters()
            .Where(v => v.FormTemplateId == formTemplate.Id)
            .Select(v => v.VersionNumber)
            .OrderByDescending(v => v)
            .FirstOrDefaultAsync(ct);
        if (formVersion < 1)
            throw new InvalidOperationException(
                $"[DemoBusinessProcessSeeder] {profile.Code}: demo form template has no published version — it must be published first.");

        // The root and its form bindings span two SaveChanges (children carry bp.Id, a DB-assigned
        // IDENTITY only known after the root commits — the child factories reject parentId <= 0). Wrap
        // both in one transaction so a phase-2 fault cannot leave a committed root with zero bindings:
        // the idempotency probe keys on the root, so a half-wired survivor would be skipped (never
        // repaired) on a plain restart. reset-all nukes first and would self-heal, but a mid-seed crash
        // on a live DB would not.
        await using var tx = await bpCtx.Database.BeginTransactionAsync(ct);

        // ── Phase 1: BP root (Draft) with pinned intake form ────────────────────────────────────────
        var bp = BusinessProcessDefinition.Create(
            gen,
            tenantId: profile.TenantId,
            code: DemoBpCode,
            name: $"Quy trình nghiệp vụ (demo) — {profile.Name}",
            workflowDefinitionId: workflowDef.Id,
            createdBy: SystemActorId,
            description: $"Quy trình demo gắn biểu mẫu tiếp nhận cho {profile.Name}.",
            intakeFormTemplateId: formTemplate.Id,
            intakeFormTemplateVersion: formVersion);

        await bpCtx.BusinessProcessDefinitions.AddAsync(bp, ct);
        await bpCtx.SaveChangesAsync(ct); // bp.Id assigned by IDENTITY

        // ── Phase 2: per-state / per-transition form bindings (cascade in one SaveChanges) ──────────
        // Bind the single intake form to the initial state (edit-while-drafting) and the Submit
        // transition (capture-on-submit) — a minimal, semantically-honest demo wiring.
        bp.AddStateForm(BusinessProcessStateForm.Create(
            gen, profile.TenantId, bp.Id, draftStateId, formTemplate.Id, formVersion, SystemActorId,
            isRequired: true, displayOrder: 0));
        bp.AddTransitionForm(BusinessProcessTransitionForm.Create(
            gen, profile.TenantId, bp.Id, submitTransitionId, formTemplate.Id, formVersion, SystemActorId,
            isRequired: true, displayOrder: 0));

        await bpCtx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Intentionally NOT activated — left in Draft for operator activation (re-runs R1-R4 in UI).
        logger.LogInformation(
            "[DemoBusinessProcessSeeder] {Code}: seeded demo BP (Draft) bound to workflow '{Key}' + intake form '{Form}' v{Version}",
            profile.Code, DemoWorkflowSeeder.DemoDefinitionKey, DemoFormTemplateSeeder.DemoTemplateCode, formVersion);
    }
}
