using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Startup diagnostic that warns when the production database connection runs under a principal
/// that defeats Row-Level Security.
///
/// Every RLS predicate function in <see cref="Persistence.RlsMigrationHelper"/> carves out
/// <c>IS_MEMBER('db_owner') = 1</c> (so migration tooling and admin batch jobs can cross tenants).
/// If the <em>runtime application</em> login is itself <c>db_owner</c>, <c>sysadmin</c>, or
/// <c>db_securityadmin</c>, then all tenant-isolation predicates are bypassed (or trivially
/// disableable) — the RLS layer becomes theatre.
///
/// This check is advisory: it logs <see cref="LogLevel.Critical"/> but does NOT throw, because
/// the host currently runs EF <c>MigrateAsync</c> on startup (Program.cs) and some deployments
/// legitimately share an elevated login until ops splits a least-privilege runtime account.
/// See <c>docs/deployment-guide.md</c> §4 "Application DB Principal".
///
/// Note: <c>db_ddladmin</c> is deliberately NOT flagged — it does not satisfy
/// <c>IS_MEMBER('db_owner')</c>, so RLS still applies to it, and it is presently required for the
/// in-app startup migrations. Drop it (and this allowance) once startup migrations move to a
/// dedicated init-container login.
///
/// Placement: call AFTER <c>app.Build()</c> and after <c>ValidateProductionSecrets()</c>
/// (so a missing ConnectionString has already produced a clear error).
/// </summary>
public static class RlsPrincipalValidator
{
    /// <summary>
    /// Server/database role-membership flags for the application's own connection.
    /// <see cref="IsDbDdlAdmin"/> is informational only (it does not defeat RLS — it can however
    /// DDL-disable a security policy, and is presently required for in-app startup migrations).
    /// </summary>
    public readonly record struct PrincipalRoleFlags(
        bool IsSysAdmin,
        bool IsDbOwner,
        bool IsDbSecurityAdmin,
        bool IsDbDdlAdmin,
        string? LoginName,
        string? DatabaseName)
    {
        public bool HasRlsDefeatingRole => IsSysAdmin || IsDbOwner || IsDbSecurityAdmin;
    }

    /// <summary>
    /// Probes the production DB principal and logs <see cref="LogLevel.Critical"/> if it holds an
    /// RLS-defeating role. No-op when not in Production. Never throws — a failed probe logs a
    /// warning and is swallowed (a diagnostic must not block startup).
    /// </summary>
    public static IHost WarnIfDbOwnerInProduction(this IHost host)
    {
        var env = host.Services.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
        if (env is null || !env.IsProduction())
            return host;

        var loggerFactory = host.Services.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        var logger = loggerFactory?.CreateLogger(typeof(RlsPrincipalValidator).FullName!);

        try
        {
            var config = host.Services.GetService(typeof(IConfiguration)) as IConfiguration;
            var connectionString = config?["ConnectionStrings:Default"];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                // ValidateProductionSecrets() already guards this; don't double-fault on a diagnostic.
                logger?.LogWarning("RLS principal check skipped — ConnectionStrings:Default is not set.");
                return host;
            }

            var flags = ProbePrincipalRoles(connectionString);
            var message = BuildCriticalMessage(flags);
            if (message is not null)
                logger?.LogCritical("{RlsPrincipalWarning}", message);
            else
                logger?.LogInformation(
                    "RLS principal check OK — login '{Login}' on '{Database}' holds no RLS-defeating role "
                    + "(db_ddladmin={IsDdlAdmin}; tolerated only while in-app startup migrations run).",
                    flags.LoginName ?? "(unknown)", flags.DatabaseName ?? "(unknown)", flags.IsDbDdlAdmin);
        }
        catch (Exception ex)
        {
            // A connectivity hiccup on a diagnostic must not abort startup — migrations/health
            // checks downstream will surface real connection problems with better context.
            logger?.LogWarning(ex, "RLS principal check could not run; continuing startup.");
        }

        return host;
    }

    /// <summary>
    /// Builds the Critical log message for an RLS-defeating principal, or <c>null</c> when the
    /// principal is safe. Pure — separated from IHost/SQL wiring for unit testing.
    /// </summary>
    public static string? BuildCriticalMessage(PrincipalRoleFlags flags)
    {
        if (!flags.HasRlsDefeatingRole)
            return null;

        var roles = new List<string>(3);
        if (flags.IsSysAdmin) roles.Add("sysadmin (server role)");
        if (flags.IsDbOwner) roles.Add("db_owner");
        if (flags.IsDbSecurityAdmin) roles.Add("db_securityadmin");

        var login = flags.LoginName ?? "(unknown login)";
        var db = flags.DatabaseName ?? "(unknown database)";

        return
            $"Application DB login '{login}' on database '{db}' is a member of: {string.Join(", ", roles)}. " +
            "Row-Level Security predicates carve out IS_MEMBER('db_owner')=1, so this principal BYPASSES " +
            "(or can disable) all tenant-isolation policies — tenant data is effectively unfiltered at the " +
            "engine level. Provision a least-privilege runtime login (db_datareader + db_datawriter + EXECUTE, " +
            "no db_owner/sysadmin/db_securityadmin) — see docs/deployment-guide.md §4 'Application DB Principal'.";
    }

    private static PrincipalRoleFlags ProbePrincipalRoles(string connectionString)
    {
        const string sql = """
            SELECT
                CAST(ISNULL(IS_SRVROLEMEMBER('sysadmin'), 0) AS bit)      AS IsSysAdmin,
                CAST(ISNULL(IS_MEMBER('db_owner'), 0) AS bit)            AS IsDbOwner,
                CAST(ISNULL(IS_MEMBER('db_securityadmin'), 0) AS bit)    AS IsDbSecurityAdmin,
                CAST(ISNULL(IS_MEMBER('db_ddladmin'), 0) AS bit)        AS IsDbDdlAdmin,
                ORIGINAL_LOGIN()                                         AS LoginName,
                DB_NAME()                                                AS DatabaseName;
            """;

        using var conn = new SqlConnection(connectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return new PrincipalRoleFlags(false, false, false, false, null, null);

        return new PrincipalRoleFlags(
            IsSysAdmin: !reader.IsDBNull(0) && reader.GetBoolean(0),
            IsDbOwner: !reader.IsDBNull(1) && reader.GetBoolean(1),
            IsDbSecurityAdmin: !reader.IsDBNull(2) && reader.GetBoolean(2),
            IsDbDdlAdmin: !reader.IsDBNull(3) && reader.GetBoolean(3),
            LoginName: reader.IsDBNull(4) ? null : reader.GetString(4),
            DatabaseName: reader.IsDBNull(5) ? null : reader.GetString(5));
    }
}
