# Session Journal — BP MVP P1-FU RepublishFormTemplate SHIPPED (v2.137p24)

**Date:** 2026-05-14 ~22:10 ICT
**Branch:** main
**Commits:** `47c5ca07` (feat) + `aca91e26` (docs) on origin/main
**Session length:** ~45 min (scout → plan → implement → test → review → docs → commit → push)
**Ship count today:** 9th distinct ship (post-fatigue user override)

## Session Goal

Close BP MVP P1-FU `RepublishFormTemplate` filed during P1 ship (`ef5491f4`). User overrode `/aqt-next` EOD recommendation despite fatigue heuristic firing strongly (25 commits today, clock 21:38 ICT). Bounded scope acceptance: no migration, no cross-module wiring, no FE — mirror freshly-shipped Publish precedent.

## What Worked

- **Advisor BEFORE plan** locked Option-A version-bump strategy (always bump in domain method) — saved a retry cycle. Three options table considered: (A) unconditional bump in `Republish`, (B) max-query + fail-on-dup, (C) bump-only-if-AddField-fired. Picked A for KISS; documented "VersionNumber = snapshot label" semantic to defuse future confusion.
- **Mirroring P1 handler shape exactly** — `AddVersionAsync` stages → `UpdateAsync` triggers single SaveChanges. Factory `Snapshot()` reused state-agnostically. Atomicity contract identical to Publish, so code-reviewer parity check passed cleanly (9.3/10).
- **Scout-first via Explore agent** — single 12-tool-call dispatch returned all 8 enumerated facts with paths + line numbers. Implementation had zero "wait what's the signature again" detours.
- **Defense in depth on unique index** — domain test `TwoSequentialCalls_ProduceMonotonicVersions` + handler test `Handle_TwoSequentialRepublish_ProduceDistinctVersionNumbers` double-lock the `(FormTemplateId, VersionNumber)` constraint against rapid republish race.

## What Didn't / Lessons

- **FluentAssertions `WithMessage` wildcard footgun.** 4 domain tests failed first run because `InvalidFormSchemaException.Message` prepends `"Invalid form schema: "` — my patterns `"GOV_FRM_010:*Active*"` couldn't match `"Invalid form schema: GOV_FRM_010: Chỉ republish..."`. Fix: leading `*` (`"*GOV_FRM_010*Active*"`). **Codify:** when domain exceptions have framework prefix, always use leading `*` in `WithMessage`. Saved to memory `bp-mvp-p1-fu-republish-shipped-260514.md`.
- **CRLF warnings on new .cs files** — harmless, `.gitattributes` normalizes next touch.
- **PreToolUse hook misfire 3× for `.cs` files** — hook guidance about kebab-case was always followed (C# uses PascalCase per language convention). Treat as ignorable for `.cs`.

## Decisions Made

1. **`VersionNumber = snapshot label (monotonic counter)`** — NOT distinct-schema-state count. Two consecutive Republish with zero edits between still produce v_n and v_n+1. Documented in domain XML doc + handler comment + changelog + memory.
2. **State guard: Active-only.** Draft → use Publish; Materializing → transitional; Inactive → no Reactivate path (out of scope). 4 explicit negative-state tests at domain + 3 at handler.
3. **No materialization queue re-trigger.** Active Table templates already have backing storage; DDL diff/apply is post-MVP. Documented in handler XML doc.
4. **No retroactive snapshot backfill.** Drift v2..v_(n-1) lost forever. Acceptable because BP renderer pins to explicit `VersionNumber` per BP config (P3); ad-hoc submissions fall back to live template state. Documented as known limitation.
5. **2 P1-FU-FU items filed (LOW from code-review)**: L1 MFA-sweep across Publish/Republish/Deactivate/Duplicate (consistency with admin destructive ops); L3 (AddField → Republish) regression-lock handler test asserting snapshot VersionNumber == post-bump (currently covered indirectly).

## Test Status

| Suite | Result | Delta |
|---|---|---|
| Forms.Domain | **123/123** PASS | +7 (`FormTemplateRepublishTests`) |
| Forms.Application | **151/151** PASS | +8 (`RepublishFormTemplateCommandHandlerTests`) |
| Architecture | **97/97** PASS | unchanged |
| Build solution-wide | **0E / 0W** | clean |
| Code-review (independent agent) | **9.3 / 10** | 0 critical/high/medium, 5 LOW (all documented or deferred) |

## Next Session

1. **P2 — FormSubmission `WorkflowInstanceId`/`StateId` linkage** (0.5d BE) — natural sequential next per BP MVP dep graph. Memory note: P2 reset to `pending` per Validation D2 (Session 1); only `WorkflowInstanceId` field shipped early. Still missing: `WorkflowStateId`, migration, factory wiring, `IFormSubmissionRunClient` contract.
2. **P1-FU-FU items** (defer unless triggered): MFA-sweep on admin destructive ops; (AddField → Republish) regression-lock test.
3. **Late-evening shipping discipline rule** (codified to memory): late-evening shipping OK ONLY when (a) bounded scope, (b) mirror freshly-shipped precedent, (c) no DB schema changes, (d) advisor pre-checked design. P1-FU satisfied all 4; any future late-night candidate must too.
