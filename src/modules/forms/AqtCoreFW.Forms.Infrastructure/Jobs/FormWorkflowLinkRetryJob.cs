using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Forms.Infrastructure.Jobs;

/// <summary>
/// Hangfire recurring job that durably retries <see cref="FormSubmissionWorkflowLinkPendingAttempt"/>
/// rows — the missing half of the orphan-workflow-link problem (Phases 02-03 write the row on
/// failure; nothing retried it until this job). Two orphan shapes land in the same table
/// (see plan.md "Problem"):
///
/// 1. Rejection orphan — <c>CreateInstanceAsync</c> returned <c>IsSuccess=false</c> (e.g.
///    <c>WF_NO_HANDLER</c>) — no instance was ever created.
/// 2. M1 throw-after-create orphan (REAL) — <c>CreateInstanceAsync</c> SUCCEEDED (the instance row
///    is already committed by Workflow's own SaveChanges) but the caller's subsequent
///    <c>submission.LinkWorkflow</c> / <c>UpdateAsync</c> threw. An active instance ALREADY EXISTS;
///    blindly re-creating would trip <c>UX_WorkflowInstances_Active_Entity</c> (duplicate active
///    instance). This job therefore ALWAYS resolves-before-creating (idempotency, step c below) —
///    never a blind re-create.
///
/// <c>[AutomaticRetry(Attempts = 0)]</c>: the 5-minute recurring cadence IS the retry mechanism.
/// A Hangfire-level retry on top would double-fire against the same claimed batch on transient
/// job-runner failure — the recurring schedule already re-visits any row still due next tick.
///
/// Per-row pinned unit of work: each claimed row is processed under its OWN
/// <see cref="ICrossModuleUnitOfWork.BeginAsync(Guid, CancellationToken)"/> pinned to that row's
/// tenant (mirrors <c>AuditEntryPersister.PersistAsync</c>) — a poison row's exception rolls back
/// ONLY that row (<see cref="FormSubmissionWorkflowLinkPendingAttempt.MarkRetrying"/> is undone with
/// it) and the loop continues; it never aborts the whole batch. A fresh DI scope is created per row
/// so each row gets its own scoped <see cref="ICrossModuleUnitOfWork"/> + enlisted DbContexts —
/// reusing one scope across rows would carry a completed/rolled-back UoW's enlistment state into
/// the next row.
///
/// Bypass guard (plan axis 4): this job references <see cref="IWorkflowModuleClient"/> ONLY — never
/// <c>IWorkflowInstanceRepository</c>, the Workflow DbContext, or <c>AddAsync</c> on a workflow
/// entity. Cross-module writes go through the sanctioned client seam (enforced by module boundary:
/// Forms.Infrastructure does not reference Workflow.Domain/Workflow.Infrastructure).
/// </summary>
[AutomaticRetry(Attempts = 0)]
public sealed class FormWorkflowLinkRetryJob(
    IServiceScopeFactory scopeFactory,
    IOptions<FormWorkflowLinkRetryOptions> options,
    ILogger<FormWorkflowLinkRetryJob> logger)
{
    /// <summary>System attribution for every mutator call this job makes — matches
    /// <see cref="FormSubmissionWorkflowLinkPendingAttempt.CreateFromEvent"/>'s system-attributed
    /// initial insert. A background job has no request principal.</summary>
    private static readonly Guid SystemActor = Guid.Empty;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        var now = DateTimeOffset.UtcNow;

        IReadOnlyList<FormSubmissionWorkflowLinkPendingAttempt> due;
        await using (var claimScope = scopeFactory.CreateAsyncScope())
        {
            var claimRepo = claimScope.ServiceProvider
                .GetRequiredService<IFormSubmissionWorkflowLinkPendingAttemptRepository>();
            due = await claimRepo.GetDueAttemptsAsync(now, opts.BatchSize, cancellationToken);
        }

        if (due.Count == 0)
            return;

        var succeeded = 0;
        var shelved = 0;
        var reArmed = 0;

        foreach (var claimed in due)
        {
            try
            {
                var outcome = await ProcessRowAsync(claimed, opts, cancellationToken);
                switch (outcome)
                {
                    case RowOutcome.Succeeded: succeeded++; break;
                    case RowOutcome.Shelved: shelved++; break;
                    case RowOutcome.ReArmed: reArmed++; break;
                }
            }
            catch (Exception ex)
            {
                // A poison row must not abort the batch — log and move on. The row's MarkRetrying
                // was rolled back with everything else in ProcessRowAsync's own catch, so it stays
                // claimable (Pending/Retrying, due) on the next tick.
                logger.LogError(ex,
                    "FormWorkflowLinkRetryJob: unhandled failure processing pending-link row {RowId} " +
                    "(submission {SubmissionId}, tenant {TenantId}) — row left claimable for next tick.",
                    claimed.Id, claimed.SubmissionId, claimed.TenantId);
            }
        }

        logger.LogInformation(
            "FormWorkflowLinkRetryJob: claimed {Claimed}, succeeded {Succeeded}, shelved {Shelved}, re-armed {ReArmed}.",
            due.Count, succeeded, shelved, reArmed);
    }

    /// <summary>
    /// Processes exactly one claimed row under its own pinned per-tenant unit of work + fresh DI
    /// scope. Returns the terminal/interim outcome for the batch-summary log. On any exception the
    /// UoW is rolled back and the exception propagates to the caller's per-row catch (batch
    /// isolation — see class doc).
    /// </summary>
    private async Task<RowOutcome> ProcessRowAsync(
        FormSubmissionWorkflowLinkPendingAttempt claimed,
        FormWorkflowLinkRetryOptions opts,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var uow = sp.GetRequiredService<ICrossModuleUnitOfWork>();
        var attemptRepo = sp.GetRequiredService<IFormSubmissionWorkflowLinkPendingAttemptRepository>();
        var submissionRepo = sp.GetRequiredService<IFormSubmissionRepository>();
        var workflowClient = sp.GetRequiredService<IWorkflowModuleClient>();

        // Pin SESSION_CONTEXT to THIS row's tenant (RLS gate for the write) — mirrors
        // AuditEntryPersister.PersistAsync. The claim READ above ran under IgnoreQueryFilters
        // (harmless, cross-tenant scan); the WRITE below is what RLS must gate per-row.
        await uow.BeginAsync(claimed.TenantId, cancellationToken);
        try
        {
            // Re-fetch the row's repository-tracked instance is unnecessary: EF Core's tracked
            // entity from the claim scope is a DIFFERENT DbContext instance than this row's fresh
            // scope, so re-attach by loading fresh within THIS scope's context to avoid cross-scope
            // tracked-entity leakage.
            var row = await attemptRepo.GetByIdAsync(claimed.Id, cancellationToken);
            if (row.IsFailed)
            {
                // Row vanished between claim and process (e.g. concurrent admin action) — nothing
                // to do; not a failure.
                await uow.RollbackAsync(cancellationToken);
                return RowOutcome.ReArmed;
            }

            var attempt = row.Value;
            attempt.MarkRetrying(SystemActor);
            await attemptRepo.UpdateAsync(attempt, cancellationToken);

            // --- M1 idempotency: resolve-before-create (NEVER blind re-create) ---
            var existingInstanceId = await workflowClient.TryResolveInstanceForEntityAsync(
                "FormSubmission", attempt.SubmissionId, attempt.TenantId, cancellationToken);

            if (existingInstanceId is Guid instanceId)
            {
                // Throw-after-create orphan: the instance already exists. Resolve the LINK only —
                // do NOT call CreateInstanceAsync (would trip UX_WorkflowInstances_Active_Entity).
                await LinkSubmissionIfUnlinkedAsync(
                    submissionRepo, attempt.SubmissionId, instanceId, cancellationToken);

                attempt.MarkSucceeded(instanceId, SystemActor);
                await attemptRepo.UpdateAsync(attempt, cancellationToken);
                await uow.CommitAsync(cancellationToken);

                logger.LogInformation(
                    "FormWorkflowLinkRetryJob: resolved existing instance {InstanceId} for submission " +
                    "{SubmissionId} (row {RowId}) — no CreateInstanceAsync call (M1 idempotency).",
                    instanceId, attempt.SubmissionId, attempt.Id);
                return RowOutcome.Succeeded;
            }

            // Never-created orphan: attempt the create.
            var result = await workflowClient.CreateInstanceAsync(
                new CreateWorkflowInstanceRequest(
                    attempt.ApprovalWorkflowDefinitionId,
                    "FormSubmission",
                    attempt.SubmissionId,
                    attempt.TenantId,
                    attempt.SubmittedBy),
                cancellationToken);

            if (result.IsSuccess)
            {
                await LinkSubmissionIfUnlinkedAsync(
                    submissionRepo, attempt.SubmissionId, result.InstanceId, cancellationToken);

                attempt.MarkSucceeded(result.InstanceId, SystemActor);
                await attemptRepo.UpdateAsync(attempt, cancellationToken);
                await uow.CommitAsync(cancellationToken);

                logger.LogInformation(
                    "FormWorkflowLinkRetryJob: created instance {InstanceId} for submission {SubmissionId} " +
                    "(row {RowId}) after {Attempts} prior attempt(s).",
                    result.InstanceId, attempt.SubmissionId, attempt.Id, attempt.AttemptCount);
                return RowOutcome.Succeeded;
            }

            // Still failing — shelve at MaxAttempts, else re-arm with exponential backoff.
            var code = result.ErrorCode ?? "UNKNOWN";
            if (attempt.AttemptCount + 1 >= opts.MaxAttempts)
            {
                attempt.MarkShelved(SystemActor);
                await attemptRepo.UpdateAsync(attempt, cancellationToken);
                await uow.CommitAsync(cancellationToken);

                logger.LogWarning(
                    "FormWorkflowLinkRetryJob: row {RowId} (submission {SubmissionId}, tenant {TenantId}) " +
                    "SHELVED after {Attempts} attempts — last failure {Code}: {Message}. Submission is " +
                    "left untouched (never cancelled); admin Retry-now can re-activate.",
                    attempt.Id, attempt.SubmissionId, attempt.TenantId,
                    attempt.AttemptCount + 1, code, result.ErrorMessage);
                return RowOutcome.Shelved;
            }

            var nextRetryAt = ComputeBackoff(now: DateTimeOffset.UtcNow, attemptCount: attempt.AttemptCount, opts);
            attempt.RecordRetryFailure(code, result.ErrorMessage, nextRetryAt, SystemActor);
            await attemptRepo.UpdateAsync(attempt, cancellationToken);
            await uow.CommitAsync(cancellationToken);
            return RowOutcome.ReArmed;
        }
        catch
        {
            await uow.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Sets the submission's workflow link only if it isn't already set — the M1 resolve path may
    /// run against a submission whose LinkWorkflow already succeeded on a PRIOR retry tick that then
    /// failed to persist the pending-attempt row's Succeeded transition (a partial-progress crash);
    /// re-linking with the same instance id is idempotent, but guarding avoids a redundant write.
    /// </summary>
    private static async Task LinkSubmissionIfUnlinkedAsync(
        IFormSubmissionRepository submissionRepo,
        Guid submissionId,
        Guid instanceId,
        CancellationToken cancellationToken)
    {
        var submissionResult = await submissionRepo.GetByIdAsync(submissionId, cancellationToken);
        if (submissionResult.IsFailed)
            return; // Submission itself vanished — nothing to link; the attempt row is still marked Succeeded.

        var submission = submissionResult.Value;
        if (submission.WorkflowInstanceId is null)
        {
            submission.LinkWorkflow(instanceId);
            await submissionRepo.UpdateAsync(submission, cancellationToken);
        }
    }

    /// <summary>Exponential backoff: BaseBackoff * 2^attemptCount, capped at MaxBackoff, added to now.</summary>
    private static DateTimeOffset ComputeBackoff(
        DateTimeOffset now, int attemptCount, FormWorkflowLinkRetryOptions opts)
    {
        var factor = Math.Pow(2, attemptCount);
        var scaled = opts.BaseBackoff * factor;
        var backoff = scaled > opts.MaxBackoff ? opts.MaxBackoff : scaled;
        return now.Add(backoff);
    }

    private enum RowOutcome { Succeeded, Shelved, ReArmed }
}
