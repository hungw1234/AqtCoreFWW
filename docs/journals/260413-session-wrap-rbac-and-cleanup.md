# 260413 Session Wrap — RBAC Bugs Fixed + 13 Plans Archived

**Date**: 2026-04-13 08:23 → 11:00  
**Severity**: Medium (bug fixes + cleanup)  
**Component**: Identity module (RBAC enforcement), AuthServer, plans/  
**Status**: Resolved

## What Shipped

- `5c554e56` — fix(identity): RBAC menu filter + seeder idempotency (F4/F8/F10 bugs, 12 tests)
- `c80bb22f` — fix(authserver): replace obsolete KnownNetworks with KnownIPNetworks (1 line)
- 13 completed plans archived from `/plans` → batch cleanup
- 5 active plans remain + 2 new backlog items (T3-04 resolved, T3-05 pending)

## The Brutal Truth

**Plan status drift is silent and dangerous.** The 260409-1524 RBAC plan was marked "Implemented — bugs". But F5/F6 weren't actually bugs — they were already fixed in an earlier session. The RBAC agent verified code before implementing, avoiding duplicate work. But the plan stayed stale. Lesson: **mark plans "Verified Completed" only after code audit, not assumption**.

Also: previous session flagged "AuthServer build error" as critical blocker. Reality: deprecation warning on `KnownNetworks`, not an error. Wasted a backlog ticket entry. Lesson: **read compiler output literally** — "error" in message ≠ build failure.

The F10 bug discovery is worth noting: plan described it as consequence of F4. Actually separate: `BuildAndPruneTree` fell through to leaf-authorization-check when `subtree.Count == 0`, incorrectly including empty group headers. Surfaced only when writing unit tests. TDD caught it.

## Technical Details

### F4/F10 Bug: Group Node Algorithm Flaw

**Problem**: Two-pass menu filter works correctly for simple trees, but when a group's all children are filtered (zero left after pruning), the algorithm treated the group as a "leaf with no permission gate" → included empty group headers in output.

**Root cause**: No distinction between true leaf nodes vs. groups that became empty after child pruning.

**Fix**: Add `hasDbChildren` boolean — detect if flat menu list has any entry with `ParentId == currentMenu.Id`.
- If `hasDbChildren=true` → this is a group node: prune if `subtree.Count == 0` after filter
- If `hasDbChildren=false` → true leaf node: apply authorization gate

```csharp
var hasDbChildren = menus.Any(x => x.ParentId == m.Id);
if (hasDbChildren && subtree.Count == 0)
    return null;  // Empty group after pruning — remove
```

Changed `BuildAndPruneTree` from `private static` to `internal static` + added `InternalsVisibleTo` to csproj for testability.

### F8 Bug: Seeder Idempotency Leak

**Problem**: E2ETestDataSeeder re-seeds RolePermissions on every test suite run. Check `AnyAsync(rp => rp.RoleId == systemAdmin.Id)` prevents duplication, but doesn't survive when admin role permissions are manually edited — re-seeding would fail if some expected permission codes got deleted.

**Fix**: Sentinel-based idempotency — check for one specific baseline permission (e.g., `IDENTITY.USER.VIEW`). If that code exists in SystemAdmin role, skip re-seed entirely. More resilient than checking role membership alone.

### Batch Plan Archive

13 completed plans (`260408-*`, `260409-1418`, `260409-1407`, `260409-1425`, etc.) were cluttering `/plans` dir. Scripted grep for status="completed" + bulk move to `/plans/archive/` took <5 min. Clarity win for future sessions.

## What We Tried

1. **Keep F10 fix inside F4 logic** → rejected: would couple two separate concerns, made algorithm harder to test
2. **Use permission lookup cache** (F8) → rejected: adds state, test isolation risk
3. **In-memory deduplication** (F8) → rejected: doesn't survive test restarts

## Root Cause Analysis

**Why plan status drifted:** RBAC enforcement was a red-team audit outcome (260409-1524). Plan documented findings correctly. But over next 3 days, findings got resolved in different sessions (F5 in T5-09 work, F6 in FE layout refactor). Plan never updated to "Verified Completed" — just lingered as "bugs exist."

**Why TDD found F10:** Plan mentioned F10 as consequence. Implementation did fix F4's primary case correctly. But test suite — when trying to verify "group with all children filtered should disappear" — revealed algorithm fell through to wrong branch. Code review alone wouldn't catch this (logic looks consistent). Tests made the gap explicit.

**Why KnownNetworks flagged:** T3-04 backlog was created prematurely. Compiler warning on obsolete property was misread as breaking error. RBAC agent fixed it cleanly in 1 line.

## Lessons Learned

1. **Plan status ≠ code state; always verify before touching.** Check git log for actual changes, not plan markup.
2. **Batch cleanup is ROI-positive.** 13 plans archived in <5 min, massive clarity for future sessions.
3. **TDD catches algorithm bugs that code review misses.** Test-first approach to F10 revealed group vs. leaf distinction, which manual review wouldn't surface.
4. **Warning ≠ error; read literally.** "Obsolete" attribute on `KnownNetworks` was deprecation path, not breaking change.
5. **Create backlog tickets for concerns, not inline scope creep.** RBAC agent correctly added T3-04 + T3-05 as separate items instead of bundling into commit.
6. **Seeder idempotency needs sentinel, not just role membership.** State-based guards are fragile; lookup-based checks survive admin edits.

## Numbers

| Metric | Value |
|--------|-------|
| Commits (today) | 2 |
| New tests (today) | 12 (9 BE + 3 FE) |
| Plans archived | 13 |
| Plans active | 5 |
| New backlog items | 2 (T3-04 resolved, T3-05 pending) |
| Lines changed | ~80 (F4 fix ~40, F8 fix ~25, KnownNetworks 1) |

## Outlook

**T3-05** (10 phase5 FE failures in smoke tests): quick win like T3-04, unrelated to RBAC work.

**260411-1014** forms-permission-gating: pending P1, now unblocked by RBAC fixes.

**T3-01** Docker CI unblock: higher leverage, should prioritize.

## Next Steps

1. Track plan status rigorously — add "Verified Completed" phase before archive
2. Add "Compiler warnings ≠ errors" to debug checklist
3. Keep seeder sentinel pattern in memory feedback
4. Review remaining 5 active plans for drift before next session
