// REMOVED 2026-05-01 (PR #71 cleanup) — All seeded report definitions were Cases-module KPI reports.
// Cases module deleted in PR #71. See code-reviewer-260501-1216-pr-71-adversarial.md (H1+H2).
// ReportDefinitionSeeder class removed. DI registration was never added to InfrastructureRegistration.
// SQL purge for existing rows shipped as supplement to infra/scripts/drop-cases-schema.sql:
//   DELETE FROM reporting.ReportDefinitions WHERE Name = 'CasesStatisticsReport';
//   DELETE FROM reporting.QueryCatalog WHERE Code IN ('SLA_BY_DEPARTMENT_PERIOD','CASE_COUNT_BY_TYPE','OFFICER_PERFORMANCE','OVERDUE_TREND','BOTTLENECK_ANALYSIS');
