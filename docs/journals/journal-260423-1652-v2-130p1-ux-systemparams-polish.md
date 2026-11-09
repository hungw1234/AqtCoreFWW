# Journal: v2.130p1 UX + SystemParams Polish Batch

**Date:** 2026-04-23 | **Session:** docs-manager | **Version:** v2.130p1

## Summary

Synced 11 post-v2.130 commits across 5 logical themes into v2.130p1 patch batch:

1. **SystemParams module migration** (3 commits: b3162836, bfc4229c, d3764a40)
   - Ported SystemParamService cache + Redis pub/sub from legacy monolithic project to layered Clean Architecture (Domain/Application/Infrastructure/Presentation)
   - Deleted legacy `AqtCoreFW.SystemParams/` project entirely
   - Rewired 38 unit tests to new structure
   - Removed orphan `session:timeout_min` DB row (M3 cleanup)

2. **Session UX cleanup** (3 commits: dfa5cce7, 2c502dc9, 9519ce81)
   - Invisible silent-renew: removed token-expiry polling overhead
   - API client 401 retry with `renewInFlight` coalescing to prevent thundering herd
   - Idle-only modal (removed conditional renew-failed branching)
   - Seeded `session:idleTimeoutMinutes` + `session:countdownSeconds` with per-key guard logic

3. **Silent-renew auto-redirect UX** (3 commits: 1d252e42, 08d3f8db, 140c3f0d)
   - After silent-renew failure: persistent antd toast with 10s countdown + "Đăng nhập ngay" button
   - Auto-redirect when countdown reaches 0
   - Extracted pure DI controller `auth-failure-controller.ts` (79 LOC) for testability
   - 10 vitest unit tests (166 LOC) validate countdown logic independently of framework
   - 3-layer defense for `feature:*` exclusion from admin CRUD (GetAll filter + GetOne 404 + Update 422)

4. **Org tree UX polish** (1 commit: ba59050b)
   - Default-expand 1 level in organization chart (improves discoverability)
   - Widened staff role column for readability

5. **Seeder robustness** (1 commit: 18e4f08f)
   - BtcOrphanPurger self-heal mechanism for orphan row cleanup
   - Identity drift wipe feature
   - Integration test (223 LOC) validates cleanup idempotency

## Technical Decisions

- **No schema changes:** All updates preserve API contracts. SystemParams legacy project removal is internal refactoring.
- **Per-key guard pattern:** SystemParams seed operations now validate keys before insert — prevents partial seed failures.
- **Pure DI for auth-failure-controller:** Extracted countdown logic to avoid router/context dependencies in unit tests.
- **Session timeout semantics:** Invisible silent-renew + idle-only modal reduces UX friction (no "renew success/fail" notifications during normal activity).

## New Files

- `web/src/features/auth/auth-failure-controller.ts` — Pure countdown logic (79 LOC)
- `web/src/features/auth/session-error-handler.ts` — Error classification
- `src/modules/organization/AqtCoreFW.Organization.Infrastructure/Seeding/BtcOrphanPurger.cs` — Cleanup logic
- `tests/integration/AqtCoreFW.Organization.Integration.Tests/BtcOrphanPurgerTests.cs` — Integration test (223 LOC)

## Deleted Files

- `src/modules/systemparams/AqtCoreFW.SystemParams/` — entire legacy project removed (functionality preserved in layered modules)

## Artifacts Updated

1. `docs/dependency-map.md` — v2.130p1 header + new file references
2. `docs/project-roadmap.md` — current version bumped to v2.130p1
3. `docs/project-changelog.md` — new v2.130p1 section with 5 themes + 11 commit hashes
4. `backlogs/backlog.md` — version bumped + v2.130p1 entry
5. `memory/project-state.md` — v2.130p1 description + commit chain
6. `memory/MEMORY.md` — project-state row updated
7. `docs/journals/journal-260423-1652-v2-130p1-ux-systemparams-polish.md` — this journal

## Quality Assurance

- All 11 commits included in version string per batch order (oldest → newest)
- Commit hashes verified against `git log origin/main..HEAD --reverse --oneline`
- No schema changes (entity, DbSet, endpoint contracts preserved)
- New files confirmed in `git diff origin/main..HEAD --name-only`
- Logical grouping by theme reflects delivery intent

## Next Steps

- git-manager commit all 7-artifact updates
- No feature flags or breaking changes
- Ready for user code review before push to origin/main
