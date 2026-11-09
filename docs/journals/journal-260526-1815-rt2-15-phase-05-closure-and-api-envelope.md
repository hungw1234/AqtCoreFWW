# Journal — RT2-15 Phase 05 closure + API error-envelope structured violations

**Date:** 2026-05-26 ~12:40–18:15 ICT
**Versions:** v2.137p116 (slice A, `8318d538`) + v2.137p117 (slice B, `f907e0ca`)
**Mode:** `/cook phase-05 --auto`, 2 slices / 2 commits (user-gated)
**Companion:** slice B has its own deep-dive `journal-260526-1800-api-envelope-structured-violations-slice-b.md`

---

## What shipped

**Slice A (p116) — RT2-15 Phase 05, closes the deferred-clause-mode arc.** Test + doc only, zero production code:
- Cross-mode **R4-lift matrix sweep**: 2 parameterized `[Theory]` (Save + Activate) over the 4 lifted modes (PreviousHandler/ByRole/ByGroup/ByOrgUnit) = 8 facts asserting no R4 "not supported" fires. A regression guard: re-populating `_notSupportedModes` breaks the matrix loudly.
- **FU-M1 anti-tautology** (`CrossMode_SharedRefId_PrincipalTypeGatesIndependentlyOfRefId`): C1/C2 used distinct PrincipalRefId per arm, so they couldn't prove `PrincipalType` is load-bearing. C3 seeds both candidate-index arms with the SAME RefId, differing only by PrincipalType → proves the read-side predicate gates on type, not RefId divergence.
- **Ops audit-kit refresh** (`phase-05b-empty-pool-preflight-audit.md`): modes 2/4/5/6 are now supported producers, so the doc's Q3/Q5 "NotSupported modes" SQL would have produced ops false-positives. Q3 reduced to Intersect-only, Q5 deprecated, mode legend + dispositions corrected, "data-dependent empty pool" note added.
- `_notSupportedModes = []` fail-closed guard **retained intact** (removing it would regress safety for future enum modes).

**Slice B (p117) — T3-API-ENVELOPE BE.** SharedKernel error-envelope contract evolution:
- `ApiError` gains an optional `Metadata` field (JsonIgnore-when-null, additive).
- `ApiResponse.Fail` surfaces ONLY a reserved `PublicMetadata` key — **default-deny opt-in**.
- `WorkflowErrors.GraphValidationFailed` projects violations to the wire via `PublicMetadata`; `GraphErrors` stays internal/log.
- V11 + Phase 02 ByGroup I4/I5 assert the end-to-end wire path. FE rendering deferred → `T3-API-ENVELOPE-FE-RENDER`.

**Tests:** slice A 753/753 (3 assemblies); slice B full cross-assembly 1216/1220 (4 pre-existing skips), 0 fail. Debug 0E/0W both. Reviews 9.5/10 (A) + 8.5/10 (B).

---

## Decisions & lessons

**1. Plan-doc decay, again (260524-E reinforcement).** The phase-05 doc claimed E-Q2 (runtime `NoHandlerResolved` dual-meta) was a ≤10 LOC metadata-bag extension. Slice-start scout disproved it: `HandlerResolution` is a binary record carrying ZERO per-mode info when empty, and the 6 `NoHandlerResolved` call-sites have no per-mode counts. Real cost is a resolver-hot-path refactor. **Descoped to backlog `RT2-15-PHASE-05-EQ2`** (observability-only, non-blocking). Lesson: a ≥2-day-old phase doc's effort claims decay — re-scout the named code before trusting the estimate.

**2. The `/aqt-next` E-Q2 mislabel — caught + corrected.** My own `/aqt-next` recommendation conflated TWO different "dual-meta" mechanisms: the phase-doc's runtime `NoHandlerResolved` keys (E-Q2) vs the Save-time `ApiResponse` envelope (the separate `T3-API-ENVELOPE` row). The user's "Phase 05 full" choice was made on that mislabel. Surfaced the discrepancy via a clarifying gate rather than silently picking — user chose "both, 2 commits", then "descope E-Q2 from slice A". Lesson: when a recommendation bundles items under one label, verify each maps to the same artifact before the user commits to it.

**3. Slice B security pivot (advisor).** Initial framing offered default-allow (exclude-reserved passthrough) vs allowlist-in-SharedKernel. Both wrong: default-allow leaks internal error metadata across the all-endpoints blast radius; allowlist forces SharedKernel to know domain keys (layering violation). Advisor's **default-deny opt-in via a single reserved `PublicMetadata` key** wins both — SharedKernel knows one key, errors opt in explicitly, producer controls the wire projection. The advisor-before-design call prevented shipping a security smell into a contract that touches every endpoint.

**4. H1 BP-Activate parity — verified NOT a regression.** Reviewer flagged `ActivateBusinessProcessCommandHandler` sets `GraphErrors` but not `PublicMetadata`, with a comment claiming FE highlighting. Verified the actual history: the pre-slice `ApiResponse.Fail` never extracted `GraphErrors` either — so it was NEVER on the wire, for any surface. The comment was pre-existing-aspirational, not broken by this slice. Disposition: deferred BP wire parity to the FE-render row (symmetric — no FE consumer renders violations on either surface yet; folding would duplicate the projection across a module boundary), and corrected the misleading comment now. Lesson (review-audit §1): verify a flagged "regression" against real history before fold-vs-defer.

**5. Cross-assembly test gate for SharedKernel changes.** `WF-TESTER-FULL-ASSEMBLY-GATE` plus: an `ApiError` field addition can break shape/equality assertions in ANY module. Ran the full cross-assembly sweep (Workflow, SharedKernel, Contracts, Forms, Identity, BusinessProcess, Architecture) + a pre-run shape-break scan. Nothing broke (additive optional field + JsonIgnore-when-null). The discipline doesn't auto-extend from a scoped slice — blast radius dictates test breadth.

**6. Discipline that held.** Audit-at-slice-start (260522-A) confirmed `_notSupportedModes=[]` empirically + corrected the phase-doc's stale test paths (`src/tests/` → `tests/`). Reverted the implementer's manual edit to an orval-generated file (anti-pattern) and folded the whole FE arc (orval regen + renderer) into the follow-up row. Single-SHA per slice (260523-D); plan files gitignored so plan-sync stayed local.

---

## Carry-forward

- **Push pending:** both commits (`8318d538`, `f907e0ca`) local on main, not yet pushed (batched).
- `RT2-15-PHASE-05-EQ2` (P3) — runtime dual-meta, needs HandlerResolution refactor decision (diagnostic field vs call-site re-derive).
- `T3-API-ENVELOPE-FE-RENDER` (P4) — orval regen + violation renderer (UX decision) + BP-Activate wire parity + the mixed camelCase/PascalCase wire-shape contract.
- Optional L1: Architecture.Tests guard that every `PublicMetadata` value is `IReadOnlyDictionary<string, object?>` (the `Fail()` `is`-match silently nulls otherwise).
- Pre-existing CS4014 in Release `TreatWarningsAsErrors` config (unrelated; predates these slices).

**RT2-15 deferred-clause-mode arc: COMPLETE** (5 phases, p108→p116).
