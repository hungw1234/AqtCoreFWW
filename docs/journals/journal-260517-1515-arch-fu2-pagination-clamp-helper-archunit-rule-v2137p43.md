# ARCH-FU2: Pagination Clamp Helper + ArchUnit Rule

**Date**: 2026-05-17 15:15 ICT
**Severity**: Medium
**Component**: Architecture / SharedKernel
**Status**: Resolved

## What Happened

Shipped ARCH-FU2 (v2.137p43): centralized pagination clamp helper + source-text scanner rule to prevent unclamped pageSize handlers from breaking FE callers. 3 new files; 50min wallclock.

## The Brutal Truth

ARCH-FU was filed as "Medium ~3-4h: blanket sweep ~25 unclamped handlers + rule". Advisor caught at scout-end that a naive blanket 100-cap would break FE pickers:
- `useRuleSets({ pageSize: 9999 })` @ rule-set-list-page.tsx:40
- `useBackupRecords(pageSize = 9999)` @ backup-api.ts:22  
- `useWorkflowTaxonomies "pageSize=200"` @ use-workflow-taxonomies.ts:56

Silent truncation in production. Scope was **wrong**, not implementation timing. Had to pivot from "sweep all handlers now" to "enforce rule on new code + allowlist grandfathered violations".

## Technical Details

**3 files shipped:**
1. `PaginationDefaults.cs` — AdminMax=100, PickerMax=1000, DefaultPageSize=20 + `Normalize(page,pageSize,max):(int,int)` helper.
2. `PaginationClampScanner.cs` — regex `\b(request|query|req|q|cmd|filter)\.PageSize\b` mirroring `CrossSchemaSqlScanner` pattern. Compliant if file has Math.Clamp/Math.Min/PaginationDefaults/InclusiveBetween(1,). Skips Migrations/bin/obj/Controllers/Commands. 31 grandfathered handlers allowlisted.
3. `PaginationClampScannerTests.cs` — 2 facts: no new violations + count snapshot (31).

**Test Results:** Architecture 97→99 PASS, SharedKernel 199/199 PASS. Build 0E/0W.

## What We Tried

**Initial approach:** Blanket sweep all 25+ handlers to Math.Clamp(pageSize, 1, 100). **Why it failed:** Would have silently capped FE rule-set picker at 100 items; pickers expect 1000-item headroom.

**Corrected approach:** Helper + rule + allowlist with count snapshot. New handlers enforced at write-time; existing violations grandfathered with decrement-count forcing function.

## Root Cause Analysis

Two blind spots in the original brief:
1. **Cross-module dependency inversion:** Handler pagination caps must respect FE caller intent. A 100-cap for admin CRUD isn't appropriate for picker operations (200-1000 range). No one-size-fits-all tier.
2. **Allowlist-as-forcing-function weakness:** Simple allowlist (like CrossSchemaSqlScanner) with zero enforcement mechanism. Each future sweep PR must: clamp handler + decrement count + remove allowlist entry. Snapshot test makes this automatic.

Advisor caught this by grepping FE callers before code. Same pattern saved 8h in morning (premise verification on ARCH-FU/TEST-FU).

## Lessons Learned

**L1 (Advisor-at-scope-confirmation):** Calling advisor right after scout step (before edits) catches FE-caller dependency. Prevents silent regression. 2nd advisor save in 24h.

**L2 (Allowlist + count snapshot = forcing function):** Pairing allowlist with strict count test makes silent expansion impossible. Test failure surfaces allowlist bloat.

**L3 (YAGNI on tier count):** Shipped 2 tiers (AdminMax/PickerMax). Memory said 3; real grep showed only 2 vary in practice. 3rd tier emerges when sweep reveals 2+ handlers diverging same direction.

**L4 (False-positive layering):** Controllers/ and Commands/ exclusions necessary. Controllers forward to clamped handlers; Commands reference ViewDefinition.PageSize config field. Scanner focus = handler layer only.

## Next Steps

**Immediate:** Nothing. Helper + rule shipped; Architecture tests green.

**Batch PRs (future ARCH-FU2.x tasks):**
- Per-module handler sweeps (e.g., ARCH-FU2.Rules, ARCH-FU2.Forms) with:
  - Clamp decision (AdminMax vs PickerMax) verified per FE caller
  - Allowlist entry removal + count decrement
  - Zero-handler regression test

**Timeline:** No forcing function; sweep as modules ship features requiring new handlers. Risk: allowlist grows if sweep stalls. Mitigation: Periodic (quarterly) full-codebase grep for orphaned allowlist entries.

---

**Status**: DONE  
**Summary**: Shipped pagination clamp helper + ArchUnit scanner rule with grandfathered allowlist. Advisor reconciliation caught FE-caller dependency before code write, pivoting scope from blanket sweep to enforced-on-new + allowlist. 2 tiers (AdminMax/PickerMax) based on actual grep, not prediction. Architecture 99/99 PASS. Carry ARCH-FU2.x batch sweeps forward per-module with FE verification.
