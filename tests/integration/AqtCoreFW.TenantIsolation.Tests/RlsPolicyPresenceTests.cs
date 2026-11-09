using AqtCoreFW.TenantIsolation.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// Verifies that all 51 expected RLS security policies are present and enabled
/// in the test database after applying all migrations.
///
/// Does NOT test enforcement (cross-tenant read/write blocking) — that requires
/// tenant-switching fixtures and is covered by RuleSetTenantIsolationTests for the
/// rules module. This test closes the "policies just aren't there" gap flagged in
/// the PHASE6-RLS-LOST audit.
///
/// Policy naming convention: pol_{schema}_{Table}_Tenant (enforced by RlsMigrationHelper).
/// Count: 2+3+2+4+1+3+3+3+3+2+2+2+3 = 33 (Phase-06 restore) + 2 (audit nullable-tenant,
/// PHASE6-RLS-NULLABLE-TENANT Option A) + 1 (config.SystemParameters, Phase 4) + 3 (BP MVP P3
/// businessprocess module) + 1 (forms.FormTemplateVersions snapshot RLS) + 1
/// (workflow.WorkflowTaskCandidateIndex read-model RLS) + 2 (identity.SigningDocFlowRecords +
/// SigningDocFlowNonces, doc-flow tenant isolation) + 1 (identity.SignerCertEnrollments) +
/// 1 (identity.SigningRequests) + 1 (identity.UserSignatureProfiles, P1 signature-profile) +
/// 3 (identity.DocumentSignatureSessions + DocumentSignatureSessionSigners +
/// SigningPendingTransactions, pre-existing gaps closed by AddDocumentSignatureSessionRlsPolicies) +
/// 1 (identity.SignerEnrollmentNonces, added by AddSignerEnrollmentNonce) +
/// 1 (forms.FormSubmissionWorkflowLinkPendingAttempts, added by AddWorkflowLinkPendingAttemptRls —
/// table landed after the Phase-06 restore, so it got a follow-up policy) = 51.
/// </summary>
[Collection(RlsPolicyPresenceCollection.CollectionName)]
public sealed class RlsPolicyPresenceTests
{
    private readonly RlsPolicyPresenceFixture _fixture;

    // ── Expected policies (schema, table) — one policy per table ─────────────
    // Each entry corresponds to: pol_{schema}_{table}_Tenant
    // Source: plan matrix (plans/260512-1233-phase6-rls-restore/plan.md) + Phase-03 + Phase-04
    //         nullable-tenant policies.
    //
    // Previously excluded (now all covered):
    //   audit.AuditLogEntries   → added by PHASE6-RLS-NULLABLE-TENANT Option A (Phase 03)
    //   audit.SecurityEventLogs → added by PHASE6-RLS-NULLABLE-TENANT Option A (Phase 03)
    //   config.SystemParameters → added by PHASE6-RLS-NULLABLE-TENANT Phase 04
    private static readonly (string Schema, string Table)[] ExpectedPolicies =
    [
        // ai (2) — VectorDocuments entity maps to table "vector_documents" (snake_case)
        ("ai",            "AiSessions"),
        ("ai",            "vector_documents"),
        // businessprocess (3) — BP MVP P3 InitialBusinessProcess migration
        ("businessprocess", "BusinessProcessDefinitions"),
        ("businessprocess", "BusinessProcessStateForms"),
        ("businessprocess", "BusinessProcessTransitionForms"),
        // audit (5) — AuditLogEntries + SecurityEventLogs added by PHASE6-RLS-NULLABLE-TENANT Option A
        ("audit",         "AuditLogEntries"),
        ("audit",         "SecurityEventLogs"),
        ("audit",         "PersonalDataProcessingLogs"),
        ("audit",         "RtbfRequests"),
        ("audit",         "AiPromptTraces"),
        // collab (2)
        ("collab",        "Conversations"),
        ("collab",        "Messages"),
        // files (4)
        ("files",         "FileRecords"),
        ("files",         "FileVersions"),
        ("files",         "DocumentTemplates"),
        ("files",         "RetentionPolicies"),
        // identity (10) — SigningDocFlow* added by AddSigningDocFlowRlsPolicies (doc-flow tenant isolation)
        //               SignerCertEnrollments added by AddSignerCertEnrollment
        //               SigningRequests added by AddSigningRequestRlsPolicies
        //               UserSignatureProfiles added by AddUserSignatureProfile (P1)
        //               DocumentSignatureSession* + SigningPendingTransactions: pre-existing gaps
        //               closed by AddDocumentSignatureSessionRlsPolicies
        //               SignerEnrollmentNonces added by AddSignerEnrollmentNonce (20260625033442)
        ("identity",      "CredentialPolicies"),
        ("identity",      "SigningDocFlowRecords"),
        ("identity",      "SigningDocFlowNonces"),
        ("identity",      "SignerCertEnrollments"),
        ("identity",      "SigningRequests"),
        ("identity",      "UserSignatureProfiles"),
        ("identity",      "DocumentSignatureSessions"),
        ("identity",      "DocumentSignatureSessionSigners"),
        ("identity",      "SigningPendingTransactions"),
        ("identity",      "SignerEnrollmentNonces"),
        // integration (3)
        ("integration",   "Partners"),
        ("integration",   "Contracts"),
        ("integration",   "MessageLogs"),
        // masterdata (3)
        ("masterdata",    "Dictionaries"),
        ("masterdata",    "DictionaryItems"),
        ("masterdata",    "ExternalMappings"),
        // notifications (3)
        ("notifications", "Notifications"),
        ("notifications", "NotificationPreferences"),
        ("notifications", "NotificationTemplates"),
        // organization (3)
        ("organization",  "OrgUnits"),
        ("organization",  "StaffPositionHistories"),
        ("organization",  "UserOrgUnitAssignments"),
        // reporting (2)
        ("reporting",     "DashboardLayouts"),
        ("reporting",     "ReportWidgets"),
        // rules (2)
        ("rules",         "RuleSets"),
        ("rules",         "DecisionTables"),
        // workflow (3) — WorkflowTaskCandidateIndex: tenant-RLS on the runtime read-model
        ("workflow",      "WorkflowTasks"),
        ("workflow",      "EscalationRules"),
        ("workflow",      "WorkflowTaskCandidateIndex"),
        // config (1) — symmetric nullable-string TenantId (Phase 04)
        ("config",        "SystemParameters"),
        // forms (5) — FormTemplateVersions: snapshot table gets the same tenant RLS as live templates;
        //             FormSubmissionWorkflowLinkPendingAttempts: added by AddWorkflowLinkPendingAttemptRls
        //             (the table landed after RestorePhase06RlsPolicies, so it needed a follow-up policy)
        ("forms",         "FormSubmissions"),
        ("forms",         "FormTemplates"),
        ("forms",         "ViewDefinitions"),
        ("forms",         "FormTemplateVersions"),
        ("forms",         "FormSubmissionWorkflowLinkPendingAttempts"),
    ];

