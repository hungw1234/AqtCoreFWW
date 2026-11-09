using AqtCoreFW.BusinessProcess.Application.Commands.ReplaceFormMaps;
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
/// Unit tests for ReplaceFormMapsCommandHandler.
/// Covers: happy path, cross-tenant (Pattern A), not found, unresolvable state/transition,
/// unresolvable FormTemplate, stale ETag (412), cross-definition state ID injection.
/// TC-BP-APP-030..037.
/// </summary>
public sealed class ReplaceFormMapsCommandHandlerTests
{
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid BpPublicId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid FormTemplatePublicId = Guid.NewGuid();

    // Workflow structure: definition 42 has state 10, transition 20
    private static readonly long WfDefinitionId = 42L;
    private static readonly long ValidStateId = 10L;
    private static readonly long ValidTransitionId = 20L;
    private static readonly long InvalidStateId = 99L; // not in definition
    private static readonly long FormTemplateInternalId = 55L;

    private readonly IBusinessProcessRepository _repo = Substitute.For<IBusinessProcessRepository>();
    private readonly IWorkflowModuleClient _wfClient = Substitute.For<IWorkflowModuleClient>();
    private readonly IFormTemplateQueryClient _ftClient = Substitute.For<IFormTemplateQueryClient>();
    private readonly IGuidGenerator _gen = Substitute.For<IGuidGenerator>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly ILogger<ReplaceFormMapsCommandHandler> _logger =
        Substitute.For<ILogger<ReplaceFormMapsCommandHandler>>();
    private readonly ReplaceFormMapsCommandHandler _handler;

    public ReplaceFormMapsCommandHandlerTests()
    {
        _tenantContext.TenantId.Returns(CurrentTenantId);
        _gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());

        // Default: form template resolves successfully
        _ftClient.TryResolveTemplateInternalIdAsync(
                FormTemplatePublicId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((long?)FormTemplateInternalId);

        // Default: workflow definition structure resolves with state 10 + transition 20
        _wfClient.GetDefinitionStructureAsync(WfDefinitionId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(new WorkflowDefinitionStructure(
                StateIds: new HashSet<long> { ValidStateId },
                TransitionIds: new HashSet<long> { ValidTransitionId }));

        _handler = new ReplaceFormMapsCommandHandler(
            _repo, _wfClient, _ftClient, _gen, _tenantContext, _cache, _logger);
    }

    /// <summary>Creates a BP with a known RowVersion and non-zero Id, attaches it to the repo stub.</summary>
    private BusinessProcessDefinition SetupBp(byte[]? rowVersion = null)
    {
        var bp = BusinessProcessDefinition.Create(_gen, CurrentTenantId, "BP", "Name", WfDefinitionId, ActorId);

        // Inject Id = 1 via reflection (EF DB-assigns Id post-INSERT; in unit tests the entity starts at 0).
        // Handler passes bp.Id to BusinessProcessStateForm.Create which validates > 0.
        // Id is declared on Entity (grandparent class); use BindingFlags to traverse hierarchy.
        typeof(BusinessProcessDefinition)
            .GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!
            .SetValue(bp, 1L);

        // Inject RowVersion (EF sets it on insert/update)
        var rv = rowVersion ?? new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 };
        typeof(BusinessProcessDefinition)
            .GetProperty(nameof(BusinessProcessDefinition.RowVersion))!
            .SetValue(bp, rv);

        _repo.GetByPublicIdAsync(bp.PublicId, Arg.Any<CancellationToken>())
            .Returns(bp);
        return bp;
    }

    private static ReplaceFormMapsCommand Cmd(
        Guid? bpPublicId = null,
        Guid? tenantId = null,
        string? eTag = null,
        IReadOnlyList<StateFormBindingDto>? stateForms = null,
        IReadOnlyList<TransitionFormBindingDto>? transitionForms = null) =>
        new(
            BusinessProcessPublicId: bpPublicId ?? BpPublicId,
            TenantId: tenantId ?? CurrentTenantId,
            ModifiedBy: ActorId,
            IfMatchETag: eTag,
            StateForms: stateForms ?? [],
            TransitionForms: transitionForms ?? []);

