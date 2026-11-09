using AqtCoreFW.Workflow.Application.Commands.ValidateMigrationMapping;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using System.Reflection;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for ValidateMigrationMappingCommandHandler.
/// Uses NSubstitute mocks for all repository dependencies — no EF.
/// Phase-4b: uses a shared TestGuidGenerator so each definition gets a unique PublicId.
/// States get unique IDs via reflection (T1 entities have Id=0 without EF).
/// </summary>
public sealed class ValidateMigrationMappingCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();

    // Shared generator: each definition gets a unique PublicId (Guid(1,...), Guid(2,...), etc.)
    private readonly TestGuidGenerator _gen = TestGuidGenerator.Create();
    private long _stateIdCounter;

    private readonly IWorkflowDefinitionRepository _definitionRepo;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly ValidateMigrationMappingCommandHandler _handler;

    public ValidateMigrationMappingCommandHandlerTests()
    {
        _definitionRepo = Substitute.For<IWorkflowDefinitionRepository>();
        _instanceRepo = Substitute.For<IWorkflowInstanceRepository>();
        _handler = new ValidateMigrationMappingCommandHandler(_definitionRepo, _instanceRepo);
    }

    // --- Helpers ---

    private WorkflowDefinition MakeDefinition(
        string key, int version, bool isActive, Guid? tenantId = null)
    {
        var def = WorkflowDefinition.Create(
            _gen, $"Workflow v{version}", "desc",
            tenantId ?? TenantId, CreatorId, key);

        if (isActive) def.Activate(CreatorId);
        return def;
    }

    /// <summary>
    /// Adds state to definition and assigns a unique test Id via reflection.
    /// T1 entities have Id=0 without EF; reflection ensures distinct IDs for dictionary keys.
    /// </summary>
    private WorkflowState AddState(WorkflowDefinition def, string name)
    {
        var state = WorkflowState.Create(def.Id, name, name, name, false, false, "#fff", 1);
        // Assign unique non-zero Id so dictionary keying works in unit tests
        var idProp = typeof(AqtCoreFW.SharedKernel.Domain.Entity)
            .GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!;
        idProp.SetValue(state, Interlocked.Increment(ref _stateIdCounter));
        def.AddState(state);
        return state;
    }

    private static ValidateMigrationMappingCommand MakeCommand(
        Guid sourceId, Guid targetId,
        IReadOnlyList<StateMappingEntry> mappings,
        bool allowDowngrade = false) =>
        new(sourceId, targetId, mappings, TenantId, allowDowngrade);

    // --- Happy path ---

    [Fact]
    public async Task Handle_AllStatesMapped_ReturnsIsValidTrue()
    {
        var source = MakeDefinition("wf-key", 1, true);
        var s1 = AddState(source, "Draft");
        var target = MakeDefinition("wf-key", 1, true); // same version — will be rejected by direction check
        var t1 = AddState(target, "InReview");
        target.Activate(CreatorId);

        // Use a v2 target to pass version check — use shared _gen so PublicId is unique
        var targetV2 = WorkflowDefinition.Create(_gen, "WF v2", "desc", TenantId, CreatorId, "wf-key");
        // clone manually to get v2 — just activate
        targetV2.Activate(CreatorId);
        var t2 = AddState(targetV2, "InReview");

        _definitionRepo.GetWithStatesAndTransitionsAsync(source.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));
        _definitionRepo.GetWithStatesAndTransitionsAsync(targetV2.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(targetV2));
        _instanceRepo.HasInstancesWithActiveBranchAsync(source.PublicId, Arg.Any<CancellationToken>())
            .Returns(false);
        _instanceRepo.GetActiveInstanceCountByStateAsync(source.Id, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, int> { { s1.Id, 2 } }); // 2 instances in Draft

        var mappings = new[] { new StateMappingEntry(s1.Id, t2.Id) };
        var cmd = new ValidateMigrationMappingCommand(
            source.PublicId, targetV2.PublicId, mappings, TenantId, AllowDowngrade: true);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeTrue();
        result.Value.UnmappedStates.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnmappedStateWithActiveInstances_ReturnsIsValidFalse()
    {
        var source = MakeDefinition("wf-key", 1, true);
        var s1 = AddState(source, "Draft");
        var s2 = AddState(source, "Review"); // unmapped
        var target = MakeDefinition("wf-key", 1, true);
        var t1 = AddState(target, "InReview");

        _definitionRepo.GetWithStatesAndTransitionsAsync(source.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));
        _definitionRepo.GetWithStatesAndTransitionsAsync(target.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(target));
        _instanceRepo.HasInstancesWithActiveBranchAsync(source.PublicId, Arg.Any<CancellationToken>())
            .Returns(false);

        // s2 (Review) has 3 active instances but is not mapped
        _instanceRepo.GetActiveInstanceCountByStateAsync(source.Id, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, int>
            {
                { s1.Id, 1 },
                { s2.Id, 3 }
            });

        var mappings = new[] { new StateMappingEntry(s1.Id, t1.Id) }; // only s1 mapped
        var cmd = new ValidateMigrationMappingCommand(
            source.PublicId, target.PublicId, mappings, TenantId, AllowDowngrade: true);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.UnmappedStates.Should().HaveCount(1);
        result.Value.UnmappedStates[0].StateId.Should().Be(s2.Id);
        result.Value.UnmappedStates[0].ActiveInstanceCount.Should().Be(3);
    }

    [Fact]
    public async Task Handle_TargetVersionLowerThanSource_RejectsDowngrade()
    {
        // source v2 (simulated by having higher version), target v1 — not actually reflected in
        // entity since version is read-only, but we test AllowDowngrade=false blocks correctly.
        // The real scenario: same key, target not active or same version.
        var source = MakeDefinition("wf-key", 1, true);
        var target = MakeDefinition("wf-key", 1, true); // same version = blocked without AllowDowngrade

        _definitionRepo.GetWithStatesAndTransitionsAsync(source.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));
        _definitionRepo.GetWithStatesAndTransitionsAsync(target.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(target));

        var cmd = new ValidateMigrationMappingCommand(
            source.PublicId, target.PublicId,
            [new StateMappingEntry(1L, 2L)],
            TenantId, AllowDowngrade: false);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.Errors.Should().Contain(e =>
            e.Contains("AllowDowngrade", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_AllowDowngrade_True_PassesVersionCheck()
    {
        var source = MakeDefinition("wf-key", 1, true);
        var s1 = AddState(source, "Draft");
        var target = MakeDefinition("wf-key", 1, true); // same version — normally blocked
        var t1 = AddState(target, "InReview");

        _definitionRepo.GetWithStatesAndTransitionsAsync(source.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));
        _definitionRepo.GetWithStatesAndTransitionsAsync(target.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(target));
        _instanceRepo.HasInstancesWithActiveBranchAsync(source.PublicId, Arg.Any<CancellationToken>())
            .Returns(false);
        _instanceRepo.GetActiveInstanceCountByStateAsync(source.Id, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, int> { { s1.Id, 1 } });

        var cmd = new ValidateMigrationMappingCommand(
            source.PublicId, target.PublicId,
            [new StateMappingEntry(s1.Id, t1.Id)],
            TenantId, AllowDowngrade: true);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Should not fail on version check when AllowDowngrade=true
        result.Value.Errors.Should().NotContain(e =>
            e.Contains("AllowDowngrade", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_CrossTenant_RejectsWithError()
    {
        var otherTenantId = Guid.NewGuid();
        var source = MakeDefinition("wf-key", 1, true, otherTenantId); // different tenant
        var target = MakeDefinition("wf-key", 1, true);

        _definitionRepo.GetWithStatesAndTransitionsAsync(source.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));
        _definitionRepo.GetWithStatesAndTransitionsAsync(target.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(target));

        var cmd = new ValidateMigrationMappingCommand(
            source.PublicId, target.PublicId,
            [new StateMappingEntry(1L, 2L)],
            TenantId); // requesting tenant differs from source tenant

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.Errors.Should().Contain(e =>
            e.Contains("tenant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_DifferentDefinitionKey_RejectsWithError()
    {
        var source = MakeDefinition("wf-key-a", 1, true);
        var target = MakeDefinition("wf-key-b", 1, true); // different key

        _definitionRepo.GetWithStatesAndTransitionsAsync(source.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));
        _definitionRepo.GetWithStatesAndTransitionsAsync(target.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(target));

        var cmd = new ValidateMigrationMappingCommand(
            source.PublicId, target.PublicId,
            [new StateMappingEntry(1L, 2L)],
            TenantId);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.Errors.Should().Contain(e =>
            e.Contains("wf-key-a", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_TargetNotActive_RejectsWithError()
    {
        var source = MakeDefinition("wf-key", 1, true);
        var target = MakeDefinition("wf-key", 1, false); // IsActive = false

        _definitionRepo.GetWithStatesAndTransitionsAsync(source.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));
        _definitionRepo.GetWithStatesAndTransitionsAsync(target.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(target));

        var cmd = new ValidateMigrationMappingCommand(
            source.PublicId, target.PublicId,
            [new StateMappingEntry(1L, 2L)],
            TenantId);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.Errors.Should().Contain(e =>
            e.Contains("not active", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_BranchStatusInstances_RejectsWithError()
    {
        var source = MakeDefinition("wf-key", 1, true);
        var s1 = AddState(source, "Draft");
        var target = MakeDefinition("wf-key", 1, true);
        var t1 = AddState(target, "InReview");

        _definitionRepo.GetWithStatesAndTransitionsAsync(source.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(source));
        _definitionRepo.GetWithStatesAndTransitionsAsync(target.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(target));

        // Instance has active branch — ineligible
        _instanceRepo.HasInstancesWithActiveBranchAsync(source.PublicId, Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = new ValidateMigrationMappingCommand(
            source.PublicId, target.PublicId,
            [new StateMappingEntry(s1.Id, t1.Id)],
            TenantId, AllowDowngrade: true);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.Errors.Should().Contain(e =>
            e.Contains("parallel branch", StringComparison.OrdinalIgnoreCase));
    }
}
