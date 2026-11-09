# T5-21-B: 29 FE Prod TS Errors Fixed, CI No-Op Gate Replaced

**Date**: 2026-04-14 15:39
**Severity**: Critical
**Component**: web — `src/**` (23 files), `ci.yml:252`, `package.json`
**Status**: Resolved (not pushed — 4 commits ahead of `origin/main`)

## What Happened

T5-21-B was scoped to fix 29 production TypeScript errors and flip the CI typecheck from
a silent no-op (`tsc --noEmit` on root config with `files: []`) to a real gate
(`tsc --noEmit -p tsconfig.app.json`). Two commits delivered both; a third cleaned up a
code-reviewer-caught semantic regression. Docs sync landed in a fourth.

Before implementation: advisor red-team challenged the plan's premise that CI was red
*because* of the 29 FE errors. `gh run view --log-failed` on the 5 most-recent runs
revealed the actual CI failure was BE `ASPDEPR005: ForwardedHeadersOptions.KnownNetworks
is obsolete` in `AuthServer/Program.cs:227` under `/warnaserror`. Then `git log` showed
`c80bb22f` had already fixed it before this session started — no CI run had triggered
since. T5-21-D (proposed BE fix) was cancelled as redundant. The 29 FE errors were real
tech debt that broke `npm run build` regardless, so T5-21-B proceeded unchanged.

## The Brutal Truth

The plan said "CI is red because of 29 FE errors." That was wrong. The real CI blocker
was a BE deprecation warning already fixed in a commit nobody knew had resolved it, because
nobody checked. We came within one decision of creating a T5-21-D commit to fix a problem
that no longer existed. That's not a close call — that's the default failure mode when
you trust stale baselines and skip verification.

The more aggravating part: the CI no-op (`tsc --noEmit` without `-b` on a root config
that checks nothing) had been in place long enough for 29 prod errors to accumulate
undetected. We replaced a gate that exits 0 unconditionally with one that actually runs
the compiler. The fact that this needed to be done in 2026, on a repo with 5+ active
contributors, is embarrassing.

## Technical Details

**Commits:**

- `f2db41e5` — 23 FE files, 29 errors resolved in 7 risk-ascending batches
- `b0898ed5` — CI gate: `tsc --noEmit` → `tsc --noEmit -p tsconfig.app.json`; `npm run typecheck` aligned to match; removed dead `tenantId` Form.Item from `abac-rules-page` (code-reviewer major finding)
- `3c5fd981` — docs sync: changelog + backlog v2.89 → v2.90 + prior T5-21 partial journal

**Batches:**

| Batch | Class | Count | Notes |
|-------|-------|-------|-------|
| A | Unused imports/vars | 9 | Plan said 8 — one more crept in |
| B | Removed `tenantId`/`modifiedBy` from DTO call sites | ~5 | BE server-derives; cascaded `useAuth` import cleanup |
| C | `page` → `pageNumber` on `UserListParams` | 2 | Pagination rename contract drift |
| D | Null-safety `template?.` in closure callbacks | 3 | TS narrowing doesn't survive closure boundary |
| E | `SyntheticListenerMap` → `NonNullable<ReturnType<typeof useSortable>['listeners']>`; DTO `subject` → `title` | 4 | dnd-kit type narrowing + Orval regeneration drift |
| F | `UserRole` cast, `DataNode` recursive typing, `MenuItemGroup.icon` widening, webhook route fix, `MigrationItemStatus` guard | 4 | Miscellaneous contract corrections |
| G | TanStack Router `search` typed params; direct file import for missing barrel export | 2 | Required search params + module resolution fix |

**Verification (post-commit):**

```
npx tsc -b           → exit 0
npm run typecheck    → exit 0 (now real gate: -p tsconfig.app.json)
npm run build        → exit 0 (40s, vite build clean)
vitest               → 1 pre-existing failure: LAYOUT.siderWidth 256≠220
                       confirmed pre-existing on HEAD before any edits
```

## What We Tried

