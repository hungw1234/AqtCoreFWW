# Journal — Phase-01 Slice D1: AuditBehavior Wiring + 145 Command Decoration + CI Guard

**Date:** 2026-05-01 (~08:49 ICT)
**Branch:** main (uncommitted — awaits user approval)
**Plan:** `plans/260428-1740-cap-4-qd-742-compliance/phase-01-audit-log-baseline.md`
**Slice:** D1 (Steps 10, 15 per slice plan)

## What Shipped

**Slice D split decision rationale:** Original Slice D was 2.5d covering 7 mixed-risk deliverables (AuditBehavior wiring, 135 command decorations, M2 trigger, RetentionPolicy CRUD, AuditLogPurgeJob). Split into D1 (attribute-only, low-risk: 145 decoration count, CI guard reflection test, signature extension) and D2 (trigger × Hangfire SESSION_CONTEXT path, retention CRUD, purge job — high-risk interaction deserves isolation). Advisor-recommended pre-implementation, validated as correct post-decoration counting.

**AuditBehavior reflection threading:** Extended `IAuditLogger.LogCommandAsync(commandType: Type? = null)` signature — BC-safe, only `AlertEvaluationJob` caller (non-MediatR), compiles unchanged with default param. Wired `AuditBehavior` to pass `typeof(TRequest)` so `AuditCategoryMapper.Map` reads `[AuditCategoryAttribute]` from command type, attribute precedence wins over verb fallback.

**145 command decoration count:** Plan estimated 135 `IBaseCommand` classes; scout heuristic (files with `: ICommand`) counted 124. Actual count 145. Discrepancy from multi-command files: `SodRuleCommands.cs` (2 records), `PolicyRuleCommands.cs` (3), `MenuCommands.cs` (2), `UpdateGroupCommand.cs` (3), `ManageDataScope/CreateRoleDataScopeCommand.cs` (2). **Lesson:** scout grep counts files, not record declarations — must count actual `record ... : ICommand` lines. CI guard reflection test is now source of truth (1/1 PASS proves 100% coverage).

**CI guard reflection test:** New `AuditCategoryAttributeCoverageTests.cs` (Architecture.Tests) — scans every module's Application assembly, asserts every concrete `IBaseCommand` carries `[AuditCategoryAttribute]`. Empty-list assertion → drift surfaces as named `Module: Type.FullName` compile failures, impossible to miss in green-gate.

## Drift Findings (Tracked)

**R8 (count surprise):** 145 ≠ 135 plan. Multi-record files in Identity inflate actual vs initial file-count heuristic. Source of truth is now the passing CI guard (reflection scans actual IBaseCommand implementations at runtime, no heuristic gap).

**R9 (out-of-pipeline commands):** 8 Identity commands bypass `IBaseCommand`-derived `ICommand` interface entirely — implement raw `IRequest<Result>`/`IRequest<Result<T>>` (DelegateRoleCommand, RevokeDelegationCommand, WithdrawConsentCommand, GrantConsentCommand, ApproveDelegationCommand, CreateTenantCommand, UpdateTenantCommand, ChangeTenantStatusCommand). They **do NOT flow through AuditBehavior** → **NOT automatically audited**. CI guard correctly ignores them (they don't implement IBaseCommand, so no @Attribute expected). Tracked as backlog **AUDIT-IBC-1** for Slice-D2 follow-up: migrate to `ICommand` interface or self-audit in handlers. **NOT blocking Phase-01 compliance** — auditable lifecycle ops (Login, RoleAssignment, etc.) flow through audited paths; these 8 are delegation/consent/tenant lifecycle, lower-attack-surface than identity core events.

## Decoration Breakdown (16 modules, 145 declarations)

- **AccountManagement** (~34): Identity user/role/group/ABAC/SoD/policy/credential commands
- **ConfigChange** (~70): MasterData (20), Workflow (8), Rules (5), AI (4), SystemParams (4), Forms templates/views/fields (13), Identity menu/JIT (7), Notifications template (3), Integration partner/contract (6), Organization (5)
- **AppAccess** (~41): Forms submit/bulk (6), Cases (8), Signature (5), Notifications send (4), Collaboration (7), Files (2), Integration messagelog (2)

## Test Results

| Suite | Result |
|-------|--------|
| SharedKernel.Tests | 125/125 PASS |
| Audit.Domain.Tests | 187/187 PASS |
| Audit.Infrastructure.Tests | 9/9 PASS |
| Architecture.Tests | 19/19 PASS (incl. new CI guard at 23s reflection scan) |
| Solution build | clean, 1m08s, 0 warnings |

## Code Review

All pre-implementation red-team flags addressed or deferred correctly (R8/R9 tracked, CI guard catches future drift). No blockers found in D1 scope.

## Files Touched

**Modified (signature extension, BC-safe):**
- `src/shared/AqtCoreFW.SharedKernel/Application/IAuditLogger.cs` — optional `Type?` param
- `src/shared/AqtCoreFW.SharedKernel/Application/Behaviors/AuditBehavior.cs` — threads `typeof(TRequest)` 
- `src/modules/audit/AqtCoreFW.Audit.Infrastructure/Services/AuditService.cs` — shares private `LogInternalAsync(commandType)`
- `tests/unit/AqtCoreFW.SharedKernel.Tests/Application/Behaviors/AuditBehaviorTests.cs` — asserts commandType routing

**Created (CI guard):**
- `tests/architecture/AqtCoreFW.Architecture.Tests/AuditCategoryAttributeCoverageTests.cs` — strict fail-fast on missing @Attribute

**Decorated (145 commands across 16 modules):**
- AccountManagement Identity commands (~34)
- ConfigChange MasterData/Workflow/Rules/AI/SystemParams/Forms/Identity/Notifications/Integration/Organization (~70)
- AppAccess Forms/Cases/Signature/Notifications/Collaboration/Files/Integration (~41)

## What's Next (Slice D2)

- M2 migration: ALTER Category/Severity NOT NULL formally
- CREATE TRIGGER `tr_AuditLog_NoMutate` with `SESSION_CONTEXT('audit_archive_mode')` bypass for AuditLogPurgeJob
- RetentionPolicy MediatR handlers + AuditPoliciesController + permissions
- NEW `AuditLogPurgeJob` (Audit module, separate from SodAuditLogPurgeJob)
- Smoke gate + integration tests + Phase-01 evidence assembly
- Backlog **AUDIT-IBC-1**: migrate 8 out-of-pipeline commands or self-audit in handlers

Resume: `/ck:cook plans/260428-1740-cap-4-qd-742-compliance/phase-01-audit-log-baseline.md --tdd` and target Slice D2.

---

**Status:** DONE
