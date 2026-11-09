using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Queries.GetWorkflowInstancesSummary;
using AqtCoreFW.Workflow.Application.Queries.ListWorkflowInstances;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Workflow.Infrastructure.QueryHandlers;

/// <summary>
/// Handles cursor-paginated list of workflow instances using EF Core keyset pagination.
/// Placed in Infrastructure (not Application) because it requires WorkflowDbContext
/// for EF Core's SQL-translatable predicates. [Org module pattern]
///
/// Keyset order: StartedAt DESC, Id DESC — matches index IX_WorkflowInstances_TenantId_StartedAt_Id_Keyset.
/// [RT-01] Uses `i.Id &lt; lastId` directly (NOT Guid.CompareTo — not SQL-translatable).
/// [RT-11] PageSize clamped server-side to [1, PaginationDefaults.AdminMax].
/// [RT-03] Cursor decode failure → BadRequestError (GOV_WORKFLOW_040) → 400.
/// </summary>
public sealed class ListWorkflowInstancesQueryHandler(WorkflowDbContext ctx)
    : IRequestHandler<ListWorkflowInstancesQuery, Result<PagedResult<WorkflowInstanceListItemDto>>>
{
    public async Task<Result<PagedResult<WorkflowInstanceListItemDto>>> Handle(
        ListWorkflowInstancesQuery request,
        CancellationToken cancellationToken)
    {
        // [RT-11] Clamp page size server-side — client cannot DoS with pageSize=10000.
        // Cursor-based pagination (no Page param) → Math.Clamp + PaginationDefaults.AdminMax
        // is the canonical idiom (mirrors Collaboration.GetMessages / SearchMessages).
        var pageSize = Math.Clamp(request.PageSize, 1, PaginationDefaults.AdminMax);

        // Decode cursor — failure returns 400 Bad Request (never 500) [RT-03]
        var hasCursor = false;
        DateTimeOffset cursorStartedAt = default;
        long cursorId = default;   // long — WorkflowInstance.Id bigint PK (SQL-translatable keyset)

        if (!string.IsNullOrEmpty(request.Cursor))
        {
            if (!CursorCodec.TryDecode(request.Cursor, out var decoded))
                return Result.Fail(new BadRequestError(
                    "The cursor token is malformed or has been tampered with.",
                    ErrorCodes.Workflow.MalformedCursor));

            hasCursor = true;
            cursorStartedAt = decoded.StartedAt;
            cursorId = decoded.Id;
        }

        // Base query: tenant-scoped.
        // IsDeleted global filter applied automatically by ModuleDbContext.ApplyGlobalFilters.
        // TenantId filter duplicated here as defense-in-depth (global filter also covers it).
        var q = ctx.WorkflowInstances
            .AsNoTracking()
            .Where(i => i.TenantId == request.TenantId);

        // Apply optional filters
        if (!string.IsNullOrEmpty(request.Filter.EntityType))
            q = q.Where(i => i.EntityType == request.Filter.EntityType);

        // StateId: long? (T1 WorkflowState.Id bigint PK — no Guid AK)
        if (request.Filter.StateId.HasValue)
            q = q.Where(i => i.CurrentStateId == request.Filter.StateId.Value);

        if (request.Filter.Status.HasValue)
        {
            q = request.Filter.Status.Value == InstanceStatus.Active
                ? q.Where(i => i.CompletedAt == null)
                : q.Where(i => i.CompletedAt != null);
        }

        // [RT-01] Keyset predicate: `i.Id < cursorId` is SQL-translatable (EF emits [Id] <).
        // i.Id is long bigint PK — integer comparison is correctly SQL-translated.
        // Guid.CompareTo() is NOT SQL-translatable — never use it.
        if (hasCursor)
            q = q.Where(i =>
                i.StartedAt < cursorStartedAt ||
                (i.StartedAt == cursorStartedAt && i.Id < cursorId));

        // Fetch pageSize+1 to detect whether a next page exists (peek strategy)
        // Project PublicId as Id (T2 Guid AK for external contract), InternalId=Id (long for cursor).
        var page = await q
            .OrderByDescending(i => i.StartedAt)
            .ThenByDescending(i => i.Id)
            .Take(pageSize + 1)
            .Select(i => new WorkflowInstanceListItemDto(
                i.PublicId,        // Guid AK — external identifier
                i.Id,              // long PK — cursor keyset key
                i.DefinitionId,    // long FK to T2 WorkflowDefinition.Id
                i.EntityType,
                i.EntityId,
                i.CurrentStateId,  // long FK to T1 WorkflowState.Id
                i.StartedAt,
                i.DueAt,
                i.CompletedAt))
            .ToListAsync(cancellationToken);

        var hasNext = page.Count > pageSize;
        var items = hasNext ? page.Take(pageSize).ToList() : page;

        // Encode cursor from the last returned item's sort key (InternalId = long bigint PK)
        var nextCursor = hasNext
            ? CursorCodec.Encode(items[^1].StartedAt, items[^1].InternalId)
            : null;

        // [RT-07] TotalCount = null in cursor mode (total is unknown without COUNT(*))
        var meta = new PaginationMeta(
            Page: null,
            PageSize: pageSize,
            TotalPages: null,
            NextCursor: nextCursor,
            PrevCursor: null,
            HasNextPage: hasNext);

        return Result.Ok(new PagedResult<WorkflowInstanceListItemDto>(items, null, meta));
    }
}

