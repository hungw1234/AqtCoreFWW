using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Closes three pre-existing RLS gaps in the [identity] schema:
    ///   1. DocumentSignatureSessions — created by 20260616155354_signature_session without RLS.
    ///   2. DocumentSignatureSessionSigners — same migration, same gap.
    ///   3. SigningPendingTransactions — tracks Viettel Mysign async-callback state; ITenantScoped.
    ///
    /// RLS pattern mirrors AddSignerCertEnrollment (20260615185000) and AddSigningDocFlowRlsPolicies.
    ///
    /// NOTE — SigningPendingTransactions and PendingTransactionCleanupJob:
    ///   PendingTransactionCleanupJob (HostedService) queries ALL tenants without setting
    ///   SESSION_CONTEXT('TenantId'). The standard RLS predicate allows db_owner bypass —
    ///   in dev (db_owner) the job continues to work. In production (least-privilege login)
    ///   the cleanup job will see zero rows and silently skip expiry.
    ///   REQUIRED FOLLOW-UP (tracked in backlog): add a TenantAgnostic/admin DB role bypass or
    ///   refactor the cleanup job to iterate tenants explicitly before hardening to least-privilege.
    ///   This migration is correct; the job interaction is a separate operational concern.
    ///
    /// Migration is RLS-only (raw SQL) — no EF model change → model snapshot unchanged.
    /// </summary>
    public partial class AddDocumentSignatureSessionRlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Each statement is its own batch (RlsMigrationHelper.GenerateRlsPolicyStatements is GO-free).

            // Gap 1: DocumentSignatureSessions (session aggregate root, TenantId non-null Guid).
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "identity", "DocumentSignatureSessions"))
                migrationBuilder.Sql(sql);

            // Gap 2: DocumentSignatureSessionSigners (child entity, TenantId non-null Guid).
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "identity", "DocumentSignatureSessionSigners"))
                migrationBuilder.Sql(sql);

            // Gap 3: SigningPendingTransactions (Viettel Mysign async-callback tracker, ITenantScoped).
            // Primary access paths (SignAsync, callback endpoint) are HTTP-request-scoped with ambient
            // tenant — RLS is safe for those paths. See NOTE above re: PendingTransactionCleanupJob.
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "identity", "SigningPendingTransactions"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop in reverse order.
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "identity", "SigningPendingTransactions"))
                migrationBuilder.Sql(sql);

            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "identity", "DocumentSignatureSessionSigners"))
                migrationBuilder.Sql(sql);

            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "identity", "DocumentSignatureSessions"))
                migrationBuilder.Sql(sql);
        }
    }
}
