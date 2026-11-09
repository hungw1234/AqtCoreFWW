using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.AuthServer.Services;

/// <summary>
/// Thin runtime read of the SystemParameter `mfa.enrolment.required` toggle.
/// Tenant override (TenantId column) wins; fallback to global (TenantId=null);
/// missing key → false. No cache (called once per token issuance — sub-ms EF query).
///
/// Why not ISystemParamService: the full implementation depends on Redis
/// (IConnectionMultiplexer) which AuthServer host does not register.
/// Pulling Redis into AuthServer just for one boolean read = YAGNI.
/// </summary>
public interface IMfaEnrolmentSettings
{
    Task<bool> IsEnrolmentRequiredAsync(string? tenantId, CancellationToken ct = default);
}

public sealed class MfaEnrolmentSettings(SystemParamsDbContext db) : IMfaEnrolmentSettings
{
    private const string Key = "mfa.enrolment.required";

    public async Task<bool> IsEnrolmentRequiredAsync(string? tenantId, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(tenantId))
        {
            var tenantParam = await db.SystemParameters
                .AsNoTracking()
                .Where(p => p.Key == Key && p.TenantId == tenantId)
                .Select(p => p.Value)
                .FirstOrDefaultAsync(ct);
            if (tenantParam is not null)
                return ParseBool(tenantParam);
        }

        var globalParam = await db.SystemParameters
            .AsNoTracking()
            .Where(p => p.Key == Key && p.TenantId == null)
            .Select(p => p.Value)
            .FirstOrDefaultAsync(ct);

        return globalParam is not null && ParseBool(globalParam);
    }

    private static bool ParseBool(string value) =>
        bool.TryParse(value, out var b) && b;
}
