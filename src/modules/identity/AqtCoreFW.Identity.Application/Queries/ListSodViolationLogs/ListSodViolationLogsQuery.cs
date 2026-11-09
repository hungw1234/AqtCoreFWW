using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.ListSodViolationLogs;

/// <summary>
/// List SoD violation audit log entries with optional filters.
/// TenantId derived from context — NOT from query param (security: inline filter pattern).
/// Page size capped at 100; default 50.
/// </summary>
public sealed record ListSodViolationLogsQuery(
    Guid TenantId,
    Guid? UserId,
    Guid? RoleId,
    long? RuleId,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize) : IQuery<ListSodViolationLogsQueryResult>;
