using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetMyProcessedInstances;

/// <summary>
/// Returns a cursor-paginated list of workflow instances where the caller has executed
/// at least one transition. Driven from WorkflowInstanceHistories (TransitionedBy index)
/// so query cost scales with the caller's participation, not the total instance count.
/// History has no TenantId; tenant isolation is enforced via the JOIN to WorkflowInstances.
/// </summary>
public sealed record GetMyProcessedInstancesQuery(
    Guid UserId,
    Guid TenantId,
    string? Cursor,
    int PageSize) : IRequest<Result<PagedResult<MyProcessedInstanceDto>>>;

/// <summary>
/// Internal Dapper projection that includes the internal InstanceId (bigint PK) for cursor
/// encoding. Not exposed beyond the handler — the public DTO carries WorkflowInstancePublicId.
/// </summary>
internal sealed record MyProcessedInstanceRow(
    long InstanceId,                    // internal bigint PK — cursor key, not sent to client
    Guid WorkflowInstancePublicId,
    long CurrentStateId,
    string CurrentStateName,
    string EntityType,
    Guid EntityId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    bool IsFinal);

public sealed class GetMyProcessedInstancesQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetMyProcessedInstancesQuery, Result<PagedResult<MyProcessedInstanceDto>>>
{
    // This list is scoped to the BP-Run experience — only WorkflowInstances created for a
    // BusinessProcess are returned. Non-BP instances (test/other entity types) are excluded so
    // the caller never sees an orphan row whose name the BP-Run UI cannot resolve.
    private const string BusinessProcessEntityType = "BusinessProcess";


    public async Task<Result<PagedResult<MyProcessedInstanceDto>>> Handle(
        GetMyProcessedInstancesQuery request,
        CancellationToken cancellationToken)
    {
        // Clamp page size — client cannot DoS with large values.
        var pageSize = Math.Clamp(request.PageSize, 1, PaginationDefaults.AdminMax);

        // Decode cursor — malformed cursor returns 400, never 500.
        long? cursorInstanceId = null;
        if (!string.IsNullOrEmpty(request.Cursor))
        {
            if (!MyProcessedInstancesCursorCodec.TryDecode(request.Cursor, out var decoded))
                return Result.Fail(new BadRequestError(
                    "The cursor token is malformed or has been tampered with.",
                    ErrorCodes.Workflow.MalformedCursor));

            cursorInstanceId = decoded;
        }

        // Query is driven from history → instance (NOT instance + EXISTS) so the DB seeks
        // only the caller's slice in IX_WorkflowInstanceHistories_TransitionedBy_InstanceId
        // then joins the much smaller result set to WorkflowInstances.
        // The JOIN ON i.TenantId = @TenantId is the authoritative tenant gate — history rows
        // have no TenantId column, so cross-tenant isolation relies entirely on this JOIN.
        // i.IsDeleted = 0 mirrors the EF global soft-delete filter that this raw query bypasses
        // (WorkflowInstance is soft-deletable; sibling raw reads apply the same predicate).
        // WorkflowStates is LEFT-joined in-module to resolve CurrentStateName/IsFinal without a
        // BusinessProcess dependency; LEFT JOIN so an instance is never dropped just because its
        // current state row is soft-deleted (s.IsDeleted = 0 in the ON clause → name coalesces
        // to '' rather than removing the dossier the caller demonstrably processed).
        // InstanceId is returned for cursor encoding only; it is not exposed to clients.
        var sql = """
            SELECT
                parts.InstanceId,
                i.PublicId                       AS WorkflowInstancePublicId,
                i.CurrentStateId,
                COALESCE(s.DisplayNameVi, N'')    AS CurrentStateName,
                i.EntityType,
                i.EntityId,
                i.StartedAt,
                i.CompletedAt,
                COALESCE(s.IsFinal, CAST(0 AS bit)) AS IsFinal
            FROM (
                SELECT DISTINCT h.InstanceId
                FROM workflow.WorkflowInstanceHistories h
                WHERE h.TransitionedBy = @UserId
                  AND (@CursorInstanceId IS NULL OR h.InstanceId < @CursorInstanceId)
            ) parts
            JOIN workflow.WorkflowInstances i
                ON i.Id = parts.InstanceId AND i.TenantId = @TenantId AND i.IsDeleted = 0
                   AND i.EntityType = @EntityType
            LEFT JOIN workflow.WorkflowStates s
                ON s.Id = i.CurrentStateId AND s.IsDeleted = 0
            ORDER BY parts.InstanceId DESC
            OFFSET 0 ROWS FETCH NEXT @FetchCount ROWS ONLY
            """;

        var rows = await db.QueryAsync<MyProcessedInstanceRow>(
            sql,
            new
            {
                request.UserId,
                request.TenantId,
                CursorInstanceId = cursorInstanceId,
                EntityType = BusinessProcessEntityType,   // scope to BP-Run dossiers only
                FetchCount = pageSize + 1   // N+1 peek to detect hasNextPage
            },
            cancellationToken);

        var page = rows.ToList();
        var hasNext = page.Count > pageSize;
        if (hasNext)
            page.RemoveAt(page.Count - 1);

        // Encode cursor from the last item's InstanceId (internal bigint PK).
        // InstanceId is monotonically assigned and unique — sufficient as sole keyset key.
        var nextCursor = hasNext && page.Count > 0
            ? MyProcessedInstancesCursorCodec.Encode(page[^1].InstanceId)
            : null;

        var items = page
            .Select(r => new MyProcessedInstanceDto(
                r.WorkflowInstancePublicId,
                r.CurrentStateId,
                r.CurrentStateName,
                r.EntityType,
                r.EntityId,
                r.StartedAt,
                r.CompletedAt,
                r.IsFinal))
            .ToList();

        var meta = new PaginationMeta(
            Page: null,
            PageSize: pageSize,
            TotalPages: null,
            NextCursor: nextCursor,
            PrevCursor: null,
            HasNextPage: hasNext);

        return Result.Ok(new PagedResult<MyProcessedInstanceDto>(items, null, meta));
    }
}
