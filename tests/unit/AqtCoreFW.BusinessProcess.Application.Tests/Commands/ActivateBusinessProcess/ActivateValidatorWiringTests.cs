using AqtCoreFW.BusinessProcess.Application.Commands.ActivateBusinessProcess;
using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.BusinessProcess.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Application.Tests.Commands.ActivateBusinessProcess;

/// <summary>
/// Wiring tests: ActivateBusinessProcessCommandHandler calls IWorkflowModuleClient.ValidateDefinitionGraphAsync
/// and propagates failures correctly (Phase 5c, architecture: BP via client hop not direct Application ref).
///
/// W-A1: ValidateDefinitionGraphAsync returns invalid → handler returns GraphValidationFailed error, BP NOT updated.
/// W-A2: ValidateDefinitionGraphAsync returns valid → handler proceeds to UpdateAsync.
///
/// S5 META-L (c): each fact asserts client call count BEFORE behavior assert.
/// </summary>
public sealed class ActivateValidatorWiringTests
{
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private readonly IBusinessProcessRepository _repo = Substitute.For<IBusinessProcessRepository>();
    private readonly IFormTemplateQueryClient _ftClient = Substitute.For<IFormTemplateQueryClient>();
    private readonly IGuidGenerator _gen = Substitute.For<IGuidGenerator>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly IWorkflowModuleClient _workflowClient = Substitute.For<IWorkflowModuleClient>();
    private readonly ILogger<ActivateBusinessProcessCommandHandler> _logger =
        Substitute.For<ILogger<ActivateBusinessProcessCommandHandler>>();
    private readonly ActivateBusinessProcessCommandHandler _handler;

    // BP with a fixed WorkflowDefinitionId FK for predictable mock setup.
    private const long WfDefId = 42L;

    public ActivateValidatorWiringTests()
    {
        _tenantContext.TenantId.Returns(CurrentTenantId);
        _gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _handler = new ActivateBusinessProcessCommandHandler(
            _repo, _ftClient, _gen, _tenantContext, _cache, _workflowClient, _logger);
    }

    private BusinessProcessDefinition InactiveBp() =>
        BusinessProcessDefinition.Create(_gen, CurrentTenantId, "BP", "Test", WfDefId, ActorId);

    // ── W-A1: graph validation fails → handler returns error, BP NOT saved ──

