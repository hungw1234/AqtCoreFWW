namespace AqtCoreFW.SharedKernel.Application.Data;

/// <summary>
/// DI keys for <see cref="IReadDbConnection"/> registrations (read-path login isolation, ISO-DAPPER).
/// The default (unkeyed) reader runs on the least-privilege NonAudit login (denied <c>[audit]</c>);
/// query handlers that legitimately read <c>[audit]</c> resolve the keyed reader with
/// <see cref="Audit"/> on the broad Default (audit-owner) login via
/// <c>[FromKeyedServices(ReadConnectionKeys.Audit)]</c>. Deny-by-default: a handler that reads
/// <c>[audit]</c> without the key is refused by the NonAudit login at runtime once it is provisioned.
/// </summary>
public static class ReadConnectionKeys
{
    /// <summary>Keyed reader on the audit-owner (Default) login — for audit-capable query handlers.</summary>
    public const string Audit = "audit";
}
