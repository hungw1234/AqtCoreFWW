using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetMigrationsByDefinition;

/// <summary>
/// Returns all migrations (source or target) for a definition key within a tenant.
/// Requires MIGRATION.READ permission.
/// </summary>
public sealed record GetMigrationsByDefinitionQuery(
    string DefinitionKey,
    Guid TenantId) : IRequest<Result<IReadOnlyList<MigrationDto>>>;
