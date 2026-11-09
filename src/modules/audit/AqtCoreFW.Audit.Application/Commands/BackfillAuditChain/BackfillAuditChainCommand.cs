using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.BackfillAuditChain;

/// <summary>
/// Admin-triggered command to enqueue HMAC chain backfill for every tenant's audit log.
/// The handler enqueues a Hangfire background job and returns the job id immediately (202 pattern)
/// so the HTTP request does not block while the backfill runs on large prod tables.
///
/// The background job re-signs ALL rows in InternalId order per tenant, under the same per-tenant
/// applock as the live signer, and writes one audit trail entry with the captured triggering actor
/// for compliance traceability (QĐ 742 §3.1).
///
/// Returns the Hangfire job id so the caller can track execution status via the Hangfire dashboard.
///
/// This command must only be dispatched by callers that have verified SystemAdmin authority.
/// The HTTP endpoint enforces this via [Authorize(Policy = PermCodes.System.Config.Manage)].
///
/// NOTE: Intentionally does NOT implement IBaseCommand. AuditBehavior would auto-write a
/// "chain-backfill" entry for the command dispatch itself; the background job already writes the
/// single canonical audit trail entry after backfill completes. Two entries for one operation
/// would produce duplicate compliance evidence — the handler skips auto-audit by design.
/// </summary>
public sealed record BackfillAuditChainCommand : IRequest<Result<string>>;
