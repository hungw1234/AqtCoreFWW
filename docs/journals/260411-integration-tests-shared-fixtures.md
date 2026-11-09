# Integration Tests + SharedFixtures DRY Refactor

**Date:** 2026-04-11
**Severity:** Medium (architecture improvement)
**Component:** Test infrastructure (17 integration test projects, SharedFixtures)
**Status:** Resolved

## What Happened

Completed T1-02 Phase 3: Added 10 new integration test projects (36 tests), then immediately refactored 17 projects to eliminate 3K lines of fixture duplication by extracting shared fixtures into a dedicated project.

## The Brutal Truth

Created fixture code duplication during rapid parallel execution (speed over DRY), then fixed it in Commit 2. This worked, but it's a lazy pattern I need to avoid: intentional duplication "for speed" followed by refactor just adds rework.

## Technical Details

- **Commit 1**: 10 new integration projects (Workflow, Collaboration, Integration, Reporting, Rules, Search, Signature, AI, MasterData, SystemParams)
- **Code Review Caught**: 5 constructor bugs (fixture wiring), 1 auth policy bug → all fixed before commit
- **Commit 2**: Extracted `tests/shared/AqtCoreFW.Tests.SharedFixtures/` with SqlServerFixture, WebAppFixture, TestAuthHandler
- **Result**: -2,865 lines (34 fixture files deleted, 3 shared files created)
- **Build**: 0 errors. 544 unit tests pass, 0 regressions

## What We Tried

1. Copy-paste fixtures per project (Commit 1) — fast but violates DRY
2. Extracted shared project immediately (Commit 2) — caught csproj path issue (needs 3 levels up from `tests/shared/`)

## Root Cause Analysis

Prioritized speed over design during parallel agent execution. Should have coordinated with agents to create SharedFixtures as first step, not added all projects then refactored. The duplication was intentional but created unnecessary rework.

## Lessons Learned

- **Don't trade DRY for speed in infrastructure.** Shared test fixtures are foundational — build once, use everywhere
- **Finalize checklist matters.** I missed dependency-map update + journal entry initially. User caught it. Makes this real
- **Code review works.** Caught 5 constructor bugs before merge — parallel execution needs human verification

## Next Steps

- Monitor new integration test projects for fixture maintenance
- When spawning parallel agents for test creation, pre-create shared infrastructure first
- Follow finalize checklist religiously: docs update → journal → sign-off

**Files:** `C:\AqtCoreFW\docs\dependency-map.md` (Section 7 added), `C:\AqtCoreFW\tests\shared\AqtCoreFW.Tests.SharedFixtures\`
