using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetMigrationStatus;

/// <summary>
/// Returns a single migration's status including live HangfireJobState
/// via IHangfireJobStateProvider. Handles null HangfireJobId (not yet executed).
/// Requires MIGRATION.READ permission.
/// </summary>
/// <summary>Phase-4b B4c: MigrationId is long (T1 WorkflowVersionMigration.Id bigint PK).</summary>
public sealed record GetMigrationStatusQuery(long MigrationId)
    : IRequest<Result<MigrationDto>>;
