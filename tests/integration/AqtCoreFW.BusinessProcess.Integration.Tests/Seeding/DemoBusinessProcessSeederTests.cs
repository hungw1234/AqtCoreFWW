using AqtCoreFW.Api;
using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.BusinessProcess.Infrastructure.Persistence;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Forms.Infrastructure.Seeding;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Seeding;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.BusinessProcess.Integration.Tests.Seeding;

/// <summary>
/// Integration coverage for <see cref="DemoBusinessProcessSeeder"/> — the first cross-module demo
/// seeder. Each test seeds the full prerequisite stack (Workflow definition + Form template) through
/// the existing demo seeders' static paths, then drives the BP seeder over three filter-free contexts
/// (mirrors startup), asserting: the Draft BP root + intake pin, the per-state/per-transition form
/// bindings resolve to the right Workflow Ids, idempotency, and fail-loud dependency resolution.
/// Cross-tenant reads/writes succeed because the TestContainers connection is db_owner (RLS bypass).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class DemoBusinessProcessSeederTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public DemoBusinessProcessSeederTests(WebAppFixture app) => _app = app;

    // ── T1: one Draft BP per tenant, wired to the demo workflow + intake form ──

    [Fact]
    public async Task T1_SeedsOneDraftBp_PerTenant_WiredToWorkflowAndIntakeForm()
    {
        await SeedFullStackAsync(TenantSeedProfiles.BoTaiChinh);
        await SeedFullStackAsync(TenantSeedProfiles.BoNoiVu);

        foreach (var profile in TenantSeedProfiles.All)
        {
            var (workflowDefId, _, _, templateId, version) = await ResolveDependencyIdsAsync(profile);

            await using var bpCtx = NewBpCtx();
            var bp = await bpCtx.BusinessProcessDefinitions
                .IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(d => d.TenantId == profile.TenantId && d.Code == DemoBusinessProcessSeeder.DemoBpCode);

            bp.IsActive.Should().BeFalse($"{profile.Code} demo BP must be seeded in Draft (operator activates)");
            bp.WorkflowDefinitionId.Should().Be(workflowDefId, "BP must link the demo workflow definition");
            bp.IntakeFormTemplateId.Should().Be(templateId, "BP must pin the demo intake form template");
            bp.IntakeFormTemplateVersion.Should().Be(version, "intake form version must match the published snapshot");
            bp.Name.Should().Contain("Quy trình nghiệp vụ (demo)", "Vietnamese diacritics must round-trip (NVARCHAR)");
        }
    }

    // ── T2: the intake form is bound to the Draft state + Submit transition with valid FKs ──

    [Fact]
    public async Task T2_FormMaps_BindIntakeForm_ToDraftStateAndSubmitTransition()
    {
        var profile = MakeThrowawayProfile(Guid.NewGuid());
        await SeedFullStackAsync(profile);

        var (_, draftStateId, submitTransitionId, templateId, version) = await ResolveDependencyIdsAsync(profile);

        await using var bpCtx = NewBpCtx();
        var bp = await bpCtx.BusinessProcessDefinitions
            .IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(d => d.TenantId == profile.TenantId && d.Code == DemoBusinessProcessSeeder.DemoBpCode);

        var stateForms = await bpCtx.BusinessProcessStateForms
            .IgnoreQueryFilters().AsNoTracking()
            .Where(sf => sf.BusinessProcessDefinitionId == bp.Id)
            .ToListAsync();
        stateForms.Should().ContainSingle("the demo binds the intake form to exactly one state");
        var stateForm = stateForms[0];
        stateForm.WorkflowStateId.Should().Be(draftStateId, "the state binding targets the Draft (initial) state");
        stateForm.FormTemplateId.Should().Be(templateId);
        stateForm.FormTemplateVersion.Should().Be(version);
        stateForm.IsRequired.Should().BeTrue();
        stateForm.TenantId.Should().Be(profile.TenantId);

        var transitionForms = await bpCtx.BusinessProcessTransitionForms
            .IgnoreQueryFilters().AsNoTracking()
            .Where(tf => tf.BusinessProcessDefinitionId == bp.Id)
            .ToListAsync();
        transitionForms.Should().ContainSingle("the demo binds the intake form to exactly one transition");
        var transitionForm = transitionForms[0];
        transitionForm.WorkflowTransitionId.Should().Be(submitTransitionId, "the transition binding targets the Submit action");
        transitionForm.FormTemplateId.Should().Be(templateId);
        transitionForm.FormTemplateVersion.Should().Be(version);
        transitionForm.IsRequired.Should().BeTrue();
        transitionForm.TenantId.Should().Be(profile.TenantId);
    }

    // ── T3: re-running the seeder is a no-op (idempotent per TenantId + Code; no duplicate maps) ──

    [Fact]
    public async Task T3_SecondRun_IsNoOp_NoDuplicateBpOrFormMaps()
    {
        var profile = MakeThrowawayProfile(Guid.NewGuid());

        await SeedFullStackAsync(profile);
        var before = await CountAsync(profile);

        // Faithful second run of the BP step only (workflow + form already present) — mirrors a second startup.
        await SeedBpAsync(profile);
        var after = await CountAsync(profile);

        before.Bps.Should().Be(1);
        after.Bps.Should().Be(1, "idempotency: a second seed run must not create a duplicate BP");
        after.StateForms.Should().Be(before.StateForms, "no duplicate state-form binding on re-run");
        after.TransitionForms.Should().Be(before.TransitionForms, "no duplicate transition-form binding on re-run");
    }

    // ── T4: missing prerequisites fail loud (no half-wired BP silently seeded) ──

    [Fact]
    public async Task T4_MissingWorkflowDependency_ThrowsRatherThanSeedingEmptyBp()
    {
        // Throwaway tenant with NO workflow/form seeded → the BP seeder must throw, not silent-skip.
        var profile = MakeThrowawayProfile(Guid.NewGuid());

        var act = async () => await SeedBpAsync(profile);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*demo workflow definition*", "a missing dependency is a startup-ordering bug, surfaced loudly");

        await using var bpCtx = NewBpCtx();
        (await bpCtx.BusinessProcessDefinitions.IgnoreQueryFilters()
                .AnyAsync(d => d.TenantId == profile.TenantId && d.Code == DemoBusinessProcessSeeder.DemoBpCode))
            .Should().BeFalse("no BP row may be persisted when the workflow dependency is absent");
    }

    // ── T5: workflow present but form template missing → fail loud on the form branch ──

    [Fact]
    public async Task T5_MissingFormDependency_ThrowsRatherThanSeedingEmptyBp()
    {
        // Seed ONLY the workflow def (no form template) → the BP seeder must throw on the form resolve.
        var profile = MakeThrowawayProfile(Guid.NewGuid());
        await using (var wfCtx = NewWfCtx())
            await DemoWorkflowSeeder.SeedTenantDefinitionAsync(wfCtx, profile, Gen(), NullLogger.Instance, CancellationToken.None);

        var act = async () => await SeedBpAsync(profile);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*demo form template*", "the form dependency is resolved fail-loud, like the workflow one");

        await using var bpCtx = NewBpCtx();
        (await bpCtx.BusinessProcessDefinitions.IgnoreQueryFilters()
                .AnyAsync(d => d.TenantId == profile.TenantId && d.Code == DemoBusinessProcessSeeder.DemoBpCode))
            .Should().BeFalse("no BP row may be persisted when the form dependency is absent");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Seeds Workflow def → Form template → BP, each step through a fresh filter-free context (mirrors startup order).</summary>
    private async Task SeedFullStackAsync(TenantSeedProfile profile)
    {
        var gen = Gen();

        await using (var wfCtx = NewWfCtx())
            await DemoWorkflowSeeder.SeedTenantDefinitionAsync(wfCtx, profile, gen, NullLogger.Instance, CancellationToken.None);

        await using (var formsCtx = NewFormsCtx())
            await DemoFormTemplateSeeder.SeedTenantTemplateAsync(formsCtx, profile, gen, NullLogger.Instance, CancellationToken.None);

        await SeedBpAsync(profile);
    }

    /// <summary>Drives only the BP seeder's static path over three fresh filter-free contexts.</summary>
    private async Task SeedBpAsync(TenantSeedProfile profile)
    {
        await using var bpCtx = NewBpCtx();
        await using var wfCtx = NewWfCtx();
        await using var formsCtx = NewFormsCtx();
        await DemoBusinessProcessSeeder.SeedTenantBusinessProcessAsync(
            bpCtx, wfCtx, formsCtx, profile, Gen(), NullLogger.Instance, CancellationToken.None);
    }

    /// <summary>Resolves the workflow def Id, Draft state Id, Submit transition Id, intake template Id + version.</summary>
    private async Task<(long WorkflowDefId, long DraftStateId, long SubmitTransitionId, long TemplateId, int Version)>
        ResolveDependencyIdsAsync(TenantSeedProfile profile)
    {
        await using var wfCtx = NewWfCtx();
        var def = await wfCtx.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(w => w.TenantId == profile.TenantId && w.DefinitionKey == DemoWorkflowSeeder.DemoDefinitionKey);
        var draftStateId = await wfCtx.WorkflowStates.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.DefinitionId == def.Id && s.Name == DemoBusinessProcessSeeder.IntakeStateName)
            .Select(s => s.Id).SingleAsync();
        var submitTransitionId = await wfCtx.WorkflowTransitions.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.DefinitionId == def.Id && t.ActionName == DemoBusinessProcessSeeder.IntakeTransitionAction)
            .Select(t => t.Id).SingleAsync();

        await using var formsCtx = NewFormsCtx();
        var template = await formsCtx.FormTemplates.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(t => t.TenantId == profile.TenantId && t.Code == DemoFormTemplateSeeder.DemoTemplateCode);
        var version = await formsCtx.FormTemplateVersions.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.FormTemplateId == template.Id)
            .Select(v => v.VersionNumber).OrderByDescending(v => v).FirstAsync();

        return (def.Id, draftStateId, submitTransitionId, template.Id, version);
    }

    private async Task<(int Bps, int StateForms, int TransitionForms)> CountAsync(TenantSeedProfile profile)
    {
        await using var bpCtx = NewBpCtx();
        var bpIds = await bpCtx.BusinessProcessDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.TenantId == profile.TenantId && d.Code == DemoBusinessProcessSeeder.DemoBpCode)
            .Select(d => d.Id).ToListAsync();
        var stateForms = await bpCtx.BusinessProcessStateForms.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(sf => bpIds.Contains(sf.BusinessProcessDefinitionId));
        var transitionForms = await bpCtx.BusinessProcessTransitionForms.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(tf => bpIds.Contains(tf.BusinessProcessDefinitionId));
        return (bpIds.Count, stateForms, transitionForms);
    }

    private IGuidGenerator Gen()
    {
        using var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
    }

    private BusinessProcessDbContext NewBpCtx() =>
        new(_app.Services.GetRequiredService<DbContextOptions<BusinessProcessDbContext>>(), SystemSeederTenantContext.Instance);

    private WorkflowDbContext NewWfCtx() =>
        new(_app.Services.GetRequiredService<DbContextOptions<WorkflowDbContext>>(), SystemSeederTenantContext.Instance);

    private FormsDbContext NewFormsCtx() =>
        new(_app.Services.GetRequiredService<DbContextOptions<FormsDbContext>>(), SystemSeederTenantContext.Instance);

    /// <summary>
    /// Builds a one-off profile for isolation tests. The seeders read only TenantId / Name / Code / RolePrefix;
    /// the remaining required fields carry inert placeholders.
    /// </summary>
    private static TenantSeedProfile MakeThrowawayProfile(Guid tenantId) => new()
    {
        TenantId = tenantId,
        Code = $"throwaway-{tenantId:N}",
        Name = "Throwaway tenant",
        RolePrefix = "TW_",
        UserCodePrefix = "TW-",
        LeadEmailPrefix = "tw.lead.",
        DevEmailPrefix = "tw.dev.",
        FillEmailPrefix = "tw",
        EmailDomain = "@throwaway.test",
        UserCount = 0,
        MaxLevel = 1,
        SeedVersion = "test",
    };
}
