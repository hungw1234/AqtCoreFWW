# T5-22: 4 Pre-existing Vitest Failures → 0 (Baseline Clean)

**Date**: 2026-04-14 18:40
**Severity**: Medium
**Component**: web — `app-smoke.test.tsx`, `api-client.test.ts`, `smoke-remaining-pages.test.tsx`
**Status**: Resolved (branch `fix/t5-22-vitest-regressions`, pending merge)

## What Happened

After T5-21-C merged, baseline was 4 failed / 720 passed across 3 files. Backlog had logged
8 failures / 4 files — T5-21-C self-healed the `notification-list-page` file as a side-effect
of fixture typing. T5-22 scope was to kill the remaining 4.

Three root causes, three fixes, zero prod-code changes:

- **Fix A** (`app-smoke.test.tsx`): Nyasha One redesign commit `22df356d` changed `LAYOUT.siderWidth`
  from 220 → 256. Test assertion never updated. One-line fix.
- **Fix B** (`api-client.test.ts`): `import.meta.env.VITE_API_BASE_URL` is baked at module-load
  time. `beforeAll(vi.stubEnv(...))` runs after ESM imports — too late. Fix: `vi.hoisted(() => vi.stubEnv(...))`,
  which physically hoists the stub before module evaluation.
- **Fix C** (`smoke-remaining-pages.test.tsx`): Backlog attributed failure to "i18n not init" —
  wrong. i18n mock was present and valid. Real cause: roles page calls 8 hooks from `roles-api`;
  all returned `undefined` → empty table → assertions fail. Plan estimated 3 hooks to stub after
  reading `roles-page.tsx`. Scout pass into `role-form-modal.tsx` + `role-permissions-drawer.tsx`
  revealed 5 additional hooks. Stubbed all 8.

Final run: **65 files / 724 tests / 0 failures** at 172s wall time (pool cap from `cb71efb8` held).
Code review: 9.7/10, 0 critical issues.

## The Brutal Truth

The backlog was wrong on both the count (8 vs 4 actual) and the root cause ("i18n" for what
was plainly missing API mocks). That stale narrative would have sent someone into i18n config
for an hour before they checked the network layer. What makes this particularly annoying is that
the true cause — roles page making 8 API calls with zero mocks — is completely obvious the
moment you look at the hook call sites. We just didn't look before writing the backlog entry.

Fix B (`vi.hoisted`) is the kind of thing that humiliates you until you know it. If you've
ever spent 45 minutes wondering why `vi.stubEnv` does nothing and your env var is still the
real value, this is why: ESM bakes `import.meta.env` at import time. `beforeAll` is a runtime
hook. The module already loaded. `vi.hoisted` is the only lever that runs before the import graph
is evaluated. Not documented prominently. You have to have been burned by it.

Fix C's extra 5 hooks weren't painful — they were caught by the scout-before-code pass, not by
a failing run. That's how it should work.

## Technical Details

**Baseline (post T5-21-C, pre T5-22):**
```
Test Files  3 failed | 62 passed (65)
Tests       4 failed | 720 passed (724)
```

**Fix A — constant drift:**
```typescript
// Before
expect(LAYOUT.siderWidth).toBe(220);
// After
expect(LAYOUT.siderWidth).toBe(256);  // Nyasha One redesign (22df356d)
```

**Fix B — module-load env baking:**
```typescript
// WRONG — runs after ESM import graph resolves
beforeAll(() => { vi.stubEnv('VITE_API_BASE_URL', 'http://test.api'); });

// CORRECT — hoisted before any import evaluates
const { env } = vi.hoisted(() => {
  vi.stubEnv('VITE_API_BASE_URL', 'http://test.api');
  return { env: import.meta.env };
});
```

**Fix C — 8-hook stub (roles page):**
```typescript
vi.mock('@/features/roles/roles-api', () => ({
  useRoles: vi.fn(() => ({ data: [], isLoading: false })),
  useDeleteRole: vi.fn(() => ({ mutate: vi.fn() })),
  useActivateRole: vi.fn(() => ({ mutate: vi.fn() })),
  useCreateRole: vi.fn(() => ({ mutate: vi.fn() })),
  useUpdateRole: vi.fn(() => ({ mutate: vi.fn() })),
  usePermissions: vi.fn(() => ({ data: [] })),
  useRolePermissions: vi.fn(() => ({ data: [] })),
  useSyncRolePermissions: vi.fn(() => ({ mutate: vi.fn() })),
}));
```

**Final verification:**
```
Test Files  65 passed (65)
Tests       724 passed (724)
Wall time   172s
```

## Root Cause Analysis

Three independent categories of drift, none related:

1. **Constant drift** — a UI constant changed in a feature commit; no process forced test update.
2. **ESM evaluation order** — test author used `beforeAll` for env stubbing, not knowing
   `import.meta.env` is baked at import time in Vitest's ESM mode.
3. **Backlog misdiagnosis** — the original failure note guessed i18n without running the test
   in isolation to read the actual assertion message. The real error was a `roles` table with
   zero rows because every `useRoles` call returned `undefined`.

## Lessons Learned

- **Trust the baseline run, not the stale narrative.** Backlog said 8 failures, 4 files, cause
  = i18n. All three were wrong. Run `vitest run --reporter=verbose` on the failing file, read
  the assertion message, then diagnose. Take 2 minutes; saves an hour.
- **`vi.hoisted` for `import.meta.env` — non-negotiable.** `beforeAll`, `beforeEach`, and even
  module-level `vi.stubEnv` calls outside `vi.hoisted` are all too late for env vars that are
  read at module load. `vi.hoisted(() => vi.stubEnv(...))` is the pattern.
- **Scout component tree before estimating mock count.** `roles-page.tsx` alone shows 3 hooks.
  Its children (`role-form-modal`, `role-permissions-drawer`) add 5 more. One read pass before
  writing the plan saves a second implementation pass.
- **Self-healing from adjacent tasks is invisible until you re-run.** T5-21-C healed one of the
  four backlog files as a fixture-typing side-effect. The backlog count was stale the moment
  T5-21-C committed. Always re-baseline before starting a fix ticket.

## Next Steps

| Item | Owner | Priority | When |
|------|-------|----------|------|
| Merge `fix/t5-22-vitest-regressions` to main | dev | HIGH | On user approval |
| Add LAYOUT constant change to PR checklist (update test assertions) | lead | LOW | Process update |
| Document `vi.hoisted` pattern in `docs/code-standards.md` test section | dev | LOW | Next housekeeping sprint |
