namespace AqtCoreFW.Audit.Domain.ValueObjects;

/// <summary>Audit action type — WHO did WHAT to WHICH resource.</summary>
public sealed record AuditAction(string Value)
{
    public static readonly AuditAction Create = new("CREATE");
    public static readonly AuditAction Read = new("READ");
    public static readonly AuditAction Update = new("UPDATE");
    public static readonly AuditAction Delete = new("DELETE");
    public static readonly AuditAction Login = new("LOGIN");
    public static readonly AuditAction Logout = new("LOGOUT");
    public static readonly AuditAction Export = new("EXPORT");
    public static readonly AuditAction Import = new("IMPORT");
    public static readonly AuditAction RoleAssign = new("ROLE_ASSIGN");
    public static readonly AuditAction RoleRevoke = new("ROLE_REVOKE");
    public static readonly AuditAction TokenRevoke = new("TOKEN_REVOKE");
    public static readonly AuditAction DataAccess = new("DATA_ACCESS");
    public static readonly AuditAction Rtbf = new("RTBF");

    // --- Authorization events (Phase D) ---
    public static readonly AuditAction PermissionGranted = new("PERMISSION_GRANTED");
    public static readonly AuditAction PermissionRevoked = new("PERMISSION_REVOKED");
    public static readonly AuditAction RoleAssigned = new("ROLE_ASSIGNED");
    public static readonly AuditAction RoleRevoked = new("ROLE_REVOKED");
    public static readonly AuditAction GroupRoleAssigned = new("GROUP_ROLE_ASSIGNED");
    public static readonly AuditAction GroupRoleRevoked = new("GROUP_ROLE_REVOKED");
    public static readonly AuditAction DataScopeChanged = new("DATA_SCOPE_CHANGED");
    public static readonly AuditAction DelegationCreated = new("DELEGATION_CREATED");
    public static readonly AuditAction DelegationRevoked = new("DELEGATION_REVOKED");
    public static readonly AuditAction AccessDenied = new("ACCESS_DENIED");
    public static readonly AuditAction SensitiveActionUsed = new("SENSITIVE_ACTION_USED");

    public override string ToString() => Value;
}