    // ─── TC-BP-APP-030: Cross-tenant pattern-A guard ─────────────────────────

    [Fact]
    public async Task Handle_CrossTenant_Rejected_BeforeAnyDbCall()
    {
        var result = await _handler.Handle(
            Cmd(tenantId: OtherTenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await _repo.DidNotReceive().GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ─── TC-BP-APP-031: BP not found ─────────────────────────────────────────

    [Fact]
    public async Task Handle_BpNotFound_ReturnsNotFoundError()
    {
        _repo.GetByPublicIdAsync(BpPublicId, Arg.Any<CancellationToken>())
            .Returns((BusinessProcessDefinition?)null);

        var result = await _handler.Handle(Cmd(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }

    // ─── TC-BP-APP-032: Stale ETag → 412 ─────────────────────────────────────

    [Fact]
    public async Task Handle_StaleETag_ReturnsPreconditionFailedError()
    {
        var bp = SetupBp(rowVersion: new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });
        // Stale: client sends a different base64
        var staleETag = Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 2 });

        var result = await _handler.Handle(Cmd(bpPublicId: bp.PublicId, eTag: staleETag), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is PreconditionFailedError);
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MissingETag_ReturnsPreconditionFailedError()
    {
        // TC-BP-APP-032b: D-B-3 If-Match is MANDATORY — absent header must also 412, not bypass
        var bp = SetupBp();

        var result = await _handler.Handle(
            Cmd(bpPublicId: bp.PublicId, eTag: null), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is PreconditionFailedError);
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyStringETag_ReturnsPreconditionFailedError()
    {
        // ASP.NET returns empty string when If-Match header is absent → same 412 gate
        var bp = SetupBp();

        var result = await _handler.Handle(
            Cmd(bpPublicId: bp.PublicId, eTag: ""), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is PreconditionFailedError);
    }

    // ─── TC-BP-APP-033: State ID not in definition → ValidationError ──────────

    [Fact]
    public async Task Handle_StateIdNotInDefinition_ReturnsValidationError()
    {
        var bp = SetupBp();
        var correctETag = Convert.ToBase64String(bp.RowVersion);
        var stateBindings = new List<StateFormBindingDto>
        {
            new(WorkflowStateId: InvalidStateId, FormTemplatePublicId: FormTemplatePublicId, Version: 1, IsRequired: false)
        };

        var result = await _handler.Handle(
            Cmd(bpPublicId: bp.PublicId, eTag: correctETag, stateForms: stateBindings), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e is ValidationError && e.Message.Contains("GOV_BP_005"));
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());
    }

    // ─── TC-BP-APP-034: Transition ID not in definition → ValidationError ─────

    [Fact]
    public async Task Handle_TransitionIdNotInDefinition_ReturnsValidationError()
    {
        var bp = SetupBp();
        var correctETag = Convert.ToBase64String(bp.RowVersion);
        var transBindings = new List<TransitionFormBindingDto>
        {
            new(WorkflowTransitionId: 999L, FormTemplatePublicId: FormTemplatePublicId, Version: 1, IsRequired: false)
        };

        var result = await _handler.Handle(
            Cmd(bpPublicId: bp.PublicId, eTag: correctETag, transitionForms: transBindings), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e is ValidationError && e.Message.Contains("GOV_BP_005"));
    }

    // ─── TC-BP-APP-035: FormTemplate not found → ValidationError ──────────────

    [Fact]
    public async Task Handle_FormTemplateNotFound_ReturnsValidationError()
    {
        var bp = SetupBp();
        var correctETag = Convert.ToBase64String(bp.RowVersion);
        var unknownTemplateId = Guid.NewGuid();
        _ftClient.TryResolveTemplateInternalIdAsync(
                unknownTemplateId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((long?)null);

        var stateBindings = new List<StateFormBindingDto>
        {
            new(WorkflowStateId: ValidStateId, FormTemplatePublicId: unknownTemplateId, Version: 1, IsRequired: false)
        };

        var result = await _handler.Handle(
            Cmd(bpPublicId: bp.PublicId, eTag: correctETag, stateForms: stateBindings), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e is ValidationError && e.Message.Contains("GOV_BP_005"));
    }

    // ─── TC-BP-APP-036: Happy path — replaces existing with new bindings ───────

    [Fact]
    public async Task Handle_HappyPath_ClearsAndAddsBindings_InvalidatesCache()
    {
        // Add a second valid state ID to the definition structure for multi-row ordering test
        _wfClient.GetDefinitionStructureAsync(WfDefinitionId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(new WorkflowDefinitionStructure(
                StateIds: new HashSet<long> { ValidStateId, 11L },
                TransitionIds: new HashSet<long> { ValidTransitionId }));

        var secondFormPublicId = Guid.NewGuid();
        _ftClient.TryResolveTemplateInternalIdAsync(
                secondFormPublicId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((long?)56L);

        var bp = SetupBp();
        var correctETag = Convert.ToBase64String(bp.RowVersion);

        // Two state bindings in specific order — proves D-B-2: displayOrder = array index [0,1]
        var stateBindings = new List<StateFormBindingDto>
        {
            new(WorkflowStateId: ValidStateId, FormTemplatePublicId: FormTemplatePublicId, Version: 1, IsRequired: true),
            new(WorkflowStateId: 11L, FormTemplatePublicId: secondFormPublicId, Version: 1, IsRequired: false)
        };
        var transitionBindings = new List<TransitionFormBindingDto>
        {
            new(WorkflowTransitionId: ValidTransitionId, FormTemplatePublicId: FormTemplatePublicId, Version: 2, IsRequired: false)
        };

        var result = await _handler.Handle(
            Cmd(bpPublicId: bp.PublicId, eTag: correctETag,
                stateForms: stateBindings, transitionForms: transitionBindings),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bp.StateForms.Should().HaveCount(2);
        bp.StateForms[0].WorkflowStateId.Should().Be(ValidStateId);
        bp.StateForms[0].IsRequired.Should().BeTrue();
        bp.StateForms[0].DisplayOrder.Should().Be(0, "D-B-2: first item gets displayOrder=0");
        bp.StateForms[1].WorkflowStateId.Should().Be(11L);
        bp.StateForms[1].DisplayOrder.Should().Be(1, "D-B-2: second item gets displayOrder=1");
        bp.TransitionForms.Should().HaveCount(1);
        bp.TransitionForms[0].WorkflowTransitionId.Should().Be(ValidTransitionId);
        bp.TransitionForms[0].DisplayOrder.Should().Be(0);
        await _repo.Received(1).UpdateAsync(bp, Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveByPrefixAsync(
            $"bp:{CurrentTenantId}:", Arg.Any<CancellationToken>());
    }

    // ─── TC-BP-APP-037: Replace-all clears pre-existing rows ─────────────────

    [Fact]
    public async Task Handle_ExistingBindings_AreReplacedNotAppended()
    {
        var bp = SetupBp();
        var correctETag = Convert.ToBase64String(bp.RowVersion);
        // Pre-populate with an existing state form that should be replaced.
        // bp.Id = 1 (injected by SetupBp reflection)
        var existingSf = BusinessProcessStateForm.Create(
            _gen, CurrentTenantId,
            businessProcessDefinitionId: 1L,
            workflowStateId: ValidStateId,
            formTemplateId: FormTemplateInternalId,
            formTemplateVersion: 1,
            createdBy: ActorId);
        bp.AddStateForm(existingSf);
        bp.StateForms.Should().HaveCount(1);

        // Replace with empty list (clear all)
        var result = await _handler.Handle(
            Cmd(bpPublicId: bp.PublicId, eTag: correctETag, stateForms: [], transitionForms: []),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bp.StateForms.Should().BeEmpty("replace-all with empty list must clear existing rows");
        bp.TransitionForms.Should().BeEmpty();
        await _repo.Received(1).UpdateAsync(bp, Arg.Any<CancellationToken>());
    }
}