/// <summary>
/// Returns KPI summary counts for the workflow instances dashboard.
/// Uses CountAsync for each metric — no N+1, separate aggregation queries.
/// Placed alongside list handler in Infrastructure for consistency (both need DbContext).
/// </summary>
public sealed class GetWorkflowInstancesSummaryQueryHandler(WorkflowDbContext ctx)
    : IRequestHandler<GetWorkflowInstancesSummaryQuery, Result<WorkflowInstanceSummaryDto>>
{
    public async Task<Result<WorkflowInstanceSummaryDto>> Handle(
        GetWorkflowInstancesSummaryQuery request,
        CancellationToken cancellationToken)
    {
        // Base scope: tenant. IsDeleted global filter applied by ModuleDbContext.
        var baseQ = ctx.WorkflowInstances
            .AsNoTracking()
            .Where(i => i.TenantId == request.TenantId);

        // Apply same optional filters as list query for consistent scoping
        if (!string.IsNullOrEmpty(request.Filter.EntityType))
            baseQ = baseQ.Where(i => i.EntityType == request.Filter.EntityType);

        if (request.Filter.StateId.HasValue)
            baseQ = baseQ.Where(i => i.CurrentStateId == request.Filter.StateId.Value);

        if (request.Filter.Status.HasValue)
        {
            baseQ = request.Filter.Status.Value == InstanceStatus.Active
                ? baseQ.Where(i => i.CompletedAt == null)
                : baseQ.Where(i => i.CompletedAt != null);
        }

        var todayUtc = DateTimeOffset.UtcNow.Date;
        var tomorrowUtc = todayUtc.AddDays(1);

        var total = await baseQ.CountAsync(cancellationToken);
        var active = await baseQ.CountAsync(i => i.CompletedAt == null, cancellationToken);
        var completedToday = await baseQ.CountAsync(
            i => i.CompletedAt != null
                 && i.CompletedAt >= todayUtc
                 && i.CompletedAt < tomorrowUtc,
            cancellationToken);

        // SLA breach rate: breached / total-with-dueAt (null if no instances have a deadline)
        var withDueAt = await baseQ.CountAsync(i => i.DueAt != null, cancellationToken);
        var nowUtc = DateTimeOffset.UtcNow;
        var breached = withDueAt > 0
            ? await baseQ.CountAsync(
                i => i.DueAt != null && i.CompletedAt == null && i.DueAt < nowUtc,
                cancellationToken)
            : 0;

        decimal? slaBreachRate = withDueAt > 0
            ? Math.Round((decimal)breached / withDueAt * 100, 2)
            : null;

        return Result.Ok(new WorkflowInstanceSummaryDto(active, completedToday, slaBreachRate, total));
    }
}
