using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.ExportSodViolationLogs;

/// <summary>
/// Export SoD violation logs as streaming CSV.
/// Writes directly to provided TextWriter — returns Unit (no buffered result).
/// TenantId derived from context — NOT from query param (security: inline filter pattern).
/// </summary>
public sealed record ExportSodViolationLogsQuery(
    Guid TenantId,
    Guid? UserId,
    Guid? RoleId,
    long? RuleId,
    DateTime? From,
    DateTime? To,
    TextWriter Writer) : IRequest<Unit>;
