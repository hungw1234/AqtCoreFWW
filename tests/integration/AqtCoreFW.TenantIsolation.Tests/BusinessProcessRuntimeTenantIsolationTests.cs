using AqtCoreFW.BusinessProcess.Application.Abstractions;
using AqtCoreFW.BusinessProcess.Application.Commands.ExecuteBusinessProcessTransition;
using AqtCoreFW.BusinessProcess.Application.Commands.StartBusinessProcessRun;
using AqtCoreFW.BusinessProcess.Application.Commands.SubmitBusinessProcessStateForm;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// P4 Slice 2C: verifies the Pattern-A pre-DB tenant guard on all 3 BP runtime handlers
/// (StartBpRun, ExecuteBpTransition, SubmitStateForm). Cross-tenant attempts are short-circuited
/// before any downstream client call — no leak via Workflow, Forms, or BP runtime queries.
///
/// Spirit: same as <see cref="FormSubmissionRunClientTenantIsolationTests"/> — substitute cross-module
/// clients and assert the guard's outcome + the absence of downstream dispatch.
/// </summary>
public sealed class BusinessProcessRuntimeTenantIsolationTests
{
    private static readonly Guid CallerTenantId = Guid.NewGuid();
    private static readonly Guid VictimTenantId = Guid.NewGuid();
    private static readonly Guid BpPublicId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static ITenantContext MakeTenantContext()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(CallerTenantId);
        ctx.IsSystemAdmin.Returns(false);
        return ctx;
    }

    [Fact]
    public async Task StartBusinessProcessRun_CrossTenant_RejectedBeforeAnyClientCall()
    {
        var runtime = Substitute.For<IBusinessProcessRuntimeQueries>();
        var workflow = Substitute.For<IWorkflowModuleClient>();
        var formTemplate = Substitute.For<IFormTemplateQueryClient>();
        var formSubmission = Substitute.For<IFormSubmissionRunClient>();
        var handler = new StartBusinessProcessRunCommandHandler(
            runtime, workflow, formTemplate, formSubmission, Substitute.For<ICrossModuleUnitOfWork>(),
            MakeTenantContext(), Substitute.For<IGuidGenerator>(),
            NullLogger<StartBusinessProcessRunCommandHandler>.Instance);

        var result = await handler.Handle(
            new StartBusinessProcessRunCommand(
                BpPublicId, VictimTenantId, "Case", Guid.NewGuid(),
                IntakeFormData: null, ConsentGiven: false, ActorId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await runtime.DidNotReceiveWithAnyArgs().GetRuntimeContextAsync(default, default, default);
        await workflow.DidNotReceiveWithAnyArgs()
            .CreateInstanceByInternalIdAsync(default, default!, default, default, default, default);
        await formSubmission.DidNotReceiveWithAnyArgs()
            .CreateForBpRunAsync(default, default, default, default, default!, default, default, default);
    }

    [Fact]
    public async Task ExecuteBusinessProcessTransition_CrossTenant_RejectedBeforeWorkflowResolve()
    {
        var runtime = Substitute.For<IBusinessProcessRuntimeQueries>();
        var workflow = Substitute.For<IWorkflowModuleClient>();
        var formTemplate = Substitute.For<IFormTemplateQueryClient>();
        var formSubmission = Substitute.For<IFormSubmissionRunClient>();
        var signatureSession = Substitute.For<IDocumentSignatureSessionClient>();
        var handler = new ExecuteBusinessProcessTransitionCommandHandler(
            runtime, workflow, formTemplate, formSubmission, signatureSession, Substitute.For<ICrossModuleUnitOfWork>(),
            MakeTenantContext(), NullLogger<ExecuteBusinessProcessTransitionCommandHandler>.Instance);

        var result = await handler.Handle(
            new ExecuteBusinessProcessTransitionCommand(
                InstanceId, BpPublicId, "Approve",
                TransitionFormTemplateId: null, TransitionFormData: null,
                ConsentGiven: false, Comment: null,
                TenantId: VictimTenantId, ActorId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await workflow.DidNotReceiveWithAnyArgs().ResolveTransitionIdAsync(default, default!, default, default);
        await workflow.DidNotReceiveWithAnyArgs().ExecuteTransitionAsync(default!, default);
        await formSubmission.DidNotReceiveWithAnyArgs()
            .CreateForBpRunAsync(default, default, default, default, default!, default, default, default);
    }

    [Fact]
    public async Task SubmitBusinessProcessStateForm_CrossTenant_RejectedBeforeBindingLookup()
    {
        var runtime = Substitute.For<IBusinessProcessRuntimeQueries>();
        var formTemplate = Substitute.For<IFormTemplateQueryClient>();
        var formSubmission = Substitute.For<IFormSubmissionRunClient>();
        var handler = new SubmitBusinessProcessStateFormCommandHandler(
            runtime, formTemplate, formSubmission, MakeTenantContext(),
            NullLogger<SubmitBusinessProcessStateFormCommandHandler>.Instance);

        var result = await handler.Handle(
            new SubmitBusinessProcessStateFormCommand(
                InstanceId, WorkflowStateId: 7L, BpPublicId, FormTemplateId: 42L,
                new Dictionary<string, object?> { ["x"] = 1 },
                ConsentGiven: false, TenantId: VictimTenantId, ActorId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await runtime.DidNotReceiveWithAnyArgs()
            .GetStateFormTemplateIdsAsync(default, default, default, default);
        await formSubmission.DidNotReceiveWithAnyArgs()
            .CreateForBpRunAsync(default, default, default, default, default!, default, default, default);
    }
}
