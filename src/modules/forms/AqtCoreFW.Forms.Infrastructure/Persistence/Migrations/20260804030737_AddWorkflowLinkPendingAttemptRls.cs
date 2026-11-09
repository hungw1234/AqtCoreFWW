using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowLinkPendingAttemptRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Close the RLS gap: forms.FormSubmissionWorkflowLinkPendingAttempts (added 2026-05-22) landed
            // AFTER RestorePhase06RlsPolicies, so it never got a tenant policy — unlike every other
            // tenant-scoped Forms child table. TenantId is a non-null uniqueidentifier, so the standard
            // FILTER + BLOCK-AFTER-INSERT policy applies. All three writers satisfy the block predicate:
            // the retry job pins SESSION_CONTEXT per row (ICrossModuleUnitOfWork.BeginAsync(row.TenantId));
            // the event handler opens its OWN pinned UoW to the event's tenant (it publishes AFTER the
            // submit command commits, and the anonymous public-form path has no ambient tenant, so it must
            // pin explicitly); the db_owner ops backfill bypasses via IS_MEMBER('db_owner').
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "forms", "FormSubmissionWorkflowLinkPendingAttempts"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "forms", "FormSubmissionWorkflowLinkPendingAttempts"))
                migrationBuilder.Sql(sql);
        }
    }
}
