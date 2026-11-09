using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetTasksByInstance;

public sealed class GetTasksByInstanceQueryHandler(
    IReadDbConnection db,
    IIdentityModuleClient identityClient,
    IOrgUnitModuleClient orgUnitClient)
    : IRequestHandler<GetTasksByInstanceQuery, Result<IReadOnlyList<WorkflowInstanceTaskDto>>>
{
    // Cap on the eligible-user list returned per pool task — a role/group can hold thousands of
    // users; the UI shows up to this many plus the total count ("còn N người").
    private const int MaxEligibleUsers = 20;

    public async Task<Result<IReadOnlyList<WorkflowInstanceTaskDto>>> Handle(
        GetTasksByInstanceQuery request,
        CancellationToken cancellationToken)
    {
        // CandidateRefsJson carries the resolved candidate pool snapshot for unassigned
        // (pool) tasks — deserialized below so the UI can name the next handler.
        const string sql = """
            SELECT t.Id, i.PublicId AS WorkflowInstanceId, t.Title, t.AssigneeId,
                   t.Status, t.Priority, t.DueDate, t.CandidateRefsJson
            FROM workflow.WorkflowTasks t
            INNER JOIN workflow.WorkflowInstances i ON i.Id = t.WorkflowInstanceId
            WHERE i.PublicId = @InstanceId
              AND t.IsDeleted = 0
            ORDER BY t.CreatedAt
            """;

        var rows = (await db.QueryAsync<TaskRow>(
            sql, new { request.InstanceId }, cancellationToken)).ToList();

        // Resolve assignee display names in one batch (mirrors history actor-name enrichment).
        var assigneeIds = rows
            .Where(r => r.AssigneeId.HasValue)
            .Select(r => r.AssigneeId!.Value)
            .Distinct()
            .ToList();

        var userMap = assigneeIds.Count > 0
            ? await identityClient.GetUserInfoByIdsAsync(assigneeIds, request.TenantId, cancellationToken)
            : null;

        var result = new List<WorkflowInstanceTaskDto>(rows.Count);
        foreach (var r in rows)
        {
            var status = (WorkflowTaskStatus)r.Status;
            var predicates = CandidatePrincipalSerializer.Deserialize(r.CandidateRefsJson);
            var candidates = predicates
                .Select(p => new WorkflowTaskCandidateDto((int)p.Type, p.Ref))
                .ToList();

            // Only resolve eligible users for active, unclaimed pool tasks — the only case the
            // "next handler" pool is meaningful (and to avoid cross-module calls on done/assigned rows).
            var isActivePool = r.AssigneeId is null
                && status is WorkflowTaskStatus.Open or WorkflowTaskStatus.InProgress
                && predicates.Count > 0;

            IReadOnlyList<WorkflowEligibleUserDto> eligibleUsers = [];
            var eligibleCount = 0;
            var hasUnresolvedPermission = false;
            if (isActivePool)
                (eligibleUsers, eligibleCount, hasUnresolvedPermission) =
                    await ResolveEligibleUsersAsync(predicates, request.TenantId, cancellationToken);

            result.Add(new WorkflowInstanceTaskDto(
                r.Id,
                r.WorkflowInstanceId,
                r.Title,
                status,
                (WorkflowTaskPriority)r.Priority,
                r.DueDate,
                r.AssigneeId,
                r.AssigneeId.HasValue && userMap is not null
                    && userMap.TryGetValue(r.AssigneeId.Value, out var user)
                        ? user.FullName
                        : null,
                candidates,
                eligibleUsers,
                eligibleCount,
                hasUnresolvedPermission));
        }

        return Result.Ok<IReadOnlyList<WorkflowInstanceTaskDto>>(result);
    }

    // Expands a pool task's candidate principals into the set of users who can claim it.
    // Combine is treated as UNION across all candidates — the only producible mode (the resolver
    // stubs Intersect as NotSupported). Permission candidates are NOT expanded (no reverse query);
    // their presence is flagged so the UI does not present the count as the complete claimable set.
    private async Task<(IReadOnlyList<WorkflowEligibleUserDto> Users, int Count, bool HasUnresolvedPermission)>
        ResolveEligibleUsersAsync(
            IReadOnlyList<CandidatePredicate> predicates,
            Guid tenantId,
            CancellationToken ct)
    {
        var roleRefs = RefsOf(predicates, CandidatePrincipalType.Role);
        var groupRefs = RefsOf(predicates, CandidatePrincipalType.Group);
        var orgUnitRefs = RefsOf(predicates, CandidatePrincipalType.OrgUnit);
        var hasUnresolvedPermission = predicates.Any(p => p.Type == CandidatePrincipalType.Permission);

        var userIds = new HashSet<Guid>();
        if (roleRefs.Count > 0)
            userIds.UnionWith(await identityClient.ResolveActiveUsersInRolesAsync(roleRefs, tenantId, ct));
        if (groupRefs.Count > 0)
            userIds.UnionWith(await identityClient.ResolveActiveUsersInGroupsAsync(groupRefs, tenantId, ct));
        if (orgUnitRefs.Count > 0)
            userIds.UnionWith(await orgUnitClient.ResolveActiveUsersInOrgUnitsAsync(orgUnitRefs, tenantId, ct));

        var total = userIds.Count;
        var capped = userIds.Take(MaxEligibleUsers).ToList();

        var names = capped.Count > 0
            ? await identityClient.GetUserInfoByIdsAsync(capped, tenantId, ct)
            : null;

        var users = capped
            .Select(id => new WorkflowEligibleUserDto(
                id, names is not null && names.TryGetValue(id, out var u) ? u.FullName : null))
            .ToList();

        return (users, total, hasUnresolvedPermission);
    }

    private static List<string> RefsOf(IReadOnlyList<CandidatePredicate> predicates, CandidatePrincipalType type) =>
        predicates.Where(p => p.Type == type).Select(p => p.Ref).Distinct().ToList();

    // WorkflowTask is T1: Id (long), WorkflowInstanceId now Guid (T2 PublicId AK via JOIN).
    internal sealed class TaskRow
    {
        public long Id { get; set; }
        public Guid WorkflowInstanceId { get; set; }
        public string Title { get; set; } = string.Empty;
        public Guid? AssigneeId { get; set; }
        public int Status { get; set; }
        public int Priority { get; set; }
        public DateTime? DueDate { get; set; }
        public string? CandidateRefsJson { get; set; }
    }
}
