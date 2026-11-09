# ARCH-FU2.1 Drop Math.Min as Pagination Defense Token

**Date**: 2026-05-17 15:35 ICT  
**Severity**: Medium  
**Component**: Architecture / Pagination Clamping Rules  
**Status**: Resolved  
**Version**: v2.137p44 (`d980c03c` + `08db8d10`)

## What Happened

ARCH-FU2.1 strengthened the pagination defense rule by removing `Math.Min` as an accepted defense token. `Math.Min(pageSize, max)` only caps the upper bound—it silently passes `PageSize <= 0` through to SQL, causing empty pages instead of 4xx validation errors. The rule now requires **two-direction clamping** via `Math.Clamp(pageSize, 1, max)`.

Scope: 7 files, ~30min wallclock. Single `src/` handler using `Math.Min`-only pattern → migrated to `Math.Clamp`. Scanner allowlist updated. Code standards doc + architecture test assertions + backlog/changelog finalized. Tests green across all modules (Architecture 99/99, PaginationClampScanner 2/2, Workflow.Application 433/433).

## The Brutal Truth

This is a **tight cleanup**—the kind that feels unnecessary until you realize the original rule had a real gap. `Math.Min` is widely taught as a legitimate clamp pattern; discovering it wasn't sufficient for **pagination semantics** (where PageSize=0 is genuinely invalid) required explicit reasoning about what "defense" means in context. ARCH-FU2 accepted it; ARCH-FU2.1 explicitly rejects it. Small scope, high confidence, no surprises.

The real friction point: **memory carry-over vs. physical state**. Memory phrasing listed FE hooks by name (`useBackupRecords`, `useWorkflowTaxonomies`) as reasons for allowlisting. Backlog—with same commit comments—referenced them too. **At edit time, audit showed:**
- `useBackupRecords` has **zero corresponding handler** in the allowlist (Backup module lives in shared/Infrastructure, scanners skip Controllers anyway). Mismatch between stated reason + actual allowlist entry.
- `useWorkflowTaxonomies` actually depends on FE-side default ~200, not 9999. Memory phrasing was wrong.

This isn't a bug—it's a pattern: **memory captures intent at write-time; physical code at ship-time can drift**. Codifies to: treat memory carry-over like backlog entries—**scout before committing to named reasons**.

## Technical Details

**Single Consumer Migration:**
```csharp
// Before (GetMigrationItemsQueryHandler.cs:24)
Math.Min(request.PageSize, 200)

// After
Math.Clamp(request.PageSize, 1, 200)
```

Only handler in entire `src/` using `Math.Min` on PageSize. Grep verified zero other instances.

**Scanner Changes (PaginationClampScanner.cs):**
- Removed `"Math.Min"` from `DefenseTokens` array.
- Updated XML doc: added ARCH-FU2.1 rationale (Math.Min weakness: upper-bound only, allows PageSize≤0).
- Updated field comment on allowed grandfathered handlers pointing to FE 200-cap: `WorkflowActionTaxonomyQueries`, `WorkflowStateTaxonomyQueries` (hook `useWorkflowTaxonomies` FE-default ~200, not 9999).

**Code Standards Update (docs/code-standards.md):**
- New Anti-pattern subsection: `Math.Min(pageSize, max)` caps upper bound only; allows PageSize≤0 silent pass-through to SQL.
- Canonical pattern: `Math.Clamp(pageSize, 1, max)` enforces both boundaries.
- Reference: ARCH-FU2.1 journal + decision date.

**Test Assertion Parity (N-1 Fold):**
- `PaginationClampScannerTests.cs` failure-message string updated to match new doc: removed `Math.Min` from allowed token list in error text.
- Codifies: XML doc + test failure-message must move in lockstep (inconsistency only surfaces when test fails—rare path, easy miss in review).

## Lessons Learned

**L1: Single-Consumer Token Deprecation Pattern**
When a rule accepts multiple tokens and one becomes unwanted, audit handlers using **only the unwanted token** first. Convert before dropping the token; otherwise count-snapshot test flips green→violated. Single grep (3min) found exact consumer; verification complete before edit. **Generalizes:** any rule-token deprecation follows this pattern.

