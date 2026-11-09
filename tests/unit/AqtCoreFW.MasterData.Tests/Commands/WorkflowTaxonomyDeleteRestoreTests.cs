using AqtCoreFW.MasterData.Application.Commands.WorkflowTaxonomy;
using AqtCoreFW.MasterData.Application.Queries.WorkflowTaxonomy;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Domain.Enums;
using AqtCoreFW.MasterData.Infrastructure.CommandHandlers;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using NSubstitute;

namespace AqtCoreFW.MasterData.Tests.Commands;

/// <summary>
/// TC-MD-C006: Delete + Restore workflow taxonomy commands — confirmCode, reason, collision.
/// </summary>
public sealed class WorkflowTaxonomyDeleteRestoreTests : IDisposable
{
    private readonly MasterDataDbContext _ctx;
    private readonly IDistributedCache _cache;
    private readonly IGuidGenerator _gen;

    public WorkflowTaxonomyDeleteRestoreTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(Guid.Empty);
        var options = new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseInMemoryDatabase($"TaxonomyDel_{Guid.NewGuid()}")
            .Options;
        _ctx = new MasterDataDbContext(options, tenantCtx);
        _cache = Substitute.For<IDistributedCache>();
        // Phase-4b: V7GuidGenerator is internal; use NSubstitute for IGuidGenerator.
        _gen = Substitute.For<IGuidGenerator>();
        _gen.NewIdRandom().Returns(_ => Guid.NewGuid());
    }

    public void Dispose() => _ctx.Dispose();

    // --- Delete happy / confirmCode / reason ---

    [Fact]
    public async Task Delete_State_Happy_SoftDeletes_InvalidatesCache()
    {
        var entity = WorkflowStateTaxonomy.Create(_gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);
        _ctx.WorkflowStateTaxonomies.Add(entity);
        await _ctx.SaveChangesAsync();

        var handler = new DeleteWorkflowStateTaxonomyCommandHandler(_ctx, _cache);
        var result = await handler.Handle(
            new DeleteWorkflowStateTaxonomyCommand(entity.PublicId, "Draft", "Replaced by new vocab 2026"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var reloaded = await _ctx.WorkflowStateTaxonomies.FirstAsync(x => x.Id == entity.Id);
        reloaded.IsActive.Should().BeFalse();
        reloaded.DeprecatedReason.Should().Be("Replaced by new vocab 2026");
        await _cache.Received(1).RemoveAsync(
            GetWorkflowStateTaxonomiesQueryHandler.CacheKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_State_ConfirmCodeMismatch_ReturnsBadRequest()
    {
        var entity = WorkflowStateTaxonomy.Create(_gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);
        _ctx.WorkflowStateTaxonomies.Add(entity);
        await _ctx.SaveChangesAsync();

        var handler = new DeleteWorkflowStateTaxonomyCommandHandler(_ctx, _cache);
        var result = await handler.Handle(
            new DeleteWorkflowStateTaxonomyCommand(entity.PublicId, "WrongCode", "Valid reason text"),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e is BadRequestError
            && e.Metadata.ContainsKey("ErrorCode")
            && e.Metadata["ErrorCode"]!.ToString() == "GOV_CONFIRM_CODE_MISMATCH");
    }

    [Fact]
    public async Task Delete_State_CaseInsensitiveCode_StillMismatches()
    {
        // Plan §Validation Delete: case-SENSITIVE compare — "draft" ≠ "Draft"
        var entity = WorkflowStateTaxonomy.Create(_gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);
        _ctx.WorkflowStateTaxonomies.Add(entity);
        await _ctx.SaveChangesAsync();

        var handler = new DeleteWorkflowStateTaxonomyCommandHandler(_ctx, _cache);
        var result = await handler.Handle(
            new DeleteWorkflowStateTaxonomyCommand(entity.PublicId, "draft", "Valid reason text"),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_State_NotFound_ReturnsNotFound()
    {
        var handler = new DeleteWorkflowStateTaxonomyCommandHandler(_ctx, _cache);
        var result = await handler.Handle(
            new DeleteWorkflowStateTaxonomyCommand(Guid.NewGuid(), "X", "Valid reason text"),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is NotFoundError);
    }

    [Fact]
    public void DeleteValidator_RejectsShortReason()
    {
        var v = new DeleteWorkflowStateTaxonomyCommandValidator();
        var bad = v.TestValidate(new DeleteWorkflowStateTaxonomyCommand(
            Guid.NewGuid(), "X", "Short"));
        bad.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void DeleteValidator_AcceptsValidReason()
    {
        var v = new DeleteWorkflowStateTaxonomyCommandValidator();
        var ok = v.TestValidate(new DeleteWorkflowStateTaxonomyCommand(
            Guid.NewGuid(), "X", "Reason at least 10 chars long"));
        ok.ShouldNotHaveValidationErrorFor(x => x.Reason);
    }

    // --- Restore happy / collision / not-found ---

    [Fact]
    public async Task Restore_State_Happy_ReactivatesRow()
    {
        var entity = WorkflowStateTaxonomy.Create(_gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);
        entity.Deprecate("temp deprecate");
        _ctx.WorkflowStateTaxonomies.Add(entity);
        await _ctx.SaveChangesAsync();

        var handler = new RestoreWorkflowStateTaxonomyCommandHandler(_ctx, _cache);
        var result = await handler.Handle(
            new RestoreWorkflowStateTaxonomyCommand(entity.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var reloaded = await _ctx.WorkflowStateTaxonomies.FirstAsync(x => x.Id == entity.Id);
        reloaded.IsActive.Should().BeTrue();
        reloaded.DeprecatedReason.Should().BeNull();
    }

    [Fact]
    public async Task Restore_State_CollisionWithActiveDuplicate_Returns409()
    {
        // Safety check — plan R6. Normally blocked by unique index, but covers edge cases.
        var inactive = WorkflowStateTaxonomy.Create(_gen, "Draft", "Nháp 1", "Draft 1", WorkflowStateCategory.Initial);
        inactive.Deprecate("old");
        // Use reflection-free alternative: add inactive first, then active separately (unique index on Code would fail in real DB)
        // InMemory doesn't enforce unique — we can add both; production relies on CREATE conflict prevention.
        _ctx.WorkflowStateTaxonomies.Add(inactive);
        await _ctx.SaveChangesAsync();

        var active = WorkflowStateTaxonomy.Create(_gen, "Draft", "Nháp 2", "Draft 2", WorkflowStateCategory.Initial);
        _ctx.WorkflowStateTaxonomies.Add(active);
        await _ctx.SaveChangesAsync();

        var handler = new RestoreWorkflowStateTaxonomyCommandHandler(_ctx, _cache);
        var result = await handler.Handle(
            new RestoreWorkflowStateTaxonomyCommand(inactive.PublicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is ConflictError);
    }

    [Fact]
    public async Task Restore_State_NotFound_ReturnsNotFound()
    {
        var handler = new RestoreWorkflowStateTaxonomyCommandHandler(_ctx, _cache);
        var result = await handler.Handle(
            new RestoreWorkflowStateTaxonomyCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is NotFoundError);
    }

    [Fact]
    public async Task Restore_State_AlreadyActive_Returns409_WithErrorCode()
    {
        // M1 guard — reviewer finding
        var entity = WorkflowStateTaxonomy.Create(_gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);
        _ctx.WorkflowStateTaxonomies.Add(entity);
        await _ctx.SaveChangesAsync();

        var handler = new RestoreWorkflowStateTaxonomyCommandHandler(_ctx, _cache);
        var result = await handler.Handle(
            new RestoreWorkflowStateTaxonomyCommand(entity.PublicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e is ConflictError
            && e.Metadata.ContainsKey("ErrorCode")
            && e.Metadata["ErrorCode"]!.ToString() == "GOV_ALREADY_ACTIVE");
    }

    // --- Action smoke ---

    [Fact]
    public async Task Delete_Action_Happy_SoftDeletes()
    {
        var entity = WorkflowActionTaxonomy.Create(_gen, "Submit", "Nộp", "Submit");
        _ctx.WorkflowActionTaxonomies.Add(entity);
        await _ctx.SaveChangesAsync();

        var handler = new DeleteWorkflowActionTaxonomyCommandHandler(_ctx, _cache);
        var result = await handler.Handle(
            new DeleteWorkflowActionTaxonomyCommand(entity.PublicId, "Submit", "Merged into Send action"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var reloaded = await _ctx.WorkflowActionTaxonomies.FirstAsync(x => x.Id == entity.Id);
        reloaded.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Restore_Action_Happy()
    {
        var entity = WorkflowActionTaxonomy.Create(_gen, "Submit", "Nộp", "Submit");
        entity.Deprecate("temp");
        _ctx.WorkflowActionTaxonomies.Add(entity);
        await _ctx.SaveChangesAsync();

        var handler = new RestoreWorkflowActionTaxonomyCommandHandler(_ctx, _cache);
        var result = await handler.Handle(
            new RestoreWorkflowActionTaxonomyCommand(entity.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
