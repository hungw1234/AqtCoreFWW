using AqtCoreFW.BusinessProcess.Application.Abstractions;
using AqtCoreFW.BusinessProcess.Domain.Errors;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using SignerSlotStatus = AqtCoreFW.SharedKernel.Contracts.Clients.SignerSlotStatus;

namespace AqtCoreFW.BusinessProcess.Application.Commands.ExecuteBusinessProcessTransition;

/// <summary>
/// Two-phase: (1) resolve transition + check form gating (R8 — form before exec). If the transition is
/// gated on a form and the caller did not bundle one, short-circuit with <see cref="ExecuteBusinessProcessTransitionOutcome.RequireForms"/>.
/// (2) when a form was bundled, submit it first under a shared local SQL transaction via
/// ICrossModuleUnitOfWork, then call <see cref="IWorkflowModuleClient.ExecuteTransitionAsync"/>.
/// Both module DbContexts enlist on one connection — no second physical connection, no DTC promotion
/// on Linux where MSDTC is unavailable. On success, look up state-forms for the new state.
/// </summary>
public sealed class ExecuteBusinessProcessTransitionCommandHandler(
    IBusinessProcessRuntimeQueries runtimeQueries,
    IWorkflowModuleClient workflowClient,
    IFormTemplateQueryClient formTemplateClient,
    IFormSubmissionRunClient formSubmissionClient,
    IDocumentSignatureSessionClient signatureSessionClient,
    ICrossModuleUnitOfWork uow,
    ITenantContext tenantContext,
    ILogger<ExecuteBusinessProcessTransitionCommandHandler> logger)
    : IRequestHandler<ExecuteBusinessProcessTransitionCommand, Result<ExecuteBusinessProcessTransitionOutcome>>
{
    public async Task<Result<ExecuteBusinessProcessTransitionOutcome>> Handle(
        ExecuteBusinessProcessTransitionCommand request, CancellationToken cancellationToken)
    {
        if (request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant ExecuteBusinessProcessTransition denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        // M-2 fix: refuse partial form payloads. If the caller supplies data we must know which template
        // binding it targets — otherwise the transition would proceed silently with the form dropped.
        if (request.TransitionFormData is not null && !request.TransitionFormTemplateId.HasValue)
            return Result.Fail(new ValidationError(
                "TransitionFormTemplateId is required when TransitionFormData is supplied."));

        // Pre-tx reads: transition resolution + form-gate check run on their own per-call connections
        // (unit of work not yet active), keeping the tx scope as narrow as possible.
        var lookup = await workflowClient.ResolveTransitionIdAsync(
            request.WorkflowInstanceId, request.ActionName, request.TenantId, cancellationToken);
        if (lookup is null)
            return Result.Fail(new NotFoundError(
                $"{BusinessProcessErrorCodes.NotFound}: Transition '{request.ActionName}' không khả dụng."));

        var transitionForms = await runtimeQueries.GetTransitionFormTemplateIdsAsync(
            request.BpPublicId, lookup.TransitionId, request.TenantId, cancellationToken);

        // P8-FU2: API contract supports at most 1 transition form per call (single
        // TransitionFormTemplateId/TransitionFormData pair). Reject multi-form bindings server-side
        // to prevent silent data drop from FE tabs #2+ when designer configures >1 bindings.
        if (transitionForms.Count > 1)
            return Result.Fail(new ValidationError(
                "Transition có nhiều hơn 1 form binding chưa được hỗ trợ. Vui lòng cấu hình tối đa 1 form cho transition này."));

        var hasRequiredForm = transitionForms.Any(f => f.IsRequired);
        if (hasRequiredForm && request.TransitionFormData is null)
        {
            // Resolve each binding's published snapshot inline so the FE can render the form fields
            // without a follow-up fetch (mirrors intake-form / state-form contract). Count is ≤1 here.
            var required = new List<BpRequiredTransitionForm>(transitionForms.Count);
            foreach (var binding in transitionForms)
            {
                var formSnapshot = await formTemplateClient.GetVersionSchemaByInternalIdAsync(
                    binding.FormTemplateId, binding.FormTemplateVersion, request.TenantId, cancellationToken);
                if (formSnapshot is null)
                    return Result.Fail(new NotFoundError(
                        $"{BusinessProcessErrorCodes.NotFound}: FormTemplate snapshot v{binding.FormTemplateVersion} không tìm thấy."));

                required.Add(new BpRequiredTransitionForm(
                    binding.FormTemplateId, formSnapshot.FormTemplatePublicId, formSnapshot.SchemaJson));
            }

            return Result.Ok(ExecuteBusinessProcessTransitionOutcome.RequireForms(required));
        }

        // ── Sign-gate check (BEFORE form-snapshot resolve and BEFORE BeginAsync) ──────────────
        // Mirrors the form-gate pattern: resolve the gate state on a pre-tx read, short-circuit with
        // RequiresMoreSignatures when incomplete, only proceed when satisfied. The gate is always
        // re-derived server-side from the stored session — the caller cannot self-attest completion.
        if (request.SignGateConfig is { } signGate)
        {
            // Open (or idempotently retrieve) the signing session for this BP transition step.
            // The session records the ordered signer list and tracks per-signer slot state.
            var openOutcome = await signatureSessionClient.OpenSessionAsync(
                request.TenantId,
                request.WorkflowInstanceId,
                signGate.StepKey,
                signGate.ArtifactRef,
                signGate.OrderedSigners,
                signGate.Threshold,
                cancellationToken);

            if (!openOutcome.IsSuccess)
                return Result.Fail(new ValidationError(
                    $"{openOutcome.ErrorCode}: {openOutcome.ErrorMessage}"));

            // Re-derive gate state from stored signer records — never trust the caller's claim.
            var gateState = await signatureSessionClient.GetGateStateAsync(
                openOutcome.SessionId,
                request.TenantId,
                cancellationToken);

            if (!gateState.IsSuccess)
                return Result.Fail(new ValidationError(
                    $"{gateState.ErrorCode}: {gateState.ErrorMessage}"));

            if (!gateState.GateSatisfied)
            {
                // Gate incomplete — surface the next expected signer. The FE drives one signing
                // ceremony for that signer via the existing /api/signing/request endpoint and
                // re-triggers this transition once the signing request reaches Completed.
                logger.LogInformation(
                    "BP sign-gate not satisfied: workflowInstanceId={WorkflowInstanceId} stepKey={StepKey} nextSigner={NextSigner}",
                    request.WorkflowInstanceId, signGate.StepKey, gateState.NextExpectedSigner);

                return Result.Ok(ExecuteBusinessProcessTransitionOutcome.RequiresMoreSignatures(
                    openOutcome.SessionId,
                    gateState.NextExpectedSigner ?? Guid.Empty,
                    gateState.SignerSlots));
            }

            logger.LogInformation(
                "BP sign-gate satisfied: workflowInstanceId={WorkflowInstanceId} stepKey={StepKey}",
                request.WorkflowInstanceId, signGate.StepKey);
        }
        // ────────────────────────────────────────────────────────────────────────────────────────

        // Resolve form-template snapshot before opening the transaction — it is a read and
        // its null result (404) must short-circuit before any writes are staged.
        // Externally identical: callers still receive 404 when the snapshot is missing.
        FormTemplateVersionSchemaDto? snapshot = null;
        BpTransitionFormBinding? matchingBinding = null;
        if (request.TransitionFormData is not null && request.TransitionFormTemplateId.HasValue)
        {
            matchingBinding = transitionForms.FirstOrDefault(f => f.FormTemplateId == request.TransitionFormTemplateId.Value);
            if (matchingBinding is null)
                return Result.Fail(new ValidationError(
                    $"{BusinessProcessErrorCodes.DuplicateBinding}: FormTemplate không thuộc transition này."));

            snapshot = await formTemplateClient.GetVersionSchemaByInternalIdAsync(
                matchingBinding.FormTemplateId,
                matchingBinding.FormTemplateVersion,
                request.TenantId,
                cancellationToken);
            if (snapshot is null)
                return Result.Fail(new NotFoundError(
                    $"{BusinessProcessErrorCodes.NotFound}: FormTemplate snapshot v{matchingBinding.FormTemplateVersion} không tìm thấy."));
        }

        // One shared local SQL transaction for all cross-module writes — FormsDbContext and
        // WorkflowDbContext both enlist onto the same connection at their first I/O, ensuring
        // atomicity without a second physical connection that would promote to DTC on Linux.
        await uow.BeginAsync(cancellationToken);
        Guid? formSubmissionId = null;
        ExecuteTransitionOutcome execOutcome;
        try
        {
            if (matchingBinding is not null)
            {
                var outcome = await formSubmissionClient.CreateForBpRunAsync(
                    request.WorkflowInstanceId,
                    lookup.FromStateId,
                    snapshot!.FormTemplatePublicId,
                    request.ActorId,
                    request.TransitionFormData!,
                    request.ConsentGiven,
                    request.TenantId,
                    request.Signature,
                    cancellationToken);

                if (!outcome.IsSuccess)
                {
                    await uow.RollbackAsync(cancellationToken);
                    return outcome.ToFailureResult<ExecuteBusinessProcessTransitionOutcome>(
                        "BusinessProcess Run không tìm thấy.");
                }

                formSubmissionId = outcome.SubmissionId;
            }

            execOutcome = await workflowClient.ExecuteTransitionAsync(
                new ExecuteTransitionRequest(
                    request.WorkflowInstanceId,
                    request.ActionName,
                    request.ActorId,
                    request.TenantId,
                    request.Comment,
                    NextHandlerUserId: request.NextHandlerUserId), // Slice 10a: thread override (DDTO-DEFAULT)
                cancellationToken);

            if (!execOutcome.IsSuccess || execOutcome.Result is null)
            {
                await uow.RollbackAsync(cancellationToken);
                return Result.Fail(new ValidationError(
                    $"{execOutcome.ErrorCode ?? "GOV_WFL_UNKNOWN"}: {execOutcome.ErrorMessage ?? "Transition failed."}"));
            }

            await uow.CommitAsync(cancellationToken);
        }
        catch
        {
            await uow.RollbackAsync(cancellationToken);
            throw;
        }

        // Post-tx read: state-form lookup for the response does not need to be in the transaction.
        var stateForms = await runtimeQueries.GetStateFormTemplateIdsAsync(
            request.BpPublicId, execOutcome.Result.NewStateInternalId, request.TenantId, cancellationToken);

        logger.LogInformation(
            "BP transition executed: workflowInstanceId={WorkflowInstanceId}, actionName={ActionName}, newStateInternalId={NewStateInternalId}, transitionFormSubmissionId={FormSubmissionId}",
            request.WorkflowInstanceId, request.ActionName, execOutcome.Result.NewStateInternalId, formSubmissionId);

        return Result.Ok(ExecuteBusinessProcessTransitionOutcome.Succeeded(
            execOutcome.Result.NewStateName,
            execOutcome.Result.NewStateInternalId,
            execOutcome.Result.IsFinal,
            stateForms.Select(f => f.FormTemplateId).ToList(),
            formSubmissionId));
    }
}
