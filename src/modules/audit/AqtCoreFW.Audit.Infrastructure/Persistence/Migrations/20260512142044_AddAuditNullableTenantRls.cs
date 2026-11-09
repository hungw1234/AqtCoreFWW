using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds asymmetric RLS policies to audit.AuditLogEntries and audit.SecurityEventLogs.
    ///
    /// Both tables have a nullable Guid? TenantId:
    ///   NULL = system/cross-tenant event (e.g. security alerts, platform audit).
    ///   non-NULL = tenant-scoped event.
    ///
    /// Design (PHASE6-RLS-NULLABLE-TENANT Option A):
    ///   FILTER predicate: exposes NULL-tenant rows ONLY when the calling handler sets
    ///     SESSION_CONTEXT(N'audit_admin_bypass') = 'true' (SystemAdmin read path only).
    ///     Non-admin tenant callers never set this flag → system events remain hidden.
    ///   BLOCK predicate (asymmetric, AFTER INSERT only):
    ///     Allows NULL-tenant INSERTs unconditionally — audit pipeline writers are
    ///     server-side and never set a session tenant context; blocking them would
    ///     silently drop audit events.  Tenant-tagged rows still require the row TenantId
    ///     to match the session TenantId.
    ///
    /// Compensating controls for the unrestricted NULL-tenant INSERT:
    ///   - tr_AuditLog_NoMutate trigger: blocks UPDATE/DELETE on AuditLogEntries.
    ///   - HMAC chain: detects forged/tampered entries.
    ///   - Writers are server-side only (not reachable by a tenant-authenticated request).
    ///
    /// PHASE6-RLS-NULLABLE-TENANT plan reference:
    ///   plans/260512-1351-phase6-rls-nullable-tenant/phase-03-audit-module.md
    /// </summary>
    public partial class AddAuditNullableTenantRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.GenerateAuditAdminBypassRlsPolicyStatements(
                "audit", "AuditLogEntries", "SecurityEventLogs"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.DropAuditAdminBypassRlsPolicyStatements(
                "audit", "AuditLogEntries", "SecurityEventLogs"))
                migrationBuilder.Sql(sql);
        }
    }
}
