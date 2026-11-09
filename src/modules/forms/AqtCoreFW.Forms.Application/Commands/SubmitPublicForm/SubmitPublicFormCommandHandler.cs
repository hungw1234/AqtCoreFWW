using AqtCoreFW.Forms.Application.Commands.SubmitForm;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Application.Telemetry;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Events;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Contracts.Events;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace AqtCoreFW.Forms.Application.Commands.SubmitPublicForm;

/// <summary>
/// Handles anonymous public form submissions.
/// Includes PDPL consent validation and optional workflow instance creation.
/// </summary>
public sealed class SubmitPublicFormCommandHandler(
    IGuidGenerator guidGenerator,
    IReadDbConnection db,
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
    ILogger<SubmitPublicFormCommandHandler> logger,
    IIdentitySigningClient signingClient)
    : IRequestHandler<SubmitPublicFormCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        SubmitPublicFormCommand request,
        CancellationToken cancellationToken)
    {
        // Resolve template Id + TenantId + PublicId by code (Dapper — no EF overhead for a single-column lookup).
        // Anonymous caller has no ambient tenant → resolve on a public_forms_bypass connection so the
        // FormTemplates RLS FILTER carve-out admits the IsPublic=1 row under a non-db_owner login.
        const string idSql = """
            SELECT Id, TenantId, PublicId FROM forms.FormTemplates
            WHERE Code = @Code AND Status = 'Active' AND IsDeleted = 0 AND IsPublic = 1
            """;

        var row = await db.QueryFirstOrDefaultWithPublicFormsBypassAsync<TemplateIdRow>(
            idSql, new { Code = request.TemplateCode }, cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError($"Form '{request.TemplateCode}' không tìm thấy hoặc chưa active."));

        // Load with fields for validation (templateRepository uses long key, T1). GetPublicWithFieldsAsync
        // pins SESSION_CONTEXT to the resolved tenant for the EF read (this runs before the write-span
        // BeginAsync pin below, so the anonymous connection is otherwise tenant-less under least-priv).
        var template = await templateRepository.GetPublicWithFieldsAsync(row.Id, row.TenantId, cancellationToken);
        if (template is null)
            return Result.Fail(new NotFoundError($"Form '{request.TemplateCode}' không tìm thấy."));

        var activeFields = template.Fields.Where(f => f.IsActive).ToList();

        // PDPL consent validation
        var hasPiiFields = activeFields.Any(f => f.IsPii && f.IsActive);
        if (template.RequiresConsent && hasPiiFields && !request.ConsentGiven)
            return Result.Fail(new ValidationError(
                "GOV_FRM_020: Yêu cầu đồng ý xử lý dữ liệu cá nhân trước khi nộp biểu mẫu.", "ConsentGiven"));

        // Validate submission data. Pass the RLS-resolved TenantId (public caller has no ambient tenant)
        // so the M1 slice 6b in-row cascade membership guard can re-resolve child option-sets server-side.
        var validationResult = await validationService.ValidateAsync(
            activeFields, request.Data, row.TenantId, cancellationToken);

        if (validationResult.IsFailed)
            return Result.Fail(validationResult.Errors);

        // UX-baseline telemetry (public path): same contract as the authenticated handler — count the
        // attempt and record the validation-error count before the rejection return. Side-effect only.
        // No `tenant` label (see FormMetrics); histogram uses bounded labels only.
        var publicSubmitTags = new TagList
        {
            { "template_code", template.Code },
            { "is_public", true },
            { "storage_mode", template.StorageMode.ToString() },
        };
        var publicErrorHistTags = new TagList
        {
            { "is_public", true },
            { "storage_mode", template.StorageMode.ToString() },
        };
        FormMetrics.FormSubmitted.Add(1, publicSubmitTags);
        FormMetrics.ValidationErrorsPerSubmit.Record(validationResult.Value.Count, publicErrorHistTags);

        if (validationResult.Value.Count > 0)
            return Result.Fail(validationResult.Value);

        // --- Digital signature verify-at-ingest (QĐ-742 integrity) ---
        // Shared FormSignatureIngest hard-blocks a missing-required signature (GOV_FRM_021) and
        // verifies a supplied bundle BEFORE any persistence (GOV_FRM_022 on failure). TenantId comes
        // from the server-resolved template row, never the client bundle; signerUserId is null —
        // the anonymous public path has no authenticated submitter to bind against.
        var verifyResult = await FormSignatureIngest.VerifyAtIngestAsync(
            signingClient, template, activeFields, request.Data, request.Signature,
            tenantId: row.TenantId, signerUserId: null, cancellationToken);

        if (verifyResult.IsFailed)
            return Result.Fail(verifyResult.Errors);

        var vr = verifyResult.Value;

        // PDPL consent is recorded INSIDE the atomic span below (not here) — see the span comment.

        // Preprocess: flatten multi-column fields, strip UI-only, evaluate formulas
        var storageData = await FormDataPreprocessor.PreprocessDataAsync(
            activeFields, request.Data, formulaEvaluationService, cancellationToken);

        // FormSubmission is T2: IGuidGenerator injects v4 PublicId (PDPL: prevents timestamp-leak).
        // SubmittedBy = Guid.Empty for anonymous guest submission.
        var submission = FormSubmission.Create(
            guidGenerator,
            row.TenantId,
            template.Id,
            template.Version,
            template.StorageMode == StorageMode.Json
                ? JsonSerializer.Serialize(storageData)
                : null,
            submittedBy: Guid.Empty,
            consentGiven: request.ConsentGiven);

        // Record verified signature evidence before first persist (multi-signer → per-role child
        // collection; single-signer → scalar block). signerUserId = null — anonymous public path.
        if (vr is not null)
            FormSignatureIngest.RecordEvidence(submission, template, vr, signerUserId: null);

        // Atomic write span (C2 fix) — mirrors SubmitFormCommandHandler: submission + Table-mode
        // parent row + the outbox row all commit or roll back together. Workflow-instance linkage
        // below stays OUTSIDE this span — its own non-fatal fire-and-log semantics are unchanged.
        //
        // Nesting-aware ownership: only the handler that opened the scoped ICrossModuleUnitOfWork
        // may Commit/Rollback it (mirrors SubmitFormCommandHandler — see its comment for the
        // BP-Run-with-intake-form nested-call scenario this guards against).
        var ownsUow = !uow.IsActive;
        // Pin SESSION_CONTEXT to the template-resolved tenant. The anonymous caller has no ambient
        // tenant (that is the whole point of the public endpoint), so BeginAsync would otherwise leave
        // SESSION_CONTEXT unset — and the RLS BLOCK PREDICATE on forms.FormSubmissions rejects the
        // INSERT under a least-privilege login. row.TenantId is server-resolved from the template row,
        // never client-supplied.
        if (ownsUow) await uow.BeginAsync(row.TenantId, cancellationToken);
        try
        {
            // PDPL consent — recorded INSIDE the atomic span (FORMS-CONSENT-PRE-SPAN-ORPHAN fix),
            // public-path parity with SubmitFormCommandHandler: ConsentRepository enlists
            // IdentityDbContext onto this UoW so the consent row commits or rolls back WITH the
            // submission below. Previously recorded pre-span with its own SaveChanges commit, which
            // orphaned it whenever the submit span rolled back. Runs before AddAsync so LinkConsent
            // sets the FK before the parent row is inserted. DataSubjectId = Guid.Empty (anonymous).
            if (request.ConsentGiven && hasPiiFields)
            {
                var evidence = JsonSerializer.Serialize(new
                {
                    formTemplateId = template.Id,
                    formVersion = template.Version,
                    submittedAt = DateTimeOffset.UtcNow,
                    ip = request.ClientIp,
                    ua = request.UserAgent
                });

                var consentRecordId = await consentClient.RecordConsentAsync(
                    new RecordConsentRequest(
                        Guid.Empty, row.TenantId, "form_submission",
                        "Consent", "citizen", evidence),
                    cancellationToken);

                submission.LinkConsent(consentRecordId);
            }

            await submissionRepository.AddAsync(submission, cancellationToken);

            // Write to table storage if applicable; submissionId = PublicId (Guid, T2 AK)
            if (template.StorageMode == StorageMode.Table)
            {
                var storage = storageFactory.Create(StorageMode.Table);
                var parentData = FormDataPreprocessor.ExcludeTableFieldEntries(activeFields, storageData);
                await storage.WriteAsync(
                    template.Id, submission.PublicId, row.TenantId,
                    template.Version, parentData, cancellationToken);

                // Write child rows for each TableField — public-path parity with SubmitFormCommandHandler.
                // Runs inside the SAME atomic span, so a child-row failure rolls back the parent row +
                // outbox event together. Previously absent: the public path persisted the parent row but
                // silently dropped all TableField child rows (PUBLIC-FORMS-TABLEFIELD-CHILDROW-DROP).
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
            // the submission write (Slice 1B-1), public path parity with SubmitFormCommandHandler.
            // Phase-07: FormTemplate retiered T1→T2 — template.PublicId now available as Guid AK.
            // SubmittedBy = Guid.Empty for anonymous public submission.
            await outboxWriter.EnqueueAsync(
                new FormSubmissionAcknowledgedIntegrationEvent(
                    submission.PublicId,
                    row.PublicId,
                    row.TenantId,
                    Guid.Empty,
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

        // Create workflow instance if template has approval workflow — non-fatal on failure
        if (template.ApprovalWorkflowDefinitionId.HasValue)
        {
            try
            {
                var wfResult = await workflowClient.CreateInstanceAsync(
                    new CreateWorkflowInstanceRequest(
                        template.ApprovalWorkflowDefinitionId.Value,
                        "FormSubmission",
                        submission.PublicId,
                        row.TenantId,
                        Guid.Empty),
                    cancellationToken);
                // Empty-pool / fail-CLOSED outcome → log + leave unlinked (manual action) +
                // emit FormSubmissionWorkflowLinkPendingEvent. Public path emits with
                // SubmittedBy=Guid.Empty (anonymous submitter — matches existing pattern).
                if (!wfResult.IsSuccess)
                {
                    logger.LogWarning(
                        "Workflow instance creation rejected for public submission {SubmissionId} (definition {DefinitionId}): {Code} {Message}",
                        submission.PublicId, template.ApprovalWorkflowDefinitionId.Value,
                        wfResult.ErrorCode, wfResult.ErrorMessage);

                    await publisher.Publish(
                        new FormSubmissionWorkflowLinkPendingEvent(
                            submission.PublicId,
                            template.Id,
                            template.ApprovalWorkflowDefinitionId.Value,
                            row.TenantId,
                            Guid.Empty,
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
                    "Failed to create workflow instance for public submission {SubmissionId} (definition {DefinitionId}). Submission saved, workflow pending manual action.",
                    submission.PublicId, template.ApprovalWorkflowDefinitionId.Value);

                // A hard throw orphans the submission exactly like a rejection — surface it
                // through the SAME pending-link signal so ops/admin see throw-orphans and
                // rejection-orphans uniformly. Handler is idempotent + fail-soft.
                await publisher.Publish(
                    new FormSubmissionWorkflowLinkPendingEvent(
                        submission.PublicId,
                        template.Id,
                        template.ApprovalWorkflowDefinitionId.Value,
                        row.TenantId,
                        Guid.Empty,
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
                row.TenantId,
                Guid.Empty,   // anonymous public submission — no known submitter
                guidGenerator.NewIdRandom()),
            cancellationToken);

        // Return T2 PublicId (Guid) as external submission identifier
        return Result.Ok(submission.PublicId);
    }

    internal sealed class TemplateIdRow
    {
        public long Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>T2 AK — used for FormSubmissionAcknowledgedIntegrationEvent after Phase-07 retier.</summary>
        public Guid PublicId { get; set; }
    }
}
