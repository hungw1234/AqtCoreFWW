# T4-04 FE↔BE Validator Regex Sync — Completed

**Date**: 2026-04-14 09:42
**Severity**: Medium
**Component**: web — forms, admin pages, i18n locale files
**Status**: Resolved (Playwright gate deferred — BE offline)

## What Happened

Closed 12 validation-gap patches across cases form + 5 admin pages. Scope expanded
from 7 → 12 after a red-team pass surfaced 5 admin-page gaps plus 2 UX blockers
(B1: double-error on required+min, B2: missing `max:100` on notifications). User
approved all additions before implementation started.

## The Brutal Truth

The original scope estimate was wrong by 40%. We shipped the plan without ever
looking at admin pages. Red-team caught it — not us. If we'd skipped the adversarial
review, phase-02 would have shipped half-baked and someone else would have found the
gaps in QA. That's embarrassing.

The master-data-page comment pointed to the wrong validator (`CreateDictionaryCommandValidator.cs`)
when the actual controller uses record DTOs with no FluentValidation at all. The High
finding in code review existed because we assumed BE always has FluentValidation —
it doesn't. Fixed to "FE-only guard — MasterDataAdminController.cs uses record DTOs
without FluentValidation".

## Technical Details

- Commit: `c3720d8a` (main, 1 ahead origin/main, not pushed)
- Phase-01: `case-form.tsx` + `en.json` / `vi.json` — new shared keys
  `common.validation.maxLength` (with `{{max}}` interpolation),
  `pattern.upperAlphaNum`, `pattern.lowerAlphaNum`
- Phase-02: 5 admin pages, 10 gap patches, all with `validateFirst` prop and
  `// Mirrors BE <Validator>.cs` comments
- Page-specific key added: `forms.codePatternError` — regex `^[a-zA-Z_][a-zA-Z0-9_\-]{0,99}$`
  cannot be expressed via generic `common.validation.pattern.*`
- Dead key removed: `common.validation.minLength` (unused after deciding to keep
  per-field min messages for the 2 sites that need contextual wording)
- Vitest: 10/10 green on first pass
- Playwright: ECONNREFUSED on port 5002 — BE not running, gate deferred

## What We Tried

- TypeScript typecheck used as substitute quality gate for Playwright — passes clean
- Code review scored 7.5/10; one High (wrong comment target) fixed immediately

## Root Cause Analysis

Two separate failures compounded:

1. **Scope undercount**: Initial audit only scanned cases form. Admin pages were
   in-scope by design (same validator sync task) but never checked. Red-team found
   them; we should have.

2. **Wrong comment assumption**: Assumed BE always backs FE validation with
   FluentValidation. `MasterDataAdminController.cs` proves that assumption false.
   Should have verified before writing `// Mirrors BE` comments.

## Lessons Learned

- Red-team every plan that touches more than 3 files. Caught 2 blockers + 5 gaps
  before a line of code was written. The cost of the adversarial pass is trivial
  compared to mid-sprint scope explosion.
- `validateFirst` on antd Form.Item is not optional where min+required co-exist —
  without it users see stacked error messages. It's a one-liner per field; there is
  no excuse for shipping without it.
- Shared i18n keys with interpolation (`{{max}}`) eliminate per-field key sprawl.
  Phase-02 used `common.validation.maxLength` 8+ times across 5 files — pure DRY win.
- Before writing `// Mirrors BE X.cs`, grep for the class and confirm it exists and
  has rules. If not, say "FE-only guard" explicitly.

## Next Steps

| Item | Owner | When |
|---|---|---|
| Playwright E2E re-run (cases + admin validator flows) | tester | When BE port 5002 online |
| Push commit `c3720d8a` to origin/main | dev | Next push window |
| Schedule T4-04-B: `partner-list-page` max:200 + `template-list-page` max:100 | backlog | Next T4 sprint |

## Open Items

- Playwright gate blocked: BE must be running on port 5002 before re-run is valid.
  Don't close T4-04 in tracking until at least the happy-path Playwright suite
  passes against live BE.
- T4-04-B (adjacent gaps found during review, out-of-scope for this session):
  `partner-list-page.tsx` `name` max:200, `template-list-page.tsx` `code` max:100.
  Logged in backlog — needs scheduling.
