using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestorePhase06RlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AuditLogEntries and SecurityEventLogs excluded: nullable Guid? TenantId →
            // strict-equality predicate would hide system/global rows. Deferred to PHASE6-RLS-NULLABLE-TENANT.
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "audit", "PersonalDataProcessingLogs", "RtbfRequests", "AiPromptTraces"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "audit", "PersonalDataProcessingLogs", "RtbfRequests", "AiPromptTraces"))
                migrationBuilder.Sql(sql);
        }
    }
}
