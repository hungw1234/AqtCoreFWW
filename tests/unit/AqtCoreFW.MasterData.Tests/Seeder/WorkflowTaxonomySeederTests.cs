using AqtCoreFW.MasterData.Domain.Constants;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Domain.Enums;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.MasterData.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace AqtCoreFW.MasterData.Tests.Seeder;

/// <summary>
/// TC-MD-S001: WorkflowTaxonomySeeder — R2 strict INSERT-IF-NOT-EXISTS by Code.
/// Contract: seeder NEVER updates existing rows; soft-deleted + hard-deleted rows
/// must be visible via IgnoreQueryFilters() so reinsert attempts don't trigger unique-index
/// violations on restart.
/// </summary>
public sealed class WorkflowTaxonomySeederTests : IDisposable
{
    private readonly MasterDataDbContext _ctx;
    private readonly IGuidGenerator _gen;

    public WorkflowTaxonomySeederTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(Guid.Empty);

        var options = new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseInMemoryDatabase($"WorkflowTaxonomySeeder_{Guid.NewGuid()}")
            .Options;

        _ctx = new MasterDataDbContext(options, tenantCtx);
        // Phase-4b: SeedStatesAsync/SeedActionsAsync now require IGuidGenerator + CancellationToken.
        // V7GuidGenerator is internal — use NSubstitute.
        _gen = Substitute.For<IGuidGenerator>();
        _gen.NewIdRandom().Returns(_ => Guid.NewGuid());
    }

    public void Dispose() => _ctx.Dispose();

    // --- State seeding ---

    [Fact]
    public async Task SeedStates_FirstRun_AddsAllBaselineCodes()
    {
        await WorkflowTaxonomySeeder.SeedStatesAsync(_ctx, _gen, WorkflowTaxonomySeeder.BaselineStates, CancellationToken.None);

        var rows = await _ctx.WorkflowStateTaxonomies.ToListAsync();
        rows.Should().HaveCount(10);
        rows.Select(r => r.Code).Should().BeEquivalentTo(new[]
        {
            WorkflowTaxonomyCodes.WorkflowStates.Draft,
            WorkflowTaxonomyCodes.WorkflowStates.Submitted,
            WorkflowTaxonomyCodes.WorkflowStates.UnderReview,
            WorkflowTaxonomyCodes.WorkflowStates.Assigned,
            WorkflowTaxonomyCodes.WorkflowStates.Approved,
            WorkflowTaxonomyCodes.WorkflowStates.Rejected,
            WorkflowTaxonomyCodes.WorkflowStates.Returned,
            WorkflowTaxonomyCodes.WorkflowStates.ReturnedForRevision,
            WorkflowTaxonomyCodes.WorkflowStates.Closed,
            WorkflowTaxonomyCodes.WorkflowStates.Cancelled,
        });
    }

    [Fact]
    public async Task SeedStates_Rerun_DoesNotDuplicate()
    {
        await WorkflowTaxonomySeeder.SeedStatesAsync(_ctx, _gen, WorkflowTaxonomySeeder.BaselineStates, CancellationToken.None);
        await WorkflowTaxonomySeeder.SeedStatesAsync(_ctx, _gen, WorkflowTaxonomySeeder.BaselineStates, CancellationToken.None);

        (await _ctx.WorkflowStateTaxonomies.CountAsync()).Should().Be(10);
    }

    [Fact]
    public async Task SeedStates_Rerun_PreservesAdminSoftDelete()
    {
        // First deploy
        await WorkflowTaxonomySeeder.SeedStatesAsync(_ctx, _gen, WorkflowTaxonomySeeder.BaselineStates, CancellationToken.None);

        // Admin deprecates Draft (IsActive = false)
        var draft = await _ctx.WorkflowStateTaxonomies.FirstAsync(x => x.Code == WorkflowTaxonomyCodes.WorkflowStates.Draft);
        draft.Deprecate("Replaced by new vocabulary");
        await _ctx.SaveChangesAsync();

        // Rerun seeder after restart
        await WorkflowTaxonomySeeder.SeedStatesAsync(_ctx, _gen, WorkflowTaxonomySeeder.BaselineStates, CancellationToken.None);

        // Draft row stays soft-deleted — seeder MUST NOT resurrect (R2)
        var reloaded = await _ctx.WorkflowStateTaxonomies.FirstAsync(x => x.Code == WorkflowTaxonomyCodes.WorkflowStates.Draft);
        reloaded.IsActive.Should().BeFalse();
        reloaded.DeprecatedReason.Should().Be("Replaced by new vocabulary");
    }

    [Fact]
    public async Task SeedStates_Rerun_DoesNotOverwriteAdminEdits()
    {
        await WorkflowTaxonomySeeder.SeedStatesAsync(_ctx, _gen, WorkflowTaxonomySeeder.BaselineStates, CancellationToken.None);

        var submitted = await _ctx.WorkflowStateTaxonomies.FirstAsync(x => x.Code == WorkflowTaxonomyCodes.WorkflowStates.Submitted);
        submitted.Update(
            nameVi: "ADMIN RENAMED",
            nameEn: submitted.NameEn,
            i18nKey: null,
            icon: submitted.Icon,
            color: submitted.Color,
            category: submitted.Category,
            sortOrder: submitted.SortOrder);
        await _ctx.SaveChangesAsync();

        await WorkflowTaxonomySeeder.SeedStatesAsync(_ctx, _gen, WorkflowTaxonomySeeder.BaselineStates, CancellationToken.None);

        var reloaded = await _ctx.WorkflowStateTaxonomies.FirstAsync(x => x.Code == WorkflowTaxonomyCodes.WorkflowStates.Submitted);
        reloaded.NameVi.Should().Be("ADMIN RENAMED");
    }

    [Fact]
    public async Task SeedStates_NewCodeAddedIncrementally_ExistingCodesUntouched()
    {
        // Simulate old deploy with only 2 codes
        var oldBaseline = new[]
        {
            new WorkflowStateTaxonomySeed(
                Code: "Draft", NameVi: "Nháp", NameEn: "Draft",
                Icon: "EditOutlined", Color: "#808080",
                Category: WorkflowStateCategory.Initial, SortOrder: 10),
            new WorkflowStateTaxonomySeed(
                Code: "Submitted", NameVi: "Đã nộp", NameEn: "Submitted",
                Icon: "SendOutlined", Color: "#1890FF",
                Category: WorkflowStateCategory.Intermediate, SortOrder: 20),
        };

        await WorkflowTaxonomySeeder.SeedStatesAsync(_ctx, _gen, oldBaseline, CancellationToken.None);
        (await _ctx.WorkflowStateTaxonomies.CountAsync()).Should().Be(2);

        // Simulate new deploy with full baseline (10 codes — phase-01 added ReturnedForRevision)
        await WorkflowTaxonomySeeder.SeedStatesAsync(_ctx, _gen, WorkflowTaxonomySeeder.BaselineStates, CancellationToken.None);

        (await _ctx.WorkflowStateTaxonomies.CountAsync()).Should().Be(10);
        // Existing 2 preserved (same Id)
        var draft = await _ctx.WorkflowStateTaxonomies.FirstAsync(x => x.Code == "Draft");
        draft.NameVi.Should().Be("Nháp");
    }

    // --- Action seeding ---

    [Fact]
    public async Task SeedActions_FirstRun_AddsAllNineBaselineCodes()
    {
        await WorkflowTaxonomySeeder.SeedActionsAsync(_ctx, _gen, WorkflowTaxonomySeeder.BaselineActions, CancellationToken.None);

        var rows = await _ctx.WorkflowActionTaxonomies.ToListAsync();
        rows.Should().HaveCount(9);
        rows.Select(r => r.Code).Should().BeEquivalentTo(new[]
        {
            WorkflowTaxonomyCodes.WorkflowActions.Submit,
            WorkflowTaxonomyCodes.WorkflowActions.Assign,
            WorkflowTaxonomyCodes.WorkflowActions.Approve,
            WorkflowTaxonomyCodes.WorkflowActions.Reject,
            WorkflowTaxonomyCodes.WorkflowActions.Return,
            WorkflowTaxonomyCodes.WorkflowActions.Close,
            WorkflowTaxonomyCodes.WorkflowActions.Cancel,
            WorkflowTaxonomyCodes.WorkflowActions.Reopen,
            WorkflowTaxonomyCodes.WorkflowActions.Reassign,
        });
    }

    [Fact]
    public async Task SeedActions_Rerun_DoesNotDuplicate()
    {
        await WorkflowTaxonomySeeder.SeedActionsAsync(_ctx, _gen, WorkflowTaxonomySeeder.BaselineActions, CancellationToken.None);
        await WorkflowTaxonomySeeder.SeedActionsAsync(_ctx, _gen, WorkflowTaxonomySeeder.BaselineActions, CancellationToken.None);

        (await _ctx.WorkflowActionTaxonomies.CountAsync()).Should().Be(9);
    }

    [Fact]
    public async Task SeedActions_Rerun_PreservesAdminSoftDelete()
    {
        await WorkflowTaxonomySeeder.SeedActionsAsync(_ctx, _gen, WorkflowTaxonomySeeder.BaselineActions, CancellationToken.None);

        var submit = await _ctx.WorkflowActionTaxonomies.FirstAsync(x => x.Code == WorkflowTaxonomyCodes.WorkflowActions.Submit);
        submit.Deprecate("Merged with Send");
        await _ctx.SaveChangesAsync();

        await WorkflowTaxonomySeeder.SeedActionsAsync(_ctx, _gen, WorkflowTaxonomySeeder.BaselineActions, CancellationToken.None);

        var reloaded = await _ctx.WorkflowActionTaxonomies.FirstAsync(x => x.Code == WorkflowTaxonomyCodes.WorkflowActions.Submit);
        reloaded.IsActive.Should().BeFalse();
    }

    // --- Baseline data contract ---

    [Fact]
    public void BaselineStates_HasTenRows()
    {
        // Phase-01 added ReturnedForRevision (option B) for legacy Cases enum parity.
        WorkflowTaxonomySeeder.BaselineStates.Should().HaveCount(10);
    }

    [Fact]
    public void BaselineActions_HasNineRows()
    {
        WorkflowTaxonomySeeder.BaselineActions.Should().HaveCount(9);
    }

    [Fact]
    public void BaselineStates_CoversAllConstantsCodes()
    {
        var baselineCodes = WorkflowTaxonomySeeder.BaselineStates.Select(s => s.Code).ToHashSet();
        var constantCodes = new[]
        {
            WorkflowTaxonomyCodes.WorkflowStates.Draft,
            WorkflowTaxonomyCodes.WorkflowStates.Submitted,
            WorkflowTaxonomyCodes.WorkflowStates.UnderReview,
            WorkflowTaxonomyCodes.WorkflowStates.Assigned,
            WorkflowTaxonomyCodes.WorkflowStates.Approved,
            WorkflowTaxonomyCodes.WorkflowStates.Rejected,
            WorkflowTaxonomyCodes.WorkflowStates.Returned,
            WorkflowTaxonomyCodes.WorkflowStates.ReturnedForRevision,
            WorkflowTaxonomyCodes.WorkflowStates.Closed,
            WorkflowTaxonomyCodes.WorkflowStates.Cancelled,
        };
        baselineCodes.Should().BeEquivalentTo(constantCodes);
    }
}
