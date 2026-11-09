using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetMigrationItems;

/// <summary>
/// Paged query handler for per-instance migration results.
/// Delegates to IWorkflowVersionMigrationRepository.ListItemsAsync (EF paged query).
/// Maps domain entities to DTOs — no Dapper needed (EF for write-side items).
/// </summary>
public sealed class GetMigrationItemsQueryHandler(
    IWorkflowVersionMigrationRepository repository)
    : IRequestHandler<GetMigrationItemsQuery, Result<PagedMigrationItemsDto>>
{
    public async Task<Result<PagedMigrationItemsDto>> Handle(
        GetMigrationItemsQuery request,
        CancellationToken cancellationToken)
    {
        // Cap page size at 200 per spec; Math.Clamp also enforces lower bound (rejects PageSize<=0).
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var page = Math.Max(request.Page, 1);

        var (items, totalCount) = await repository.ListItemsAsync(
            request.MigrationId,
            request.StatusFilter,
            page,
            pageSize,
            cancellationToken);

        var dtos = items.Select(i => new MigrationItemDto
        {
            Id = i.MessageId,       // T3 AK: MessageId (Guid) is the public identity
            InstanceId = i.InstanceId,   // long (FK to T2 WorkflowInstance.Id internal)
            OldStateId = i.OldStateId,   // long (T1 WorkflowState.Id)
            NewStateId = i.NewStateId,   // long (T1 WorkflowState.Id)
            Status = i.Status,
            ErrorMessage = i.ErrorMessage,
            ProcessedAt = i.ProcessedAt
        }).ToList();

        return Result.Ok(new PagedMigrationItemsDto
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }
}

/// <summary>FluentValidation validator — caps PageSize at 200.</summary>
public sealed class GetMigrationItemsQueryValidator
    : FluentValidation.AbstractValidator<GetMigrationItemsQuery>
{
    public GetMigrationItemsQueryValidator()
    {
        RuleFor(q => q.MigrationId).NotEmpty();
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 200);
    }
}
