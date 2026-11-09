using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetVersionHistory;

/// <summary>
/// Returns all versions of a workflow definition grouped by DefinitionKey,
/// with active instance count per version.
/// Requires MIGRATION.READ permission.
/// </summary>
public sealed record GetVersionHistoryQuery(
    string DefinitionKey,
    Guid TenantId) : IRequest<Result<IReadOnlyList<VersionHistoryDto>>>;
