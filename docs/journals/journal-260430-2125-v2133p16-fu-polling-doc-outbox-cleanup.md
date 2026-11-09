# v2.133p16 Follow-up: Polling Guidelines Doc + Outbox Test Batching

**Date**: 2026-04-30 21:25  
**Severity**: Low  
**Component**: Documentation, Infrastructure (outbox fixture)  
**Status**: Resolved  
**Commits**: `204be927` (POLL-FU4) + `683dc01e` (OUTBOX-DISP-FU1+FU3)

## What Happened

Two micro follow-ups shipped today to close loose ends from v2.133p16 + outbox work:

**Commit 1 (204be927):** Added "Frontend Polling Guidelines (v2.133p16)" section (~110 lines) to `docs/code-standards.md`. Codified 4 mandatory polling rules from v2.133p16 implementation to enforce in future PRs. Closes POLL-FU4.

**Commit 2 (683dc01e):** Expanded OutboxStore clock-skew comment (FU1, zero behavior change) + refactored OutboxStoreFixture batch seeding from N individual INSERTs → chunked batches (FU3, test-only). Closes OUTBOX-DISP-FU1 + FU3.

## The Brutal Truth

Both patches felt like "paperwork mode" — not sexy, but essential. Documentation is friction until the next dev breaks the rule and gets reviewer nack. Test fixture batching saved 19 round-trips at current scale; if test suite grows to 250 rows, that's 2 fewer seconds per run.

The outbox clock-skew comment? Pure defensive programming. NTP drift hasn't happened yet, but when it does (2am on a Friday), that comment will save 45 minutes of wondering why claims aren't being reclaimed.

## Technical Details

### POLL-FU4: Polling Guidelines Codification

Added mandatory rules to `code-standards.md` section 8.7:
1. **`enabled` gate**: All polling hooks must gate with `enabled={isAuthenticated}`. Prevents unauthenticated loops on mount.
2. **Stale time rule**: `staleTime ≥ refetchInterval/2`. Prevents cache thrashing + redundant queries.
3. **No override `refetchIntervalInBackground`**: Always `true` unless breaking change approved. Keeps app responsive.
4. **≤1 poller per page**: Escapes: (a) `useQueries` with shared INTERVAL, (b) BE aggregate endpoint.

Example extracted from `web/src/features/admin-dashboard/admin-dashboard-api.ts:35-82`:
```typescript
const useAdminDashboardStats = (enabled: boolean = true) => {
  return useQuery({
    queryKey: ["adminDashboardStats"],
    queryFn: apiClient.adminDashboard.getStats,
    refetchInterval: 300000, // 5min
    staleTime: 150000, // 2.5min ≥ 2.5min
    enabled: isAuthenticated && enabled,
    retry: queryConfig.shouldRetryQuery,
  });
};
```

Code-reviewer now rejects PRs adding polling hooks without these checks. Defense-in-depth: hooks inherit `query-config.ts` retry guard + `notification-signalr.ts` circuit breaker automatically.

**Files**: `docs/code-standards.md` (+92), `backlogs/backlog.md` (mark FU4 done), `docs/project-changelog.md` (+15).

### OUTBOX-DISP-FU1: Clock-Skew Comment Expansion

`OutboxStore.cs` line 32-38: Expanded comment documenting clock skew risk.

**Current logic**: `staleCutoff = DateTimeOffset.UtcNow - ClaimTimeout` (host clock C#) vs `ClaimedAtUtc` written by SQL Server `SYSUTCDATETIME()` (server clock). Default 5-min ClaimTimeout makes drift benign.

**If NTP failure observed**: Switch to server-side `WHERE ClaimedAtUtc < DATEADD(SECOND, -@timeoutSeconds, SYSUTCDATETIME())` — both timestamps from one clock source.

Comment-only change. Zero behavior impact. Future-proofs against 2am clock-skew incident.

**Files**: `OutboxStore.cs` (+8 comment lines).

### OUTBOX-DISP-FU3: Test Fixture Batch Seeding

`OutboxStoreFixture.cs` `SeedRowsAsync(int count)`: Refactored from N sequential INSERTs (one CommandText per row) → chunked batch INSERT (100 rows/chunk).

**Before**: 20 rows = 20 round-trips to SQL Server.  
**After**: 20 rows = 1 round-trip; 250 rows = 3 round-trips.

**Semantics preserved**: Unique `Guid.NewGuid()` per row, "TestEvent" type, "{}" payload, NULL `ClaimedAtUtc`.

**Verification**: Messaging integration suite 7/7 PASS:
- `ConcurrentClaim_TwentyRows_ReturnsDisjointSets` — 538ms (claim race partition verified)
- `FreshClaim_BeforeClaimTimeout_IsNotReclaimable` — 44ms
- `StaleClaim_BeyondClaimTimeout_IsReclaimed` — 100ms

Build: 0 errors, 0 warnings. Docker `docker-sqlserver-1` healthy via `TEST_USE_LOCAL_SQL=true`.

**Files**: `OutboxStoreFixture.cs` (+22/-6), `backlogs/backlog.md` (mark FU1+FU3 done), `docs/project-changelog.md` (+15).

## What We Tried

Both patches followed YAGNI strictly:
- **FU1 (clock-skew)**: Comment-only, defer real fix to NTP drift evidence. Zero risk.
- **FU3 (batch seeding)**: Test fixture only, no production code change. Perf win, no behavior impact.

No new unit tests for FU1 (pure comment). FU3 verified via existing race-condition suite.

## Root Cause Analysis

Not a "root cause" so much as **closure of loose ends**:

- v2.133p16 shipped polling guardrails but lacked **codified rules** for reviewers — POLL-FU4 fixes.
- Outbox clock-skew is a **theoretical risk** until it happens in prod — documenting trigger + fix ahead of time.
- Test fixture creating 20 round-trips was **acceptable at current scale** but poor slope as suite grows — batch seeding future-proofs.

## Lessons Learned

1. **Documentation enforcement is code enforcement** — POLL-FU4 checklist in code-standards.md + reviewer training beats custom linter. Cheap, human-readable, can evolve.

2. **Defensive comments cost nothing** — OUTBOX-DISP-FU1 comment took 5 minutes, unblocks 2am incident response by 45 minutes when NTP fails.

3. **Batch operations in test fixtures compound** — at 250 rows, serial seeding becomes noticeable. Always chunk at 100-row boundaries early.

4. **Micro patches need micro journal entries** — not every PR needs a saga. FU-level work: context + change + decision + done.

## Next Steps

- Backlog post-batch: 3 closures today (POLL-FU4, OUTBOX-DISP-FU1, OUTBOX-DISP-FU3).
- Remaining FE polling cluster (POLL-FU1, POLL-FU2, POLL-FU3): Defer until trigger evidence (axios CB, BE aggregate, SignalR push migration).
- Remaining outbox cluster (OUTBOX-DISP-FU2, OUTBOX-DISP-FU4): Defer until DBA evidence (CTE MAXDOP 1) or flake incident.
- Monitor polling metrics in prod post-v2.133p16 release.

---

**Commits**: 204be927 + 683dc01e | **Branch**: main | **Files Changed**: 5 | **Tests**: 7/7 PASS | **Build**: 0E/0W