    public RlsPolicyPresenceTests(RlsPolicyPresenceFixture fixture)
    {
        _fixture = fixture;
    }

    // ── P1: all expected policies present and enabled ─────────────────────────

    [Fact]
    public async Task AllExpectedRlsPolicies_ArePresent_AndEnabled()
    {
        // Query all enabled security policies in the test DB
        const string sql = """
            SELECT s.name AS schema_name, p.name AS policy_name
            FROM sys.security_policies p
            JOIN sys.schemas s ON s.schema_id = p.schema_id
            WHERE p.is_enabled = 1
            ORDER BY s.name, p.name
            """;

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var conn = new SqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            found.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        }

        // Build expected set from the static matrix above
        var expectedSet = ExpectedPolicies
            .Select(e => $"{e.Schema}.pol_{e.Schema}_{e.Table}_Tenant")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Report missing policies for actionable failure messages
        var missing = expectedSet.Except(found).OrderBy(x => x).ToList();
        var unexpected = found
            .Where(f => f.Contains("pol_") && !expectedSet.Contains(f))
            .OrderBy(x => x)
            .ToList();

        missing.Should().BeEmpty(
            $"these RLS policies are missing or disabled in the DB:{Environment.NewLine}" +
            $"  {string.Join($"{Environment.NewLine}  ", missing)}");

        // Also assert the total count matches — catches both over and under
        expectedSet.Should().HaveCount(ExpectedPolicies.Length,
            "sanity check: ExpectedPolicies array and expectedSet must match");

        found.Count(f => f.Contains("pol_"))
            .Should().BeGreaterThanOrEqualTo(ExpectedPolicies.Length,
                $"expected at least {ExpectedPolicies.Length} RLS policies; unexpected extra: " +
                $"{string.Join(", ", unexpected)}");
    }

    // ── P2: exact policy count matches expectation ────────────────────────────

    [Fact]
    public async Task RlsPolicyCount_Equals_Expected()
    {
        const string sql = """
            SELECT COUNT(*)
            FROM sys.security_policies p
            JOIN sys.schemas s ON s.schema_id = p.schema_id
            WHERE p.is_enabled = 1
              AND p.name LIKE 'pol_%_Tenant'
            """;

        await using var conn = new SqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        var count = (int)(await cmd.ExecuteScalarAsync())!;

        count.Should().Be(ExpectedPolicies.Length,
            $"exactly {ExpectedPolicies.Length} RLS tenant policies should exist and be enabled; " +
            $"found {count}. Run all RestorePhase06RlsPolicies migrations + AddAuditNullableTenantRls " +
            $"or check for drift in the excluded-table list.");
    }
}
