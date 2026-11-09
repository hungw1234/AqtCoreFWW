# T5-18 Batch Transition Confirm Dialog — UX Safety Guard for >20 Selections

**Date**: 2026-04-14 10:41
**Severity**: Low
**Component**: web — `workflow-batch-action-toolbar.tsx`, i18n locales, new test file
**Status**: Resolved

## What Happened

Shipped the soft-threshold confirm intercept for batch workflow transitions. Selecting
>20 items now triggers an AntD `Modal.confirm` before the main transition modal opens.
Hard cap (MAX_BATCH=50) stays as the terminal disable — this is a purely additive
safety layer. Estimate was 0.5d; actual ~0.3d. TDD kept the scope honest.

## The Brutal Truth

This was the easy task of the day — clean design, no architectural ambiguity, no BE
dependency. The small irritation: first test run failed on all 4 cases because
`ThunderboltOutlined` bleeds its aria-label into the button's accessible name, so
`getByRole('button', { name: 'workflow.batch.actionButton' })` matched nothing. Had
to swap to a regex partial match. Thirty seconds to diagnose, thirty seconds to fix —
but it's the kind of thing that makes you question whether AntD's icon insertion
strategy was ever reviewed by anyone who tests UIs. It wasn't a project mistake; it's
just AntD being AntD.

Cosmetic jsdom warnings about `getComputedStyle` scrollbar measurement from AntD Modal
were noise — tests green, ignore.

## Technical Details

- **Commit**: `5b0dce84` — `feat(web): confirm dialog guard for batch transitions >20 selections (T5-18)`
- **Files changed**: 5, +160/−8, pushed fast-forward `ae448ac6..0b31937f` → `origin/main`
- **Threshold constant**: `CONFIRM_THRESHOLD = 20` in toolbar component — not exported,
  not env-configured; plain const for now.
- **Handler pattern**:
  ```ts
  const handleActionClick = () => {
    if (selectedCount > CONFIRM_THRESHOLD) {
      Modal.confirm({ title, content, okText, cancelText, onOk: () => setModalOpen(true) });
    } else {
      setModalOpen(true);
    }
  };
  ```
- **i18n keys added** (`workflow.batch.*`):
  - `confirmTitle`, `confirmContent` (with `{{count}}` interpolation), `confirmOk`
  - Both `en.json` and `vi.json` updated — vi copy asserts "không thể hoàn tác"
- **Test file** (NEW): `web/src/test/features/workflow/workflow-batch-action-toolbar.test.tsx`
  - 4 TDD tests; key pattern: `vi.spyOn(Modal, 'confirm').mockImplementation(cfg => { captured = cfg.onOk; ... })`
  - Avoids jsdom portal DOM assertions entirely — deterministic, portable to future imperative dialogs

## What We Tried

- First test selector: `getByRole('button', { name: 'workflow.batch.actionButton' })` — failed,
  icon aria-label contaminating accessible name.
- Fixed to: regex partial `/workflow\.batch\.actionButton/` — green immediately.
- Runtime OpenAPI snapshot not applicable; purely FE component, no BE contract dependency.

## Root Cause Analysis

No real failure here. The icon aria-label contamination is a known AntD quirk that
the codebase had not yet encountered in button-with-icon tests. Root cause of the
minor friction: no established test pattern for icon-decorated AntD buttons existed
in the workflow test suite, so the selector strategy had to be worked out live.

## Lessons Learned

- AntD icon components inject aria-labels that bubble into accessible name computation.
  Always use regex partial match for `getByRole` on icon-decorated buttons — exact
  string match will break.
- `vi.spyOn(Modal, 'confirm').mockImplementation(...)` + capturing `onOk` is the
  correct pattern for testing AntD imperative dialogs in jsdom. Extracting `onOk`
  and calling it directly avoids portal rendering hell. Record this pattern; we will
  hit `Modal.confirm` again.
- Soft threshold + hard cap is the right layered model: confirm dialog = user
  education, hard cap = absolute guardrail. Don't collapse them into one.
- TDD on UI interaction logic pays off most when the logic is a pure conditional
  (>20 → confirm, else direct). Writing the test first locked the threshold contract
  before a line of implementation existed.

## Next Steps

| Item | Owner | When |
|---|---|---|
| Regression watch: confirm threshold UX in manual QA session | QA | Next BE-online session |
| Decide if `CONFIRM_THRESHOLD` should be tenant-configurable | lead/product | After user feedback |
| Soften vi copy if undo capability added (T5-xx) | i18n/UX | When undo is scoped |

## Open Questions

1. **Threshold configurability**: `CONFIRM_THRESHOLD=20` is a hard const. Bigger tenants
   running 30-40 item batches routinely may find the dialog noisy. Worth a tenant setting
   or at least an env override — defer until we get actual usage feedback.

2. **Vietnamese copy**: "không thể hoàn tác" ("cannot be undone") is accurate now but
   becomes misleading if undo capability is added later. If T5-xx ever scopes batch undo,
   this key needs revisiting before release.