**Code-reviewer catch on Batch A:** "remove unused `...record` spread in abac-rules-page"
was typed-correct but broke Edit pre-fill UX — the spread populated form fields. Reviewer
caught intent drift in what looked like a trivial deletion. Fix landed in `b0898ed5` along
with the `tenantId` Form.Item removal (field was mapped to `CreateAbacRuleDto` which never
included `tenantId`, so it silently sent `undefined` to BE every save — dead code that
looked functional).

**Rejected: skip baseline re-verification.** Advisor insisted on fresh `tsc` baseline
before any edits. Baseline confirmed 29 errors matched plan exactly — valuable not for
the count but for the discipline. A drift between plan baseline and actual would have
invalidated the batch mapping.

**Rejected: fix `UserRole` cast properly.** The `string[]` → `UserRole[]` cast is
technically unsafe — if BE returns a non-enum string, it silently passes. Flagged as
minor. Proper fix is a Zod parse or discriminated union at the API boundary. Out of scope;
noted for T5-21-C or a dedicated BE-contract audit.

## Root Cause Analysis

Two compounding issues, same root: **we stopped verifying the gate we relied on.**

1. **CI no-op**: `tsc --noEmit` on a root config with `files: []` + `references` checks
   zero files. Exit 0 always. This was never regression-tested after the tsconfig
   references restructure. The gate looked healthy because it never failed.

2. **Stale plan premise**: The claim "CI is red because of FE errors" was inference, not
   observation. Nobody ran `gh run view --log-failed` before writing the plan. Had we
   proceeded on that premise, T5-21-D would have committed a fix for a problem already
   solved in `c80bb22f`.

The red-team step (advisor challenge before implementation) was the only catch. It's not
a comfortable thing to admit that a mandatory process step saved ~30 min and one wrong
commit — because that means the alternative (skipping it) is a real temptation.

## Lessons Learned

- **`tsc --noEmit` on a references-mode root config is a no-op.** Always run with
  `-p tsconfig.app.json` or `-b`. If you can't break the gate with a deliberate error,
  the gate isn't running. Validate gates by breaking them.

- **`gh run view --log-failed` before planning, not after.** "CI is red" is not a
  diagnosis. The actual failure message is one command away. We didn't run it. The plan
  was built on inference and nearly spawned a redundant fix.

- **Closure boundaries kill TS narrowing.** `if (template) { return () => template.id }`
  — inside the arrow function `template` is re-evaluated as `T | undefined`. The fix
  (`template?.id`) is obvious once you see it; the surprise is how often TS catches this
  late when closures are involved.

- **Removing "unused" spreads can break UX silently.** `...record` in a form `initialValues`
  isn't dead code — it's population. TypeScript has no way to flag this as used.
  Code-reviewer caught it; automated tools cannot.

- **Dead form fields that look live are a silent contract bug.** `tenantId` Form.Item
  wired to a DTO that doesn't include `tenantId` sends `undefined` on every save with
  zero error. Only caught by cross-referencing Orval-generated DTO types against rendered
  form fields. This will happen again unless we add DTO-to-form field coverage to the
  review checklist.

## Next Steps

| Item | Owner | Priority | When |
|---|---|---|---|
| Push 4 commits to `origin/main` | dev | CRITICAL | On user approval |
| T5-21-C: Fix 33 test TS errors + add `typecheck:test` CI step | dev | HIGH | Next session (unblocked) |
| Alert on `main` CI status | lead | HIGH | This sprint |
| BE contract audit: server-side tenant enforcement on `SaveGraph`/`ReassignTask` | BE owner | MEDIUM | Next sprint |
| `UserRole` cast safety: Zod parse at API boundary | dev | LOW | T5-21-C or later |
| Add DTO-to-form field check to PR review checklist | lead | MEDIUM | Process update |

## Open Questions

1. **How long was the CI no-op in place?** `gh run list` only shows recent runs.
   `git log -- web/tsconfig.json` would establish when the references restructure removed
   `files` from root config. If it predates T5-17, the 29 errors are older than assumed.

2. **Is the `UserRole` cast (`string[]` → `UserRole[]`) actually safe in production?**
   BE currently only returns valid enum values, but there is no contract enforcement at
   the deserialization boundary. Silent pass-through on unexpected values.
