# CI npm-to-pnpm Lockfile Policy Migration

**Date**: 2026-06-10 11:25
**Severity**: High
**Component**: CI (`.github/workflows/ci.yml`), web (`package.json`, `Dockerfile`, `.gitignore`)
**Status**: Resolved (commit `64e98a69` on `main`, pushed origin+bitbucket)

## What Happened

Frontend CI job failed at install since 2026-06-09. Root cause: `web/package-lock.json` (last touched May 27) was stale relative to `web/package.json` — specifically, `@dagrejs/dagre` (added 06-09 for parallel-branch visual layout) was in `package.json` but absent from `package-lock.json`. Running `npm ci` against a stale lock triggered install failure (lock-mismatch).

The defect: two lockfile formats coexisted. Local dev uses pnpm (`.pnpm-lock.yaml` committed, auto-updated). CI used npm (`package-lock.json`, hand-committed). When a new package landed, the developer updated the pnpm lock (which happens automatically), but npm lock was never touched. CI failed.

Decision: user chose policy (b) "standardize on pnpm everywhere" via `AskUserQuestion` — this reverses the prior decision to gitignore `pnpm-lock.yaml`, which is user-confirmed and intentional.

## The Brutal Truth

This is a classic **infrastructure drift** problem. We had two parallel lockfile systems for the exact same `package.json`. The dev flow auto-updates one; the CI flow hand-edits the other. Someone will always forget to update the hand-edited one. The frustrating part: this was entirely preventable with a `pnpm-lock.yaml` pre-commit hook or CI validation. We shipped a 20-line Dockerfile + 3 CI jobs without ever verifying that the lockfile policy was consistent across all of them. The bug wasn't hidden — it was silently permitted by our process.

## Technical Details

**Root cause — lock mismatch:**
```
web/package.json:  "@dagrejs/dagre": "^2.2.8"
web/package-lock.json:  (no @dagrejs/dagre entry, last touch 2026-05-27)
npm ci:  lock mismatch → install fail → test job red
```

**Scope of npm references (whole repo):**
- `.github/workflows/ci.yml:237` frontend job `npm ci`
- `.github/workflows/ci.yml:268` e2e job `npm ci` (disabled `if:false`, issue #21)
- `.github/workflows/ci.yml:293` docker-integration job `npm ci` (disabled `if:false`, issue #21)
- `web/Dockerfile:7–8` `COPY package-lock.json` + `npm ci` (discovered by code-review, NOT my sweep)

**Changes (8 files):**
1. `web/package.json` — added `"packageManager": "pnpm@10.33.0"` field
2. `.gitignore` — removed `web/pnpm-lock.yaml` from ignore list, added `web/package-lock.json`
3. `git rm web/package-lock.json` — delete stale npm lock, staged as deletion
4. `web/pnpm-lock.yaml` — committed (now tracked)
5. `.github/workflows/ci.yml` — migrated all 3 web jobs (frontend active; e2e/docker `if:false`):
   - Add `pnpm/action-setup@v4` step with `package_json_file: web/package.json` (action-setup ignores `defaults.run.working-directory`)
   - Add cache:pnpm (replaces cache:npm)
   - Replace `npm ci` with `pnpm install --frozen-lockfile`
   - Replace `npm run` with `pnpm exec` for tsc/vitest (action-setup v4 pattern)
6. `web/Dockerfile:7–8` — `COPY package-lock.json` → `COPY pnpm-lock.yaml`, `npm ci` → `pnpm install --frozen-lockfile`
7. `docs/project-changelog.md` — added entry
8. `docs/project-backlog.md` — closed row CI-FE-NPM-LOCKFILE-STALE

## What We Tried

Single decision path: user chose "pnpm everywhere" over "regenerate npm lock + re-gitignore pnpm lock". The latter would have re-created the same drift (eventually).

## Root Cause Analysis

**Why two lockfiles?** Historical: repo likely started npm-based, pnpm was added later for local dev speed. Decision to gitignore `pnpm-lock.yaml` + commit `package-lock.json` seemed pragmatic — "use what works locally, CI has official lock." But this violates the invariant: **one package.json must have exactly one tracked lockfile, and all tools must use it.**

**Why now?** New dependency `@dagrejs/dagre` added 06-09 for parallel-branch layout feature. The developer (on pnpm locally) ran `pnpm install` → pnpm-lock.yaml auto-updated. CI still had the old npm lock. No pre-commit hook, no CI validation to catch the mismatch.

**Why code-review caught the Dockerfile?** My change sweep was workflow-file-scoped (`.github/workflows/*.yml`). I did not grep the whole repo for `package-lock.json` usage. Code-review's repo-wide context found `web/Dockerfile` using the same stale lock. Lesson on grep scope discipline (see Lessons Learned).

## Lessons Learned

1. **Blast-radius beyond the named artifact.** One deleted lockfile broke 3 CI jobs + 1 Docker build layer. Never assume the file lives in only one place. `grep -r package-lock web` would have found all 4 before I touched anything. Automate this via pre-commit hook or `lefthook` validation.

2. **Code-review has higher grep coverage than implementation scope.** My change was "fix frontend job" — I fixed the workflow file and missed `web/Dockerfile` entirely. Reviewer with full-repo context caught it. Always ask for repo-wide grep on deletion/migration tasks, or scope the task as "audit and fix all npm refs in repo" not "fix CI job."

3. **pnpm/action-setup@v4 gotcha:** The action ignores `defaults.run.working-directory: web`. Must pass `package_json_file: web/package.json` explicitly, and action-setup must precede `setup-node` if caching. The step order matters: setup-node reads the "active package manager" from `packageManager` field, which action-setup populates.

4. **esbuild "ignored build scripts" warning under pnpm 10 is non-issue.** Locally, `pnpm run build` exits clean (1m6s). Binary ships via platform optional-deps, not postinstall. Did not add `onlyBuiltDependencies` config (YAGNI).

## Next Steps

| Item | Verification | Status |
|------|--------------|--------|
| Local pnpm install --frozen-lockfile | ✓ pass | done |
| Local tsc + vitest 7/7 tests | ✓ pass | done |
| Local pnpm run build (esbuild) | ✓ pass, 1m6s | done |
| Repo sweep: 0 executable npm refs | ✓ grep confirms | done |
| CI run actual result (frontend job) | ⏳ pending next push | blocker |
| Docker build verification | ⏳ skipped (jobs disabled) | deferred |

**Honest gaps:**
- `web/Dockerfile` was converted faithfully but NOT docker-build-verified (both docker jobs are `if:false` per issue #21). If docker jobs re-enable, run `docker build web/` to confirm.
- CI will run pnpm + maxWorkers:2 for the first time on this push. Actual CI runtime result not yet confirmed (depends on GitHub Actions free-tier runner behavior).
- Stale npm-era comments in test/config files exist but deferred (cosmetic, low priority).

**Status:** Row CI-FE-NPM-LOCKFILE-STALE closed. Policy decision logged. All local verifications passing. Ready for CI push.
