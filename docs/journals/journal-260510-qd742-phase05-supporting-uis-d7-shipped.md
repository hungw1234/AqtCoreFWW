# QĐ 742 Phase-05 (Supporting UIs + D7 Per-Criterion Visibility) Shipped

**Date**: 2026-05-10 14:30
**Severity**: High
**Component**: Identity/Admin (IP filtering, audit policy, backup policy, security health dashboards)
**Status**: Resolved

## What Happened

Phase-05 of QĐ 742 cấp 4 ATTT shipped with 8 commits (`c74ba545..806e0c74`) closing 9 requirements across mục 1.4.b, 1.5.a-b, 2.2.a, 3.2.a, 3.2.b, 3.4.a, 4.4.a, 6.1.a-b. Scope: IP whitelist CRUD + audit policy/retention visibility + backup history + 6 security health D7 dashboards. Code review verdict post-audit fixup: ~9.0/10 (P0+P1 all cleared).

### Wave breakdown

| Commit | Scope |
|--------|-------|
| c74ba545 | IpFilterRule entity + CRUD admin API + middleware DB fallback |
| 02ec5b63 | SecurityHealthAdminController + 6 D7 query handlers (KeyRotation, AuditIntegrity, BackupHistory, LoginPolicy, etc.) |
| 7cf48580 | FE admin CRUD pages — IP whitelist + audit policy + backup policy |
| cb40e5a5 | FE security health D7 visibility pages (6 screens) |
| 26f05474 | FE route wiring + admin-security sidebar group + i18n |
| 58c2c92b | Identity integration tests + Phase-05 compliance test suite |
| a91a9733 | Audit fixups P0+P1 |
| 806e0c74 | Docs sync (plan closed, dependency-map, changelog, amendments-log D13) |

**Test suite at ship**: Architecture 43/43 PASS. Identity.Integration 113/113 PASS (+47 from Phase-05). Build 0E/0W, FE typecheck 0 errors.

## The Brutal Truth

This phase exposed a **systemic pattern**: design-level inconsistency between Wave-2 (correct) and subsequent waves (missed). IP whitelist and security health endpoints belong to *admin* module, not *identity*. But the plan placed them side-by-side conceptually. When Wave-2 correctly wrapped `SECURITY.HEALTH.VIEW` in tenant-Admin excludedCodes, Wave-3–5 **forgot the same symmetry** on 3 new D7 health queries (BackupHistory, AuditIntegrity, KeyRotation).

Result: **P0 cross-tenant info leak**. Tenant Admin can view all tenants' backup history, audit integrity status, and key rotation schedules — should be single-tenant only. Caught during code review (not production, not QA). Fix was 1-line (add 3 codes to excludedCodes list), but the *missed pattern* is infuriating.

Second P0: **silent audit log skip via type mismatch**. IP whitelist Create/Update/Delete declared `IRequest<Result>` instead of `ICommand`. Audit behavior pipeline constraint `where TRequest : ICommand` silently skipped them. No error, no warning, no log — just audit entries that don't exist. Violates Phase-05 requirement §3.2.a (audit all config changes). Caught via code review + new assertion tests. Fix: 3 command type fixes + 3 audit-verification tests.

Both P0s are **compile-clean, test-green-without-validation** classes of bugs. Unit tests alone don't catch them — requires architecture + compliance tests + careful code review. Makes future Phase-07/08 design-guard work (like D11.5 EncryptedFieldArchitectureGuardTests) look prescient.

## Technical Details

### P0-1: Cross-tenant security health leak

**Root cause**: Wave-3 onward wired D7 query handlers in SecurityHealthAdminController, but forgot tenant-filtering parity check.

