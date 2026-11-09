using AqtCoreFW.Audit.Application.Commands.BackfillAuditChain;
using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.SharedKernel.Domain;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Audit.Infrastructure.Commands.BackfillAuditChain;

/// <summary>
/// Enqueues the admin-triggered HMAC chain backfill as a Hangfire background job.
/// Returns the Hangfire job id so the HTTP endpoint can respond 202 Accepted immediately —
/// the backfill runs asynchronously on the audit lane, avoiding 504 on large prod tables.
///
/// Actor identity (who triggered the backfill) is captured HERE from the HTTP-bound ICurrentUser
/// and passed as explicit job arguments. The Hangfire job runs without an HTTP context, so
/// ICurrentUser would be empty there. The job writes the audit trail entry using the captured
/// actor once backfill completes.
///
/// Handler lives in Infrastructure because IBackgroundAuditJobEnqueuer implementation
/// (HangfireAuditJobEnqueuer) is registered there. Infrastructure already pulls SharedKernel.
/// </summary>
public sealed class BackfillAuditChainCommandHandler(
    IBackgroundAuditJobEnqueuer jobEnqueuer,
    ICurrentUser currentUser,
    IHttpContextAccessor httpContextAccessor,
    ILogger<BackfillAuditChainCommandHandler> logger)
    : IRequestHandler<BackfillAuditChainCommand, Result<string>>
{
    public Task<Result<string>> Handle(
        BackfillAuditChainCommand request,
        CancellationToken cancellationToken)
    {
        // Capture actor identity AND request origin before enqueue — both ICurrentUser and the
        // HttpContext are bound to the HTTP request and will be unavailable in the background job.
        // IP + correlation id become tamper-evidence on the backfill audit entry (same capture
        // pattern as AuditService for the normal audit path).
        var triggeredByUserId   = currentUser.UserId;
        var triggeredByTenantId = currentUser.TenantId;
        var triggeredByUserName = currentUser.UserName;

        var ctx                 = httpContextAccessor.HttpContext;
        // RemoteIpAddress is the immediate socket peer — behind a reverse proxy/load balancer this is
        // the proxy IP unless ForwardedHeaders middleware rewrites it. Treat the IP as corroborating
        // evidence; the authenticated UserId remains the authoritative forensic actor.
        var triggeredByIp       = ctx?.Connection.RemoteIpAddress?.ToString();
        var correlationId       = ctx?.TraceIdentifier;

        logger.LogInformation(
            "HMAC chain backfill enqueued by user {UserId}", triggeredByUserId);

        var jobId = jobEnqueuer.EnqueueBackfillChain(
            triggeredByUserId, triggeredByTenantId, triggeredByUserName,
            triggeredByIp, correlationId);

        return Task.FromResult(Result.Ok(jobId));
    }
}
