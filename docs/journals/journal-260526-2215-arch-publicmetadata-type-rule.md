# Arch Rule: PublicMetadata Narrowing Cast Guard

**Date**: 2026-05-26 22:15
**Severity**: Low
**Component**: SharedKernel Architecture Rules / API Response Envelope
**Status**: Resolved

## What Shipped

v2.137p119 commit `cca9a025` — `Rule_PublicMetadata_WithMetadata_Value_MustCast_To_IReadOnlyDictionary` + 5-row `[Theory]` anti-tautology test added to `AqtCoreFW.SharedKernel.Tests/Architecture/SourceLevelRulesTests.cs`. Uses existing `SourceCodeScanner` (regex-based text scan of `src/**/*.cs`, skips comments, whitelists obj/bin/tests/Migrations/Seeding) to enforce inline `(IReadOnlyDictionary<string, object?>)` cast at every `WithMetadata("PublicMetadata", …)` call-site. Zero production code. SharedKernel.Tests 199→205 (+6), full build 0E/0W.

## The Brutal Truth

Slice-start audit (260522-A protocol) revealed the backlog row's premise had **zero factual gap**. (a) "V11 guards Workflow.Domain" — V11 exists but it's a UNIT test, not arch rule, covering only ONE existing call-site already correctly cast. (b) "BP.Domain path unchecked" — path doesn't exist; BP-Activate has only a *comment* explaining BP intentionally defers PublicMetadata; wire-surfacing is future backlog row with zero consumers. The rule is **forward-looking insurance**, not a fix. Frustrating because it ate ~40min of a 16-ship day on correctness-theater instead of impact.

## Technical Details

`IReadOnlyDictionary<TKey, TValue>` is invariant in TValue. `ApiResponse.Fail` uses pattern match `is IReadOnlyDictionary<string, object?>` to extract reserved "PublicMetadata" key for wire projection. A bare `Dictionary<string, object>` or user-constructed dict with non-nullable value type SILENTLY fails the match at runtime → null metadata on wire. No exception, no log — easy to miss.

**Slice-start finding:** backlog row claimed existing gaps in Workflow.Domain and BP.Domain paths. Audit grep found:
- `WorkflowErrors.cs:156` — single existing call-site, already correctly cast `(IReadOnlyDictionary<string, object?>)`
- `ActivateBusinessProcessCommandHandler.cs:78` — not a call-site, only a comment explaining BP intentionally omits PublicMetadata
- No BP-Activate wire-surface consumer exists (deferred to future backlog row)

CONCLUSION: the invariant exists in production only at one location and it's already correct. The rule prevents a theoretical future mistake.

## What We Tried

Option A (rejected): Backport arch rule to cover existing gaps. Audit found no gaps.

Option B (accepted): Forward-looking insurance. Wire the rule as-is, document it's preventive, not corrective. User gated via AskUserQuestion "proceed-minimal" (rule-only, no audit-driven changes).

## Root Cause Analysis

Backlog row written when BP-Activate API contract was still in flux (v2.137p117). Drift between row-date and audit-at-slice-start: author's mental model of "unchecked paths" was preempted by v2.137p118 FE consumer arrival, which forced the envelope closure and exposed no production gaps. Row was directionally correct (PublicMetadata needs type guards) but specific target paths stale.

Lesson: backlog rows older than 3–5 days require empirical 260522-A probe; "future gap" rows specifically need scope-check at slice-start (audit grep call-sites, not just comment presence).

## Lessons Learned

1. **Reflection can't see static types.** Arch rule via reflection (Castle.DynamicProxy, Roslyn AST) cannot inspect a call-site argument's static type signature. Only hand-rolled text/regex scanner can find `WithMetadata("PublicMetadata", …)` patterns on source lines. Mirrors prior AUTHSERVER-DI-ARCH-ALT rejection ("static analysis can't validate runtime/type facts it can't see").

2. **Inline-only cast requirement.** The rule enforces the cast MUST be inline on the call line — a correctly-typed named-local variable (`IReadOnlyDictionary<…> pm = …; WithMetadata("PublicMetadata", pm)`) would RED the rule. Trade-off: KISS over multi-line AST tracking. No suppress hook wired (could add if future code legitimately uses indirection, but none exists today).

3. **Forward-looking rules are theater without user gate.** Shipping a rule for zero detected gaps is correctness-theater unless the user has *explicitly* chosen to harden a likely future path. The AskUserQuestion gate ("keep rule as-is? scope narrowing? YAGNI-close?") was essential. User chose "proceed-minimal" → rule ships, no sweeping changes. Good discipline.

## Next Steps

- Backlog row (a) T3-API-ENVELOPE-FE-BP-ACTIVATE remains P4 dependent on BP-Activate production consumer arrival (not in scope for T3 arc).
- Docs: dependency-map.md synced + carry-fwd marked DONE; changelog v2.137p119 + backlog row resolution recorded.
- Monitor: any future `WithMetadata("PublicMetadata", …)` calls outside `Workflow.Domain` will auto-RED at build if not cast. No action needed unless a new domain adopts PublicMetadata.

---

**Status:** DONE
**Summary:** Arch insurance rule shipped v2.137p119; slice-start audit found zero production gaps (row premise stale); forward-looking prevention ratified via user gate; test suite +6 (all pass).

## Unresolved Questions

None. Audit conclusive, user gate decisive, rule scope narrow.
