# T5-21-C: 44 → 0 Test TS Errors + CI Test Typecheck Gate Wired

**Date**: 2026-04-14 17:42
**Severity**: High
**Component**: web — `src/**/*.test.tsx` / `*.spec.ts` (19 files), `ci.yml`, `tsconfig.test.json`
**Status**: Resolved (commits `bb555c1e`, `d29a55e6` on `fix/t5-21-c-test-tsc-fix`)

## What Happened

Entered this session with T5-21-B just closed: prod errors 62 → 0, CI gate hardened to
`tsc --noEmit -p tsconfig.app.json`. Test files were never in scope for T5-21-B — by design,
because `tsconfig.test.json` excluded `src` and included only `**/*.test.*`. The correct
consequence was 44 test-only strict-mode errors that had accumulated since T5-21 first
activated strict typing.

What nobody expected: fixing those 44 errors revealed DTO drift across ~10+ additional DTOs.
The 44-error count was a lower bound, not the scope. Every time `[] as SomeDto[]` replaced a
`never[]` inference on a mock fixture, TypeScript picked up the actual required fields and
surfaced 3–8 new missing-property errors per file. Cluster 5 alone uncovered notification
`title`→`subject`, user `status/mfaEnabled`→`isActive/clearanceLevel`, `CaseStatus` enum
rename `PendingApproval`→`Submitted`, and a spread of newly-required fields
(`createdAt`, `parametersSchema`, `publishedAt`, `actionSchema`, etc.).

Plan built 7 risk-ascending clusters, advisor reviewed, two framing issues caught before
implementation. Fullstack-developer executed, 44 → 0. Code review 9.6/10 with one M1 nit
addressed in a follow-up commit. T5-21 arc complete.

## The Brutal Truth

The 44 headline number was misleading. Test fixture drift is not proportional to raw error
count — it is proportional to how many DTO shapes changed since the fixtures were last
typed. Strict mode forces you to see all of it at once, and the "at once" part is a genuine
shock when it lands mid-fix as cascading new errors.

This is the expected, correct outcome. That doesn't make it less painful. You start a cluster
thinking you have 6 errors, you add `as NotificationDto[]`, TypeScript gives you 9 new ones,
and you're now doing Orval-vs-fixture reconciliation that was never on the ticket. The
frustrating part is that none of this was hidden — it was always there, `as any` and
`unknown` casts were just papering over it.

The closure is satisfying: prod and tests now both block CI on TypeScript violations. DTO
drift surfaces at PR time. The frustration is that we needed a two-session, three-ticket arc
to get to a state that should have been baseline.

## Technical Details

**Error progression by cluster:**

| Cluster | Class | Pre-fix count | Notes |
|---------|-------|--------------|-------|
| 1 | Unused imports in test files | ~8 | Mechanical; plan said 6, more crept in via cascade |
| 2 | `never[]` mock arrays (`[] as XDto[]`) | ~10 | Each fix exposed required-field mismatches below |
| 3 | Required DTO field gaps on typed fixtures | ~12 | Cascade from Cluster 2 — bigger than plan predicted |
| 4 | Mock return-type mismatches (`vi.fn()` inference) | ~6 | `mockResolvedValue` return shapes needed typing |
| 5 | Deep DTO drift (notification/user/case/template) | ~5 | Revealed ~10 additional DTO shape changes |
| 6 | Enum renames at call sites | ~2 | `PendingApproval`→`Submitted` in CaseStatus |
| 7 | `vi.mocked` generic inference conflicts | ~1 | Resolved by typing at source, not via mocked<> |

**DTO changes surfaced (not in original 44-error scope):**
- `NotificationDto`: `title` → `subject`
- `UserDto`: `status`/`mfaEnabled` → `isActive`/`clearanceLevel`
- `CaseStatus` enum: `PendingApproval` → `Submitted`
- Templates: `publishedAt`, `parametersSchema`, `actionSchema` added as required
- Partner/form/workflow fixtures: various `createdAt`/`updatedAt` fields newly required

**CI gate added (`ci.yml`):**
```yaml
- name: Type check (tests)
  working-directory: web
  run: npx tsc --noEmit -p tsconfig.test.json
```

**Commits:**
- `bb555c1e` — 44-error fix across 19 test files + CI `Type check (tests)` step wired
- `d29a55e6` — M1 follow-up: `notification-bell` `items: []` typed as `NotificationDto[]`
  (code-reviewer catch: the only fixture left with an untyped empty array)

**Post-commit verification:**
```
npx tsc --noEmit -p tsconfig.test.json   → exit 0
npx tsc --noEmit -p tsconfig.app.json   → exit 0
vitest run                               → 1 pre-existing failure (LAYOUT.siderWidth 256≠220)
                                           confirmed pre-existing; out of T5-21-C scope
```

## What We Tried

