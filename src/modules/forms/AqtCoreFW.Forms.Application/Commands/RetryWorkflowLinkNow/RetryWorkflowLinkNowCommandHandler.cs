using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.RetryWorkflowLinkNow;

/// <summary>
/// Handler for the Phase-05 admin Retry-now override. Branches on the row's current
/// <see cref="WorkflowLinkPendingStatus"/> (LOCKED Q4):
/// <list type="bullet">
///   <item><description>Shelved → <c>ReactivateForManualRetry</c> (un-shelve, manual-path only).</description></item>
///   <item><description>Pending/Retrying → <c>ArmForImmediateRetry</c> (re-arm NextRetryAt=now,
///     does NOT increment AttemptCount — a manual nudge is not a failed attempt).</description></item>
///   <item><description>Succeeded → conflict (already resolved, no-op).</description></item>
/// </list>
/// The API never creates the workflow instance itself — the Hangfire retry job (Phase 04) is the
/// single create path (DRY); this command only re-arms the row for the job's next tick.
/// </summary>
public sealed class RetryWorkflowLinkNowCommandHandler(
    IFormSubmissionWorkflowLinkPendingAttemptRepository repository)
    : IRequestHandler<RetryWorkflowLinkNowCommand, Result>
{
    public async Task<Result> Handle(
        RetryWorkflowLinkNowCommand request,
        CancellationToken cancellationToken)
    {
        var row = await repository.GetTrackedBySubmissionIdAsync(
            request.SubmissionId, request.TenantId, cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError(
                $"Không tìm thấy bản ghi chờ kết nối quy trình cho submission '{request.SubmissionId}'."));

        switch (row.Status)
        {
            case WorkflowLinkPendingStatus.Shelved:
                row.ReactivateForManualRetry(request.ActorId);
                break;

            case WorkflowLinkPendingStatus.Pending:
            case WorkflowLinkPendingStatus.Retrying:
                row.ArmForImmediateRetry(request.ActorId);
                break;

            case WorkflowLinkPendingStatus.Succeeded:
                return Result.Fail(new ConflictError(
                    "Kết nối quy trình đã được xử lý thành công, không thể yêu cầu thử lại."));

            default:
                return Result.Fail(new ConflictError(
                    $"Không thể yêu cầu thử lại ngay từ trạng thái {row.Status}."));
        }

        await repository.UpdateAsync(row, cancellationToken);
        return Result.Ok();
    }
}
