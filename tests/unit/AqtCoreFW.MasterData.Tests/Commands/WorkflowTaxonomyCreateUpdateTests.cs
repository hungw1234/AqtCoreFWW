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
/// TC-MD-C005: Create + Update workflow taxonomy commands — happy path, conflict, concurrency.
/// </summary>
public sealed class WorkflowTaxonomyCreateUpdateTests : IDisposable
{
    private readonly MasterDataDbContext _ctx;
    private readonly IDistributedCache _cache;
    private readonly IGuidGenerator _gen;

    public WorkflowTaxonomyCreateUpdateTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(Guid.Empty);
        var options = new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseInMemoryDatabase($"TaxonomyCmd_{Guid.NewGuid()}")
            .Options;
        _ctx = new MasterDataDbContext(options, tenantCtx);
        _cache = Substitute.For<IDistributedCache>();
        // Phase-4b: V7GuidGenerator is internal; use NSubstitute for IGuidGenerator.
        _gen = Substitute.For<IGuidGenerator>();
        _gen.NewIdRandom().Returns(_ => Guid.NewGuid());
    }

    public void Dispose() => _ctx.Dispose();

    // --- Create: State happy path ---

    [Fact]
    public async Task Create_State_Happy_ReturnsId_AndInvalidatesCache()
    {
        var handler = new CreateWorkflowStateTaxonomyCommandHandler(_ctx, _cache, _gen);
        var result = await handler.Handle(
            new CreateWorkflowStateTaxonomyCommand("NewState", "Mới", "New", WorkflowStateCategory.Initial),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _ctx.WorkflowStateTaxonomies.CountAsync()).Should().Be(1);
        await _cache.Received(1).RemoveAsync(
            GetWorkflowStateTaxonomiesQueryHandler.CacheKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_State_DuplicateCode_ReturnsConflict_WithErrorCode()
    {
        _ctx.WorkflowStateTaxonomies.Add(
            WorkflowStateTaxonomy.Create(_gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial));
        await _ctx.SaveChangesAsync();

        var handler = new CreateWorkflowStateTaxonomyCommandHandler(_ctx, _cache, _gen);
        var result = await handler.Handle(
            new CreateWorkflowStateTaxonomyCommand("Draft", "Nháp 2", "Draft 2", WorkflowStateCategory.Initial),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        // M3 — ErrorCode metadata lets FE distinguish duplicate-code vs concurrency/collision
        result.Errors.Should().Contain(e =>
            e is ConflictError
            && e.Metadata.ContainsKey("ErrorCode")
            && e.Metadata["ErrorCode"]!.ToString() == "GOV_DUPLICATE_CODE");
    }

    [Fact]
    public async Task Create_State_SoftDeletedCodeConflict_StillRejects()
    {
        // Even a soft-deprecated row with same Code should block Create (plan R2: codes are unique by Code, regardless of IsActive)
        var existing = WorkflowStateTaxonomy.Create(_gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);
        existing.Deprecate("test");
        _ctx.WorkflowStateTaxonomies.Add(existing);
        await _ctx.SaveChangesAsync();

        var handler = new CreateWorkflowStateTaxonomyCommandHandler(_ctx, _cache, _gen);
        var result = await handler.Handle(
            new CreateWorkflowStateTaxonomyCommand("Draft", "Nháp", "Draft", WorkflowStateCategory.Initial),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Update: State happy + not-found ---

    [Fact]
    public async Task Update_State_NotFound_ReturnsNotFoundError()
    {
        var handler = new UpdateWorkflowStateTaxonomyCommandHandler(_ctx, _cache);
        var result = await handler.Handle(
            new UpdateWorkflowStateTaxonomyCommand(
                Guid.NewGuid(), "a", "b", null, null, null,
                WorkflowStateCategory.Initial, 0, [1]),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is NotFoundError);
    }

    [Fact]
    public async Task Update_State_Happy_InvalidatesCache()
    {
        var entity = WorkflowStateTaxonomy.Create(_gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);
        _ctx.WorkflowStateTaxonomies.Add(entity);
        await _ctx.SaveChangesAsync();

        var handler = new UpdateWorkflowStateTaxonomyCommandHandler(_ctx, _cache);
        var result = await handler.Handle(
            new UpdateWorkflowStateTaxonomyCommand(
                entity.PublicId, "Renamed", "Renamed", null, null, null,
                WorkflowStateCategory.Intermediate, 5, entity.RowVersion),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _cache.Received(1).RemoveAsync(
            GetWorkflowStateTaxonomiesQueryHandler.CacheKey, Arg.Any<CancellationToken>());
    }

    // --- Validator: Create ---

    [Fact]
    public void CreateValidator_RejectsInvalidCodeFormat()
    {
        var v = new CreateWorkflowStateTaxonomyCommandValidator();
        v.TestValidate(new CreateWorkflowStateTaxonomyCommand(
                "123Start", "N", "N", WorkflowStateCategory.Initial))
            .ShouldHaveValidationErrorFor(x => x.Code);
        v.TestValidate(new CreateWorkflowStateTaxonomyCommand(
                "has space", "N", "N", WorkflowStateCategory.Initial))
            .ShouldHaveValidationErrorFor(x => x.Code);
        v.TestValidate(new CreateWorkflowStateTaxonomyCommand(
                "ValidCode", "N", "N", WorkflowStateCategory.Initial))
            .ShouldNotHaveValidationErrorFor(x => x.Code);
    }

    [Fact]
    public void CreateValidator_RequiresCodeNameViNameEn()
    {
        var v = new CreateWorkflowStateTaxonomyCommandValidator();
        var bad = v.TestValidate(new CreateWorkflowStateTaxonomyCommand(
            "", "", "", WorkflowStateCategory.Initial));
        bad.ShouldHaveValidationErrorFor(x => x.Code);
        bad.ShouldHaveValidationErrorFor(x => x.NameVi);
        bad.ShouldHaveValidationErrorFor(x => x.NameEn);
    }

    // --- Validator: Update ---

    [Fact]
    public void UpdateValidator_RequiresRowVersion()
    {
        var v = new UpdateWorkflowStateTaxonomyCommandValidator();
        var bad = v.TestValidate(new UpdateWorkflowStateTaxonomyCommand(
            Guid.NewGuid(), "a", "b", null, null, null,
            WorkflowStateCategory.Initial, 0, []));
        bad.ShouldHaveValidationErrorFor(x => x.RowVersion);
    }

    // --- Action smoke ---

    [Fact]
    public async Task Create_Action_Happy_ReturnsId()
    {
        var handler = new CreateWorkflowActionTaxonomyCommandHandler(_ctx, _cache, _gen);
        var result = await handler.Handle(
            new CreateWorkflowActionTaxonomyCommand("Submit", "Nộp", "Submit"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _ctx.WorkflowActionTaxonomies.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Update_Action_Happy()
    {
        var entity = WorkflowActionTaxonomy.Create(_gen, "Submit", "Nộp", "Submit");
        _ctx.WorkflowActionTaxonomies.Add(entity);
        await _ctx.SaveChangesAsync();

        var handler = new UpdateWorkflowActionTaxonomyCommandHandler(_ctx, _cache);
        var result = await handler.Handle(
            new UpdateWorkflowActionTaxonomyCommand(
                entity.PublicId, "Gửi", "Send", null, null, 99, entity.RowVersion),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Action_CreateValidator_RejectsInvalidCodeFormat()
    {
        var v = new CreateWorkflowActionTaxonomyCommandValidator();
        v.TestValidate(new CreateWorkflowActionTaxonomyCommand("1Bad", "a", "b"))
            .ShouldHaveValidationErrorFor(x => x.Code);
    }
}