**L2: Doc-Runtime Parity**
XML doc + test failure-message string must stay synchronized. Inconsistency only triggers on rare test failure; easy miss in code-review. **Protocol:** when describing a defense-token / rule rationale in XML doc, grep for same phrasing in test assertions and update in lockstep.

**L3: Memory Carry-Over ≠ Physical State**
Memory phrasing named `useBackupRecords` + `useWorkflowTaxonomies` as FE-dep reasons for allowlist entries. Scout revealed: `useBackupRecords` had zero handler entry (Backup module excluded by scanner anyway), and `useWorkflowTaxonomies` cap was ~200, not 9999. Memory captured intent at write-time; code drifted by ship-time. **Codifies:** treat memory carry-over reason-phrasing the same as backlog entries—**scout physical state before committing to named reasoning**. Third occurrence this week (pattern established).

**L4: Low Estimate Holding at Realistic Effort**
~30min actual vs. Low estimate. First time today estimate matched wallclock (vs. morning's 4-5x compressions). Small scope + well-defined + advisor pre-cleared = no compression headroom. **Validates:** compression ratios correlate with planning maturity; well-scoped work compresses little.

**L5: Ship Cadence at EOD**
Ship #11 of day ~15:35 ICT. Prior memory recommended wrapping at EOD; FU2.1 was follow-up cleanup to ARCH-FU2 (same reviewer concern M-1). Small enough to proceed; natural boundary after completion. Test fatigue is empirical, not numeric caps—19 commits / 11 ships over ~8h without QoL regression.

## Root Cause Analysis

ARCH-FU2 review flagged `Math.Min` as partial defense (M-1). `Math.Clamp` was already in the codebase (Identity module precedent: `ListSodViolationLogsQueryHandler`). **The oversight:** accepting `Math.Min` without modeling PageSize≤0 as invalid. Pagination semantics ≠ general numeric clamping. Once articulated, fix is trivial (1 handler, 1 line) + rule tightening (1 token removal).

## What We Tried

None—scope was pre-validated:
1. Grep'd `Math.Min.*PageSize` → single match (Workflow handler).
2. Checked whether removal would fail count-snapshot test (yes, but that's expected).
3. Updated handler, scanner token list, doc, test assertions, backlog/changelog in sequence.
4. Ran architecture test: green 99/99.

No alternative branches explored—validation via advisor review + memory already closed the design question.

## Next Steps

- **ARCH-FU2.x sweep**: Audit remaining modules per allowlist (5 handlers in Workflow at AdminMax=100; similar passes through Forms, BP). ~3-5 FUs backlog-filed.
- **N-2 nit (optional)**: Add XML doc traceability link to `97071a25` (ARCH-FU2 original journal). Low value; do not file backlog row.
- **EOD wrap**: Natural boundary; memory prior advisory stands. Session fatigue monitoring empirical (no regression observed across 11 ships).

---

**Test Summary:**  
Architecture 99/99 PASS (12s)  
PaginationClampScanner 2/2 PASS (371ms)  
Workflow.Application 433/433 PASS (0.7s)  
Build 0E/0W

**Review:** 9.6/10 DONE_WITH_CONCERNS (N-1 test-doc parity folded inline; N-2 optional traceability deferred)

**Cadence Today:** 20 commits / 11 feature ships ~8h total  
`cc620a76`(P7-FU1.2) → `fac04940`(ARCH-FU) → `ae4814d2`(TEST-FU) → `6b04d679`(P8-FU1) → `5cc8e8d4`(P8-FU2 BE) → `264b91a9`(P8-FU2.1 FE) → `6eebf8a2`(P8-FU2.1.1 UX) → `9054f860`(P7-FU1.1) → `ca321a97`(P7-FU1.1-FU) → `71273fbc`(ARCH-FU2) → `d980c03c`(ARCH-FU2.1)
