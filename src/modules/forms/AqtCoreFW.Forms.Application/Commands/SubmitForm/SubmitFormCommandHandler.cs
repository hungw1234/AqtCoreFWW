using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Application.Telemetry;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Events;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Contracts.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace AqtCoreFW.Forms.Application.Commands.SubmitForm;

public sealed class SubmitFormCommandHandler(
    IGuidGenerator guidGenerator,
    IFormTemplateRepository templateRepository,
    IFormSubmissionRepository submissionRepository,
    IFormValidationService validationService,
    IFormDataStorageFactory storageFactory,
    IFormulaEvaluationService formulaEvaluationService,
    ITableFieldDataRepository tableFieldDataRepository,
    IConsentModuleClient consentClient,
    IWorkflowModuleClient workflowClient,
    IPublisher publisher,
    ICrossModuleUnitOfWork uow,
    IOutboxWriter outboxWriter,
    ILogger<SubmitFormCommandHandler> logger,
    IIdentitySigningClient signingClient)
    : IRequestHandler<SubmitFormCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        SubmitFormCommand request,
        CancellationToken cancellationToken)
    {
        // Load template with fields via public Guid AK (T2 external addressing)
        var template = await templateRepository.GetByPublicIdWithFieldsAsync(
            request.FormTemplatePublicId, request.TenantId, cancellationToken);

        if (template is null || template.Status != FormTemplateStatus.Active)
            return Result.Fail(FormErrors.TemplateNotFound(request.FormTemplatePublicId));

        var activeFields = template.Fields.Where(f => f.IsActive).ToList();

        // PDPL consent validation
        var hasPiiFields = activeFields.Any(f => f.IsPii && f.IsActive);
        if (template.RequiresConsent && hasPiiFields && !request.ConsentGiven)
            return Result.Fail(new ValidationError(
                "GOV_FRM_020: Yêu cầu đồng ý xử lý dữ liệu cá nhân trước khi nộp biểu mẫu.", "ConsentGiven"));

        // Validate data against active FormField entities. TenantId is required by the M1 slice 6b
        // in-row cascade membership guard (server-side re-resolve of Enum/Internal child option-sets).
        var validationResult = await validationService.ValidateAsync(
            activeFields, request.Data, request.TenantId, cancellationToken);

        if (validationResult.IsFailed)
            return Result.Fail(validationResult.Errors);

        // UX-baseline telemetry: count this as a completed submit attempt (success OR validation
        // rejection) and record the validation-error count, BEFORE the rejection return below so a
        // rejected submit still contributes to the errors-per-submit distribution. Metric emission is
        // a side-effect only — it never alters the submit outcome. No `tenant` label (one tenant per
        // deployment; per-tenant label = unbounded cardinality + attribution leak). The histogram
        // carries only bounded labels (bucket × user-defined code would blow up series count).
        var submitTags = new TagList
        {
            { "template_code", template.Code },
            { "is_public", false },
            { "storage_mode", template.StorageMode.ToString() },
        };
        var errorHistTags = new TagList
        {
            { "is_public", false },
            { "storage_mode", template.StorageMode.ToString() },
        };
        FormMetrics.FormSubmitted.Add(1, submitTags);
        FormMetrics.ValidationErrorsPerSubmit.Record(validationResult.Value.Count, errorHistTags);

        if (validationResult.Value.Count > 0)
            return Result.Fail(validationResult.Value);

        // --- Digital signature verify-at-ingest (QĐ-742 integrity) ---
        // Shared FormSignatureIngest hard-blocks a missing-required signature (GOV_FRM_021) and
        // verifies a supplied bundle BEFORE any persistence (GOV_FRM_022 on failure). SignerUserId =
        // the authenticated submitter, binding the signing cert to them (anti-impersonation).
        var verifyResult = await FormSignatureIngest.VerifyAtIngestAsync(
            signingClient, template, activeFields, request.Data, request.Signature,
            tenantId: request.TenantId, signerUserId: request.SubmittedBy, cancellationToken);

        if (verifyResult.IsFailed)
            return Result.Fail(verifyResult.Errors);

        var vr = verifyResult.Value;

        // PDPL consent is recorded INSIDE the atomic span below (not here) — see the span comment.

        // Preprocess complex types — flatten multi-column fields, strip UI-only, evaluate formulas
        var storageData = await FormDataPreprocessor.PreprocessDataAsync(
            activeFields, request.Data, formulaEvaluationService, cancellationToken);

        // Json mode: serialize data into SubmissionData; Table mode: null (data goes to dynamic table)
        string? submissionDataJson = template.StorageMode == StorageMode.Json
            ? JsonSerializer.Serialize(storageData)
            : null;

        // FormSubmission is T2: IGuidGenerator injects v4 PublicId (PDPL: prevents timestamp-leak).
        // BP-Run path: WorkflowInstanceId pre-set by BP module (P2). R14 cross-tenant guard
        // is enforced at the InProcessFormSubmissionRunClient boundary BEFORE this handler runs;
        // direct mediator.Send callers passing WorkflowInstanceId must guarantee tenant match.
        var submission = request.WorkflowInstanceId.HasValue
            ? FormSubmission.CreateForBpRun(
                guidGenerator,
                request.TenantId,
                request.WorkflowInstanceId.Value,
                request.WorkflowStateId,
                template.Id,
                template.Version,
                submissionDataJson,
                request.SubmittedBy,
                consentGiven: request.ConsentGiven)
            : FormSubmission.Create(
                guidGenerator,
                request.TenantId,
                template.Id,
                template.Version,
                submissionDataJson,
                request.SubmittedBy,
                consentGiven: request.ConsentGiven);

        // Record verified signature evidence onto the entity before first persist (multi-signer →
        // per-role child collection; single-signer → scalar block). signerUserId = the authenticated
        // submitter (Guid.Empty normalized to null inside the helper).
        if (vr is not null)
            FormSignatureIngest.RecordEvidence(submission, template, vr, request.SubmittedBy);

        // Atomic write span (C2 fix): submission + child rows + the outbox row all commit or roll
        // back together — a Table-mode child-row failure must NOT leave an orphan FormSubmission
        // (the previous per-call SaveChanges made that possible) nor a stray outbox event for a
        // submission that never actually persisted. Reuses the same CrossModuleUnitOfWork as
        // StartBusinessProcessRunCommandHandler for the span itself, but NOT its shape end-to-end:
        // that handler's span is the LAST write (nothing touches its enlisted contexts after
        // CommitAsync), whereas here the workflow-instance-linkage block below deliberately runs
        // a POST-commit EF write (submission.LinkWorkflow + submissionRepository.UpdateAsync) on
        // the SAME enlisted FormsDbContext — non-fatal fire-and-log semantics (an orphan submission
        // with a pending workflow link is a deliberately recoverable state, not a rollback trigger).
        // Any future post-commit write on an enlisted context relies on CrossModuleUnitOfWork
        // resetting that context's transaction binding in Commit/RollbackAsync (H1 fix) — do not
        // assume Commit is always the terminal write on the enlisted contexts.
        //
        // Nesting-aware ownership: StartBusinessProcessRunCommandHandler (BP-Run-with-intake-form)
        // opens the SAME scoped ICrossModuleUnitOfWork BEFORE calling into this handler via
        // InProcessFormSubmissionRunClient → mediator.Send. Only the handler that opened the UoW
        // may Commit/Rollback it — a nested Begin on an already-active UoW throws. When nested
        // (ownsUow == false), the writes below simply ride the OUTER transaction and the OUTER
        // handler commits/rolls back everything atomically (submission + workflow instance + the
        // outbox event together — the correct semantics for a BP-Run intake-form submission).
        var ownsUow = !uow.IsActive;
        if (ownsUow) await uow.BeginAsync(cancellationToken);
        try
        {
            // PDPL consent — recorded INSIDE the atomic span (FORMS-CONSENT-PRE-SPAN-ORPHAN fix):
            // ConsentRepository enlists IdentityDbContext onto this UoW, so the consent row commits
            // or rolls back WITH the submission below. Previously recorded pre-span with its own
            // SaveChanges commit, which orphaned it whenever the submit span rolled back. Runs before
            // AddAsync so LinkConsent sets the FK before the parent row is inserted.
            if (request.ConsentGiven && hasPiiFields)
            {
                var evidence = JsonSerializer.Serialize(new
                {
                    formTemplateId = template.Id,
                    formVersion = template.Version,
                    submittedBy = request.SubmittedBy,
                    submittedAt = DateTimeOffset.UtcNow
                });

                var consentRecordId = await consentClient.RecordConsentAsync(
                    new RecordConsentRequest(
                        request.SubmittedBy, request.TenantId, "form_submission",
                        "Consent", "officer", evidence),
                    cancellationToken);

                submission.LinkConsent(consentRecordId);
            }

            await submissionRepository.AddAsync(submission, cancellationToken);

            // Write typed data to the appropriate storage backend
            var storage = storageFactory.Create(template.StorageMode);

            // For Table mode: strip TableField entries from parent row before inserting —
            // child rows are written separately to child tables.
            var parentData = template.StorageMode == StorageMode.Table
                ? FormDataPreprocessor.ExcludeTableFieldEntries(activeFields, storageData)
                : storageData;

            // submissionId = PublicId (Guid, T2 external AK) for storage key
            await storage.WriteAsync(
                template.Id, submission.PublicId, request.TenantId,
                template.Version, parentData, cancellationToken);

            // Phase 3: write child rows for each TableField (Table mode only)
            if (template.StorageMode == StorageMode.Table)
            {
                var tableFields = activeFields.Where(f => f.Type == FormFieldType.TableField).ToList();
                foreach (var tableField in tableFields)
                {
                    if (!storageData.TryGetValue(tableField.FieldKey, out var rawRows) || rawRows is null)
                        continue;

                    var rows = FormDataPreprocessor.ParseTableFieldRows(rawRows);
                    if (rows.Count == 0) continue;

                    await tableFieldDataRepository.InsertRowsAsync(
                        template.Id, tableField.Id, submission.PublicId, rows, cancellationToken);
                }
            }

            // Cross-module integration event — enqueued into the Outbox in the SAME transaction as
            // the submission write (Slice 1B-1). Phase-07: FormTemplate retiered T1→T2 —
            // template.PublicId now available as Guid AK. Replaces the former direct
            // messageBus.PublishAsync (at-most-once, lost on broker outage); the outbox dispatcher
            // (Slice 1A) now delivers at-least-once, and a failed submission never emits it at all.
            await outboxWriter.EnqueueAsync(
                new FormSubmissionAcknowledgedIntegrationEvent(
                    submission.PublicId,
                    template.PublicId,
                    request.TenantId,
                    request.SubmittedBy,
                    guidGenerator.NewIdMonotonic(),
                    DateTimeOffset.UtcNow),
                "forms",
                cancellationToken);

            if (ownsUow) await uow.CommitAsync(cancellationToken);
        }
        catch
        {
            if (ownsUow) await uow.RollbackAsync(cancellationToken);
            throw;
        }

        // Create workflow instance if template has approval workflow — non-fatal on failure.
        // BP-Run path (P2): skip — instance is pre-created by BP module and already linked above.
        if (template.ApprovalWorkflowDefinitionId.HasValue && !request.WorkflowInstanceId.HasValue)
        {
            try
            {
                var wfResult = await workflowClient.CreateInstanceAsync(
                    new CreateWorkflowInstanceRequest(
                        template.ApprovalWorkflowDefinitionId.Value,
                        "FormSubmission",
                        submission.PublicId,
                        request.TenantId,
                        request.SubmittedBy),
                    cancellationToken);
                // Empty pool at initial state (WF_NO_HANDLER) → IsSuccess=false. Submission
                // already saved; log + leave WorkflowInstanceId null (manual-action path preserved)
                // + emit FormSubmissionWorkflowLinkPendingEvent so a downstream handler surfaces
                // the orphan to ops/admin.
                if (!wfResult.IsSuccess)
                {
                    logger.LogWarning(
                        "Workflow instance creation rejected for submission {SubmissionId} (definition {DefinitionId}): {Code} {Message}",
                        submission.PublicId, template.ApprovalWorkflowDefinitionId.Value,
                        wfResult.ErrorCode, wfResult.ErrorMessage);

                    await publisher.Publish(
                        new FormSubmissionWorkflowLinkPendingEvent(
                            submission.PublicId,
                            template.Id,
                            template.ApprovalWorkflowDefinitionId.Value,
                            request.TenantId,
                            request.SubmittedBy,
                            wfResult.ErrorCode ?? "UNKNOWN",
                            wfResult.ErrorMessage,
                            guidGenerator.NewIdRandom()),
                        cancellationToken);
                }
                else
                {
                    submission.LinkWorkflow(wfResult.InstanceId);
                    await submissionRepository.UpdateAsync(submission, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to create workflow instance for submission {SubmissionId} (definition {DefinitionId}). Submission saved, workflow pending manual action.",
                    submission.PublicId, template.ApprovalWorkflowDefinitionId.Value);

                // A hard throw (broker/network/timeout, or a failed link-write after a created
                // instance) orphans the submission exactly like a rejection — surface it through
                // the SAME pending-link signal so throw-orphans and rejection-orphans are visible
                // to ops/admin uniformly. The handler is idempotent + fail-soft, so emitting here
                // cannot disturb the already-committed submit response.
                await publisher.Publish(
                    new FormSubmissionWorkflowLinkPendingEvent(
                        submission.PublicId,
                        template.Id,
                        template.ApprovalWorkflowDefinitionId.Value,
                        request.TenantId,
                        request.SubmittedBy,
                        FormSubmissionWorkflowLinkPendingEvent.ExceptionFailureCode,
                        ex.Message,
                        guidGenerator.NewIdRandom()),
                    cancellationToken);
            }
        }

        await publisher.Publish(
            new FormSubmittedEvent(
                submission.PublicId,
                template.Id,
                request.TenantId,
                request.SubmittedBy,
                guidGenerator.NewIdRandom()),
            cancellationToken);

        // Return T2 PublicId (Guid) as external submission identifier
        return Result.Ok(submission.PublicId);
    }
}
