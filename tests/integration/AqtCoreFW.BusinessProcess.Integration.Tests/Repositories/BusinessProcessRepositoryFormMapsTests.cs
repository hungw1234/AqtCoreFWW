using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.BusinessProcess.Domain.Repositories;
using AqtCoreFW.BusinessProcess.Infrastructure.Persistence;
using AqtCoreFW.BusinessProcess.Infrastructure.Repositories;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Integration.Tests.Repositories;

/// <summary>
/// Integration coverage for T3-BP-FORMMAP-REPRO production fix (Path C ClientCascade).
///
/// SUT: <see cref="BusinessProcessRepository.UpdateAsync"/> at lines 87-118 plus the EF
/// child-relationship cascade config at <see cref="Persistence.Configurations.BusinessProcessDefinitionConfiguration"/>.
///
/// Bug class: Replace-form-maps round-trip with at least one identical (BPId, TransitionId, FormTemplateId)
/// triple hits UX_BusinessProcessTransitionForms_Bp_Transition_Form 2601 unique-index violation under EF
/// Core 10 + SQL Server. InMemory provider does NOT honour unique indexes (per
/// <c>BusinessProcessRepositoryWriteRejectTests.cs:16-21</c>) — Testcontainers SQL Server mandatory.
///
/// F1 (anti-regression):    Re-add SAME triples after Clear*Forms — SaveChanges must succeed, row counts stable.
/// F2 (anti-tautology 260522-E): Re-add SUBSET (1 of 2 originally) — dropped row IS deleted, not no-op'd.
///
/// Tenant context: scope's HttpContextTenantContext returns null TenantId (no HttpContext pushed).
/// Read query filter (<c>ModelBuilderExtensions.ApplyGlobalFilters</c>) treats null tenant as
/// "no restriction" (<c>!tenantHasValue</c> short-circuit), so all rows visible. Repository's write-time
/// guard receives an NSubstitute-stubbed ITenantContext returning the test's tenantId so
/// <c>bp.TenantId != _tenantContext.TenantId</c> does not throw GOV_BP_002.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class BusinessProcessRepositoryFormMapsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _fixture;

    public BusinessProcessRepositoryFormMapsTests(WebAppFixture fixture)
    {
        _fixture = fixture;
    }

    private static IGuidGenerator NewGen()
    {
        var g = Substitute.For<IGuidGenerator>();
        g.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return g;
    }

    private static ITenantContext NewTenantContextStub(Guid tenantId)
    {
        var tc = Substitute.For<ITenantContext>();
        tc.TenantId.Returns(tenantId);
        tc.IsSystemAdmin.Returns(false);
        return tc;
    }

    /// <summary>
    /// Seed cross-module fixtures (Workflow def+state+transition, Forms templates) and one BP
    /// with N initial transition-form bindings (state-form binding always = 1). Returns IDs needed
    /// for re-load + assertions plus the manually-constructed repository (stub tenant context).
    /// </summary>
    private static async Task<SeedHandles> SeedAsync(IServiceProvider sp, int transitionFormCount)
    {
        var gen = NewGen();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        // Workflow seed — definition + 2 states + 1 transition
        var workflowDb = sp.GetRequiredService<WorkflowDbContext>();
        var workflow = WorkflowDefinition.Create(gen, $"WF-{Guid.NewGuid():N}", "test wf", tenantId, actorId);
        workflowDb.WorkflowDefinitions.Add(workflow);
        await workflowDb.SaveChangesAsync();

        var stateA = WorkflowState.Create(workflow.Id, "DRAFT", "Draft", "Draft", true, false, "#fff", 1);
        var stateB = WorkflowState.Create(workflow.Id, "DONE", "Done", "Done", false, true, "#fff", 2);
        workflowDb.Set<WorkflowState>().AddRange(stateA, stateB);
        await workflowDb.SaveChangesAsync();

        var transition = WorkflowTransition.Create(workflow.Id, stateA.Id, stateB.Id, "submit", "Submit", "Submit", null, 1);
        workflowDb.Set<WorkflowTransition>().Add(transition);
        await workflowDb.SaveChangesAsync();

        // Forms seed — 1-2 templates depending on fact's needs
        var formsDb = sp.GetRequiredService<FormsDbContext>();
        var ft1 = FormTemplate.Create(gen, tenantId, $"FT1-{Guid.NewGuid():N}", "FT1 Vi", $"ft-1-{Guid.NewGuid():N}", StorageMode.Json, actorId);
        formsDb.Set<FormTemplate>().Add(ft1);
        FormTemplate? ft2 = null;
        if (transitionFormCount >= 2)
        {
            ft2 = FormTemplate.Create(gen, tenantId, $"FT2-{Guid.NewGuid():N}", "FT2 Vi", $"ft-2-{Guid.NewGuid():N}", StorageMode.Json, actorId);
            formsDb.Set<FormTemplate>().Add(ft2);
        }
        await formsDb.SaveChangesAsync();

        // BP seed via manually-constructed repo (stub tenant ctx bypasses HttpContextTenantContext null TenantId)
        var bpDb = sp.GetRequiredService<BusinessProcessDbContext>();
        var repo = new BusinessProcessRepository(bpDb, NewTenantContextStub(tenantId), NullLogger<BusinessProcessRepository>.Instance);

        var bp = BusinessProcessDefinition.Create(gen, tenantId, $"bp-{Guid.NewGuid():N}", "Test BP", workflow.Id, actorId);
        await repo.AddAsync(bp); // assigns bp.Id via IDENTITY

        // Initial form-map bindings: 1 state-form + N transition-forms
        bp.AddStateForm(BusinessProcessStateForm.Create(gen, tenantId, bp.Id, stateA.Id, ft1.Id, 1, actorId));
        bp.AddTransitionForm(BusinessProcessTransitionForm.Create(gen, tenantId, bp.Id, transition.Id, ft1.Id, 1, actorId));
        if (transitionFormCount >= 2)
        {
            bp.AddTransitionForm(BusinessProcessTransitionForm.Create(gen, tenantId, bp.Id, transition.Id, ft2!.Id, 1, actorId));
        }
        await repo.UpdateAsync(bp); // baseline commit

        return new SeedHandles(
            BpPublicId: bp.PublicId,
            BpId: bp.Id,
            StateId: stateA.Id,
            TransitionId: transition.Id,
            FormTemplate1Id: ft1.Id,
            FormTemplate2Id: ft2?.Id ?? 0,
            TenantId: tenantId,
            ActorId: actorId,
            Repo: repo,
            Db: bpDb,
            Gen: gen);
    }

    private sealed record SeedHandles(
        Guid BpPublicId,
        long BpId,
        long StateId,
        long TransitionId,
        long FormTemplate1Id,
        long FormTemplate2Id,
        Guid TenantId,
        Guid ActorId,
        IBusinessProcessRepository Repo,
        BusinessProcessDbContext Db,
        IGuidGenerator Gen);

    // ── F1 (anti-regression): same triples re-added → SaveChanges succeeds, row counts stable ──

    /// <summary>
    /// After <c>ClearStateForms</c> + <c>ClearTransitionForms</c> followed by re-adding the IDENTICAL
    /// (transitionId, formTemplateId) triples, <c>UpdateAsync</c> must succeed without a 2601
    /// unique-index violation. Asserts row counts unchanged post-replace.
    ///
    /// Pre-Path-C behaviour: RED with <c>DbUpdateException</c> wrapping
    /// <c>SqlException 2601 UX_BusinessProcessTransitionForms_Bp_Transition_Form</c>.
    /// Post-Path-C: GREEN.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_AfterClearAndReAddSameTriples_SaveChangesSucceeds_RowCountStable()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var seed = await SeedAsync(sp, transitionFormCount: 1);

        // Pre-condition: 1 state-form + 1 transition-form baseline
        (await seed.Db.Set<BusinessProcessStateForm>().AsNoTracking()
            .CountAsync(s => s.BusinessProcessDefinitionId == seed.BpId))
            .Should().Be(1, "baseline 1 state-form binding");
        (await seed.Db.Set<BusinessProcessTransitionForm>().AsNoTracking()
            .CountAsync(t => t.BusinessProcessDefinitionId == seed.BpId))
            .Should().Be(1, "baseline 1 transition-form binding");

        // Act: re-load → Clear → re-add SAME (transitionId, formTemplateId) triple → UpdateAsync
        var reloaded = await seed.Repo.GetByPublicIdAsync(seed.BpPublicId);
        reloaded.Should().NotBeNull("seed BP must be visible to scope tenant filter (null TenantId → all visible)");

        reloaded!.ClearStateForms();
        reloaded.ClearTransitionForms();
        reloaded.AddStateForm(BusinessProcessStateForm.Create(seed.Gen, seed.TenantId, seed.BpId, seed.StateId, seed.FormTemplate1Id, 1, seed.ActorId));
        reloaded.AddTransitionForm(BusinessProcessTransitionForm.Create(seed.Gen, seed.TenantId, seed.BpId, seed.TransitionId, seed.FormTemplate1Id, 1, seed.ActorId));

        Func<Task> act = () => seed.Repo.UpdateAsync(reloaded);

        // Assert: no exception + row counts stable
        await act.Should().NotThrowAsync<DbUpdateException>(
            "Path C ClientCascade must emit DELETE-before-INSERT batch — no UX_*_Bp_Transition_Form 2601 collision");

        (await seed.Db.Set<BusinessProcessStateForm>().AsNoTracking()
            .CountAsync(s => s.BusinessProcessDefinitionId == seed.BpId))
            .Should().Be(1, "state-form count stable post-replace");
        (await seed.Db.Set<BusinessProcessTransitionForm>().AsNoTracking()
            .CountAsync(t => t.BusinessProcessDefinitionId == seed.BpId))
            .Should().Be(1, "transition-form count stable post-replace");
    }

    // ── F2 (anti-tautology): subset re-add → dropped row IS deleted, not no-op'd ──

    /// <summary>
    /// Anti-tautology proof per memory anchor 260522-E: F1 alone could pass with a buggy
    /// "no-op on collision" implementation; F2 verifies the walk ACTUALLY mutates state by
    /// re-adding a SUBSET of original triples. The dropped (transitionId, ft2) must be DELETED
    /// in DB; the kept (transitionId, ft1) must persist. Two transitions sharing the same
    /// WorkflowTransitionId but pointing at distinct FormTemplateIds — only the unique index
    /// (BPId, TransitionId, FormTemplateId) prevents duplicates, not a uniqueness on (BPId, TransitionId).
    /// </summary>
    [Fact]
    public async Task UpdateAsync_AfterClearAndAddSubset_DroppedRowDeleted_KeptRowsPersist()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var seed = await SeedAsync(sp, transitionFormCount: 2);

        // Pre-condition: 1 state-form + 2 transition-forms baseline
        (await seed.Db.Set<BusinessProcessTransitionForm>().AsNoTracking()
            .CountAsync(t => t.BusinessProcessDefinitionId == seed.BpId))
            .Should().Be(2, "baseline 2 transition-form bindings (ft1 + ft2)");

        // Act: re-load → Clear transition-forms → re-add ONLY ft1 → UpdateAsync
        var reloaded = await seed.Repo.GetByPublicIdAsync(seed.BpPublicId);
        reloaded.Should().NotBeNull();

        reloaded!.ClearTransitionForms();
        reloaded.AddTransitionForm(BusinessProcessTransitionForm.Create(seed.Gen, seed.TenantId, seed.BpId, seed.TransitionId, seed.FormTemplate1Id, 1, seed.ActorId));

        Func<Task> act = () => seed.Repo.UpdateAsync(reloaded);

        await act.Should().NotThrowAsync<DbUpdateException>("subset re-add must commit cleanly");

        // Assert: exactly 1 row remains, and it's (transition, ft1) — proving ft2 row was actually DELETED
        var remaining = await seed.Db.Set<BusinessProcessTransitionForm>().AsNoTracking()
            .Where(t => t.BusinessProcessDefinitionId == seed.BpId)
            .ToListAsync();

        remaining.Should().HaveCount(1, "dropped ft2 row must be deleted, not no-op'd");
        remaining[0].WorkflowTransitionId.Should().Be(seed.TransitionId);
        remaining[0].FormTemplateId.Should().Be(seed.FormTemplate1Id, "the kept tuple is (transition, ft1)");
    }
}
