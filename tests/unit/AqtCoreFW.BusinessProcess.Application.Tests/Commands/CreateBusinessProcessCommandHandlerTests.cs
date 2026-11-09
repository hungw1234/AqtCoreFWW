using AqtCoreFW.BusinessProcess.Application.Commands.CreateBusinessProcess;
using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.BusinessProcess.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateBusinessProcessCommandHandler.
/// Pattern A guard + duplicate-code conflict + PublicId-resolve paths (GOV_BP_011/012) + happy-path.
/// Repo is substituted — Pattern A throw on AddAsync covered by Infrastructure.Tests (cross-tenant reject).
/// BP MVP P7: handler now resolves Guid PublicIds → bigint via IWorkflowModuleClient + IFormTemplateQueryClient.
/// </summary>
public sealed class CreateBusinessProcessCommandHandlerTests
{
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid WfPublicId = Guid.NewGuid();
    private static readonly Guid IntakePublicId = Guid.NewGuid();

    private readonly IBusinessProcessRepository _repo = Substitute.For<IBusinessProcessRepository>();
    private readonly IWorkflowModuleClient _wfClient = Substitute.For<IWorkflowModuleClient>();
    private readonly IFormTemplateQueryClient _ftClient = Substitute.For<IFormTemplateQueryClient>();
    private readonly IGuidGenerator _gen = Substitute.For<IGuidGenerator>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly ILogger<CreateBusinessProcessCommandHandler> _logger =
        Substitute.For<ILogger<CreateBusinessProcessCommandHandler>>();
    private readonly CreateBusinessProcessCommandHandler _handler;

    public CreateBusinessProcessCommandHandlerTests()
    {
        _tenantContext.TenantId.Returns(CurrentTenantId);
        _gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        // Default: workflow resolve succeeds
        _wfClient.TryResolveDefinitionInternalIdAsync(WfPublicId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((long?)42L);
        _handler = new CreateBusinessProcessCommandHandler(
            _repo, _wfClient, _ftClient, _gen, _tenantContext, _cache, _logger);
    }

    private static CreateBusinessProcessCommand Cmd(Guid tenantId, string code = "BP-001",
        Guid? wfPublicId = null, Guid? intakePublicId = null, int? intakeVersion = null) =>
        new(tenantId, code, "Test BP", Description: null,
            WorkflowDefinitionPublicId: wfPublicId ?? WfPublicId,
            IntakeFormTemplatePublicId: intakePublicId,
            IntakeFormTemplateVersion: intakeVersion,
            CreatedBy: ActorId);

    [Fact]
    public async Task Handle_CrossTenant_Rejected_BeforeAnyDbCall()
    {
        var result = await _handler.Handle(Cmd(OtherTenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await _repo.DidNotReceive().GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddAsync(Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateCode_ReturnsConflict()
    {
        var existing = BusinessProcessDefinition.Create(
            _gen, CurrentTenantId, "BP-001", "Existing", 42L, ActorId);
        _repo.GetByCodeAsync("BP-001", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _handler.Handle(Cmd(CurrentTenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ConflictError);
        result.Errors.First().Message.Should().Contain("GOV_BP_003");
        await _repo.DidNotReceive().AddAsync(Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HappyPath_AddsRepoAndInvalidatesCache()
    {
        _repo.GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BusinessProcessDefinition?)null);

        var result = await _handler.Handle(Cmd(CurrentTenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty("the new BP's PublicId must be returned to the controller for 201 Location");
        await _repo.Received(1).AddAsync(
            Arg.Is<BusinessProcessDefinition>(b => b.TenantId == CurrentTenantId && b.Code == "BP-001" && !b.IsActive),
            Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveByPrefixAsync(
            $"bp:{CurrentTenantId}:", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_IntakePairingXorViolation_ReturnsValidationError()
    {
        _repo.GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BusinessProcessDefinition?)null);
        // Intake form resolves but no version supplied → XOR violation
        _ftClient.TryResolveTemplateInternalIdAsync(IntakePublicId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((long?)99L);
        var badCmd = Cmd(CurrentTenantId, "BP-XOR", intakePublicId: IntakePublicId, intakeVersion: null);

        var result = await _handler.Handle(badCmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        result.Errors.First().Message.Should().Contain("GOV_BP_010");
        await _repo.DidNotReceive().AddAsync(Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_IntakePinned_NoPublishedSnapshot_ReturnsValidationError()
    {
        _repo.GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BusinessProcessDefinition?)null);
        _ftClient.TryResolveTemplateInternalIdAsync(IntakePublicId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((long?)99L);
        // Template resolves but the pinned version has no published snapshot → fail-fast
        _ftClient.GetVersionSchemaByInternalIdAsync(99L, 1, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((FormTemplateVersionSchemaDto?)null);

        var result = await _handler.Handle(
            Cmd(CurrentTenantId, "BP-NOSNAP", intakePublicId: IntakePublicId, intakeVersion: 1),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        result.Errors.First().Message.Should().Contain("chưa được publish");
        await _repo.DidNotReceive().AddAsync(Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotFoundWorkflowDef_ReturnsValidationError_GOV_BP_011()
    {
        var unknownWfId = Guid.NewGuid();
        _wfClient.TryResolveDefinitionInternalIdAsync(unknownWfId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((long?)null);

        var result = await _handler.Handle(Cmd(CurrentTenantId, wfPublicId: unknownWfId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        result.Errors.First().Message.Should().Contain("GOV_BP_011");
        await _repo.DidNotReceive().AddAsync(Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotFoundIntakeTemplate_ReturnsValidationError_GOV_BP_012()
    {
        _repo.GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BusinessProcessDefinition?)null);
        _ftClient.TryResolveTemplateInternalIdAsync(IntakePublicId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((long?)null);

        var result = await _handler.Handle(
            Cmd(CurrentTenantId, intakePublicId: IntakePublicId, intakeVersion: 1),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        result.Errors.First().Message.Should().Contain("GOV_BP_012");
        await _repo.DidNotReceive().AddAsync(Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());
    }
}
