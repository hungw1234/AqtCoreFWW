using AqtCoreFW.Workflow.Application.Commands.CreateVersionMigration;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetMigrationsByDefinition;

/// <summary>
/// Returns all migrations for a definition key (source or target side), tenant-filtered.
/// Uses EF repository ListByDefinitionKeyAsync — ordered by CreatedAt DESC.
/// </summary>
public sealed class GetMigrationsByDefinitionQueryHandler(
    IWorkflowVersionMigrationRepository repository)
    : IRequestHandler<GetMigrationsByDefinitionQuery, Result<IReadOnlyList<MigrationDto>>>
{
    public async Task<Result<IReadOnlyList<MigrationDto>>> Handle(
        GetMigrationsByDefinitionQuery request,
        CancellationToken cancellationToken)
    {
        var migrations = await repository.ListByDefinitionKeyAsync(
            request.DefinitionKey,
            request.TenantId,
            cancellationToken);

        var dtos = migrations
            .Select(CreateVersionMigrationCommandHandler.MapToDto)
            .ToList();

        return Result.Ok<IReadOnlyList<MigrationDto>>(dtos);
    }
}
