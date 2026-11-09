using AqtCoreFW.Forms.Application.Commands.SubmitForm;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Infrastructure.Clients;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentAssertions;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// Verifies the R14 cross-tenant guard on <see cref="InProcessFormSubmissionRunClient.CreateForBpRunAsync"/>
/// (P2 BP-Run linkage, FU3).
///
/// Design notes:
///   - The guard is implemented as an explicit <see cref="IWorkflowModuleClient.GetTenantForInstanceAsync"/>
///     call before delegating to <see cref="IMediator.Send"/>(<see cref="SubmitFormCommand"/>). It is a
///     cross-module client boundary, NOT an EF query filter.
///   - Therefore the test surface is the boundary itself: substitute the Workflow client + mediator and
///     verify the 3 guard outcomes. Standing up the full Forms.Application DI tree (Forms DbContext,
///     Workflow DbContext, outbox, storage providers, validators) only to exercise these 3 branches would
///     be brittle without additional integration value — the unit tests for SubmitFormCommandHandler
///     already cover the post-guard write path.
///   - Placed in TenantIsolation.Tests because the guard is a tenant isolation enforcement point — same
///     spirit as RLS / write-guard checks in the other tests of this project.
/// </summary>
public sealed class FormSubmissionRunClientTenantIsolationTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid FormTemplatePublicId = Guid.NewGuid();
    private static readonly Guid SubmittedBy = Guid.NewGuid();

    private static InProcessFormSubmissionRunClient MakeClient(
        IWorkflowModuleClient workflowClient,
        IMediator mediator)
    {
        var db = Substitute.For<IReadDbConnection>();
        var storageFactory = Substitute.For<IFormDataStorageFactory>();
        return new InProcessFormSubmissionRunClient(
            db,
            mediator,
            workflowClient,
            storageFactory,
            NullLogger<InProcessFormSubmissionRunClient>.Instance);
    }

    // -------------------------------------------------------------------------
    // FU3-T1: instance-not-found rejected
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateForBpRunAsync_InstanceNotFound_ReturnsCrossTenantOutcome()
    {
        var workflowClient = Substitute.For<IWorkflowModuleClient>();
        workflowClient.GetTenantForInstanceAsync(InstanceId, Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        var mediator = Substitute.For<IMediator>();
        var client = MakeClient(workflowClient, mediator);

        var outcome = await client.CreateForBpRunAsync(
            workflowInstanceId: InstanceId,
            workflowStateId: null,
            formTemplatePublicId: FormTemplatePublicId,
            submittedBy: SubmittedBy,
            data: new Dictionary<string, object?> { ["field1"] = "value1" },
            consentGiven: true,
            tenantId: TenantA);

        outcome.IsSuccess.Should().BeFalse(
            "missing WorkflowInstance must short-circuit before mediator dispatch");
        outcome.ErrorCode.Should().Be("GOV_BP_002",
            "a non-existent run is reported with the same code as a cross-tenant run (does not leak existence)");

        // Mediator MUST NOT be invoked when guard rejects.
        await mediator.DidNotReceive().Send(Arg.Any<SubmitFormCommand>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // FU3-T2: tenant-mismatch rejected (GOV_BP_002)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateForBpRunAsync_TenantMismatch_ReturnsCrossTenantOutcome()
    {
        // WorkflowInstance belongs to TenantA, but caller passes TenantB.
        var workflowClient = Substitute.For<IWorkflowModuleClient>();
        workflowClient.GetTenantForInstanceAsync(InstanceId, Arg.Any<CancellationToken>())
            .Returns(TenantA);

        var mediator = Substitute.For<IMediator>();
        var client = MakeClient(workflowClient, mediator);

        var outcome = await client.CreateForBpRunAsync(
            workflowInstanceId: InstanceId,
            workflowStateId: 42L,
            formTemplatePublicId: FormTemplatePublicId,
            submittedBy: SubmittedBy,
            data: new Dictionary<string, object?> { ["field1"] = "value1" },
            consentGiven: false,
            tenantId: TenantB);

        outcome.IsSuccess.Should().BeFalse();
        outcome.ErrorCode.Should().Be("GOV_BP_002",
            "cross-tenant BP-Run write must be rejected with the GOV_BP_002 error code");

        await mediator.DidNotReceive().Send(Arg.Any<SubmitFormCommand>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // FU3-T3: happy-path — guard passes, mediator dispatched, submission Id returned
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateForBpRunAsync_TenantMatch_DispatchesSubmitFormCommand_AndReturnsSubmissionId()
    {
        const long stateId = 7L;
        var expectedSubmissionId = Guid.NewGuid();

        var workflowClient = Substitute.For<IWorkflowModuleClient>();
        workflowClient.GetTenantForInstanceAsync(InstanceId, Arg.Any<CancellationToken>())
            .Returns(TenantA);

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<SubmitFormCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(expectedSubmissionId));

        var client = MakeClient(workflowClient, mediator);

        var data = new Dictionary<string, object?> { ["full_name"] = "Nguyễn Văn A", ["age"] = 30 };

        var outcome = await client.CreateForBpRunAsync(
            workflowInstanceId: InstanceId,
            workflowStateId: stateId,
            formTemplatePublicId: FormTemplatePublicId,
            submittedBy: SubmittedBy,
            data: data,
            consentGiven: true,
            tenantId: TenantA);

        outcome.IsSuccess.Should().BeTrue();
        outcome.SubmissionId.Should().Be(expectedSubmissionId);

        // Verify the dispatched SubmitFormCommand carries the BP-Run fields end-to-end.
        await mediator.Received(1).Send(
            Arg.Is<SubmitFormCommand>(c =>
                c.TenantId == TenantA &&
                c.FormTemplatePublicId == FormTemplatePublicId &&
                c.SubmittedBy == SubmittedBy &&
                c.ConsentGiven == true &&
                c.WorkflowInstanceId == InstanceId &&
                c.WorkflowStateId == stateId &&
                c.Data.Count == 2),
            Arg.Any<CancellationToken>());
    }
}
