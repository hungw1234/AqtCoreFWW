# Journal — T3-API-ENVELOPE-FE-RENDER: FE consumes & renders structured graph-validation violations

**Date:** 2026-05-26 ~20:46–22:00 ICT
**Version:** v2.137p118 (`9e6c2442` + `fce9859f` + `872bab17`)
**Mode:** `/cook T3-API-ENVELOPE-FE-RENDER --auto` + fatigue override (user explicit)
**Companions:** v2.137p117 BE slice `journal-260526-1800-*`, phase-05-closure `journal-260526-1815-*`

---

## What shipped

**Orval regen + FE violation renderer (3 commits, single slice):**

1. **Chore (Orval sync)** `9e6c2442`: `npm run generate:api` pulls live `/openapi/v1.json` → `ApiError.metadata` field + RT2-15 read-side client drift (workflow-tasks inbox/claim/unclaim hooks, new user-groups tag, TError default unknown→ProblemDetails). Additive, tsc-clean. Split from feat commit to isolate unrelated client-layer synchronization.

2. **Feature (Violation renderer)** `fce9859f`: `normalizeError` preserves `errors[0].metadata.violations` into `NormalizedApiError.violations`; `handleSave` renders multi-line Vietnamese toast (8s, capped 5 lines, `pre-line` wrapper for newline preservation) surfacing per-state rule violations + UnknownGroupCodes. Files: `web/src/core/api/api-error.ts`, `web/src/features/workflow/components/use-visual-designer-handlers.ts`, `vi.json`+`en.json` (3 new keys under workflow.designer), new `web/src/test/api/api-error.test.ts` (6 assertions).

3. **Docs** `872bab17`: changelog p118, backlog partial-close T3-API-ENVELOPE, dependency-map FE↔BE contract documented.

**Tests:** vitest 891 pass / 14 fail (all 14 pre-existing T3-FE-TESTS-DRIFT jsdom getComputedStyle; zero new regression). tsc 0. Dev stack booted (Auth:5002, API:5001, FE:5173) to verify regen + render live.

---

## Decisions & lessons

**1. Fatigue-override stayed clean (260522-M precedent valid).** 13th+ ship of the day at EOD; `/aqt-next` recommended wrap; user overrode with explicit `/cook`. Outcome: consistent with project's fatigue-floor invariant — override safe when scope bounded + gated. 4 explicit user gates this session: (a) approval of plan, (b) orval-now-vs-defer, (c) commit split, (d) feat finalization. Scope mechanical, reversible, full test coverage. No time debt created. **Reconfirm:** fatigue override works at minimal-mechanical scope when gate density ≥3 + user explicit + prior invariants honored. Fails if scope expands mid-session.

**2. Audit-at-slice-start (INVARIANT 260522-A) verified REAL-READY.** Row was new same-day but empirically audited before code touched: orval regen (gate: `/openapi/v1.json` actually emits `ApiError.metadata`?), violation wire-shape (gate: PascalCase dict keys confirmed), FE consumer (gate: prior use-visual-designer-handlers.ts exists). All 5 claims matched ground-truth file:line. Row was not a "guess ready" — pre-gated.

**3. R1 fork — OpenAPI schema shape ≠ C# contract.** Swashbuckle emits `IReadOnlyDictionary<string, object?>` as free-form `{type:[null,object]}` (nullable object) in spec. Orval regen authoritatively added the field but couldn't type-narrow the violations sub-dict. Resolution: **hand-written narrowed `GraphViolation` type in FE** for `[*].metadata.violations`. Lesson: nullable C# dictionaries don't emit fine-grained OpenAPI schemas — expect the field but don't trust the spec's dict structure; FE needs a hardened parser type regardless of regen.

**4. Orval blast-radius gate (R2: hard-gate, no side-effects).** `npm run generate:api` pulls the WHOLE live spec. Diff review found 7 generated entries touched (not just ApiError): read-side clients for inbox/claim/unclaim + user-groups tag + TError narrowing. **All legitimate accumulation** from 5 prior BE-focused slices that never regenerated the FE layer (additive, zero removed symbols). Absorbed empirically as a **separate `chore(api)` commit** rather than silking the feat. Lesson: orval regen against live spec is never "just one field" — always diff-review (chore vs feat vs breaking) + split unrelated drift into its own commit. Keeps feature intent narrow.

**5. Casing contract — silent-failure trap.** Wire path mixes camelCase outer keys with PascalCase inner dict keys (`errors[0].metadata.violations[*].metadata.UnknownGroupCodes`) because BE sets no `DictionaryKeyPolicy`. **Documented in 3 places:** type narrowing, parser test fixture, toast renderer comment. Casing typo can't silently no-render — test fixture exercises the actual camelCase-to-PascalCase deserial boundary.

**6. Review Medium, fixed in-slice.** code-reviewer (9.1/10) caught AntD `appMessage.error` (inline `message` prop) collapses `\n` → run-on. Fixed with `pre-line`-wrapped `createElement` node + 8s duration. **2 Low + 1 Nit deferred:** (a) `transitionActionName` not rendered in bullet (can upsell later), (b) structural narrowing inline comment overly verbose, (c) empty-rule-set fallback UX (low priority). Full feature shipped, polish deferred.

---

## Carry-forward

- BP-Activate `PublicMetadata` parity (BE ~5 LOC + FE renderer mirror), L1 Architecture.Tests rule validating `PublicMetadata` value type, per-state canvas highlight (richer UX), render `transitionActionName` in toast bullet, wire-shape casing contract doc in `code-standards.md`.
- **FE vitest drift unresolved** — 14 pre-existing jsdom getComputedStyle failures (unrelated T3-FE-TESTS-DRIFT row, not new). Filed as observation for next `/aqt-next` session.

---

## Unresolved questions

- Do we need BP `PublicMetadata` parity this cycle or deferred (symmetric with FE — neither surface has violations rendered yet)?
- Inline-comment verbosity for `GraphViolation` narrowing — accept or refactor?

**Status:** DONE. Slice shipped clean, zero regressions, all gates honored, fatigue-safe precedent reinforced.
