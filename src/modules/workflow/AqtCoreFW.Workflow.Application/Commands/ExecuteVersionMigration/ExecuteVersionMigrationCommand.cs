using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ExecuteVersionMigration;

/// <summary>
/// Triggers Hangfire execution of an Approved migration.
/// Re-verifies MappingHash, sets Status=InProgress, enqueues batch job.
/// Returns MigrationDto + HangfireJobId.
/// Permission: WORKFLOW.MIGRATION.EXECUTE
/// </summary>
public sealed record ExecuteVersionMigrationCommand(
    long MigrationId,
    Guid TenantId) : IRequest<Result<MigrationDto>>;
