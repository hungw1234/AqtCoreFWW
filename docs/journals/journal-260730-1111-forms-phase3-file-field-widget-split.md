# Journal — Forms Phase-3 (A): file-field widget extraction

**Date:** 2026-07-30 · **Branch:** main · **Plan:** `260730-1040-forms-phase3-component-split`

## What happened

User directed `/cook "Forms Phase-3"`. Phase-3 was under a **HARD GATE = STOP** (2A ≥50-concurrency
load-test + real-path concurrent-submit test both absent — 2/4 gate boxes red). Surfaced the conflict
rather than silently opening it; user **consciously overrode** the gate. Recorded the override in the
parent plan's phase-03 file (not silent). Then narrowed scope to part (A) component-split — part (B)
UX behavior needs baseline metrics dev traffic cannot supply, so B deferred. (The scope question
timed out with the user away → chose A on best judgment: A has an oracle, B does not.)

## Delivered

- Extracted the inline `FileFieldWidget` + `parseFileRules` from `form-field-renderer.tsx`
  (**377→170 lines**) into `web/src/features/forms/builder/file-field-widget.tsx` (197 lines).
- Matches the established convention: every OTHER field-type widget already lives in its own file;
  File was the last one inline.
- **Pure move** — code-review machine-diffed the block as byte-identical to the original except the
  intended `export` keyword. `renderInteractiveField` signature unchanged → all 7 consumers untouched.
- Verify: typecheck clean, eslint clean (0 unused imports left behind), `vitest run src/features/forms`
  = 7 files / 80 tests pass incl. `form-snapshot-adapter.test.ts` (render identical — the oracle).

## Non-obvious

- The refactor's real risk is the **full-replace import prune**: moving a big widget leaves imports
  used only by it (Upload, Progress, useUploadFile, file-lifecycle, the `Link` Typography destructure…).
  eslint's no-unused-import is the guard that the prune was complete — a clean lint = correct prune.
- Splitting a widget out is safe here BECAUSE the public surface is a single function
  (`renderInteractiveField`), not the widget itself — the widget was already file-internal (0 external
  imports), so extraction cannot touch any consumer.

## Scope status

Phase-3 part A file-field split: DONE. Still PENDING under the same override:
`sign-submission-document-button.tsx` split (420 lines, a separate slice) + part B UX behavior
(drag feedback, wizard nav, inline validation — needs metrics). HARD GATE remains formally red;
proceeding only under the user's explicit override.

## Session arc note

This was the 4th cook of the day. Unlike the prior three (all doc/config, environment-gated
blockers), this one shipped real FE code with a clean oracle — but it is FEATURE work opened against
a red launch-gate by explicit user choice, not launch-critical-path.
