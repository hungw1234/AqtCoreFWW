# i18n Migration: RBAC & Forms Internationalization

**Date**: 2026-04-11 22:03
**Severity**: Medium
**Component**: i18n / RBAC + Forms FE
**Status**: Resolved

## What Happened

T4-01 (RBAC i18n) and T4-02 (Forms i18n) completed in same session. T4-01 was already functionally complete from prior work but lacked TDD tests. T4-02 required full implementation: 15 new keys, 8 files refactored, architectural decision on function vs component patterns.

## The Brutal Truth

Plan drift killed visibility. T4-01 showed "pending" when it was done. We shipped code without updating plan status, so nobody knew. Then we came back and had to reverse-engineer what was already working, wasting time on a false restart. The architecture call (plain function gets `t: TFunction` param, component uses hook) was clean but forced 3 callers to change — documentation would have saved friction.

## Technical Details

**T4-01 migration:** 26 i18n keys across RBAC module, 56 passing TDD tests covering all cases.

**T4-02 migration:**
- `renderInteractiveField` (plain fn): added `t: TFunction` to `RenderFieldProps`, updated 3 callers
- `FileFieldWidget` (component): uses `useTranslation()` hook
- 15 new keys, 30 TDD tests

**Code review:** 8/10 pass. Medium concern: `common.operationFailed` + `common.operationSuccess` keys missing from locale files — defaultValue fallbacks hide bug from non-English users.

**Test results:** 599 Vitest pass + 16 pre-existing jsdom failures (no regression, 86 new tests added).

## Root Cause Analysis

T4-01 plan drift: completed work wasn't reflected in plan status. Assumption that PR merge = plan update was wrong. Lesson: mark plan tasks complete WHEN code finishes, not after review.

Pre-existing locale gap: `common.*` keys used but never localized. Fallback hides scope creep until English users hit them.

## Lessons Learned

1. **Update plan status immediately** when task code completes — don't defer to merge/review stage
2. **Document architectural decisions** before refactoring callers — function vs component pattern deserves a comment
3. **Validate locale completeness** beyond code coverage — check that all i18n keys have entries in all locale files

## Next Steps

1. Add missing `common.operationFailed/Success` keys to locale files (scope creep but must close)
2. Update plan to reflect T4-01 + T4-02 complete status
3. Run full E2E Playwright test to verify i18n renders correctly