**Advisor pre-flight caught two traps before implementation:**

1. **Root `tsconfig.json` test reference rejected.** Adding `tsconfig.test.json` as a
   reference in root `tsconfig.json` would make `tsc -b` (run by `npm run build`) traverse
   test files. Every prod build would also typecheck tests. Slower builds and test-only
   issues surfacing as build failures. Fix: CI step runs `tsc -p tsconfig.test.json`
   independently. Root config untouched.

2. **`vi.mocked<typeof Hook>` generic pattern rejected.** When `useHook` returns a union
   type, wrapping mocks in `vi.mocked<typeof useHook>()` fights TypeScript overload
   resolution and produces cryptic `never` inference. Preferred pattern:
   `const data: XDto[] = [{ ...fields }]` — type at source, let inference flow naturally.
   No overload ambiguity.

**Cluster 2→3 cascade handled correctly:** rather than retroactively patching test fixtures
with `as any` to silence the new errors, every required field was populated with realistic
values matching current Orval-generated DTO shapes. This is the only acceptable approach —
fake-typed fixtures defeat the entire purpose of strict mode.

## Root Cause Analysis

Test fixture drift is the inevitable consequence of DTO evolution without a typecheck gate
on test files. From T5-21 base through T5-21-B, every DTO fix on the prod side was
logically disconnected from test fixtures because `tsconfig.test.json` was not in CI.
Strict mode on test files was configured but not enforced. Drift accumulated silently.

The secondary cause is how `never` inference compounds: a single untyped `[]` in a mock
factory suppresses all downstream field-shape checking. You see 1 error instead of 8.
When you fix the `[]` typing, the 7 hidden errors surface. The 44-error count was therefore
not the actual scope — it was the count with suppression still active. Every "cluster 2"
fix was also a suppression removal.

## Lessons Learned

- **Strict-mode error counts undercount when `never` inference is active.** An untyped
  `[] as any[]` or `{} as any` in a fixture masks all downstream field errors. When you
  finally type it, you get all the deferred errors at once. Budget 2–3× the headline count
  when activating strict mode on a codebase with legacy mock patterns.

- **`[] as XDto[]` at source beats `vi.mocked<typeof Hook>` generics.** Union return types
  and overload resolution do not play well with `vi.mocked<>` wrappers. Type the data, not
  the mock — TypeScript infers the rest without fighting the overload resolver.

- **Never add test tsconfig as a reference in root tsconfig.** Composite builds traverse
  references. Test-only strict issues become build failures; every `npm run build` also
  typechecks your test suite. Run it as a separate CI step (`-p tsconfig.test.json`).

- **DTO drift magnitude is visible only after fixture typing.** You cannot estimate test
  fixture update scope from the raw error count alone. Run a quick `tsc -p tsconfig.test.json`
  pass and count unique DTO shape mismatches, not total error lines.

- **Test fixtures are a lagging contract snapshot.** They were last updated when written.
  Every Orval regeneration that changes a DTO silently breaks the snapshot. The only fix is
  CI enforcement — which is now in place.

## T5-21 Arc Summary

| Phase | Scope | Result |
|-------|-------|--------|
| T5-21 base | Add `tsconfig.test.json`, `typecheck:test` script | 114 → 62 prod errors |
| T5-21-B | Fix 62 → 0 prod errors, CI `-p tsconfig.app.json` gate | 0 prod errors; CI enforced |
| T5-21-C | Fix 44 → 0 test errors, CI `Type check (tests)` gate | 0 test errors; CI enforced |

CI-enforced TypeScript parity: both prod and test strict-mode violations fail at PR time.
DTO drift from Orval regenerations is no longer invisible until runtime.

## Next Steps

| Item | Owner | Priority | When |
|------|-------|----------|------|
| Merge `fix/t5-21-c-test-tsc-fix` to main | dev | CRITICAL | On user approval |
| Monitor first PR through new CI gate — confirm no false positives | lead | HIGH | This sprint |
| `UserRole` cast safety: Zod parse at API boundary | dev | LOW | Backlog (T5-22 or later) |
| DTO-to-form field check on PR review checklist | lead | MEDIUM | Process update |
| When next Orval regen runs: expect test fixture updates — now they will be visible | all | FYI | Ongoing |

## Open Questions

1. **How many Orval regenerations happened between T5-21 base and T5-21-C without anyone
   noticing fixture drift?** The `notification title→subject` rename alone implies at least
   one full regen that touched notification DTO shape without cascading to test files.
   Duration of accumulated drift is unknown.

2. **Is `LAYOUT.siderWidth 256≠220` the only pre-existing Vitest failure?** Confirmed
   pre-existing on HEAD before T5-21-C edits, but the full Vitest suite was not re-run
   end-to-end. Low risk — that test is layout-constant drift, not contract drift.
