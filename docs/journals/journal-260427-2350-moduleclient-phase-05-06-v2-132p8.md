# Journal — v2.132p8 ModuleClient + Events Extraction Prep: Phase 05-06 (Epic Complete)

**Date:** 2026-04-27 (late evening, follow-up to v2.132p7)
**Branch:** main (SQL Server)
**Session:** /cook --auto on `plans/260427-1943-moduleclient-events-extraction-prep/plan.md`

## TL;DR
Closed 2 final phases of the ModuleClient extraction-prep epic. Phase 05 split SharedKernel.Contracts into 10 per-module Contracts assemblies (HIGH risk per plan); Phase 06 synced docs. Build green, all unit tests + 117/117 integration pass, **zero behavior change**. Epic done (6/6).

## Phase 05 — Per-Module Contract Assembly Split (HIGH risk, shipped clean)

### What
- Created 10 csproj under `src/shared/`: Chat, Consent, Files, Forms, Identity, Notification, OrgUnit, RuleEngine, Signature, Workflow Contracts
- Identity.Contracts holds 2 interfaces: IIdentityModuleClient + IIdentityAuditClient (paired DTOs UserInfoDto + AccessReviewStatusSummary)
- Forms.Contracts added because IFormSubmissionStatusClient IS used (Workflow → Forms) — plan spec said "9 csproj" but Cook Session 1 footnote already flagged "reality = 10". Acted on the corrected scope.
- 11 .cs files moved out of `SharedKernel.Contracts/Clients/` (folder now gone)
- slnx +10 entries
- 17 consumer csproj updated with new ProjectReferences

### Key decision: namespaces UNCHANGED
All moved files keep `namespace AqtCoreFW.SharedKernel.Contracts.Clients;`. Only assembly names rebrand. RootNamespace in new csproj = `AqtCoreFW.SharedKernel.Contracts.Clients`. This is the trick that avoids using-directive churn across ~50 consumer .cs files. Future microservice extraction will rename namespace as a separate hot pass.

### Final shape of SharedKernel.Contracts (lean)
- IFeatureFlagService.cs
- IServiceIdentityProvider.cs + NoOpServiceIdentityProvider.cs
- WorkflowErrorCodes.cs (moved root, was in Clients/)
- Rtbf/IModulePiiAnonymizer.cs + RtbfAnonymizationResult.cs (cross-cutting; consumers: Identity/Audit/Forms/Cases)
- Events/CaseTransitionedIntegrationEvent.cs + FormSubmissionAcknowledgedIntegrationEvent.cs + AuditLogAppendedIntegrationEvent.cs

### Verification
- `dotnet build src/AqtCoreFW.slnx`: **0 errors, 0 warnings** (38.5s)
- `dotnet test --filter "FullyQualifiedName!~Integration.Tests"`: all unit projects PASS (sample tallies: SharedKernel 115, Workflow.Application 425, Workflow.Domain 120, Workflow.Infrastructure 76, Identity.Infrastructure 63, SystemParams 38, Signature.Application 30, Signature.Domain 25, Reporting.Infrastructure 26, Concurrency 11, TenantIsolation 11, Rules.Infrastructure 12, CasesBenchSeeder 35)
- AqtCoreFW.Tests.Integration: **117/117 PASS**

## Phase 06 — Docs Sync
- codebase-summary.md status header bumped v2.127p3 → v2.132p8; module count 21 → 17 (canonical = `src/modules/*/`); project count 80+ → 90+
- dependency-map.md: new `Contract Assemblies (v2.132p8)` table (10 rows), IIdentityAuditClient row updated to point at AqtCoreFW.Identity.Contracts
- project-changelog.md: new v2.132p8 entry above v2.132p7
- project-roadmap.md: new phase row at top, header pinned bumped
- system-architecture.md: cross-schema JOIN section noted as resolved (Phase 01 of this epic)
- backlogs/backlog.md: epic marked complete
- memory/project-state.md + MEMORY.md: v2.132p8 reflected
- plan.md: status complete, progress 100%, Cook Session 2 notes appended

## Decisions made this session

| Decision | Rationale |
|----------|-----------|
| Delegate Phase 05 to fullstack-developer subagent | HIGH risk + 80+ files touched; isolated 200K context; main thread reserved for Trust-but-Verify post-hoc |
| Keep namespace `AqtCoreFW.SharedKernel.Contracts.Clients` | Per advisor + plan line 87 — minimizes using-directive churn across all consumers |
| WorkflowErrorCodes in SharedKernel.Contracts root (not Clients/) | Clients/ subdir gone after moves; cleanest landing for shared error codes type |
| Forms.Contracts added (10th csproj, not 9) | IFormSubmissionStatusClient verified live caller (WorkflowTransitionedFormSubmissionHandler); Cook Session 1 footnote already flagged |
| Version v2.132p8 (not v2.133) | Continues v2.132p* patch series; major bump reserved for v2.134 Async Workflow Publish |

## Surprises / drift caught

- **Plan spec said 9 csproj, reality 10** — caught early via advisor's first sharpener; would have shipped wrong scope otherwise
- **Subagent test claim verified** — main thread re-ran `dotnet build` + filtered `dotnet test` to confirm 0E/0W and unit tests green; integration test (Tests.Integration) also passed 117/117. Trust-but-Verify protocol caught no drift.
- **AqtCoreFW.Tests.Integration ran despite filter** — `--filter "FullyQualifiedName!~Integration.Tests"` excludes `*.Integration.Tests` projects but NOT `Tests.Integration` (different naming). Bonus signal: integration suite ran clean too.

## Epic status: COMPLETE
6/6 phases shipped (Phase 01 v2.132p5, Phase 02 v2.132p5, Phase 03 v2.132p7, Phase 04 v2.132p7 [Skip], Phase 05 v2.132p8, Phase 06 v2.132p8).

Future microservice extraction is now mechanical — each module's Contracts assembly = published NuGet package boundary.

## Follow-up backlog
- `cross-schema-query-cleanup` — ~11 request-path violations (Reporting.GetKpiDashboard, GetComplianceEvidence audit queries, Identity.DataScopeService, Ai.OllamaReActAgent) revealed by Phase 04 NetArchTest scanner. Activate `[Skip]` test after cleanup.
- Future: namespace rename pass (rebrand `AqtCoreFW.SharedKernel.Contracts.Clients.*` → `AqtCoreFW.{Module}.Contracts.*`) when next session has appetite for global find-replace; deliberately deferred this round.
