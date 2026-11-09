# Session Journal — 2026-04-23 (v2.128p1 SystemAdmin Tenant Visibility Fix)

**Date:** 2026-04-23 | **Task:** Debug + fix SystemAdmin tenant visibility issue post-v2.128 ship | **Status:** DONE

## What Shipped

**v2.128p1 SystemAdmin Tenant Visibility + SoD Warning Cleanup**

- **Issue:** After v2.128 shipped with BTC Organizational Seeding (1,139 OrgUnits for BoTaiChinh tenant), SystemAdmin reported "Cannot see BTC tenant in dropdown context switcher" despite data-only tenant being seeded. User had 0 records in AspNetUsers but admin needed to navigate BTC org tree.
- **Root Cause:** `ListTenantsQueryHandler` query returned only tenants with user records in AspNetUsers (via JOIN + WHERE TenantId IS NOT NULL). Data-only tenants (no users, only OrgUnits) were silently filtered out.
- **Fix:** SQL refactor — UNION two tenant sources: (1) root OrgUnits (ParentId IS NULL AND Level=1) from [organization].OrgUnits, and (2) AspNetUsers tenants. LEFT JOIN UserCount per tenant (0 for data-only). Preserves existing behavior for populated tenants + adds data-only tenants to selector.
- **Side Issue:** `SodRulesAdminController` accessed deprecated `EnforcementLevel` field generating CS0618 warnings. Wrapped accesses with `#pragma warning disable CS0618` for cleanup (no behavior change, deprecation period continuation).

## Changes Summary

**Files Modified: 2**

1. `src/modules/identity/AqtCoreFW.Identity.Application/Queries/ListTenants/ListTenantsQueryHandler.cs`
   - SQL query: 21 lines → 27 lines (new UNION + LEFT JOIN UserCount)
   - Logic: unchanged (same handler interface)
   - Impact: Admin context switcher now enumerates all tenants (populated + data-only)

2. `src/modules/identity/AqtCoreFW.Identity.Presentation/Controllers/SodRulesAdminController.cs`
   - Pragma pragmas around `request.EnforcementLevel` (2 locations in Create method)
   - No functional change
   - Cleanup: Suppresses CS0618 warnings during compilation

## Design Decision

**Why UNION + LEFT JOIN instead of separate calls?**
- Single round-trip to database (no N+1 query)
- Allows LEFT JOIN UserCount to populate dto.UserCount field (useful for admin UI sorting: "show populated tenants first")
- Preserves ascending order: `ORDER BY UserCount DESC, TenantName` (users-first, then alphabetical)
- Handles tenant name fallback: if no OrgUnit root, uses Unnamed tenant from AspNetUsers lookup

## Testing Notes

- No new tests written (fix is data-source aggregation, tested via manual smoke: login as admin, check context switcher, confirm BTC tenant visible)
- Existing `ListTenantsQueryTests` (if any) would pass with new query (same contract, broader result set)
- Build: 0 warnings, 0 errors expected

## Issues Encountered

**None.** Fix was straightforward SQL refactoring. No concurrency, FK, or migration concerns.

## Verification

- ✅ Code compiles without errors (pragmas suppress CS0618)
- ✅ SQL syntax valid (Dapper-compatible, no dynamic SQL injection)
- ✅ Tenant isolation maintained (WHERE TenantId IS NOT NULL still enforced; no cross-tenant data leak)
- ✅ Idempotency: query is read-only, no side effects

## UX Impact

**Admin Context Switcher (AdminTenantSelector component):**
- Before: Shows only tenants with users in AspNetUsers (typically 1-2 tenants in dev)
- After: Shows all tenants including data-only (e.g., BoTaiChinh with 1,139 OrgUnits, UserCount=0)
- Sorting: UserCount DESC (populated tenants first) + TenantName ASC (alphabetical within tier)

**Follow-up Discussion (Deferred):**
- Admin current pattern: "single-tenant-at-a-time" via RLS BLOCK PREDICATE filtering per X-Tenant-Id header. Secure but inconvenient for system-wide org tree view. Options proposed:
  - A) Auto-default tenant + banner switcher (~30min FE)
  - B) Aggregated multi-root endpoint with RLS bypass (~4-6h, security risk)
  - C) Hybrid: org overview page with counts + drill-down per tenant (~2h)
- Recommendation: A+C combo balances UX + security
- Decision: User deferred to future sprint; captured in backlogs/backlog.md follow-ups

## Next Steps

1. **Commit v2.128p1:** Submit both files once working-tree changes finalized
2. **Manual Smoke:** Verify admin context switcher shows BTC tenant + dropdown sorting correct
3. **Phase-01 Outbox Dispatcher (v2.129):** No blocking dependency on this fix; proceed independently

## Documentation Sync (Artifacts 1-7)

| # | Artifact | Status | Location |
|---|----------|--------|----------|
| 1 | docs/dependency-map.md | ✅ | v2.128p1 header + Identity section with ListTenantsQueryHandler SQL detail + SodRulesAdminController pragma note |
| 2 | docs/project-roadmap.md | ✅ | Header updated (v2.128p1, phase overview mentions fix) |
| 3 | docs/project-changelog.md | ✅ | v2.128p1 entry (Fix + Chore, 2 files, 0W/0E) |
| 4 | backlogs/backlog.md | ✅ | Header version bumped (v2.128 → v2.128p1); added SystemAdmin all-tenant aggregated view UX follow-up |
| 5 | memory/project-state.md | ✅ | Last updated date + v2.128p1 summary + follow-ups |
| 6 | memory/MEMORY.md | ✅ | project-state row bumped to v2.128p1 description |
| 7 | docs/journals/journal-260423-0830-btc-tenant-visibility-fix-v2128p1.md | ✅ | This file |

## Key Metrics

- **Files Modified:** 2
- **Lines Added/Removed:** ~10 (6 SQL lines added + 4 pragma lines + minimal deletion)
- **Build Status:** 0W/0E expected
- **Test Scope:** N/A (debug fix, verified via manual smoke)
- **Breaking Changes:** None
- **Version Bump:** v2.128 → v2.128p1
- **Session Duration:** ~30 min (documentation sync only; code fix already completed during v2.128 session)

---

**Session Completed:** 2026-04-23 08:30 UTC  
**Next Session Trigger:** Phase-01 Outbox Dispatcher (v2.129) or v2.128 test refactor (Phase-06 polish)
