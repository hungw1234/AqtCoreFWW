# Journal — 2026-06-09 21:10 · Stale-test triage (WFE-TAXONOMY-MIGRATION-TEST-FLAKE)

## Session goal
`/cook WFE-TAXONOMY-MIGRATION-TEST-FLAKE --auto` — triage the backlog row claiming "6 failing FE tests / 4 files (taxonomy/migration), pre-existing flake." Separate genuine breakage from load-flake, fix what's real.

## What worked (verified + commit ref)
- **Audit-at-slice-start (INVARIANT 260522-A) refuted the row premise.** Stack DOWN + isolation probe: the 162-test workflow folder and each named file pass in isolation; the "flake" was a conflation of two unrelated classes. Probing first (not trusting the row) reshaped the entire task.
- **Genuine breakage fixed — 5 tests, one family (SUT contract migrated, test mock/constant went stale):**
  - `workflow-migration` (4) — SUT moved permissions to `useMyEffectivePermissions` (`/me/permissions`); spec patched the dead `profile.permission` JWT claim. Mocked the API hook (mirrors `use-permissions.test.ts`), made the hide/show assertions non-vacuous. `c735fc2e`.
  - `vgca-hash` (wrong sha256("a") vector — SUT correct, openssl-verified), `admin-tenant-selector` (gate `hasRole`→`hasPermission('SYSTEM.CONFIG.MANAGE')`), `auth-helper-invalidation` (stale on-disk token cache short-circuits ROPC), `change-password-page` (mock missing `appMessage` → success path threw before navigate). `669b99e5`.
- **Full suite 993/993** at `testTimeout=20000` (was intermittently 1-6 red at default 5000) · tsc 0 · code-review APPROVE 0C/0H ×2.

## What didn't / lessons
- **`npx vitest run <path>` from the wrong cwd is a trap.** When cwd drifted to repo root, vitest (a) failed to resolve the `@/` alias (no `web/vitest.config.ts` loaded) and (b) substring-matched the same-named test files in stale `.claude/worktrees/*` — producing 6 fake "suite-level" failures that looked like my fixes broke. Always pin `cd web` in the same command; the worktrees sit above `web/` and only get swept in when cwd is the repo root.
- **The "flake" was a 50/50 mix.** Non-determinism across runs = the contention-timeout portion shuffling which heavy-render file blew the 5s cap, overlaid on a STABLE set of deterministic stale-tests. Raising the timeout (20000) peeled the two apart cleanly: timeout-class cleared, deterministic stayed red.
- **A 4th stale-test (change-password) surfaced only during full-suite verification** — not named in the original scope-of-3. Same family; fixing it completed the green-suite intent. Flagged as a transparent scope extension rather than silently dropped or silently expanded.

## Decisions made
- **Scope (AskUserQuestion):** user chose "extend — fix the 3 stale-tests" over "file only" or "+testTimeout bump."
- **Did NOT apply the `testTimeout` global bump** (user scoped to stale-test fixes). Filed `WFE-FULLSUITE-CONTENTION-TIMEOUT` with the now-verified one-line fix as opt-in. Advisor's earlier objection ("bump doesn't green the suite") no longer holds once the breakages are fixed — surfaced that changed circumstance instead of acting on it.
- **Each fix is test-only** — every SUT was verified correct; no product code touched. The stale tests were masking nothing (vgca's test constant was wrong, not the hash).

## Test status
- Per-file isolation (cwd=web): vgca-hash 6/6 · auth-helper 3/3 · admin-tenant 2/2 · workflow-migration 13/13 · change-password 8/8.
- Full FE suite: **993/993** at `testTimeout=20000`. At default 5000 the environmental contention flake remains (separate row).
- tsc 0 · code-review APPROVE 0C/0H (migration) + APPROVE 0C/0H (4-file batch).

## Next session
- **`WFE-FULLSUITE-CONTENTION-TIMEOUT`** — apply the verified `testTimeout`/`hookTimeout` ~20000 bump (or raise `maxForks` / lower `maxConcurrency`); also a Vitest-4 `poolOptions` migration pass (deprecation warning).
- **Push** `c735fc2e` + `669b99e5` to origin+bitbucket (committed local, push pending).
- Carry: `WF-DAGRE-COORDS-SAVE-VERIFY`, e2e-in-CI open-Q (these stale tests may have been red in CI silently), `R4-EXEC` (stack-gated).