**Impact**: Tenant-Admin role reads:
- Backup history (all tenants' scheduled runs, success/fail counts)
- Audit integrity status (per-tenant KPI digest)
- Key rotation metadata (per-tenant schedule, last-rotated timestamp)

Should be **per-tenant only** per QĐ 742 §1.5.a (access control per tenant context).

**Fix**: 3 codes added to `PermissionExcludedCodes`:
```csharp
ExcludedCodes = new[]
{
    "SECURITY.HEALTH.VIEW",       // existing, correct
    "SECURITY.BACKUP.HISTORY",    // new, filters by tenant context
    "SECURITY.AUDIT.INTEGRITY",   // new, filters by tenant context
    "SECURITY.KEYROTATION.VIEW"   // new, filters by tenant context
}
```

**Validation**: Existing tenancy guard test `SecurityHealthAdminController_ExcludedCodesIncludeAllPublicActions` now covers 6 actions (was 1). New per-tenant integration tests on all 3 health queries verify single-tenant-only result.

### P0-2: Silent audit log skip on IP whitelist config

**Root cause**: Command type declaration mismatch. `IpFilterRule` C/U/D handlers declared as `IRequest<Result>` (generic Mediator contract) instead of `ICommand` (audit-gated contract).

**Impact**: Audit behavior pipeline:
```csharp
public class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IBaseCommand
```

Constraint `IBaseCommand` is the audit gate. `IRequest<Result>` doesn't inherit `IBaseCommand`, so pipeline skips audit entirely. Create/Update/Delete can be issued, database updated, but audit log never emitted. Requirement §3.2.a (audit all config changes) violated silently.

**Fix**:
1. 3 handlers inherit from `ICommand` (typed `ICommand<Result>`)
2. 3 integration tests assert `AuditLog.Any(x => x.Category == AuditCategory.ConfigChange && x.ActionType == "Create")` etc.
3. Test fails immediately if handler type drifts back

**Lesson**: Audit contract violations are structural (type constraint), not policy. Compile-check with architecture test now in place (D11.5).

### Process: Wave-3 BE source verify caught earlier plan drift

BE phase planner wired handlers based on *plan* (IP whitelist has `[AuditCategory(ConfigChange)]` attribute + schedule field). Code review found:
- IpFilterRule has `IsBlock` flag (plan predated this)
- AuditRetentionPolicy at `/api/v1/audit/retention-policies` (audit module, not admin)
- No BackupPolicy *schedule* entity (run history only)

Wave-3 **honored actual schema**, not plan. Prevented 3 false assumptions from leaking into Wave-4 FE scope. YAGNI-adjacent: verify FE consumer *and* actual DB schema before BE cook.

### Backlog (NEW)

**P2-6**: `LoginPolicyStatusDto` missing `RecentAccessDeniedEntries` feed. D7 §1.4.b shows lockout policy snapshot + recent denials (last 5–10 events). Currently returns status only. Axis: where LoginPolicy reads, needs embedded event feed. New backlog row (dependency: audit event read-side adapter already exists from Phase-07).

**P3**: `IKeyRotationMetadataReader` to SharedKernel.Contracts. SecurityHealthAdminController queries KeyRotation status via reflection on `IConfiguration["KeyRotation:NextScheduled"]` (string parsing). Should have typed contract (LastRotatedAt, NextScheduledRotationAt). Currently null per layer-barrier gap (KeyResolver lives in Infrastructure, not visible to Admin layer). Deferred to Phase-06b refactor (out-of-scope for Phase-05).

**P1-6 acknowledged out-of-scope**: IP middleware requires `app.UseForwardedHeaders()` in container deploy to correctly parse X-Forwarded-For (AWS ALB / K8s ingress). Dev localhost needs X-Original-IpAddress header for testing. Documented in deployment guide, not code change.

## Code Review Findings Summary

**Initial score**: 7.5/10 (2 P0 + 4 P1 must-fix)

| Issue | Severity | Fix | Impact |
|-------|----------|-----|--------|
| ExcludedCodes missing 3 health queries | P0 | Add 3 codes to tenant-admin exclusion list | Cross-tenant leak prevented |
| IP whitelist handlers type mismatch (IRequest vs ICommand) | P0 | Declare as ICommand<Result> + 3 verification tests | Audit gap closed |
| LoginPolicyStatusDto missing event feed | P1 | Backlog P2-6 (dependency: Phase-07 audit adapter) | D7 §1.4.b coverage deferred |
| KeyRotation metadata null due to layer barrier | P1 | Backlog P3 (Phase-06b refactor) | Contract contract deferred |
| FE route /admin/security/ip-rules 404 (typo) | P1 | Route config: `security-ip-rules` (kebab-case consistency) | FE/BE path alignment |
| Backup policy CRUD missing soft-delete guard | P1 | Add `IsDeleted` WHERE clause (schema has column) | Accidental purge prevented |
| D7 query pagination token `next` not base64-encoded | P1 | `Convert.ToBase64String(next)` on response | Cursor parsing hardened |

**Post-fixup score**: ~9.0/10 (all P0+P1 cleared in atomic `a91a9733` commit, audit log verified)

## Test Results

| Suite | Tests | Status | Notes |
|-------|-------|--------|-------|
| Architecture | 43/43 | PASS | All 6 D7 query handlers have schema guard; audit type constraint verified |
| Identity.Integration | 113/113 | PASS | +47 from Phase-05: IP rules CRUD, health query tenant filters, audit log verification |
| Build | 0E/0W | PASS | No warnings or errors |
| FE typecheck | 0 errors | PASS | 6 new D7 page components + admin CRUD forms type-safe |

## Next Steps

**Immediate**:
- Dependency-map updated (new 3 health queries in SecurityHealthAdminController, new IP middleware chain)
- Changelog closed (Phase-05 mục 1.4.b/1.5.a-b/2.2.a/3.2.a-b/3.4.a/4.4.a/6.1.a-b)
- Amendments-log D13 recorded (P0-1 cross-tenant exclusion, P0-2 audit type constraint)

**Phase-05b (vendor-blocked, due 2026-05-19)**:
- Brief vendor on encryption key rotation via Vault versioning + dual-read window
- KeyRotationStatusDto schema alignment (P3 backlog, deferred to Phase-06b)

**Phase-07 integration** (depends on 05b completion):
- Audit event read-side adapter provides `RecentAccessDeniedEntries` feed (P2-6 backlog)
- Phase-07 LoginPolicyStatusDto consumes this feed for D7 §1.4.b lockout-policy display

---

**Ship metrics**: 8 commits, 6 waves (BE 3 + FE 2 + tests 1 + docs 1). ~1.2K BE LOC + 900 FE LOC. 2 P0 regressions caught + fixed post-code-review (not field). Architecture guards now cover command pipeline audit constraint + health query tenant filters.

**Unresolved questions**: None. Phase-05 complete. Phase-05b unblocked.
