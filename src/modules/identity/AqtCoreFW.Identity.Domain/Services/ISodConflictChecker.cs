namespace AqtCoreFW.Identity.Domain.Services;

/// <summary>
/// Checks for Segregation of Duties violations when assigning a role to a user.
/// Compares the user's current effective permissions against the permissions
/// carried by the role being assigned.
/// </summary>
public interface ISodConflictChecker
{
    /// <summary>
    /// Returns all active SoD conflicts that would arise if <paramref name="roleToAssign"/>
    /// were granted to <paramref name="userId"/>.
    /// Empty list means no violations.
    /// </summary>
    Task<IReadOnlyList<SodViolation>> CheckAsync(
        Guid userId,
        Guid roleToAssign,
        CancellationToken ct = default);
}

/// <summary>
/// Describes a single SoD conflict detected between two permission codes.
/// </summary>
/// <param name="RuleId">FK to SodConflictRule.Id (long T1 PK) for audit logging.</param>
/// <param name="PermissionCodeA">First permission code in the conflicting pair.</param>
/// <param name="PermissionCodeB">Second permission code in the conflicting pair.</param>
/// <param name="Severity">Severity level as string (Low/Medium/High).</param>
/// <param name="Description">Human-readable explanation of the conflict.</param>
public sealed record SodViolation(
    long RuleId,
    string PermissionCodeA,
    string PermissionCodeB,
    string Severity,
    string? Description);
