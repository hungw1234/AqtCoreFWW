using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CancelVersionMigration;

/// <summary>
/// Cancels a migration.
/// PendingApproval | Approved → immediate MarkCancelled().
/// InProgress → RequestCancel() sets CancelRequested=true; Hangfire worker polls between batches.
/// Permission: WORKFLOW.MIGRATION.CANCEL
/// </summary>
public sealed record CancelVersionMigrationCommand(
    long MigrationId,
    Guid TenantId) : IRequest<Result<MigrationDto>>;