    [Fact]
    public async Task Handle_WhenGraphValidationFails_ReturnsGraphValidationError_AndDoesNotSave()
    {
        // Arrange
        var bp = InactiveBp();
        _repo.GetByPublicIdAsync(bp.PublicId, Arg.Any<CancellationToken>()).Returns(bp);

        var violation = new WorkflowGraphViolationDto("R2", "Draft", "Submit", "Missing assignment.");
        _workflowClient.ValidateDefinitionGraphAsync(WfDefId, Arg.Any<CancellationToken>())
            .Returns(WorkflowGraphValidationOutcome.Fail([violation]));

        var command = new ActivateBusinessProcessCommand(CurrentTenantId, bp.PublicId, ActorId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert — S5 META-L (c): precondition — client WAS called exactly once.
        await _workflowClient.Received(1).ValidateDefinitionGraphAsync(
            WfDefId, Arg.Any<CancellationToken>());

        // Precondition — UpdateAsync was NOT called (BP not activated).
        await _repo.Received(0).UpdateAsync(
            Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());

        // Behavior: result must be failed.
        result.IsFailed.Should().BeTrue(
            "graph validation failure must prevent BP activation");
        // 4-B-A: verify dual-metadata — WorkflowErrorCode provenance key must be GOV_WFL_019.
        result.Errors.Should().Contain(e =>
            e.Metadata.ContainsKey("WorkflowErrorCode") &&
            e.Metadata["WorkflowErrorCode"] as string == WorkflowErrorCodes.GraphValidationFailed,
            "error must carry WorkflowErrorCode=GOV_WFL_019 (4-B-A dual-metadata invariant)");
        // GraphErrors: structured violation list must be present and carry the propagated violations.
        // Mirrors Domain factory shape; allows FE BP page per-rule inline highlighting on Activate failure.
        // Cast outside LINQ lambda — 'is' pattern is not valid in expression trees (CS8122).
        var graphError2 = result.Errors.FirstOrDefault(e => e.Metadata.ContainsKey("GraphErrors"));
        graphError2.Should().NotBeNull("error must carry GraphErrors key (M1 consistency invariant)");
        var graphErrors = graphError2!.Metadata["GraphErrors"] as IReadOnlyList<WorkflowGraphViolationDto>;
        graphErrors.Should().NotBeNull("GraphErrors value must be IReadOnlyList<WorkflowGraphViolationDto>");
        graphErrors!.Count.Should().Be(1, "must propagate exactly the 1 violation returned by the mock");
    }

    // ── W-A3: wire projection — PublicMetadata.violations surfaces through ApiResponse.Fail ──
    //
    // Anti-tautology guard: W-A1 asserts the FluentResults GraphErrors bag (passes even with a
    // broken PublicMetadata cast). This fact proves the violation list survives the
    // `is IReadOnlyDictionary<string, object?>` match in ApiResponse.Fail — i.e. the inline cast
    // in the handler is correct and the wire envelope actually carries the structured violations.

    [Fact]
    public async Task Handle_WhenGraphValidationFails_ProjectsViolationsToWireEnvelope()
    {
        // Arrange
        var bp = InactiveBp();
        _repo.GetByPublicIdAsync(bp.PublicId, Arg.Any<CancellationToken>()).Returns(bp);

        var violation = new WorkflowGraphViolationDto("R3", "Review", "Approve", "No transition target.");
        _workflowClient.ValidateDefinitionGraphAsync(WfDefId, Arg.Any<CancellationToken>())
            .Returns(WorkflowGraphValidationOutcome.Fail([violation]));

        var command = new ActivateBusinessProcessCommand(CurrentTenantId, bp.PublicId, ActorId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Precondition: handler failed.
        result.IsFailed.Should().BeTrue("graph validation failure must return a failed result");

        // Wire projection: project through ApiResponse.Fail (the wire boundary). This path applies
        // the `is IReadOnlyDictionary<string, object?>` match — a wrong/missing inline cast in the
        // handler would produce a null Metadata here without throwing.
        var apiResp = ApiResponse<bool>.Fail(result.Errors);

        apiResp.Errors.Should().HaveCount(1, "one FluentResults error maps to one ApiError");
        var apiError = apiResp.Errors![0];

        // Load-bearing assertion: Metadata non-null proves the IReadOnlyDictionary cast succeeded.
        apiError.Metadata.Should().NotBeNull(
            "PublicMetadata must survive ApiResponse.Fail's is-pattern match (inline cast required)");

        // Violations count: proves the violation list was projected, not an empty placeholder.
        var violations = apiError.Metadata!["violations"] as System.Collections.ICollection;
        violations.Should().NotBeNull("violations must be a non-null collection");
        violations!.Count.Should().Be(1, "exactly 1 violation must be projected from the mock outcome");
    }

    // ── W-A2: graph validation passes → BP is saved ──────────────────────────

    [Fact]
    public async Task Handle_WhenGraphValidationPasses_ProceedsToActivate()
    {
        // Arrange
        var bp = InactiveBp();
        _repo.GetByPublicIdAsync(bp.PublicId, Arg.Any<CancellationToken>()).Returns(bp);

        _workflowClient.ValidateDefinitionGraphAsync(WfDefId, Arg.Any<CancellationToken>())
            .Returns(WorkflowGraphValidationOutcome.Ok());

        _repo.UpdateAsync(Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var command = new ActivateBusinessProcessCommand(CurrentTenantId, bp.PublicId, ActorId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert — S5 META-L (c): precondition — client WAS called exactly once.
        await _workflowClient.Received(1).ValidateDefinitionGraphAsync(
            WfDefId, Arg.Any<CancellationToken>());

        // Precondition — UpdateAsync WAS called (BP activated).
        await _repo.Received(1).UpdateAsync(
            Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());

        // Behavior: result must succeed.
        result.IsSuccess.Should().BeTrue(
            "graph validation pass must allow BP activation to proceed");
    }
}
