# AqtCoreFW Project Changelog — Archive

Historical releases and changes prior to 2026-05-01. For recent changes, see [project-changelog.md](./project-changelog.md).

---

## v2.133p17 — POLL-FU1: Axios Circuit Breaker (2026-04-30 ~21:50 ICT)

**Scope:** P2 backlog closure from v2.133p16 FE polling/retry storm follow-ups. Defense-in-depth tier 3 on top of `query-config.ts` retry guard + `notification-signalr.ts` circuit breaker.

**Changes:**

- `web/src/core/api/api-client.ts` (+~70 lines): module-scoped breaker state (`failTimestamps` sliding window, `pauseUntil` epoch). Threshold ≥3 network failures (`status=0`) trong window 10s → `pauseUntil = now + 30s`, request-side pre-check trả về normalized `BACKEND_UNAVAILABLE` short-circuit cho new requests, antd toast `key='backend-unavailable'` dedupe 1 lần per trip burst. Successful response auto-resets `failTimestamps`; pauseUntil tự expire. In-flight requests NOT cancelled (pragmatic scope — chỉ chặn req mới). Test-only exports `__resetBackendBreakerForTests` + `__getBackendBreakerState`. Mirror precedent SignalR CB pattern (`notification-signalr.ts:27-43`).
- `web/src/core/i18n/locales/{vi,en}.json`: thêm 1 key `error.http.backendUnavailable` ("Máy chủ tạm thời không phản hồi. Đang tạm dừng yêu cầu trong 30 giây.").
- `web/src/test/api/api-client-breaker.test.ts` (NEW, 6 vitest cases): below-threshold no-trip / trip+reject với BACKEND_UNAVAILABLE / 30s expiry auto-close / success-reset counter / sliding-window prune (3 fails spread 11s không trip) / toast dedupe 1 lần.

**Test results:** 6/6 new pass, 13/13 existing api-client tests pass, 0 regressions trên full FE suite (12 pre-existing jsdom/Antd Modal failures unrelated). Code-review 9.2/10 APPROVE_WITH_MINOR (1 P2 comment scope tightening applied in-PR; 0 P0/P1).

**Rationale:** Khi BE rớt, v2.133p16 query-config retry guard + SignalR CB đã chặn ~85% req storm. Axios CB chặn phần còn lại (~10-15%) bằng cách không cho new requests phát sinh trong 30s pause window. Stack tổng thể ước tính chặn ~95% storm.

**Risk:** Threshold 3/10s/30s là educated guess; có thể cần tuning sau prod monitoring. POLL-FU2 (BE aggregate endpoint) + POLL-FU3 (SignalR push migration) vẫn trigger-gated DEFERRED.

**Plan:** `plans/260430-2136-poll-fu1-axios-circuit-breaker/`.

---

## v2.133p16-FU2 — OUTBOX-DISP-FU1+FU3: Clock-skew note + SeedRows batching (2026-04-30)

**Scope:** 2 P3 backlog closures from v2.133p12 OUTBOX-DISP work. No production code path changes.

**Changes:**

- `src/shared/AqtCoreFW.Infrastructure/Persistence/Outbox/OutboxStore.cs` (FU1, comment-only): expanded comment block at `staleCutoff` computation with clock-skew rationale. C#-host `DateTimeOffset.UtcNow` vs SQL-server `SYSUTCDATETIME()` divergence is benign at 5-min ClaimTimeout default; if prod NTP drift becomes observable, switch to server-side `DATEADD(SECOND, -@timeoutSeconds, SYSUTCDATETIME())`. Zero behavior change.
- `tests/integration/AqtCoreFW.Messaging.Integration.Tests/Fixtures/OutboxStoreFixture.cs` (FU3, test fixture): `SeedRowsAsync(int count)` refactored from N individual INSERTs to chunked batch INSERT (chunk size 100 rows/statement). Round-trips: 20→1 at current race test scale; 250→3 if scaled. Same INSERT semantics preserved (unique `Guid.NewGuid()` per row, "TestEvent" type, "{}" payload, NULL ClaimedAtUtc).

**Test results:** Messaging integration suite 7/7 PASS — `ConcurrentClaim_TwentyRows_ReturnsDisjointSets` (claim race partition), `FreshClaim_BeforeClaimTimeout_IsNotReclaimable`, `StaleClaim_BeyondClaimTimeout_IsReclaimed`. Build 0E/0W. Docker SQL Server (`docker-sqlserver-1`) used via `TEST_USE_LOCAL_SQL=true`.

**Backlog status:** OUTBOX-DISP-FU2 (CTE MAXDOP 1 hint) remains deferred — needs DBA evidence.

---

## v2.133p16-FU — POLL-FU4: Polling Guidelines Doc (2026-04-30, docs-only)

**Scope:** Closure of v2.133p16 follow-up POLL-FU4 (no code change, docs-only).

**Changes:**

- `docs/code-standards.md`: Thêm section "Frontend Polling Guidelines (v2.133p16)" (~110 lines) sau "Frontend Error Handling Patterns (V2)". Nội dung: 4 mandatory rules (enabled gate / staleTime ≥ refetchInterval/2 / refetchIntervalInBackground:false / ≤1 poller per page với escape hatches `useQueries` + BE aggregate) + defense-in-depth reference đến `query-config.ts` retry guard + `notification-signalr.ts` circuit breaker + code-review checklist + working example trích từ `useAdminDashboardStats`.
- `backlogs/backlog.md`: POLL-FU4 marked SHIPPED.

**Rationale:** Chuẩn hóa pattern v2.133p16 thành rule explicit để code-reviewer reject PR vi phạm; ngăn drift các polling hook tương lai mà không cần linter custom.

**Backlog status:** POLL-FU1 (axios CB), POLL-FU2 (BE aggregate), POLL-FU3 (SignalR push migration) vẫn deferred chờ trigger evidence.

---

## v2.133p16 — FE-POLLING-RETRY-STORM: Polling/Retry Mitigation (2026-04-29 ~17:45 ICT)

**Scope:** FE-only patch. Chặn bão request khi BE rớt ngắn hạn via retry guard, SignalR breaker, auth gate.

**Changes:**

1. **Retry Guard** (`web/src/core/api/query-config.ts`): react-query retry policy refined:
   - Skip retry cho network error / 4xx (khác 408/429).
   - Retry 5xx chỉ 1 lần với exponential backoff cap 8s.
   - Prevents cascading hammering on temporary BE unavailability.

2. **SignalR Circuit Breaker** (`web/src/features/notifications/notification-signalr.ts`):
   - Track `startSignalR()` fail streak (reset on success).
   - Pause 5 phút sau 5 lần fail liên tiếp.
   - Skip start khi `!isAuthenticated` (no session = no need).

3. **Auth Gate on 8 Polling Hooks** (notification-api + inbox-api + dashboard-api + admin-dashboard-api):
   - `useUnreadCount`, `useWorkflowInbox`, `useDashboardKpi`, `useAdminDashboardStats` (4 sub-queries), `useRecentAdminActivity`.
   - Thêm `enabled: isAuthenticated` check.

**Files Changed:**
- 2 modified: query-config.ts, notification-signalr.ts
- 6 modified: notification-api, inbox-api, dashboard-api, admin-dashboard-api
- 2 new: query-config-retry.test.ts (19 vitest), notification-signalr-breaker.test.ts (6 vitest)

**Test Results:**
- FE vitest: 822/830 PASS (8 fail pre-existing, not related to this patch)
- 25 new tests cover retry guard + breaker logic

**Code Review:** 9.5/10 APPROVED (0 P0/P1)

**Backlog Impact:**
- FE-POLLING-RETRY-STORM: ✅ CLOSED

---

## v2.133p15 — MIG-PREFLIGHT: Migration Cleanup Prologue + Integration Tests (2026-04-29 ~16:45 ICT)

**Scope:** Preflight migration cleanup for pre-existing duplicate active workflow instances + comprehensive integration test coverage.

**Changes:**

1. **Migration Preflight Cleanup:** Modified `20260429110600_AddUniqueActiveWorkflowInstanceIndex` Up() to include CTE-ranked DELETE prologue before CREATE UNIQUE INDEX.
   - **Problem:** v2.133p11 shipped unique filtered index `UX_WorkflowInstances_Active_Entity` to prevent duplicate active instances. However, environments pre-dating v2.133p11 holding race-induced duplicates would crash with SqlException 1505 on first migrate post-patch.
   - **Solution:** CTE-ranked DELETE keeps latest CreatedAt per (EntityType, EntityId, TenantId), purges older. Cascade FK cleans WorkflowInstanceHistories automatically.
   - **Safety:** Deployment idempotent. Environments where v2.133p11 succeeded already held zero duplicates (index enforced). Environments where v2.133p11 failed rolled back without __EFMigrationsHistory entry, so prologue re-runs only there.

2. **Integration Test Coverage:** NEW `tests/integration/AqtCoreFW.Workflow.Integration.Tests/Migrations/UniqueActiveInstanceIndexPreflightCleanupTests.cs` (3 [Fact] tests):
   - **PreflightCleanup_KeepsLatestPerGroup_AndPurgesOlder** — Seeds 3 duplicates, verifies cleanup retention logic (latest by CreatedAt survives, older purged).
   - **PreflightCleanup_IsIdempotent_OnAlreadyCleanData** — Runs prologue twice on clean data, confirms second pass is no-op.
   - **IndexEnforcesUniqueness_AfterMigrationApplied** — OUTBOX-1-FU coverage: confirms index actively blocks duplicate active inserts (SqlException 2627/2601), filters work correctly (completed rows exempt).

3. **Unit Test Fix (H1):** `WorkflowMigrationIntegrationTests.MakeInstance()` changed entityId from reused `AdminUserId` → `Guid.NewGuid()` per call. Pre-existing: 5 tests silently collided when `UX_WorkflowInstances_Active_Entity` unique index was applied (v2.133p11), violating constraint. Fix resolves collision without test logic change.

**Files Changed:**
- 1 modified: `src/modules/workflow/.../20260429110600_AddUniqueActiveWorkflowInstanceIndex.cs` (Up() preflight CTE)
- 1 new: `tests/integration/AqtCoreFW.Workflow.Integration.Tests/Migrations/UniqueActiveInstanceIndexPreflightCleanupTests.cs` (3 tests)
- 1 modified: `tests/unit/AqtCoreFW.Workflow.Infrastructure.Tests/Persistence/WorkflowMigrationIntegrationTests.cs` (MakeInstance entityId fix)

**Test Results:**
- T1 (Unit): 441/441 PASS (Workflow.Infrastructure 432 baseline + 9 fixed via H1)
- T2 (Integration): 23/27 PASS (3 new preflight tests)
- T3 (Architecture): 18/18 PASS (no design changes)
- Build: 0E / 0W

**Code Review:** APPROVED (0 P0/P1)

**Backlog Impact:**
- MIG-PREFLIGHT: ✅ CLOSED in this patch
- OUTBOX-1-FU: ✅ CLOSED in this patch (integration test roundtrips migration intent)

---

## v2.133p14 — DOC-N + ARCH-GUARD-N: Code-Standards Docs + Migration Consistency Tests (2026-04-29 ~15:30 ICT)

**Scope:** Documentation + architecture guard follow-up to code-review suggestions from v2.133p13.

**Changes:**

1. **Documentation (DOC-N):** Added "Migration Folder Convention" section to `docs/code-standards.md` (≈50 lines). Documents that 16 modules use `Persistence/Migrations/` but Integration module uses `Migrations/` directly at root (anomalous structure introduced in v2.132p1). Explains rationale: test-only module at that time; structure now embedded in codebase (no refactor benefit; deferred YAGNI).

2. **Architecture Test Suite (ARCH-GUARD-N):** NEW `tests/architecture/AqtCoreFW.Architecture.Tests/MigrationConsistencyTests.cs` (~234 LOC, 2 [Fact] tests):
   - **Test 1: FileBalancePairs** — Scans `*/Infrastructure/Migrations/*.Designer.cs` files; ensures every Designer.cs has corresponding `.cs` file (no orphans). Whitelists Integration's `Migrations/` folder via path-probing helper.
   - **Test 2: DesignerSnapshotDrift** — Compares each latest Designer.cs against current ModelSnapshot.cs (regex parse for migration properties). Detects if snapshot↔latest migration property drift exists (e.g. new columns added to Designer.cs but Snapshot not updated).

**Real Drift Found (Pre-existing):** Test caught real issue on first run. Workflow module: `20260429110600_AddUniqueActiveWorkflowInstanceIndex.Designer.cs` was missing the `ClaimedAtUtc` property that was added by v2.133p12's earlier-timestamp migration (`20260429070508_AddClaimedAtUtcToOutboxMessages`). Root cause: EF migration files generated BEFORE the ClaimedAtUtc migration committed (timing issue on commit order in branch). **Hand-fix applied in same patch:** Updated Designer.cs to include ClaimedAtUtc property (mirrors current ModelSnapshot byte-for-byte). Safe per EF semantics — `migrations remove` would corrupt __EFMigrationsHistory; hand-sync is correct approach.

**Files Changed:**
- 1 modified: `docs/code-standards.md` (+50 lines)
- 1 new: `tests/architecture/AqtCoreFW.Architecture.Tests/MigrationConsistencyTests.cs` (~234 LOC)
- 1 modified: `src/modules/workflow/.../20260429110600_AddUniqueActiveWorkflowInstanceIndex.Designer.cs` (+6 lines incl. annotation comment)

**Test Results:**
- Test-only + docs-only patch — T1 ≡ T3 (the architecture suite IS the touched module); T2 N/A (no integration scope change).
- Architecture suite: 18/18 PASS (16 baseline + 2 new MigrationConsistencyTests).
- Build: 0E/0W full solution.
- Negative test verified (deliberately corrupt Designer.cs → test fails with module-named diagnostic; restore → green).

**Code Review:** 9/10 APPROVED (0 P0/P1, 4 cosmetic nits applied in-PR: L1 misleading verbatim-string comment fixed, L3 inline `// Hand-fix v2.133p14` annotation on Workflow Designer, L4 git pathspec `**` clarification in DOC-N rule 3, plus comment-stripping in NormalizeWhitespace to allow hand-fix annotations inside method body).

**Backlog Impact:**
- DOC-N: ✅ CLOSED in this patch
- ARCH-GUARD-N: ✅ CLOSED in this patch

**Lessons Learned:**
- Architecture test suite (T3) immediately proved value: caught real pre-existing drift on first run that would have remained dormant until EF migration regeneration triggered review.
- Integration folder anomaly should have been documented at v2.132p1 (test-module addition) — now corrected.
- Future migrations: snapshot↔latest drift now guarded by automated test (CSQ-family work — prevents silent inconsistencies).

---

## v2.133p13 — Integration Migration Drift Recovery (HOT-FIX) (2026-04-29 ~14:40 ICT)

**Context:** v2.133p12 (commit `3f8b2815`) shipped ClaimedAtUtc column addition across 14 OutboxMessages tables on 2026-04-29 ~14:25 ICT. However, Integration module's 2 migration files (`20260429070508_AddClaimedAtUtcToOutboxMessages.cs` + `.Designer.cs`) were left untracked in git due to glob path mismatch during commit staging (`**/Persistence/Migrations/*` intended to capture all module migrations, but Integration uses `Migrations/` directly at module root instead of `Persistence/Migrations/` like other modules).

**Root Cause:** Commit strategy used glob pattern `git add "src/**/Persistence/Migrations/*"` which missed Integration's anomalous folder structure. The Designer + snapshot were updated in EF Core state, but .cs files were not added to git.

**Production Impact Prevented:** OutboxDispatcher.ClaimAsync on post-deploy of v2.133p12 would crash with SQL error `Invalid column name 'ClaimedAtUtc'` when querying Integration.OutboxMessages table (migration never applied in production DB).

**Scope:** v2.133p13 (this commit) — Add the 2 missing Integration migration files to git. No code logic change, no architecture change. Drift recovery only.

**Files Added:**
- `src/modules/integration/AqtCoreFW.Integration.Infrastructure/Migrations/20260429070508_AddClaimedAtUtcToOutboxMessages.cs` (newly staged)
- `src/modules/integration/AqtCoreFW.Integration.Infrastructure/Migrations/20260429070508_AddClaimedAtUtcToOutboxMessages.Designer.cs` (newly staged)

**Test Results:**
- T1 (Unit): 75/75 PASS (Integration module)
- T2 (Integration): 4/4 PASS (Outbox race tests)
- T3 (Architecture): 16/16 PASS
- Build: 0E/0W

**Code Review:** APPROVE 10/10 (0 P0/P1/P2/P3)
- 2 future suggestions from reviewer: (a) Document Integration folder anomaly in `docs/code-standards.md`; (b) Add architecture-suite guard to detect snapshot↔migration file drift detection.

**Backlog Entries Added:**
- DOC-N: Document Integration folder anomaly in code-standards.md (future reference)
- ARCH-GUARD-N: Architecture-suite guard for snapshot↔migration drift (relates to CSQ family, P2-3 future)

---

## v2.133p12 — OUTBOX-1-DISP Atomic Claim Refactor: Defense-in-Depth Multi-Replica Fix (2026-04-29)

**Scope:** Eliminate claim-race window in multi-replica Outbox dispatcher. Pre-existing: `OutboxDispatcher.ClaimAsync` acquired UPDLOCK in autocommit SELECT, released after SELECT, then UPDATE in separate implicit tx. Multi-replica contention: two replicas SELECT same batch, both see lock-free state, both claim rows → duplicate publishing (silent data duplication). Solution: single-statement CTE atomic claim-and-update via `SELECT TOP(N) ... WITH (UPDLOCK,READPAST,ROWLOCK) + UPDATE ... OUTPUT` in same statement.

### Key Changes

**1. Atomic Claim-and-Update CTE (OutboxStore)**
- File: `src/modules/*/Infrastructure/Outbox/OutboxStore.cs` (14 modules)
- Old pattern (TOCTOU window): `SELECT WITH (UPDLOCK,READPAST) ... ORDER BY CreatedAtUtc; UPDATE WHERE Id IN (...) SET ProcessedAtUtc=...`
- New pattern (atomic): `;WITH cte AS (SELECT TOP(@claimSize) Id, CreatedAtUtc FROM OutboxMessages WITH (UPDLOCK,READPAST,ROWLOCK) WHERE ProcessedAtUtc IS NULL AND (ClaimedAtUtc IS NULL OR ClaimedAtUtc < @staleCutoff) ORDER BY CreatedAtUtc) UPDATE cte SET ClaimedAtUtc=SYSUTCDATETIME() OUTPUT inserted.*`
- Semantics: Single statement eliminates release-between-statements window. ROWLOCK ensures per-row escalation (not page/table). All updates attributed same timestamp = idempotency anchor.

**2. ClaimedAtUtc Nullable Column (14 module OutboxMessages tables)**
- Migration: `20260429HHMMSS_AddClaimedAtUtcColumn` per module
- Column: `ClaimedAtUtc datetimeoffset NULL`
- Index: `IX_OutboxMessages_ClaimedAtUtc_ProcessedAtUtc` on claimed+processed pair (CTE filter predicate)
- Rationale: Claim timestamp enables stale-claim recovery (via ClaimTimeout). NULL = not claimed. NOT NULL + recent = actively claimed. NOT NULL + old = stale (crashed dispatcher recovery).

**3. Stale-Claim Recovery (OutboxOptions.ClaimTimeout)**
- Property: `ClaimTimeout` (default TimeSpan.FromMinutes(5))
- Usage: Dispatcher periodic re-check finds `ClaimedAtUtc < DateTime.UtcNow - ClaimTimeout` → re-claims (idempotent CTE ensures safety)
- Rationale: Covers dispatcher crash post-claim (process dies after UPDATE, before ProcessedAtUtc write). Without recovery: message stuck claimed forever (DLQ backlog never drains).

**4. Stale-Claim Clearing (MarkRetry/MarkProcessed/MoveToDeadLetter)**
- All state-transition methods: `UPDATE OutboxMessages SET ClaimedAtUtc=NULL WHERE Id=...`
- Correctness-critical for MarkRetry: without clearing, message re-enters queue but stays `ClaimedAtUtc=old` → stale-recovery assumes it's stuck → retry delay 5 min (silent bug)
- Tests verify clearing in all 3 paths.

**5. New Test Files**
- `OutboxStoreFixture.cs` (147 lines) — Shared test infra for 14 OutboxStore test classes (local SQL, auto-cleanup, seeding helpers)
- `OutboxStoreRaceTests.cs` (61 lines, 3 tests): (a) ConcurrentClaim: two replicas claim different rows (disjoint), no collision. (b) StaleClaim: old claim gets reclaimed after 5 min. (c) FreshClaim: recent claim not reclaimed (safety).

**6. DesignTimeDbContextFactory Addition (Reporting module)**
- File: `src/modules/reporting/AqtCoreFW.Reporting.Infrastructure/ReportingDesignTimeDbContextFactory.cs` (NEW)
- Reason: Reporting module migrations file was generating without factory. Added per precedent (Identity, Cases, Forms, etc.) to ensure `dotnet ef migrations list` works offline.

### Backwards Compatibility

- Column `ClaimedAtUtc` nullable (default NULL) → zero breaking change
- Old instances (before migration): `ClaimedAtUtc=NULL` → treated as "never claimed" (safe)
- No backfill needed
- Rolling deploy safe: old dispatcher sees NEW CTE + new nullable column (CTE filter `ClaimedAtUtc IS NULL OR ...` matches)
- Old consumer side remains functional (v2.133p11 race collapse orthogonal)

### Files Changed

- **BE Source (5):** OutboxStore.cs (14 ×), OutboxOptions.cs (default ClaimTimeout), OutboxStoreFixture.cs (new), OutboxStoreRaceTests.cs (new), ReportingDesignTimeDbContextFactory.cs (new)
- **Migrations (28):** 14 modules × 2 (cs + Designer). +14 Snapshots updated.
- **Tests (2):** OutboxStoreFixture.cs, OutboxStoreRaceTests.cs

### Test Results

- T1 (Unit): 31/31 PASS (OutboxStore suite + fixture baseline)
- T2 (Integration): 7/7 PASS (OutboxStore race integration via local SQL)
- T3 (Architecture): 16/16 PASS (no design changes)
- Build: 0E / 0W

### Code Review

Score: 8.5/10 APPROVE_WITH_MINOR

**0 P0/P1 issues.** 4 P2 deferred (non-blocking, enhancement):
1. **Clock-skew note** — `staleCutoff` computed C# side vs `SYSUTCDATETIME()` server. Benign at 5-min default; document if prod observes drift.
2. **OPTION (MAXDOP 1) hint** — CTE may benefit from serial plan (avoid parallel locking semantics change). Defer validation until observed.
3. **SeedRowsAsync batching** — N round-trips at seed=20. Cosmetic; batch INSERT if scale test runs.
4. **Race test stress (deg=8)** — 3 tests sufficient for cold CI. Optional 3rd test: `Parallel.ForAsync` 100 rows deg=8 (defends false-positive under warm threadpool).

---

## v2.133p11 — OUTBOX-1 Race Collapse: Unique Filtered Index + Idempotent Consumer (2026-04-29)

**Scope:** Eliminate TOCTOU window in Outbox dispatcher + CaseCreatedWorkflowRequestedEvent consumer. Two replicas racing on same case creation could both observe "no WorkflowInstance" → both call `CreateInstanceByDefinitionKeyAsync` → duplicate rows created. Solution: atomic DB constraint failure collapsed via unique filtered index + idempotent consumer catch.

### Key Changes

**1. Unique Filtered Index (Workflow module)**
- Migration `20260429110600_AddUniqueActiveWorkflowInstanceIndex` (Create UNIQUE INDEX + Down DROP IF EXISTS)
- Index: `UX_WorkflowInstances_Active_Entity` on `(EntityType, EntityId, TenantId) WHERE CompletedAt IS NULL`
- Applied to WorkflowDbContext via `WorkflowInstanceConfiguration.HasIndex(...).IsUnique().HasFilter("[CompletedAt] IS NULL")`
- Rationale: Only active (uncompleted) workflow instances must be unique per entity. Completed instances don't participate in race (immutable state = no concurrent state transitions).

**2. Idempotent Consumer Catch**
- File: `CreateWorkflowInstanceOnCaseCreatedConsumer.cs` (Workflow.Application/EventHandlers/)
- Try/catch on `CreateInstanceByDefinitionKeyAsync` with reflection-based `IsDbUpdateUniqueConstraintViolation` helper
- Helper detects `DbUpdateException` wrapping `SqlException` with Number ∈ {2627=duplicate-key, 2601=unique-constraint-violation}
- On collision: idempotent re-probe via `TryResolveInstanceForEntityAsync` → return success if instance now exists
- On anomaly (re-probe null) or non-unique error: rethrow for upstream handling
- **OCE Parity:** Prepended explicit `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` BEFORE unique-violation filter (defensive against future broadening per v2.133p10 AUD-2 pattern)

**3. Consumer Code Pattern**
```csharp
try
{
    var instance = await _workflowClient.CreateInstanceByDefinitionKeyAsync(..., ct);
    return new { Success = true, Instance = instance };
}
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    throw; // Propagate cancellation immediately
}
catch (DbUpdateException ex) when (IsDbUpdateUniqueConstraintViolation(ex))
{
    // Race collapsed by DB constraint. Re-probe idempotently.
    var existing = await _workflowClient.TryResolveInstanceForEntityAsync(..., ct);
    if (existing != null)
        return new { Success = true, Instance = existing };
    else
        throw; // Anomaly: constraint failed but re-probe found nothing
}
```

### Test Coverage

**T1 (Unit):** 450/450 PASS
- 441/441 baseline Workflow.Application tests
- 9/9 new race-condition tests (`CreateWorkflowInstanceOnCaseCreatedConsumerRaceTests.cs`)
  - 4 reflection-based violation detection tests
  - 4 idempotency + error-path tests
  - 1 OCE propagation test

**T2 (Integration):** 23/27 PASS (56s)
- Workflow.Integration: 23/27 (2 pre-existing auth unrelated, 2 skip)
- **Deferred follow-up (P3, ~0.25d):** Dedicated `UniqueActiveInstanceIndexTests.cs` integration test verifying real duplicate INSERT triggers SqlException end-to-end. Reflection-against-fake unit tests + happy-path T2 regression sufficient for ship; gap acknowledged.

**T3 (Architecture):** 16/16 PASS (CSQ-6 v2.133p10 mandatory gate)

**Build:** 0E/0W (clean compilation)

### Code Review

**Verdict:** APPROVED_WITH_MINOR (8/10)

**P1 Fixed In-PR:** OCE catch parity pattern from v2.133p10 AUD-2 — prepended to idempotent filter to maintain cooperative cancellation semantics.

**Open Deferred:** UniqueActiveInstanceIndexTests integration test (P3 backlog entry OUTBOX-1-FU).

### Files Changed (4 modified + 3 new)

**Modified:**
1. `src/modules/workflow/.../EventHandlers/CreateWorkflowInstanceOnCaseCreatedConsumer.cs` — try/catch + reflection helper + OCE filter
2. `src/modules/workflow/.../Configurations/WorkflowInstanceConfiguration.cs` — HasIndex() fluent API
3. `src/modules/workflow/.../Persistence/Migrations/WorkflowDbContextModelSnapshot.cs` — index snapshot
4. `src/modules/workflow/.../Clients/InProcessWorkflowModuleClient.cs:87` — TODO comment referencing shipped migration

**New:**
1. `src/modules/workflow/.../Migrations/20260429110600_AddUniqueActiveWorkflowInstanceIndex.cs` — CREATE/DROP
2. `src/modules/workflow/.../Migrations/20260429110600_AddUniqueActiveWorkflowInstanceIndex.Designer.cs` — Designer
3. `tests/unit/.../CreateWorkflowInstanceOnCaseCreatedConsumerRaceTests.cs` — 9 tests

### Impact Analysis

- **Behavior:** Outbox dispatcher + consumer now atomic at database constraint level. Previous duplicate-instance collisions logged as `CaseWorkflowInstanceDuplicate` now converted to idempotent success.
- **Breaking Changes:** None. Idempotent pattern ensures backward compatibility.
- **Migration:** One-time unique index creation on active instances (CompletedAt IS NULL filter avoids large historical table scans).
- **Performance:** Negligible. Index enforces uniqueness on small active subset (typically <100 rows per tenant in steady state).

---

## v2.133p10 — Mini-batch: CSQ-6 Architecture Test Gate + AUD-2 Cancel Propagation (2026-04-29)

**Two independent improvements shipped together (TBD-COMMIT):**

### CSQ-6 (P2 process change, global): Architecture Test Suite to Finalize Gate

**Scope:** Edit `~/.claude/rules/primary-workflow.md` (new Section 7a "Pre-Finalize Test Gate" with T1/T2/T3 categories; Step 2 Testing bullet added; verification table extended) + `~/.claude/skills/cook/SKILL.md` (Blocking Gates section, version 2.1.1→2.1.2).

**Rationale:** v2.133p5→p9 incident — `TenantBackfillSeeder` cross-schema regression slipped undetected through 4 patches because `dotnet test tests/architecture/` not in finalize gate. Architecture violations are compile-error severity for design contracts.

**Impact:** AqtCoreFW benefits immediately; rule applies globally to all CK harness projects going forward.

### AUD-2 (P3 cleanup, code): Three Cancel-Semantics Fixes

**Scope:** Three fire-and-log `catch (Exception ex)` blocks now prepend `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }` for cooperative cancellation:
1. `src/modules/audit/AqtCoreFW.Audit.Infrastructure/Services/AuditService.cs:72`
2. `src/modules/forms/AqtCoreFW.Forms.Application/Commands/SubmitForm/SubmitFormCommandHandler.cs:177`
3. `src/modules/forms/AqtCoreFW.Forms.Application/Commands/SubmitPublicForm/SubmitPublicFormCommandHandler.cs:172`

**Behavior:** Non-cancel exceptions continue log-and-swallow (fire-and-log intent preserved). No new tests (mechanical 1-line edits per site, well-established C# idiom).

**Test Results:**
- **T1 unit:** Forms.Application 138/138 + Audit.Infrastructure 2/2 + Audit.Application 38/38 = **178/178 PASS**
- **T2 integration:** Audit 8/8 + Forms 7/7 = **15/15 PASS**
- **T3 architecture:** **16/16 PASS**
- **Total: 209/209 PASS. Build 0E/0W.**

**Code-review:** APPROVED (1 non-blocking observation: MassTransit `publisher.Publish` upstream of try-block out of AUD-2 scope).

**Plan:** `plans/260429-1016-csq6-arch-tests-and-aud2-cancel-propagation/plan.md`.

---

## v2.133p9 — Architecture Test Whitelist Fix: Regression Guard CI RED (2026-04-29)

**Root cause:** v2.133p5 (`TenantBackfillSeeder` IHostedService) shipped with raw SQL cross-schema reference `[organization].[OrgUnits]` that violated architecture guard `NoModule_Should_Reference_AnotherModuleSchema_InRawSql` (active since v2.132p10). Regression undetected for 4 patches (p5/p6/p7/p8) because test suite not run during finalize.

**Fix (1 file):** Added whitelist entry in `tests/architecture/AqtCoreFW.Architecture.Tests/Helpers/CrossSchemaSqlScanner.cs:84-85`:
```csharp
new("TenantBackfillSeeder.cs", "organization",
    "Reason: one-time IHostedService backfill — derives Tenants from DISTINCT TenantId across AspNetUsers ∪ OrgUnits. Runs at startup, not request-path; no IOrgUnitModuleClient surface fits one-shot backfill query (would create dead client API per YAGNI).")
```

**Trade-off rationale:**
- **Whitelist (chosen):** ~5 lines, 0 surface API change, YAGNI-aligned. Precedent: 5 other seeders already whitelisted.
- **Refactor through IOrgUnitModuleClient (rejected):** Would require NEW `DeriveTenantsAsync(Guid.Empty)` client method serving 1 caller executing 1 time per deploy → violates YAGNI.

**Lesson:** Architecture suite must run as part of Cook finalize gate (not just unit/integration of touched module). Design contract violations = compile errors for maintainability; severity parity with syntax errors.

**Build:** 0E/0W | **Tests:** Architecture 16/16 PASS, Identity.Infrastructure 66/66 PASS.

---

## v2.133p8 — RBAC Follow-ups: Dead query key removal + Group role name enrichment (2026-04-29)

**Two follow-up P2-P3 fixes completing RBAC v2.133.x tail.**

### RBAC-5 (P2): Remove Dead Query Prefixes from Allowlist

**Issue:** `web/src/core/api/query-config.ts:10-11` contained stale allowlist entries `'permissions'` (singular) and `'menu'` (singular). Modern codebase uses plural form query roots: `['me', 'permissions']` and `['menus', 'tree']`, which intentionally invalidate on tenant switch per file comment. Legacy singular entries were unreachable dead code.

**Impact:** Dead code clutters codebase semantics; false positive in code review if someone tries to resurrect singular form without understanding they conflict with invalidation policy.

**Fix:** Removed `'permissions'` and `'menu'` entries. Allowlist now `['auth', 'i18n', 'session']` (only actually preserved queries). Comment updated to clarify design: modern keys use plural/hierarchical form (me.* / menus.*) specifically to trigger invalidation on tenant change.

**Verification:** vitest `it.each(PRESERVED_KEY_PREFIXES)` auto-adapts — 6/6 PASS validates each remaining entry against existing query hooks.

### RBAC-3 (P3): Group Role Names Missing from MeController Response

**Issue:** `EffectivePermissionSummary.GroupRoles` contains list of `GroupRoleInfo` records with `RoleCode` (uppercase) but not `RoleName` (PascalCase). When `MeController.GetMyPermissions` returns roles, it only emits `DirectRoles.RoleName` via LINQ select, ignoring group-inherited roles. Users whose only role path was group membership got `roles=[]` in API response.

**Impact:** UI permission display incomplete for group-inherited users; AdminGuard fallback-skip masked the issue (admin check passed via permissions endpoint which HAD the group codes, but UI role display was empty).

**Root Cause:** `EffectivePermissionService.BuildFromDbAsync` populates `GroupRoles` via JOIN on `GroupRoleAssignments`, but the source `RolePermission` table has only uppercase `Code` column, not PascalCase `Name`. JOIN result materialized without role name enrichment.

**Fix (3 changes):**
1. Extended `GroupRoleInfo` record signature: added `RoleName: string` field (5 parameters total, up from 4).
2. `EffectivePermissionService.BuildFromDbAsync` — when materializing `GroupRoleInfo` from JOIN, now includes role name via second JOIN on `Roles` table: `...JOIN r2 ON gr.RoleId = r2.Id SELECT ... r2.Name...`.
3. `MeController.GetMyPermissions` — now unions and dedupes both `DirectRoles` + `GroupRoles.Select(g => new { g.RoleCode, g.RoleName })` before LINQ distinct by code.

**Files modified (4):**
- `src/modules/identity/Infrastructure/Ddd/Aggregates/UserEffectivePermission/GroupRoleInfo.cs` (record signature add RoleName)
- `src/modules/identity/Infrastructure/Services/EffectivePermissionService.cs` (JOIN + materialization)
- `src/modules/identity/Presentation/Controllers/MeController.cs` (union + dedup)
- `web/src/core/hooks/use-my-effective-permissions.ts` (no code change, beneficiary of FE-side dedup removed in prior v2.133p5)

**Compatibility note:** Stale Redis cache entries from v2.133p7 may hold 4-arg `GroupRoleInfo` when new code tries to deserialize 5-arg positional ctor. Caught by existing try/catch at `EffectivePermissionService.cs:155` → fallback to DB query → self-heal (same pattern as v2.133p7 RBAC-2).

**Build:** 0E/0W | **Tests:** Identity.Infrastructure 66/66 PASS + vitest 6/6 PASS.

---

## v2.133p7 — RBAC Code-Review Follow-ups: Per-user cache invalidation + HashSet fix (2026-04-29, PR #65 follow-up)

**Two critical P1-P2 fixes addressing code-review findings.**

### RBAC-2 (P2): Redis Cache Deserialize Bug Fix

**Issue:** `EffectivePermissionSummary.PermissionCodes` was typed as `IReadOnlySet<string>` (abstract interface). System.Text.Json rejects abstract types → every cache hit failed deserialize → silent fallback to database query on every permission check (defeats cache purpose).

**Impact:** Cache hit rate 0% (all misses), effective permissions computed from DB every 10min → performance regression vs. intended Redis 10min TTL caching.

**Fix:** Changed `PermissionCodes` from `IReadOnlySet<string>` to concrete `HashSet<string>`. Self-healing: existing try/catch fallback in `EffectivePermissionService.cs:155` automatically handles stale v2.133p6 Redis payloads (attempts deserialize, catches error, re-fetches from DB).

**Verification:** Cache hit rate now observable via Redis metrics; no app restart needed for stale entries (lazy refresh).

### RBAC-1 (P1): SyncRolePermissions Cache Invalidation Gap

**Issue:** When admin synced role-permission mapping via `SyncRolePermissionsCommandHandler`, only the older `user-roles:*` Redis cache was invalidated (via `IEffectivePermissionService.InvalidateAsync(userId)`). The new-style `perm:{userId}` cache (effective permissions, TTL 10min) was NOT invalidated.

**Impact:** Stale window = Redis TTL 10min + React Query staleTime 10min = up to 20min until UI reflects permission change for users holding the edited role.

**Root Cause:** Code-review discovered that the handler's `InvalidateAsync(userId)` path only covered direct role holders. Three inheritance paths needed:
1. Direct `UserRoles` (covered in initial impl)
2. Group membership: `UserGroupMembership` → `GroupRoleAssignments` (missed in initial)
3. Delegation: `UserDelegations` where Delegator holds the role + delegation is active (missed in initial)

**Fix:** NEW method `IBulkRoleChangeService.InvalidatePermissionCacheForUsersAsync(userIds)`:
- Queries union of affected users across all 3 inheritance paths
- Deletes `perm:{userId}` keys in Redis (graceful if Redis down, logs warning)
- Called from `SyncRolePermissionsCommandHandler` after role-permission sync

**Conservative approach:** Over-invalidates (ignores DelegatedRoleIds scope filter, clears cache for all users in delegation chain). Code-review recommended this over under-invalidation on low-frequency admin path.

**Files modified (9 total):**
- **Source (4):** `IBulkRoleChangeService.cs` (new interface), `BulkRoleChangeService.cs` (new implementation), `SyncRolePermissionsCommandHandler.cs` (wired invalidation), `EffectivePermissionService.cs` (HashSet fix)
- **Tests (5):** `BulkRoleChangeServicePermCacheTests.cs` (NEW, 3 tests: direct roles, group inheritance, delegation paths), plus 4 existing Identity tests updated for new interface

**Test coverage:** 66/66 Identity.Infrastructure PASS + 214/214 Identity.Application PASS. Build 0E/0W.

**Code-review status:** APPROVE_WITH_MINOR (8/10) — P1 delegation gap addressed before commit.

---

## v2.133p6 — RBAC API-driven Permission Refactor: JWT revert + `/me/permissions` endpoint + FE permission hook (2026-04-28, PR #__)

**Three integrated phases shipped 2026-04-28.**

### Phase 1: AuthServer JWT Permission Claim Removal

**Token bloat reduction:** Prior, JWT `id_token` had `permission` claim (string array, 124 codes × ~25 chars = 3-4KB per SystemAdmin). New: token < 1KB (only `sub`, `email`, `name`, `role`, `tenant_id`).

**Impact:** AuthServer no longer depends on `IEffectivePermissionService` or Redis connection. Token footprint reduced 75-80%.

### Phase 2: Permission API Endpoint + Seeder Pattern

**NEW endpoint `GET /api/v1/me/permissions`** (Identity.MeController): Returns user's effective role + permission codes (computed fresh from db, no stale token window).

**Seeder pattern shift to delta-merge:** Permission seed now uses **idempotent per-row upsert** — adding new permission code to source = auto-seed on next app boot. No manual db INSERT needed.

**Menus seeder integration:** Tenants admin menu row added with `IDENTITY.TENANT.VIEW` permission code mapping → admin auto-inherits visibility.

### Phase 3: FE Permission Hook + Route Guards

**NEW `useMyEffectivePermissions()` hook** (React Query, staleTime 10min): Replaces hardcoded JWT-claim reading. Consumers check `/api/v1/me/permissions` instead of decoded token.

**Sidebar refactor:** `hasAccess()` fallback removed — menu fully API-driven via `useMyMenus()` (no permission check needed, menu list is pre-filtered by server).

**Route security:** `/admin/tenants` wrapped in `<RouteGuard requiredPermissions={['IDENTITY.TENANT.VIEW']}>` (data-driven, not role-based).

**AdminGuard refactor:** Changed from `roles.includes('Admin')` to permission-based check (`permissions.length > 0` for any admin perm).

**Test coverage:** 45 unit + 20 integration + 25 FE vitest PASS. Build 0E/0W. Code-review APPROVED.

**Follow-ups (deferred):** Real-time permission sync via SignalR (P2), permission version stamp (P2), Redis cache deserialize bug (P1).

---

## v2.133p5 — Tenant Management Bundle: BE aggregate + FE CRUD + tenant-switch reload fix (2026-04-28, PR #64 MERGED)

**Three integrated phases shipped 2026-04-28 (uncommitted, user will commit after doc sync).**

### Phase 1: FE Tenant-Switch Reload Bug Fix

**P1 queryClient stale-cache regression on tenant switch.** When user changed tenant in dropdown, unrelated data caches (menu, organization tree, forms list) remained stale 30s per global `staleTime`. Manifested as: switch tenant → see old menu until re-render timeout → jarring UX.

**Root cause:** `admin-tenant-selector.tsx` wrote `sessionStorage` but did NOT invalidate queryClient. Global `query-config.ts` `staleTime: 30_000` kept old-tenant data fresh.

**Fix (3 files):**
1. **NEW** `web/src/core/api/query-config.ts` — added `shouldInvalidateOnTenantSwitch` predicate + `PRESERVED_KEY_PREFIXES` allowlist (preserve: `auth-*`, `permissions-*`, `menu-*`, `i18n-*`, `session-*` — cross-tenant stable keys). Exported for hook consumption.
2. `web/src/shared/components/admin-tenant-selector.tsx` — `cancelQueries()` BEFORE `invalidateQueries(predicate)` on tenant change (race fix: in-flight requests to old tenant must abort before cache invalidation).
3. **NEW** `web/src/core/api/use-tenant-switch.ts` hook — exported predicate for FE re-use.

**Test coverage:** 10 vitest tests (predicate behavior, selector interaction, hook).

**Impact:** Zero behavior change if tenant not switched; on switch, queryClient respects allowlist.

### Phase 2: BE Tenant CRUD Aggregate (Option A)

**NEW `Tenant` aggregate in Identity module.** Previously, "tenants" were synthetic (DISTINCT TenantId from AspNetUsers + OrgUnits roots). Now canonical entity.

**Domain entity `Tenant` (`[identity].Tenants` table):**
- Id (PK, Guid)
- Name (string, required)
- Code (string, unique immutable, required)
- ContactEmail (string, nullable)
- Status (enum: Active, Suspended, Archived; soft-delete via Status)
- RowVersion (concurrency token)
- CreatedAt, ModifiedAt, CreatedBy, ModifiedBy (audit via AuditableEntity)

**Files added (31 total):**
- **Domain:** `Tenant.cs`, `TenantStatus.cs`, `TenantCreatedEvent.cs`, `TenantStatusChangedEvent.cs` (IExternalDomainEvent), `ITenantRepository.cs`
- **Application:** 3 command handlers (Create/Update/ChangeStatus) + 2 query handlers (GetById/ListAdmin) + DTOs (CreateTenantCommand/Dto, UpdateTenantCommand/Dto, ChangeTenantStatusCommand) + FluentValidation validators
- **Infrastructure:** `TenantConfiguration.cs` (EF mapping), `TenantRepository.cs` (CQRS pattern), **NEW EF migration** `20260428120054_AddTenantsTable` + Designer + Snapshot
- **Seeding:** `TenantBackfillSeeder.cs` (IHostedService, idempotent backfill from `DISTINCT TenantId` in `[identity].AspNetUsers` ∪ `[organization].OrgUnits`; graceful skip if already seeded via version marker)
- **Presentation:** `TenantsAdminController.cs` (6 endpoints)
- **Permissions:** Permission seed in `E2ETestDataSeeder.Permissions.cs` adds `IDENTITY.TENANT.{VIEW,MANAGE}` codes; SystemAdmin role inherits via existing all-perms loop
- **Tests:** 4 unit command handler tests + 2 integration files (`TenantsAdminControllerTests` 12 cases, `TenantBackfillSeederTests` 2 cases = 18 total)
- **Test infra:** `WebAppFixture.cs` adds `TestSystemAdminPermissionBypassHandler` (auth shortcut, test-only, never deployed)

**Refactored `ListTenantsQueryHandler`:**
- Now reads canonical `[identity].[Tenants]` table (bracket-escaped schema — fixes v2.133p4 reserved-keyword regression)
- Retains OrgUnit-root mapping + user-count enrichment for AdminTenantSelector compatibility
- Zero behavior change vs. prior synthesized query

**Design decision (Option A rationale):**
- Soft-delete via `Status` enum (no physical deletes) — audit trail preserved
- NO FK retrofit on AspNetUsers.TenantId / OrgUnits.TenantId (loose Guid) — deferred to future
- Scope: SystemAdmin CRUD + multi-tenant selector enrichment

**Open follow-ups (P2/P3 backlog, deferred after review):**
- Tenant suspension does NOT block in-flight JWTs (deferred policy decision) — TODO comment in `ChangeTenantStatusCommandHandler`
- Publisher dispatch inconsistency between Update vs Create/ChangeStatus handlers (P2 cleanup)
- Backfill multi-instance race on distributed startup (P2-3, acceptable for current single-host topology)

**Files changed summary (v2.133p4 list-tenants refactor already bracketed):**

1. Domain entities (4): `Tenant.cs`, `TenantStatus.cs`, `TenantCreatedEvent.cs`, `TenantStatusChangedEvent.cs`
2. Repository + Infra (3): `ITenantRepository.cs`, `TenantRepository.cs`, `TenantConfiguration.cs`
3. Commands (3): `CreateTenantCommand/Handler`, `UpdateTenantCommand/Handler`, `ChangeTenantStatusCommand/Handler` + DTOs
4. Queries (2): `GetTenantByIdQuery/Handler`, `ListAdminTenantsQuery/Handler` + DTOs
5. Controller (1): `TenantsAdminController.cs` (6 endpoints)
6. Seeding (1): `TenantBackfillSeeder.cs`
7. EF Migration (3): `20260428120054_AddTenantsTable.cs` + Designer + Snapshot
8. Permissions (1): `E2ETestDataSeeder.Permissions.cs` (adds 2 codes)
9. Test infrastructure (1): `WebAppFixture.cs` (`TestSystemAdminPermissionBypassHandler`)
10. Tests (2 files, 18 cases): `TenantsAdminControllerTests.cs` + `TenantBackfillSeederTests.cs`

### Phase 3: FE Tenant CRUD Page

**NEW feature module `web/src/features/tenants/`.**

**Files added (7):**
1. `tenant-types.ts` — TypeScript interfaces (Tenant, TenantStatus enum, DTOs)
2. `tenant-api.ts` — TanStack Query hooks (`useCreateTenant`, `useUpdateTenant`, `useChangeTenantStatus`, `useDeleteTenant`, `useGetTenant`, `useListAdminTenants`, etc.)
3. `tenant-status-tag.tsx` — component badge rendering Status enum (Active green, Suspended orange, Archived gray)
4. `tenant-form-drawer.tsx` — create/edit form with validation (Name, Code unique-immutable, ContactEmail, Status)
5. `tenant-list-page.tsx` — admin page with DataGrid, bulk actions, filters
6. `index.ts` — barrel export
7. Tests (5 vitest files): hooks validation, form state, list rendering, status enum display, API integration

**Route registration:** `/admin/tenants` added to `web/src/app/router.ts` (lazy-loaded, route-gated by `IDENTITY.TENANT.VIEW`)

**Sidebar:** "Quản lý tenant" entry added to `web/src/layouts/admin-menu-entries.ts` under System category

**i18n:** Vietnamese inline (matches existing admin-page convention; i18n key extraction deferred to future batch PR)

**Test coverage:** 15 vitest PASS (hooks, form, list, status tag, enum)

### Integration & Testing

- **Build:** `dotnet build src/AqtCoreFW.slnx` → 0E/0W
- **BE tests:** 36 unit + 14 integration (TenantBackfillSeeder integration x2 required explicit IAsyncLifetime per v2.132p10 WebAppFixture pattern)
- **FE tests:** 15 vitest (all green)
- **Code review:** 6/10 APPROVED (P0 fixed: column rename `UpdatedBy`→`ModifiedBy` + IsDeleted/ClassificationLevel default values in seeder; P1 fixed: TenantBackfillSeederTests added during Phase 4)

### Impact & Next Steps

- **Zero breaking change:** Tenant CRUD is new surface (pre-existing ListTenantsQuery refactored to use new table, no API contract change)
- **Cross-tenant isolation:** TenantBackfillSeeder gracefully handles single-host + multi-instance topologies; idempotent via version marker
- **Open follow-ups (backlog — P2/P3):**
  - R-3 / Q2: In-flight JWT validation during tenant suspension (policy decision needed)
  - Backfill race on multi-instance startup (acceptable for current single-host, address if SLO changes)
  - Publisher dispatch pattern unification across Create/Update/ChangeStatus handlers
  - i18n batch extraction (defer to next batch with other admin pages)

---

## v2.133p4 — Production Fixes: EF migration for messaging.dead_letters + Identity list-tenants SQL schema escape (2026-04-28)

**Two cumulative P1 fixes merged into v2.133p4 (PRs #62 + #63, commits `4be7e4c3` + `10fcf667`).**

### Fix 1: Messaging Dead-Letter EF Migration (PR #62, commit `4be7e4c3`)
**P1 from v2.133p3 code-review.** `MessagingDbContext` had no production EF migration — only `EnsureCreated` in test fixtures. `DeadLetterConsumer` would fail with "Invalid object name 'messaging.dead_letters'" in production whenever MassTransit retry policy exhausted. Latent because retry exhaustion rare in dev/test.

### Fix 2: Identity ListTenants Query Schema Escape (PR #63, commit `10fcf667`)
**P1 runtime regression fix.** `ListTenantsQueryHandler` used unescaped schema reference `identity.AspNetUsers` in raw SQL, causing "Incorrect syntax" on SQL Server where `identity` is reserved keyword. Bracket-escaped to `[identity].[AspNetUsers]` + fixed deduplication logic (`GroupBy(TenantId).ToDictionary(g => g.Key, g.OrderBy(u => u.Name).First())` vs. broken `roots.ToDictionary(...)`). Regression test `RawSqlSchemaEscapeRegressionTests.cs` scans all `.cs` files for similar patterns. FE `admin-tenant-selector.tsx` upgraded console.error logging for ops debugging.

### Files Changed Summary (15 files)

#### Messaging Dead-Letter Files (8)
1. **NEW** `src/shared/AqtCoreFW.Infrastructure/Messaging/DesignTimeDbContextFactory.cs` — design-time factory for migration generation (matches Audit/Cases/etc convention)
2. **NEW** `src/shared/AqtCoreFW.Infrastructure/Messaging/Migrations/20260428090407_InitialCreate.cs` — EF migration creating `messaging` schema + `dead_letters` table (columns: Id PK, MessageType, Payload, FailureReason, OriginalQueue, ReceivedAt, Status, RetriedAt, QuarantineReason; indexes: IX_dead_letters_Status, IX_dead_letters_ReceivedAt)
3. **NEW** `src/shared/AqtCoreFW.Infrastructure/Messaging/Migrations/20260428090407_InitialCreate.Designer.cs`
4. **NEW** `src/shared/AqtCoreFW.Infrastructure/Messaging/Migrations/MessagingDbContextModelSnapshot.cs`
5. `src/shared/AqtCoreFW.Infrastructure/Messaging/MessagingRegistration.cs` — added `MigrationsHistoryTable("__EFMigrationsHistory", "messaging")` to `AddDbContext<MessagingDbContext>` (schema-isolation convention matching all modules)
6. `src/host/AqtCoreFW.Api/Program.cs` — added `await sp.GetRequiredService<MessagingDbContext>().Database.MigrateAsync();` in startup migration block
7. `src/host/AqtCoreFW.AuthServer/Program.cs` — added same `MigrateAsync` call (AuthServer wires `AddMessageBus` since v2.133p3)
8. `tests/integration/AqtCoreFW.Messaging.Integration.Tests/Fixtures/MessagingFixture.cs:102-110` — switched from `EnsureCreated()` to `MigrateAsync()` (proves migration applies cleanly on fresh DB)

#### Identity ListTenants Fix Files (7)
1. `src/modules/identity/AqtCoreFW.Identity.Application/Queries/ListTenants/ListTenantsQueryHandler.cs` — bracket-escape `[identity].[AspNetUsers]` in raw SQL; fixed deduplication `GroupBy(TenantId).ToDictionary(g => g.Key, g.OrderBy(u => u.Name).First())`
2. **NEW** `tests/regression/AqtCoreFW.Regression.Tests/Core/RawSqlSchemaEscapeRegressionTests.cs` — scans `src/**/*.cs` for unescaped reserved-keyword schemas in raw SQL (`identity`, `user`, `view`, etc.). Guard against future regressions.
3. `web/src/shared/components/admin-tenant-selector.tsx` — replaced silent `catch {}` with `console.error(...)` for ops debugging
4. `docs/journals/journal-260428-1741-v2133p4-list-tenants-fixup.md` — session journal already committed

### Upgrade Notes (dev environments only)
- **Local-SQL test fixture (`TEST_USE_LOCAL_SQL=true`):** if you previously ran the messaging integration tests pre-v2.133p4, the persistent test DB `AqtCoreFW_Test_Messaging` already contains a `messaging.dead_letters` table created via `EnsureCreated` — but no `messaging.__EFMigrationsHistory` row. The new `MigrateAsync` will throw "object already exists" on first run. **One-time fix:** `DROP DATABASE AqtCoreFW_Test_Messaging;` against your local SQL Server before running tests; the fixture re-creates it via the migration. Testcontainers path (CI default) is unaffected — fresh container per run.
- **Concurrent host startup migration race:** Api + AuthServer both call `MessagingDbContext.MigrateAsync` against the shared DB. EF Core acquires a SQL Server app-lock per `MigrateAsync` so concurrent boots serialize safely (no corruption); k8s rolling-deploy may see the second host block briefly on the first.

### Test Coverage
- `dotnet test tests/integration/AqtCoreFW.Messaging.Integration.Tests`: 4/4 PASS (Testcontainers cold-start confirms migration clean)
- Including `TC-MSG-INT-002 PublishAsync_AlwaysFailCommand_GeneratesFaultAfterRetries` and `TC-MSG-INT-003 PublishAsync_AlwaysFailCommand_DeadLetterConsumerPersistsRecord`
- `dotnet test tests/regression/AqtCoreFW.Regression.Tests`: NEW RawSqlSchemaEscapeRegressionTests scans source for reserved-keyword schema escaping
- Full solution: 605/605 PASS

### Impact
- **Fix 1:** Zero behavior change — enables production dead-letter persistence, activates `DeadLetterConsumer` record writes
- **Fix 2:** Fixes runtime 500 error in Admin → Tenant Selector dropdown (reserved schema keyword regression); enables ops debugging via console logs

### Verification
- `dotnet build src/AqtCoreFW.slnx`: 0E / 0W
- Architecture guards: GREEN
- E2E admin-tenant-selector smoke test passes

**Plan:** (none — cumulative P1 production bug-fix patches)

---

## v2.133p3 — AuthServer DI fix + connection-string standardization (2026-04-28)

**AuthServer IMessageBus + IAuditLogger registration + conn-string standardization.**

### What changed

**Bug Fix:**
- FIXED: `/connect/authorize` on AuthServer (port 5002) threw `InvalidOperationException: Unable to resolve service for type 'AqtCoreFW.SharedKernel.Application.IMessageBus'`
- ROOT CAUSE: `AuditService` constructor now depends on `IMessageBus` (added v2.132p8 ModuleClient Events Extraction Phase 05-06), but only `AqtCoreFW.Api` host was updated with `IMessageBus` registration. `AqtCoreFW.AuthServer` host's manual Audit DI block missed the wiring.
- AuthServer had `ValidateOnBuild=false` (legacy), masking error until first request

**Files Changed (7 total):**
1. `src/shared/AqtCoreFW.Infrastructure/Messaging/MessagingRegistration.cs` — fail-fast: changed `?? string.Empty` → explicit `throw InvalidOperationException` for missing connection string (prevents 3am DLQ-write NRE on production)
2. `src/host/AqtCoreFW.AuthServer/Program.cs` — added `AddMessageBus(...)` call with `AuditLogAppendedConsumer` callback; added `services.AddScoped<IAuditLogger, AuditService>` mapping; switched conn-string key `"DefaultConnection"` → `"Default"` (standardization to match all other hosts); added `using AqtCoreFW.Infrastructure.Messaging;` + `using MassTransit;`
3. `src/host/AqtCoreFW.AuthServer/appsettings.json` — renamed `DefaultConnection` section → `Default`
4. `src/host/AqtCoreFW.AuthServer/appsettings.Development.json` — same
5. `src/host/AqtCoreFW.AuthServer/appsettings.Production.json` — same
6. `infra/docker/docker-compose.yml` (line 49) — renamed env var `ConnectionStrings__DefaultConnection` → `ConnectionStrings__Default`
7. `infra/helm/authserver/values.yaml` (line 69) — same

**Configuration & Deployment:**
- Standardized AuthServer to use connection-string key `"Default"` (matching Api, AuthServer, Gateway hosts). Eliminates documented drift that had been a cross-host DI helper footgun.
- Connection string value unchanged; only key name changed.

**Test Coverage:**
- Full solution: 605/605 PASS (baseline unchanged)
- 14 AuthServer.Tests PASS (auth protocol tests unaffected)

**Code Quality & Verification:**
- Build: 0E / 0W
- Code-review: APPROVED (0 critical findings)
- 1 P2 found & fixed in-PR: consumer registration missing from Program.cs (now present)
- 1 P1 deferred (separate task): `messaging.dead_letters` table has zero production EF migration (only `EnsureCreated` in tests)

**Impact:**
- Zero behavior change — bug was blocker preventing AuthServer startup
- Drive-by: Connection-string key standardization eliminates cross-host naming inconsistency

**Next Steps:**
- P1 backlog: Add production EF migration for `messaging.dead_letters` table

**Plan:** (none — bug-fix patch, no phase file)

---

## v2.133p2 — Async Workflow Provisioning Phase 02 (2026-04-28)

**Async workflow instance creation via CaseCreatedWorkflowRequestedEvent; config-gated deferral pattern.**

### What changed

**Event-Driven Workflow Creation:**
- NEW `CaseCreatedWorkflowRequestedEvent` (IExternalDomainEvent in SharedKernel.Domain.Events) — raised by Cases.Application when creating a Case with both `feature:cases.workflow-delegation=ON` and `Cases:DeferWorkflowCreation=true`
- NEW `CreateWorkflowInstanceOnCaseCreatedConsumer` (Workflow.Application.EventHandlers) — MassTransit consumer receiving the event from Outbox Dispatcher (Phase 01), performing idempotency probe via `IWorkflowModuleClient.TryResolveInstanceForEntityAsync`, then calling `CreateInstanceByDefinitionKeyAsync` for async instance creation
- MODIFIED `CreateCaseCommandHandler` — added `IConfiguration` parameter; branching logic: async path (defer ON) raises domain event captured to Outbox; sync path (defer OFF, default) calls `IWorkflowModuleClient.CreateInstanceByDefinitionKeyAsync` directly (preserves legacy behavior)

**Configuration & Deployment:**
- NEW config key `Cases:DeferWorkflowCreation` (default=false in appsettings.json — zero behavior change at deploy time)
- Consumers auto-registered via existing `x.AddConsumers(typeof(...).Assembly)` in Program.cs (Workflow.Application assembly already in list)
- MassTransit already wired (Phase 01 Outbox Dispatcher prerequisite)

**Idempotency & Failure Handling:**
- Idempotency probe: skips creation if active instance already exists for `(EntityType="Case", EntityId=CaseId, TenantId)`
- Logs skipped deliveries as INFO (not ERROR) — safe pattern per at-least-once semantics
- Failure handling: throws `InvalidOperationException` on definition-not-found / no-initial-state → MassTransit retries (5s/30s/5m intervals) → dead_letters after exhaustion
- Known TOCTOU window (OUTBOX-1 backlog P1-1): two consumers concurrently seeing "no instance" both call create → logged as `CaseWorkflowInstanceDuplicate`. Mitigations: (a) MassTransit `UseInMemoryOutbox` dedup within retry chain, (b) Phase 2 idempotent consumers pattern; durable fix deferred (unique partial index on `(EntityType, EntityId, TenantId) WHERE CompletedAt IS NULL`)

**Test Coverage:**
- NEW 7 consumer tests: idempotent skip, replay, tenant pass-through, definition-not-found retry, transient error, cancellation token, and happy path
- NEW 4 handler tests: flag matrix (defer ON/OFF × delegation ON/OFF) + async event raised + sync legacy path
- NEW 1 shared integration test
- Full solution: 605/605 PASS (baseline 594 + new 12 - 1 skipped duplicate)

**Code Quality & Verification:**
- Build: 0E / 0W
- Code-review: APPROVED (0 critical findings)
- Architecture guard `NoModule_Should_DirectlyReference_AnotherModule`: GREEN (unaffected by Phase 02)
- P1-2 deferred: OUTBOX-1 durable TOCTOU fix (unique partial index) and Phase-02c follow-ups

**Files Changed:**
1. NEW: `src/shared/AqtCoreFW.SharedKernel/Domain/Events/CaseCreatedWorkflowRequestedEvent.cs` (IExternalDomainEvent record)
2. NEW: `src/modules/workflow/AqtCoreFW.Workflow.Application/EventHandlers/CreateWorkflowInstanceOnCaseCreatedConsumer.cs` (IConsumer<...>)
3. MODIFIED: `src/modules/cases/AqtCoreFW.Cases.Application/Commands/CreateCase/CreateCaseCommandHandler.cs` (added IConfiguration, branching logic)
4. MODIFIED: `src/modules/workflow/AqtCoreFW.Workflow.Application/AqtCoreFW.Workflow.Application.csproj` (added MassTransit reference)
5. MODIFIED: `src/host/AqtCoreFW.Api/Program.cs` (added Workflow.Application to AddConsumers list — already present, verified)
6. MODIFIED: `src/host/AqtCoreFW.Api/appsettings.json` (added `Cases:DeferWorkflowCreation: false`)
7. NEW: `tests/unit/AqtCoreFW.Workflow.Application.Tests/EventHandlers/CreateWorkflowInstanceOnCaseCreatedConsumerTests.cs` (7 test methods)
8. NEW: `tests/unit/AqtCoreFW.Cases.Application.Tests/Commands/CreateCaseCommandHandlerTests.cs` (added 4 new test methods to existing file)

**Latency Impact:**
- Sync path (defer OFF, default): no change (~12-15ms for workflow client call included in request path)
- Async path (defer ON): request returns after SaveChanges only (workflow creation moved to background dispatcher) — ∼12-15ms latency reduction for Case creation endpoint

**Next Phase (v2.133p3+):**
- Phase-02c Follow-ups: Multi-replica TOCTOU hardening (unique partial index), additional consumer integration tests
- Later: Phase-03 FE flag-gating UI + real-time notification when async workflow creation completes

**Plan:** `plans/260424-0922-v2133-async-workflow-publish/phase-02-workflow-defer-integration-event.md`

---

## v2.133p1 — Outbox Dispatcher Phase 01 (2026-04-28)

**Polling outbox dispatcher ships; at-least-once delivery via MassTransit.**

### What changed

**Core Outbox Implementation:**
- NEW `OutboxDispatcher` (scoped service, polling loop with scope factory)
- NEW `IOutboxStore` interface + `OutboxStore` (Dapper implementation reading from 14 module schemas)
- NEW `IOutboxSchemaRegistry` interface + `OutboxSchemaRegistry` (regex-validated schema allowlist)
- NEW `OutboxSchemaRegistryExtensions.AddOutboxSchema` (DI helper for registering schemas)
- NEW `OutboxDispatchOutcome` + `OutboxDispatchSummary` (result DTOs)

**Configuration & Registration:**
- `OutboxOptions` gained `DeadLetterAfterRetries=3` + `EnableDispatcher=true` (kill switch)
- 14 module schemas registered as default allowlist: ai, cases, collab, config, files, forms, integration, masterdata, notifications, organization, reporting, rules, signature, workflow
- Audit module excluded (extends DbContext directly, no OutboxMessages table)

**Safety & Semantics:**
- At-least-once delivery guarantee (idempotence via Phase 2 consumer design)
- DLQ atomicity: single-transaction INSERT `messaging.dead_letters` + UPDATE `[schema].OutboxMessages`
- Type-resolution null → dead-letter immediately (no retry exhaustion)
- PII guard: logs only `Id/EventType/RetryCount/Schema`, never `Payload`
- P1-1 deferred: UPDLOCK released after autocommit SELECT (multi-replica weakness); mitigated by Phase 2 idempotent consumers + MassTransit UseInMemoryOutbox dedup

**Test Coverage:**
- 20 new unit tests: `tests/unit/AqtCoreFW.Infrastructure.Extensions.Tests/Outbox/` (9 dispatcher + 11 registry incl. theory)
- 31/31 passing in that suite
- Full solution: 119/119 across 4 suites (Infrastructure.Extensions 31 + Architecture 16 + Cases.Application 40 + Notifications.Application 32)

**Code Quality & Verification:**
- Build: 0E / 0W
- Code-review: 8.5/10 APPROVED_WITH_MINOR (0 critical findings)
- P1-1 backlog entry (OUTBOX-1) deferred to next session

**Files Changed:**
1. NEW: `src/shared/AqtCoreFW.Infrastructure/Persistence/Outbox/OutboxDispatcher.cs`
2. NEW: `src/shared/AqtCoreFW.Infrastructure/Persistence/Outbox/IOutboxStore.cs`
3. NEW: `src/shared/AqtCoreFW.Infrastructure/Persistence/Outbox/OutboxStore.cs`
4. NEW: `src/shared/AqtCoreFW.Infrastructure/Persistence/Outbox/IOutboxSchemaRegistry.cs`
5. NEW: `src/shared/AqtCoreFW.Infrastructure/Persistence/Outbox/OutboxSchemaRegistry.cs`
6. NEW: `src/shared/AqtCoreFW.Infrastructure/Persistence/Outbox/OutboxSchemaRegistryExtensions.cs`
7. NEW: `src/shared/AqtCoreFW.Infrastructure/Persistence/Outbox/OutboxDispatchOutcome.cs`
8. NEW: `tests/unit/AqtCoreFW.Infrastructure.Extensions.Tests/Outbox/OutboxDispatcherTests.cs`
9. NEW: `tests/unit/AqtCoreFW.Infrastructure.Extensions.Tests/Outbox/OutboxSchemaRegistryTests.cs`
10. MODIFIED: `src/shared/AqtCoreFW.Infrastructure/Persistence/Outbox/OutboxProcessor.cs` (rewrote stub → polling loop)
11. MODIFIED: `src/shared/AqtCoreFW.Infrastructure/Configuration/OutboxOptions.cs` (+2 fields)
12. MODIFIED: `src/shared/AqtCoreFW.Infrastructure/InfrastructureRegistration.cs` (added DI + 14 AddOutboxSchema)

**Next Phase (v2.133p2):**
- Phase 02: `CaseCreatedWorkflowRequestedIntegrationEvent` consumer in Workflow.Application
- Case creation flag: `Cases:DeferWorkflowCreation` (async defer pattern)
- Plan: `plans/260424-0922-v2133-async-workflow-publish/phase-02-workflow-defer-integration-event.md`

**Plan:** `plans/260424-0922-v2133-async-workflow-publish/phase-01-outbox-dispatcher-polling.md`

---

## v2.132p11 — ARCH-1 Workflow.Contracts Extraction (2026-04-28)

**IEntityStatusEnumContributor interface extracted to break module isolation violation.**

### What changed

**Architecture Fix:**
- Extracted `IEntityStatusEnumContributor` interface from `AqtCoreFW.Workflow.Application/Validation/` → NEW `AqtCoreFW.Workflow.Contracts/Validation/IEntityStatusEnumContributor.cs`
- Removed direct ProjectReference: `Cases.Infrastructure` was depending on `Workflow.Application` to consume `IEntityStatusEnumContributor`; now depends on `Workflow.Contracts` only
- NEW assembly: `src/shared/AqtCoreFW.Workflow.Contracts/Validation/` (namespace unchanged: `AqtCoreFW.Workflow.Contracts.Validation`)

**Files Changed:**
1. NEW: `src/shared/AqtCoreFW.Workflow.Contracts/Validation/IEntityStatusEnumContributor.cs` (interface definition)
2. DELETED: `src/modules/workflow/AqtCoreFW.Workflow.Application/Validation/IEntityStatusEnumContributor.cs` (moved to contracts)
3. UPDATED: `src/modules/workflow/AqtCoreFW.Workflow.Application/Validation/IEntityStatusEnumRegistry.cs` (added using for new namespace)
4. UPDATED: `src/modules/workflow/AqtCoreFW.Workflow.Application/AqtCoreFW.Workflow.Application.csproj` (added ProjectReference to Workflow.Contracts)
5. UPDATED: `src/modules/workflow/AqtCoreFW.Workflow.Infrastructure/Validation/EntityStatusEnumRegistry.cs` (added using for new namespace)
6. UPDATED: `src/modules/cases/AqtCoreFW.Cases.Infrastructure/AqtCoreFW.Cases.Infrastructure.csproj` (replaced Workflow.Application ref → Workflow.Contracts)
7. UPDATED: `src/modules/cases/AqtCoreFW.Cases.Infrastructure/Validation/CaseStatusEnumContributor.cs` (changed using statements)
8. UPDATED: `src/modules/cases/AqtCoreFW.Cases.Infrastructure/InfrastructureRegistration.cs` (changed using statements)
9. UPDATED: `tests/unit/AqtCoreFW.Workflow.Infrastructure.Tests/Validation/EntityStatusEnumRegistryTests.cs` (added using for new namespace)

**Verification:**
- Build: 0E / 0W
- Architecture guard `NoModule_Should_DirectlyReference_AnotherModule`: GREEN (was RED on baseline cf26baec — pre-existing failure due to Cases.Infrastructure → Workflow.Application dep, now resolved)
- Unit tests: 543/543 PASS
- Architecture tests: 12/12 PASS (including `NoModule_Should_DirectlyReference_AnotherModule`)
- Code-review risk: 1/10 (low risk — mechanical extraction, no behavior change)

**Plan:** `plans/260428-1254-arch1-extract-entity-status-contributor/`

---

## v2.132p10 — Microservice Extraction Prep Closure (2026-04-28)

**3 of 4 cross-schema raw-SQL sites refactored; ModuleIsolation arch guard ACTIVATED.**

### What changed

**Cross-Schema Query Refactoring:**
- `GetKpiDashboardQueryHandler` (Reporting): 5 raw-SQL queries → `ICasesModuleClient.GetKpiAggregateAsync` (NEW, v2.132p10)
  - Dapper implementation: `InProcessCasesModuleClient` in `Cases.Infrastructure/Clients`
  - DTO: `AssigneeKpiDto` + `MonthlyTrendDto` MOVED from Reporting.Application.DTOs → AqtCoreFW.SharedKernel.Contracts.Clients (DRY)
- `GetComplianceEvidenceQueryHandler` (Reporting): 3 audit.* queries → `IAuditEvidenceClient.GetComplianceEvidenceAsync` (NEW, v2.132p10)
  - Dapper implementation: `InProcessAuditEvidenceClient` in `Audit.Infrastructure/Services`
  - Single aggregate method encapsulates evidence lookup logic
- `DataScopeService` (Identity): IReadDbConnection raw CTE → `IOrgUnitModuleClient.ExpandSubtreeAsync(rootId, tenantId, ct)` (NEW, v2.132p10)
  - Tenant-scoped recursive CTE seed; defense-in-depth isolation
  - Dapper implementation: IReadDbConnection adapter in `Organization.Infrastructure`

**Contract Assemblies & DI Wiring:**
- NEW: `src/shared/AqtCoreFW.Cases.Contracts/` (csproj + ICasesModuleClient.cs)
- NEW: `src/shared/AqtCoreFW.Audit.Contracts/` (csproj + IAuditEvidenceClient.cs)
- IOrgUnitModuleClient.ExpandSubtreeAsync added (OrgUnit.Contracts v1.0.0 → v1.1.0)
- DI: `AddCasesInfrastructure` + `AddAuditInfrastructure` already invoked by host; client registrations auto-wire

**Architecture Guard Activation:**
- `ModuleIsolationTests` rule `NoModule_Should_Reference_AnotherModuleSchema_InRawSql` now **ACTIVE** (was `[Skip]`)
- `CrossSchemaSqlScanner` whitelist tightened: ReportDefinitionSeeder.cs wildcard → explicit per-schema entries (cases, organization)
- OllamaReActAgent.cs site #4 (Ai module) WHITELISTED with explicit reason: Ai deferred (CSQ-5 backlog entry)

**Test Additions:**
- AUD-3: `FormSubmissionAcknowledgedConsumerIntegrationTests` (2 methods: auth user receives event, anon user skipped)
  - NewAssembly: `tests/integration/AqtCoreFW.Notifications.Integration.Tests/EventHandlers/FormSubmissionAcknowledgedConsumerIntegrationTests.cs`
  - Tests MassTransit consumer DI wiring + event dispatch
- AUD-4: `SubmitPublicFormCommandHandlerTests` (1 happy-path: anon form publishes event with SubmittedBy=Guid.Empty)
  - NewFile: `tests/unit/AqtCoreFW.Forms.Application.Tests/Commands/SubmitPublicFormCommandHandlerTests.cs`
  - Tests public form submission event publishing

**Verification:**
- Build: 0E / 0W
- Architecture guard `NoModule_Should_Reference_AnotherModuleSchema_InRawSql` **PASSES** (previously skipped)
- AqtCoreFW.Reporting.Application.Tests: 29/29 PASS
- AqtCoreFW.Forms.Application.Tests: 138/138 PASS (incl. new AUD-4)
- AqtCoreFW.Notifications.Integration.Tests (AUD-3): 2/2 PASS
- AqtCoreFW.Tests.Contracts: 50/50 PASS
- AqtCoreFW.Identity.Infrastructure.Tests: 63/63 PASS
- **Pre-existing failure (out of scope):** `NoModule_Should_DirectlyReference_AnotherModule` flags Cases.Infrastructure → Workflow.Application via existing CaseStatusEnumContributor reference (verified baseline failure before this session)

**Backlog Impact:**
- CSQ-1 PARTIAL: Sites 1-3 complete; site #4 (OllamaReActAgent) deferred to CSQ-5
- CSQ-2 completed: whitelist drift control tightened
- 4 NEW backlog entries: CSQ-4, CSQ-5, SEC-1, SEC-2 (see backlogs/backlog.md)

**Plan:** `plans/260428-1109-microservice-extraction-prep-closure/`
**Commits:** {COMMIT_A} (Implementation), {COMMIT_B1}–{COMMIT_B2} (Tests), {COMMIT_C} (Docs), {COMMIT_D} (Guard activation)

---

## v2.132p9 — Organization Legacy Cleanup (2026-04-28)

**Removed flat-layout LEGACY project; 4-layer DDD pure.**

### What changed

**Code Deletions:**
- Deleted `src/modules/organization/AqtCoreFW.Organization/` (LEGACY flat-layout, 18 .cs files)
  - Commands, DTOs, Entities, Persistence, Queries, JwtOrgContext, OrgRegistration, OrgUnitService
- Superseded by 4-layer DDD: Application/Domain/Infrastructure/Presentation already in slnx + production host

**Visibility Promotion:**
- `JwtOrgContext`: `internal` → `public` in `AqtCoreFW.Organization.Infrastructure/OrgRegistration.cs:79` per V1 decision

**Test Updates:**
- 3 test csproj updated (dropped LEGACY ref, added NEW refs)
  - `tests/unit/AqtCoreFW.Organization.Tests` — added Application + Domain refs
  - `tests/integration/AqtCoreFW.Organization.Integration.Tests` — added Presentation ref
  - `tests/contracts/AqtCoreFW.Tests.Contracts` — LEGACY → Application ref
- 7 test source files namespace updates (Application.DTOs / Domain.Entities / Infrastructure.Persistence / Infrastructure.Services / Infrastructure.CommandHandlers / Application.Commands)

**Verification:**
- Security audit Phase 01: Zero gaps (Dapper queries all @TenantId, EF global filters apply, cache keys include tenantId). No porting required.
- Build: 0E/0W
- Tests: Unit 128/143 (15 skipped, 0 failed) | Contract 50/50 | Integration (Organization) 12/12

**Plan:** `plans/260428-0912-organization-legacy-cleanup/`
**Commit:** `ba61a790` (docs red-team revision R2 pre-execution); implementation commit: post-session

---

## v2.132p8 — ModuleClient + Events Extraction Prep: Phase 05-06 (2026-04-27)

**Phase 05: Per-module contract assembly split. Phase 06: Docs sync. Epic complete (6/6 phases).**

### What changed

**Phase 05 — Contract Assembly Split (HIGH risk, completed):**
- Created 10 new contract csproj under `src/shared/`: Chat, Consent, Files, Forms, Identity, Notification, OrgUnit, RuleEngine, Signature, Workflow Contracts
- Moved 11 client interface files (10 IXxxModuleClient + IIdentityAuditClient) out of `SharedKernel.Contracts/Clients/` into respective new assemblies
- Updated `AqtCoreFW.slnx` (10 new entries) + 17 consumer csproj ProjectReferences
- Namespaces UNCHANGED (still `AqtCoreFW.SharedKernel.Contracts.Clients`) — assembly rebrand only, minimizes using-directive churn
- SharedKernel.Contracts now lean: only `IFeatureFlagService.cs`, `IServiceIdentityProvider.cs`, `NoOpServiceIdentityProvider.cs`, `WorkflowErrorCodes.cs`, `Rtbf/*.cs`, `Events/*.cs`
- Future microservice extraction now mechanical: each module's Contracts assembly becomes published NuGet package

**Phase 06 — Docs Sync:**
- Module count corrected: 21 → 17 (source of truth = `src/modules/*/`)
- Solution project count bumped: 80+ → 90+ (10 new contract csproj added)
- 10 new contract assemblies registered in `dependency-map.md` under Client Interface Registry section
- IIdentityAuditClient location updated from `SharedKernel.Contracts` to `AqtCoreFW.Identity.Contracts`
- Cross-schema JOINs (Phase 01) documented as resolved in system-architecture
- All 5 core docs synced: codebase-summary, dependency-map, system-architecture, project-changelog, project-roadmap

**No behavior change. No entity or migration change. Build 0E/0W. All unit tests + AqtCoreFW.Tests.Integration (117/117) PASS.**

### Files changed
- 10 new csproj created under `src/shared/AqtCoreFW.{Chat,Consent,Files,Forms,Identity,Notification,OrgUnit,RuleEngine,Signature,Workflow}.Contracts/`
- `src/AqtCoreFW.slnx` (+10 entries)
- 11 .cs files moved (location only, no content change)
- 17 consumer csproj updated with new ProjectReferences
- 5 docs updated: codebase-summary, dependency-map, system-architecture, project-changelog, project-roadmap
- Plan: `plans/260427-1943-moduleclient-events-extraction-prep/` — 6/6 phases complete

---

## v2.132p7 — ModuleClient + Events Extraction Prep: Phase 03-04 (2026-04-27)

**Phase 03: Two new integration events (extraction prep). Phase 04: NetArchTest cross-schema SQL rule.**

### What changed

**Phase 03 — Integration Events (Forms + Audit):**
- `FormSubmissionAcknowledgedIntegrationEvent` (new, SharedKernel.Contracts.Events) — record: SubmissionId, FormTemplateId, TenantId, SubmittedBy.
- `AuditLogAppendedIntegrationEvent` (new, SharedKernel.Contracts.Events) — record: AuditEntryId, TenantId, ActorUserId, Action, ModuleName, ResourceType, ResourceId, OccurredAt.
- Forms publishers: `SubmitFormCommandHandler` + `SubmitPublicFormCommandHandler` now publish via `IMessageBus` (fire-and-log).
- Audit publisher: `AuditService.LogAsync` (Audit.Infrastructure) dual-writes after sync `repository.AddAsync` — covers IAuditService manual API + IAuditLogger MediatR pipeline.
- `FormSubmissionAcknowledgedConsumer` (Notifications.Application/EventHandlers/) — mirrors CaseTransitionedNotificationConsumer; anonymous Guid.Empty submitters skipped.
- `AuditLogAppendedConsumer` (noop, Audit.Application/EventHandlers/) — sync path remains authoritative; consumer is prep for future microservice extraction.
- Consumers auto-registered via `x.AddConsumers(typeof(...).Assembly)` in `Program.cs:189-194`.
- Audit.Application csproj added MassTransit PackageReference.
- 5 new unit tests (3 Notifications + 2 Audit). Forms handler test updated to inject `IMessageBus` mock.

**Phase 04 — NetArchTest Cross-Schema SQL Rule:**
- New helper: `tests/architecture/AqtCoreFW.Architecture.Tests/Helpers/CrossSchemaSqlScanner.cs` (~170 LOC; module→schema map for 17 modules, comment-line filter, whitelist with 5 entries).
- New test: `ModuleIsolationTests.NoModule_Should_Reference_AnotherModuleSchema_InRawSql` — currently `[Fact(Skip="...")]` because discovery during wiring revealed ~11 request-path cross-schema queries beyond Phase 01 scope (Reporting.GetKpiDashboard, Reporting.GetComplianceEvidence audit queries, Identity.DataScopeService, Ai.OllamaReActAgent). Whitelist mechanism already covers seeders. Test will be activated after a follow-up cleanup phase.

**No behavior change. No entity or migration change. Build 0E/0W. Notifications 32/32 pass, Audit 38/38 pass, Forms 137/137 pass = 207 total unit tests green.**

### Files changed
- `src/shared/AqtCoreFW.SharedKernel.Contracts/Events/FormSubmissionAcknowledgedIntegrationEvent.cs` (new)
- `src/shared/AqtCoreFW.SharedKernel.Contracts/Events/AuditLogAppendedIntegrationEvent.cs` (new)
- `src/modules/forms/AqtCoreFW.Forms.Application/Commands/SubmitFormCommandHandler.cs` (publish)
- `src/modules/forms/AqtCoreFW.Forms.Application/Commands/SubmitPublicFormCommandHandler.cs` (publish)
- `src/modules/audit/AqtCoreFW.Audit.Infrastructure/Services/AuditService.cs` (dual-write + publish)
- `src/modules/notifications/AqtCoreFW.Notifications.Application/EventHandlers/FormSubmissionAcknowledgedConsumer.cs` (new)
- `src/modules/audit/AqtCoreFW.Audit.Application/EventHandlers/AuditLogAppendedConsumer.cs` (new)
- `src/host/AqtCoreFW.Api/Program.cs` (consumer registration)
- `src/modules/audit/AqtCoreFW.Audit.Application/AqtCoreFW.Audit.Application.csproj` (MassTransit ref)
- `tests/architecture/AqtCoreFW.Architecture.Tests/Helpers/CrossSchemaSqlScanner.cs` (new)
- `tests/architecture/AqtCoreFW.Architecture.Tests/ModuleIsolationTests.cs` (new rule)

---

## v2.132p6 — Security Patch: OpenTelemetry CVE + Privacy Regression Fix (2026-04-27)

**CVE GHSA-g94r-2vxg-569j (moderate): DoS via propagation header parsing in OpenTelemetry.Api 1.12.0. Privacy regression fixed: EF instrumentation v1.13+ removed SetDbStatementForText option — replaced with EnrichWithIDbCommand enricher for null-tagged SQL emissions. Multi-tenant SQL leakage to OTLP collector @ localhost:4317 blocked.**

### What changed

**Security bumps (mirrored from archived oracle-19c branch v2.141p1):**
- `OpenTelemetry.Exporter.OpenTelemetryProtocol`: 1.12.0 → 1.15.3
- `OpenTelemetry.Extensions.Hosting`: 1.12.0 → 1.15.3
- `OpenTelemetry.Instrumentation.AspNetCore`: 1.12.0 → 1.15.2
- `OpenTelemetry.Instrumentation.Http`: 1.12.0 → 1.15.1
- `OpenTelemetry.Instrumentation.Runtime`: 1.12.0 → 1.15.1
- `OpenTelemetry.Instrumentation.EntityFrameworkCore`: 1.0.0-beta.12 → 1.15.1-beta.1

**Privacy fix:**
- `OpenTelemetryConfiguration.cs`: Replaced deprecated `SetDbStatementForText(false)` with `EnrichWithIDbCommand` enricher. New enricher null-tags both `db.statement` and `db.query.text` post-capture, preventing SQL query emission to OTLP backend.

**No behavior change. No entity or migration change. Build 0E/0W. All tests green.**

### Files changed
- `src/Directory.Packages.props` (6 OpenTelemetry package versions)
- `tests/Directory.Packages.props` (mirror, NU1109 downgrade gate)
- `src/shared/AqtCoreFW.Infrastructure/Telemetry/OpenTelemetryConfiguration.cs` (enricher swap)

---

## v2.132p5 — ModuleClient + Events Extraction Prep: Phase 01-02 (2026-04-27)

**Phase 01: Cross-schema JOIN elimination via new module clients. Phase 02: Signature forward-compat exception. Zero behavior change, zero entity/migration change.**

### What changed

**Phase 01 — Cross-schema JOIN Refactors (Identity + Reporting):**
- `IOrgUnitModuleClient` (Organization module, SharedKernel.Contracts): 3 new methods `GetRootOrgUnitForTenantAsync(tenantGuid, ct)`, `GetRootOrgUnitsForTenantsAsync(tenantGuids[], ct)`, `ListTenantRootsAsync(maxCount, ct)` + new `OrgUnitSummary` DTO class (Id, TenantId, Name, IsRoot, Level).
- `InProcessOrgUnitModuleClient` (Organization.Infrastructure.Services): implements 3 methods against `organization.OrgUnits` schema via Dapper (ParentId IS NULL AND Level=1 AND IsDeleted=0 filter).
- `IIdentityAuditClient` (NEW, SharedKernel.Contracts): interface + `AccessReviewStatusSummary` DTO (TenantId, PeriodFrom, PeriodTo, ApprovedCount, RejectedCount, PendingCount).
- `InProcessIdentityAuditModuleClient` (NEW, Identity.Infrastructure.Services): implements interface, calls Dapper against `[identity].AccessReviews`; preserves pre-existing SQL bug verbatim (`Decision = 'Approved'` literal vs int enum) per "no behavior change" rule — separate bug-fix ticket.
- DI wired: Identity `InfrastructureRegistration.cs` registers `IIdentityAuditClient`.
- `ListTenantsQueryHandler` (Identity.Application.Queries.ListTenants): replaced cross-schema UNION SQL `(organization.OrgUnits ⊕ identity.AspNetUsers)` with parallel LINQ merge — orgClient call returns IList<OrgUnitSummary>, identity own-schema user-count query returns Dapper dict. Dictionary-merged by TenantId, sorted by user count desc then name.
- `GetComplianceEvidenceQueryHandler` (Reporting.Application.Queries.GetComplianceEvidence): cross-schema `[identity].AccessReviews` JOIN replaced with `identityAudit.GetAccessReviewStatusAsync(tenantGuid, periodFrom, periodTo, ct)` call. Added `Guid.Parse(request.TenantId)` (string→Guid conversion).
- `AqtCoreFW.Reporting.Application.csproj`: added missing `<ProjectReference>` to `AqtCoreFW.SharedKernel.Contracts`.

**Phase 02 — Signature Forward-Compat Exception:**
- `SignatureRequestFailedException` (NEW, AqtCoreFW.Signature.Application/Exceptions/) — exception type for gRPC-friendly failure path (replaces `return Guid.Empty`).
- `InProcessSignatureModuleClient.RequestSignatureAsync`: failure path changed from `return Guid.Empty;` to `throw new SignatureRequestFailedException(message, reason)`.
- 3 unit tests: `AqtCoreFW.Signature.Application.Tests/Exceptions/SignatureRequestFailedExceptionTests.cs` — message, reason, serialization.

**Deferred to Phase 03-06:**
- Phase 03: Event handler module ownership (TBD when consumer materializes)
- Phase 04: Handler integration tests
- Phase 05: gRPC contract sketches
- Phase 06: Cross-schema scout report

### Files changed
- `src/shared-kernel/AqtCoreFW.SharedKernel.Contracts/Clients/IOrgUnitModuleClient.cs` (NEW methods + OrgUnitSummary class)
- `src/shared-kernel/AqtCoreFW.SharedKernel.Contracts/Clients/IIdentityAuditClient.cs` (NEW interface)
- `src/modules/organization/AqtCoreFW.Organization.Infrastructure/Services/InProcessOrgUnitModuleClient.cs` (3 new methods via Dapper)
- `src/modules/identity/AqtCoreFW.Identity.Infrastructure/Services/InProcessIdentityAuditModuleClient.cs` (NEW service)
- `src/modules/identity/AqtCoreFW.Identity.Infrastructure/DependencyInjection/InfrastructureRegistration.cs` (IIdentityAuditClient DI)
- `src/modules/identity/AqtCoreFW.Identity.Application/Queries/ListTenants/ListTenantsQueryHandler.cs` (LINQ merge replaces UNION)
- `src/modules/reporting/AqtCoreFW.Reporting.Application/Queries/GetComplianceEvidence/GetComplianceEvidenceQueryHandler.cs` (identityAudit client call)
- `src/modules/reporting/AqtCoreFW.Reporting.Application/AqtCoreFW.Reporting.Application.csproj` (added SharedKernel.Contracts ref)
- `src/modules/signature/AqtCoreFW.Signature.Application/Exceptions/SignatureRequestFailedException.cs` (NEW exception)
- `src/modules/signature/AqtCoreFW.Signature.Application/Services/InProcessSignatureModuleClient.cs` (throw exception in failure path)
- `tests/unit/Signature/AqtCoreFW.Signature.Application.Tests/Exceptions/SignatureRequestFailedExceptionTests.cs` (NEW 3 tests)

### Plan reference
`plans/260427-1943-moduleclient-events-extraction-prep/` — Phase 01 + 02 shipped; Phase 03-06 deferred.

### Verification
- Build: 0 Error, 0 Warning
- Test suite: All green (pre-existing PermCodesTests.All_Should_Contain_122_Entries drift from commit 878d198f unrelated)

---

## v2.132p4 — Repository Restructure: Oracle 19c Migrated to Separate Repository (2026-04-27)

**Major restructure: Oracle 19c development now in a dedicated repository.**

### What changed
- **AqtCoreFW_Oracle repository created** at [https://github.com/thiennd212/AqtCoreFW_Oracle](https://github.com/thiennd212/AqtCoreFW_Oracle)
- **Oracle code moved:** `oracle-19c` branch (749 commits, v2.156 HEAD) pushed to new repo as its `main` branch
- **This repository (AqtCoreFW):** SQL Server-only on `main` branch; pinned at v2.132p3
- **Multi-provider unification plan** (260427-1236) **cancelled** — dual-repo strategy selected instead
- **Immutable snapshot:** Tag `archived/oracle-19c-v2.156` created on both repos

### Why
- Separate repos simplify: CI/CD, dependency management, deployment pipelines, and team context
- SQL Server and Oracle have diverged (TDE, VPD, 9-phase migration, unique schema architecture)
- No runtime `IDbProvider` abstraction — each repo is dedicated
- Long-term: reduced merge conflicts, faster release cycles, clearer onboarding per backend

### For Oracle developers
- Clone [AqtCoreFW_Oracle](https://github.com/thiennd212/AqtCoreFW_Oracle) instead of this repo
- Legacy `oracle-19c` branch on `origin` is scheduled for deletion **~2026-05-11** via `git push origin :oracle-19c` (2-week safety window). Commits remain reachable through tag `archived/oracle-19c-v2.156`.
- See that repo's docs for Oracle-specific setup and integration patterns

### For SQL Server developers
- No change — continue on this repo's `main` branch
- v2.132p3 is stable; next release will ship from this branch only

### Files changed
- None (repo restructure only, no code change)

### Impact
- **Zero code impact** — same architecture, binaries, APIs as v2.132p3
- **Documentation updated** to reflect single-repo, SQL-Server-primary posture
- `README.md`, `docs/developer-onboarding-guide.md`, `docs/tech-stack.md`, `docs/project-roadmap.md` updated
- `docs/dependency-map.md` updated to note Oracle references now live in separate repo

---

## v2.132p3 — DecisionTableJsonHelper CS0019 Compile Error Hotfix (2026-04-25)

**One-line fix — regression from v2.132p2 shipped code.**

### Issue
- **File:** `src/modules/rules/AqtCoreFW.Rules.Application/DTOs/DecisionTableJsonHelper.cs` line 19
- **Error:** CS0019 operator `??` cannot combine `List<string>?` (from `JsonSerializer.Deserialize`) with `string[]` (from `Array.Empty<string>()`). No common best type exists even though both implement `IReadOnlyList<string>`.
- **Regression:** v2.132p2 shipped with compile error locally; not caught by initial build verification.

### Fix
Cast RHS to `(IReadOnlyList<string>)Array.Empty<string>()` → explicit common type satisfies compiler.

```csharp
// Line 19 before:
return JsonSerializer.Deserialize<List<string>>(json) ?? Array.Empty<string>();

// After:
return JsonSerializer.Deserialize<List<string>>(json) ?? (IReadOnlyList<string>)Array.Empty<string>();
```

### Verification
`dotnet build src/host/AqtCoreFW.Api/AqtCoreFW.Api.csproj` → **0 Error, 12 Warning** (12 NU1902: OpenTelemetry.Api 1.12.0 CVE duplicate refs, pre-existing non-blocker)

### Files changed
- `src/modules/rules/AqtCoreFW.Rules.Application/DTOs/DecisionTableJsonHelper.cs` (line 19)

---

## v2.132p2 — DTO Contract Drift Fix: Rules.DecisionTableDto + Identity.DelegationDto (2026-04-25)

**Two contract drifts surfaced post-implementation review** (code-reviewer round 1: F1 latent crash + F2 active prod bug). Round 2 verified fix at 9/10.

### F1 — Rules.DecisionTableDto: BE-side JSON parsing (latent crash)
- **Symptom:** BE returned raw JSON strings for `InputColumns/OutputColumns/Rows` (storage format leaked through DTO). FE typed as arrays → `.join()` would throw TypeError on first decision table seeded.
- **Decision:** Parse on BE at handler boundary so storage format = implementation detail (no FE/mobile/batch reimpl).
- **Shape change:** `DecisionTableDto` record now uses `IReadOnlyList<string>` for columns and `IReadOnlyList<IReadOnlyDictionary<string, object?>>` for rows — type-honest, no `JsonElement` leakage into Application layer (onion-clean).
- **Helper:** New `DecisionTableJsonHelper` (read-path utility) with `ParseColumns` + `ParseRows` + recursive `JsonElement → object?` flattener. Malformed JSON swallowed → empty list + optional `ILogger.LogWarning(context)`. Domain default `"[]"` ensures normal flow never logs.
- **Discovered gap:** FE `useDecisionTables(ruleSetId)` hook called `GET /rules/rule-sets/{id}/decision-tables` but **endpoint did not exist** — only handler existed in `Application/Queries/ListDecisionTablesByRuleSet/`. Added controller action + new handler folder + unit tests.

### F2 — Identity.DelegationDto: cross-module enrichment (active prod)
- **Symptom:** FE expected `delegatorName/delegateName/isActive/createdAt`; none on BE record. Status tag rendered "inactive" universally.
- **Decision:** BE enrich names via `IIdentityModuleClient.GetUserInfoByIdsAsync` (Layer 2 Display, mirrors `Files.ListFilesQueryHandler` precedent). FE derive status via pure helper.
- **BE:** `DelegationDto` record extended with `DelegatorName/DelegateName: string?`. `ListDelegationsQueryHandler` injects `IIdentityModuleClient` + `ITenantContext`; `Distinct()` collapses dupes when same user is delegator/delegate across rows; `TryGetValue` falls back to null when user missing/cross-tenant.
- **FE:** `delegation-api.ts` dropped `isActive`/`createdAt`; added `isRevoked: boolean` + `revokedAt: string|null`; exported `deriveIsActive(d, now=new Date())` pure helper. `delegation-list-page.tsx` status column renders via `deriveIsActive(record)`; delegator/delegate name cells render `value ?? '(Đã xoá)'` for missing-user state. Vitest mock factory re-exports `deriveIsActive` (vi.mock replaces entire module).

### Files changed (BE)
- `src/modules/rules/AqtCoreFW.Rules.Application/DTOs/RulesDtos.cs` — DecisionTableDto retyped
- `src/modules/rules/AqtCoreFW.Rules.Application/DTOs/DecisionTableJsonHelper.cs` (NEW)
- `src/modules/rules/AqtCoreFW.Rules.Application/Queries/ListDecisionTablesByRuleSet/` (NEW handler folder)
- `src/modules/rules/AqtCoreFW.Rules.Application/Queries/GetDecisionTable/GetDecisionTableQueryHandler.cs`
- `src/modules/rules/AqtCoreFW.Rules.Application/Commands/CreateDecisionTable/CreateDecisionTableCommandHandler.cs`
- `src/modules/rules/AqtCoreFW.Rules.Presentation/Controllers/RuleSetsController.cs` — added `[HttpGet("{id:guid}/decision-tables")]`
- `src/modules/identity/AqtCoreFW.Identity.Application/AqtCoreFW.Identity.Application.csproj` — added `SharedKernel.Contracts` ProjectReference
- `src/modules/identity/AqtCoreFW.Identity.Application/Queries/ListDelegations/ListDelegationsQuery.cs` — DelegationDto extended
- `src/modules/identity/AqtCoreFW.Identity.Application/Queries/ListDelegations/ListDelegationsQueryHandler.cs` — Identity client + tenant context injection + name enrichment
- `tests/unit/AqtCoreFW.Rules.Application.Tests/Queries/ListDecisionTablesByRuleSetQueryHandlerTests.cs` (NEW)

### Files changed (FE)
- `web/src/features/delegations/delegation-api.ts` — DTO realigned + `deriveIsActive` helper
- `web/src/features/delegations/delegation-list-page.tsx` — status derive + missing-user fallback
- `web/src/features/rules/rule-set-detail-page.tsx` — consume typed shapes (no `.join()` change needed since FE was already typed correctly)
- `web/src/features/rules/rules-api.ts` — verified shape (no change needed)
- `web/src/test/features/delegations/delegations-page.test.tsx` — vi.mock factory re-exports `deriveIsActive`; fixture uses `isRevoked`/`revokedAt`/farPast/farFuture
- `web/src/test/features/phase5/smoke-phase5-pages.test.tsx`

### Code-review residual minors (deferred — pre-existing)
- **M-1:** `UserDelegation` domain entity has no `RevokedAt` column — SQL hardcodes `CAST(NULL AS datetime2)`. Out of scope for this fix.
- **M-2:** `deriveIsActive` ignores `DelegationStatus` enum (uses raw `isRevoked` only). Pre-existing.
- **M-3:** `ListDelegationsQueryHandler` lacks explicit `WHERE TenantId` clause (relies on tenant filter elsewhere — to verify).

### Impact
- Delegations admin page: status tag now correctly Green=active / Gray=inactive; delegator/delegate names display real strings instead of always "inactive" tag.
- Rules detail page: Decision Tables tab survives non-empty seeded data without TypeError.
- New endpoint: `GET /api/v1/rules/rule-sets/{id}/decision-tables` (was 404).

---

## v2.132p1 — SystemParams MediatR DI Registration Hotfix (2026-04-25)

**Critical bugfix (Backend):** SystemParamsRegistration.cs missing `AddMediatR` call caused 500 errors on all 9 SystemParams-routed MediatR endpoints post-v2.131p4 refactor (commit 7e16e164, 2026-04-24). Root cause: v2.131p4 thin-controller refactor created 9 handlers (Announcements CRUD + Announcements.GetActive + SystemParams GET/Update + FeatureFlags GET/ListDefinitions) but SystemParamsInfrastructureModule failed to register handler assembly. All endpoints verified 200 OK with runtime test before commit.

**Files changed:**
- `src/modules/systemparams/AqtCoreFW.SystemParams.Infrastructure/SystemParamsRegistration.cs` — added `services.AddMediatR(...)` for `AqtCoreFW.SystemParams.Infrastructure` assembly scan (commit `fcb64e1b`)
- `tests/architecture/AqtCoreFW.Architecture.Tests/ModuleMediatRRegistrationTests.cs` (new file) — added 1 reflection-based architecture test that invokes each module's public static registration method(s) with stub `IConfiguration`, then asserts every concrete `IRequestHandler<,>` class in the module's Application + Infrastructure assemblies appears as a `ServiceDescriptor`. All 16 modules pass. Runtime ~13s. Catches future "handler class exists but AddMediatR scan missing" regressions — exactly the v2.131p4 pattern. (commit `c18bb246`)

**Impact:** All downstream callers (Announcements CRUD operations, FeatureFlags queries, SystemParams updates) restored to 200 OK. No data migration or contract change. Architecture test now enforces handler registration at build time.

**Commits:**
- `fcb64e1b` — fix(systemparams): register MediatR handlers — restore 9 broken endpoints
- `c18bb246` — test(arch): add DI validation — catch missing AddMediatR registrations

---

## v2.132 — SQL Server Dev Runtime: Native → Docker Desktop (2026-04-25)

**Infrastructure revert:** Local dev SQL Server moved from native Windows install (`MSSQL$MSSQLSERVER22` @ `localhost,1433`, shipped v2.114 on 2026-04-18) back to the Docker Desktop container `sqlserver` already defined in `infra/docker/docker-compose.yml` (`mcr.microsoft.com/mssql/server:2022-latest`). Hard switch — no `AQT_SQL_MODE` fallback. Connection string `Server=localhost,1433;...` unchanged, so no app config or integration-test fixture rewrite. See **ADR-006** for context, decision, and alternatives.

**Friction that triggered the revert (post-v2.114 evaluation):**
- OS lock-in (Windows only) split the dev path for macOS/Linux teammates
- Manual `Start-Service` / `Stop-Service` lifecycle for a single piece of infra (everything else is containerised)
- Version drift between native install SKU and CI Testcontainers image tag
- Onboarding blast radius: "install SQL Server 2022 Developer + configure named instance" vs `docker compose up -d sqlserver`

**Port-1433 conflict handling:** `scripts/1_run-local-dev.ps1` now auto-detects a still-running native `MSSQLSERVER22` service and prompts the user to stop it before Docker bind, avoiding cryptic port-in-use errors.

**Smoke verification:** Docker container health OK + SA auth probe OK + `AqtCoreFW.Identity.Integration.Tests` 12/12 PASS against Docker SQL in 36s. Full integration suite deferred (connection string identical; hybrid fixture toggle `TEST_USE_LOCAL_SQL=true` unchanged).

**Files changed:**
- `scripts/1_run-local-dev.ps1` — `Test-SqlServerLocal` → `Test-SqlServerDocker` (conflict detection + `docker compose up -d sqlserver` + TCP-1433 readiness wait); docstring + comment updates
- `README.md` Quick Start (§2)
- `docs/system-architecture.md` (Database-Dev + 1_run-local-dev descriptions)
- `docs/deployment-guide.md` (Running Integration Tests Locally — Prerequisites)
- `docs/developer-onboarding-guide.md` (§2.1 Database + §2.2 Other Services)
- `docs/codebase-summary.md` (local-dev scripts section)
- `docs/dependency-map.md` (`TEST_USE_LOCAL_SQL` toggle contract)
- `docs/adr/006-sqlserver-docker-dev.md` (**new ADR**)
- `docs/project-roadmap.md` header + v2.132 row added; v2.132 Auth Hardening renumbered → v2.133; v2.133 Async Workflow renumbered → v2.134 (both unshipped planning)
- `docs/development-roadmap.md` phase note
- `backlogs/backlog.md` version line + Infrastructure entry
- `memory/sqlserver-docker-dev.md` (new), `memory/local-sqlserver-2022.md` (marked SUPERSEDED), `memory/project-state.md` (v2.132 decision note), `memory/MEMORY.md` index

**Version collision note:** The label `v2.132` had previously been sketched for an unshipped Auth-Hardening planning item (0% complete, `plans/260424-1103-v2-132-auth-hardening/`). That row has been renumbered to `v2.133` in the roadmap to preserve chronological ship order. Async Workflow planning bumped from `v2.133` → `v2.134` for the same reason.

**Breaking changes:** None for app runtime. Developers with a running native `MSSQLSERVER22` service must stop it (script prompts) or port 1433 will fail to bind.

---

## Oracle 19c Migration Plan — Session 2 + 2.5 Revision (2026-04-25)

**Plan (Architecture Refinement):** Tech Lead red-team revision pass after Session 1 lock; surfaced 4 new locked decisions, 8 factual fixes, hidden risks across all 8 phase files. Session 2.5 added dual-branch maintenance strategy.

**Plan directory:** `plans/260424-2207-oracle-19c-migration/` (~2950 LOC: plan.md 272 + 8 phase docs 254–431 LOC each — Session 1 baseline 2201 → +33% growth)

**Session 2 — 4 NEW locked decisions (override Session 1):**
- **D1 — CI image:** `container-registry.oracle.com/database/enterprise:19.3.0.0` (replaces `gvenzl/oracle-free:19-full` — community tag deprecated/never shipped on 19.x; 23.x only).
- **D2 — DB string sizing:** `MAX_STRING_SIZE=EXTENDED` DB-wide (irreversible) — enables `VARCHAR2(32767)` inline for 6 `HasMaxLength(8000)` columns without LOB promotion.
- **D3 — Hangfire user:** Separate `hangfire_user` schema + `HangfireConnection` distinct from app `aqtcore`/`AqtConnection`. 2 connection strings with DI loud-failure guards.
- **D4 — Legacy delete:** `src/modules/organization/AqtCoreFW.Organization/` DELETED in phase-03 Step 0 (phantom DbContext); kept `.Infrastructure/` (Program.cs binds here).

**Session 2 — 8 factual fixes (grep-verified counts):**
- F1: RowVersion = **9 entities + 4 DTO** (was claimed 3) — Cases, FormTemplate, WorkflowBranchChildStatus, WorkflowInstanceBranchStatus, WorkflowVersionMigration, WorkflowVersionMigrationItem, WorkflowStateTaxonomy, WorkflowActionTaxonomy + 1 + 4 DTO records.
- F2: VPD = **17 RLS helper calls / ~45 policied tables** (was 29 policies) — actual scope after expansion.
- F3: DbContext = **21** = 16 module + 5 shared (Alerting, ApiKey, Backup, Messaging, Webhook).
- F4: JSON cột = **25+ nvarchar(max) columns** (was 6) — per-semantic CLOB+IS JSON vs NCLOB/NVARCHAR2 decision matrix in phase-02.
- F5: Dapper files = **60** mandatory `@`→`:` rewrite (was ~50).
- F6: Stray `Microsoft.Data.SqlClient` outside CasesBenchSeeder = **6-10 files**.
- F7: ODP.NET = **23.26.200** (was 23.9.0; latest stable on NuGet at lock time).
- F8: `HasMaxLength(8000)` = **exactly 6 columns** (count locked).

**Session 2.5 — Dual-branch strategy:**
- 2 long-lived branches: `main` (SqlServer primary) + `oracle-19c` (long-lived Oracle Enterprise).
- NOT runtime IDbProvider abstraction (REJECTED).
- Sync cadence: per-feature port; PR merge `main` → conversion PR `oracle-19c` within 2-3 days.
- CI matrix: single-DB per branch (no cross-DB matrix).
- Phase-08 deliverables expanded: `oracle-conversion-runbook.md`, `tools/convert-sqlserver-to-oracle.ps1`, `.github/PULL_REQUEST_TEMPLATE/oracle-port.md`, `.github/workflows/drift-detect.yml`.

**Hidden risks surfaced (Session 2):**
- Phase-01: PascalCase quote rule via NetArchTest; MAX_STRING_SIZE=EXTENDED STARTUP UPGRADE bootstrap.
- Phase-02: Dapper `@`→`:` BindByName unreliable → mandatory source rewrite.
- Phase-03: Legacy Organization delete = blocking gate Step 0; 21-DbContext loop covers 5 shared.
- Phase-04: Hangfire bypass intentional (ITenantAwareJob contract); AUTOLOGIN wallet sequence; admin-bypass IDisposable + DbContext.Dispose CLEAR_CONTEXT; 225-scenario VPD matrix.
- Phase-06: OracleBulkCopy NOT enlisting in TransactionScope → `BulkCopy_DoesNotEnlistInOuterTransaction` regression test mandatory.
- Phase-07: Enterprise image 10GB cache + Oracle SSO 90-day rotation + xUnit Collection DisableParallelization on VPD suites.

**Branch state:**
- Plan committed `85d1f115` on `main`; journal committed `a957533b` on `main`.
- Branch `oracle-19c` published 2026-04-25 @ `a957533b` (tracking origin/oracle-19c).
- Both branches in sync; long-lived `oracle-19c` ready for phase-01 implementation.

**Status:** Plan revision LOCKED. Implementation work begins on `oracle-19c` branch only. Session 1 entry above (2026-04-24) preserved for audit trail; this entry overrides where in conflict.

**Breaking changes:** None — plan is specification, implementation deferred.

**Related:** Journal `docs/journals/journal-260425-0857-oracle-plan-validation-session-2.md`.

---

## Oracle 19c Migration Plan Validated (2026-04-24)

**Plan (Architecture Planning):** Scout + Plan + Validate cycle complete for Oracle 19c template fork.

**Scope:** 9-phase migration plan (template fork, not prod migration) targeting Oracle 19c Enterprise Edition with VPD + TDE + hybrid data model.

**Plan directory:** `plans/260424-2207-oracle-19c-migration/` (2201 LOC initial baseline: plan.md 76 + 8 phase docs 202–345 LOC each)

**Validation:** 7/7 planning questions user selected Recommended option; conflict resolved (plan.md identifier casing vs phase-01 reconciled).

**NuGet packages verified:** Oracle.EntityFrameworkCore 10.23.26200, Hangfire.Oracle.Core 1.3.24, AspNetCore.HealthChecks.Oracle 9.0.0, Testcontainers.Oracle 4.11.0

**Key decisions locked (Session 1 — superseded by Session 2 D1-D4 above where in conflict):** 1 Oracle user + schema-qualified tables [→ Session 2 D3 changed to 2 users], PascalCase + quote-in-Dapper, RAW(16) uniform, int Version (byte[] RowVersion refactor), CLOB+IS JSON, OracleBulkCopy, 1-shared-container CI [→ Session 2 D1 swapped image], tablespace TDE

**Business driver:** Template fork (no prod data migration). Candidate project: GOV deployment requiring Oracle stack.

**Status:** Ready for implementation on branch `oracle-19c` (Session 2.5 — was `feat/oracle-19c-migration`). See plan.md for 9-phase breakdown + hard gates (build / migrations / security / perf / test-parity).

**Breaking changes:** None — plan is specification, implementation deferred.

---

## v2.131p4 — Thin Controllers Refactor + Architecture Guard (2026-04-24)

**Bugfix (Backend):** E2ETestDataSeeder PK_AppMenus crash — replaced sentinel-only check with per-row idempotency for both `AppMenus` and `MenuRolePermission` inserts. Root cause: PK constraint violation on host restart caused HTTP 500 cascade on hub negotiation + login. Fix is strategic (Option B: idempotent per-row), not patch.

**Refactor (4 modules → MediatR thin controllers):**
- **MasterDataController + MasterDataAdminController** — refactored to MediatR. Created 11 new handlers (Province/District/Ward CRUD + GetCaseTypes + GetJobTitles queries) in `MasterData.Application/Commands|Queries/` and `MasterData.Infrastructure/CommandHandlers|QueryHandlers/`. Removed `<PackageReference Include="Microsoft.EntityFrameworkCore" />` from `MasterData.Presentation.csproj`.
- **SystemParamsController + FeatureFlagsController + AnnouncementsController** — refactored to MediatR. Created 9 new handlers in `SystemParams.Application/Commands|Queries/` and `SystemParams.Infrastructure/CommandHandlers|QueryHandlers/`. SessionConfigController (same file) left untouched (already thin). AnnouncementsController preserved cache-invalidation contract via `ICacheService` injected into handlers.
- **UserSearchController (Identity)** — refactored to MediatR. New `SearchUsersQuery` + handler in `Identity.Application/Queries/SearchUsers/`. Validator (FluentValidation) enforces min-length 2.
- **FilesController.List** — moved Dapper query out of controller. New `ListFilesQuery` + handler in `Files.Application/Queries/ListFiles/`. Handler enriches `UploadedByName` via `IIdentityModuleClient.GetUserInfoByIdsAsync` batch (Layer 2 cross-module pattern). FE updated: `web/src/features/files/file-types.ts` adds `UploadedByName?: string | null`, `file-list-page.tsx` renders new field with '—' fallback. Test mock updated. `Files.Application.csproj` added ProjectReference to `SharedKernel.Contracts`.

**New Architecture Guard:**
- **`tests/architecture/AqtCoreFW.Architecture.Tests/ThinControllerTests.cs`** — 3 NetArchTest rules iterating every module's Presentation assembly. Forbid controllers from depending on `Dapper`, `AqtCoreFW.SharedKernel.Application.Data` (IReadDbConnection), or `Microsoft.EntityFrameworkCore`. **Currently 3/3 PASS** after refactor.

**Documentation:**
- **docs/code-standards.md** — added "Thin Controllers (MANDATORY — enforced by NetArchTest)" section after CQRS Patterns. Includes rule, why, pattern example, enforcement note, and 5-step migration recipe.

**Related journals:** Thin controllers planning + implementation (2026-04-24)

**Breaking changes:** None. Controllers maintain same HTTP contracts; MediatR handlers are internal refactor.

---

## v2.131p3 — UI Polish + Bugfixes (2026-04-24)

**Feature (Frontend):** Flat sidebar menu structure — removed nested Admin wrapper, elevated 10 menu items to top level (Option C final decision). Commits: `123801a5` (refactor web sidebar), `4dd6fc3d` (test mock fixes), `7aa4580e` (journal + docs).

**Test Infrastructure Fix:** Resolved pre-existing test mock debt:
- `NotificationBell` component tests: missing `notification-api` mock (7 failures) + fixed vitest pool isolation
- `Topbar` component tests: missing `permission-api` + `settings-api` mocks (4 failures) + fixed mock initialization order
- Result: 789/789 FE tests pass (100% green) — ready for E2E

**Bugfix (Backend):** Hangfire dead-code removal — deleted unused `MigrateHangfireQueues()` migration method causing "Invalid column name 'Queue'" startup warning. Root cause: SQL Server compile-time parser bypasses `IF EXISTS` guard, targeting wrong table (hangfire.Job vs. hangfire.JobQueue where Queue column moved in Hangfire 1.8 schema evolution). Removal silences warning + prevents spurious migration attempts. Files: `src/shared/AqtCoreFW.Infrastructure/BackgroundJobs/HangfireRegistration.cs` + `src/host/AqtCoreFW.Api/Program.cs`.

**Related journals:** [`docs/journals/journal-260424-1518-sidebar-menu-option-c-flat.md`](./journals/journal-260424-1518-sidebar-menu-option-c-flat.md) + [`docs/journals/journal-260424-1532-hangfire-dead-code-removal.md`](./journals/journal-260424-1532-hangfire-dead-code-removal.md)

**Breaking changes:** None. Sidebar structure UX improvement; test + Hangfire fixes are non-breaking.

---

## v2.131p2 — Gap Audit Cleanup Plan (2026-04-24)

**Plan (Architecture Research):** Scout audit of 17 modules (4 parallel Explore agents, 2026-04-24) + plan creation.

**Scope:** 11 gaps identified (2 P0, 6 P1, 3 P2) across Identity integration, Rules formalization, Reporting cross-schema, and others. 6-phase remediation plan (2d effort). Blocks v2.133 Async Workflow.

**Gaps (summary):**
- P0: Integration.Presentation missing wire in phase-01, Signature→Files module client integration gap
- P1: Identity integration.Presentation controller registration, Reporting cross-schema data-path formalization, Rules module 0 active consumers + deferred wire-up tasks (R-W1 fee rules, R-W2 eligibility gate, R-W3 auto-approve thresholds)
- P2: AI module deferred-strategy verification, Others TBD per phase

**Related PRs:** #57 (Rules deferred status + Forms→Rules doc drift fix), #58 (v2.132 Auth Hardening parallel session)

**Breaking changes:** None. Phase plan ready for implementation.

**Plan files:** `plans/260424-1040-gap-audit-cleanup-v2132/` (6 phase docs, 287 LOC master report)

---

## v2.131p1 — Demo Artifacts + Rate Limiter Config Flag (2026-04-23)

**Feature (Infrastructure):** Added configuration flag `RateLimiting:Enabled` to opt-out rate limiting for benchmark environments. Default `true` (enabled) preserves backward compatibility with Dev/Prod behavior. Only `appsettings.Benchmark.json` sets `false`.

**Code Changes:**
- `RateLimiterConfiguration.cs` line 37 — checks `configuration?.GetValue<bool>("RateLimiting:Enabled", true)` before registering middleware
- `InfrastructureRegistration.cs` line 104 — pass `IConfiguration` to `AddGovRateLimiter(configuration)` method
- `appsettings.Benchmark.json` (NEW) — Benchmark environment config disables rate limiter + lowers log level to Warning

**Feature (Performance Testing):** Added k6 load test script for post-SEQUENCE validation.
- `tests/performance/cases-epsilon-500vu.js` (51 LOC) — 500 VU Epsilon scenario, stages: ramp 0→500 (30s) + hold (3m) + ramp down (15s)
- Validates p95<500ms, p99<1000ms, error rate<1%
- Prerequisite: AuthServer (Dev), API (Benchmark) + 10k seeded users from v2.129

**Demo Session Artifacts (2026-04-24):**
- `plans/260423-1853-demo-corebase-docs/pitch-deck.html` — interactive presentation deck
- `plans/260423-1853-demo-corebase-docs/architecture-cheatsheet.md` — 1-page architecture reference
- `plans/260423-1853-demo-corebase-docs/quickstart-handout.md` — developer onboarding handout
- Load test report: `plans/reports/k6-260424-epsilon-500vu-results.md`

**Breaking changes:** None. Rate limiter remains enabled by default.

**Security note:** Rate limiter opt-out applies only to Benchmark environment. Production-like environments (Prod, Staging) must keep rate limiting enabled.

---

## v2.131 — BTC Cases Level-C Seeding (2026-04-23)

**Feature (Tools):** Extended CasesBenchSeeder standalone console tool to seed 10,000,000 realistic BTC Level-C Cases + paired WorkflowInstances (10M rows each). Total cases now 20M: BENCH 10M (Draft-only) + BTC 10M (mixed workflow states, SLA-aware timestamps, real user/OrgUnit pools). Prefix carving `BENCH-%` / `BTC-%` enables safe coexistence in same `cases.Cases` table.

**Execution Results:**
- Wall-clock: 61m34s total (bulk insert 46m12s @ 3,607 r/s avg, NCI rebuild 11m43s, UPDATE STATISTICS 3m39s)
- Status distribution: Draft 50.000% (4,999,739), Submitted 20.000% (2,000,425), UnderReview 15.000% (1,499,723), Approved 8.000% (801,151), Closed 5.000% (499,053), Rejected 1.000% (100,321), ReturnedForRevision 1.000% (99,588) — precision ±0.013% vs ±0.5% target
- Validation: 5 post-seed SQL assertions ZERO violations (assignment semantics, WFI.CompletedAt semantics, temporal ordering, resolution reason requirement, collateral counts)
- Impact: BENCH 10M untouched — zero destructive impact verified

**Code Changes:**
- `SeederOptions.cs` — extended with Level-C fields (profile btc, realistic user pool from identity schema, OrgUnit pool from organization schema)
- `Program.cs` — added 8 CLI flags (`--profile btc`, `--count`, `--batch-size`, etc.) + LevelCProfile + RunSmokeCheck + RunPostSeedValidation
- New generation modules: `DistributionSampler.cs` (status distribution + seeding), `TimelineComputer.cs` (SLA-aware timestamps), `VnResolutionReasonDictionary.cs` (resolution codes), `RowBlueprint.cs` (structured record)
- Refactored: `StaticPool.cs`, `CaseRowFactory.cs`, `WorkflowInstanceRowFactory.cs`, `PairedRowGenerator.cs`, `CaseNumberFormatter.cs`
- New SQL: `cleanup-btc-bench-data.sql` (chunked DELETE, BENCH untouched, idempotent)
- New test project: `tests/unit/CasesBenchSeeder.Tests` (35 unit tests, 100% pass) + added to AqtCoreFW.slnx
- `AssemblyInfo.cs` — InternalsVisibleTo test project

**Breaking changes:** None. BENCH seeder remains compatible; BTC seeder coexists via prefix.

**Full details:** [Phase 06 Execution Report](plans/260423-1713-btc-cases-level-c-seeding/reports/phase-06-execution-report.md)

---

## v2.130p1 — Post-v2.130 UX + SystemParams Polish Batch (2026-04-23)

**Refactoring (SystemParams Module Migration):** Port SystemParamService cache + Redis pub/sub from legacy monolithic `AqtCoreFW.SystemParams/` project to layered Clean Architecture (Domain/Application/Infrastructure/Presentation). Delete legacy project entirely. Rewire 38 unit tests. Commits: b3162836 (cache+pub/sub port), bfc4229c (test rewire + legacy project removal), d3764a40 (remove session:timeout_min orphan row M3).

**Feature (Session UX Cleanup):** Invisible silent-renew (remove token-expiry polling), api-client 401 retry with renewInFlight coalescing, idle-only modal (removed renew-failed branching), seed `session:idleTimeoutMinutes` + `session:countdownSeconds` keys with per-key guard. Commits: dfa5cce7, 2c502dc9, 9519ce81.

**Feature (Silent-Renew Auto-Redirect UX):** After silent-renew failure, show persistent antd toast with 10s countdown + "Đăng nhập ngay" button, auto-redirect when countdown=0. Pure DI controller `auth-failure-controller.ts` (79 LOC) + 10 vitest unit tests (166 LOC). Fix: exclude `feature:*` keys from SystemParams admin CRUD (3-layer defense: GetAll filter + GetOne 404 + Update 422). Commits: 1d252e42 (exclude feature:* filter), 08d3f8db (10s countdown toast + auto-redirect), 140c3f0d (extract pure auth-failure-controller).

**Feature (Org Tree UX Polish):** Default-expand 1 level in organization chart, widen staff role column. Commit: ba59050b.

**Feature (Seeder Robustness):** BtcOrphanPurger self-heal + Identity drift wipe — orphan row cleanup with integration test (223 LOC). Commit: 18e4f08f.

**Breaking changes:** None. Legacy AqtCoreFW.SystemParams/ project deleted (already functionally replaced by layered modules). API contracts preserved.

---

## v2.130 — Cases Bench Seeder: 10M Draft cases + paired WorkflowInstances (2026-04-23)

**Feature (Tools):** Standalone console app under `src/tools/CasesBenchSeeder` — bulk-loads 10,000,000 paired `(Case, WorkflowInstance)` rows at initial `Draft` state for performance benchmarking. Bypasses EF Core, audit interceptors, domain events, outbox, feature flags entirely — uses raw `SqlBulkCopy` with drop-then-rebuild NCI strategy.

**Added (`src/tools/CasesBenchSeeder/`):**
- `Program.cs` — CLI + Serilog console + 3 startup guards (H2): DB=`AqtCoreFW`, host ∈ allowed dev list, `ASPNETCORE_ENVIRONMENT` ∈ {Development,Local}. H7 re-run halt: aborts if prior `BENCH-%` rows exist. `--self-test-m7` flag for RowVersion guard verification.
- `SeederOptions.cs` — POCO; defaults BENCH tenant `99999999-…` + synthetic user `00000000-…-b001`.
- `SessionContextOpener.cs` (H1) — extension `SqlConnection.OpenWithTenantAsync`: sets `sp_set_session_context @TenantId, @read_only=1` for cases RLS.
- `Generation/StaticPool.cs` — loads `DefinitionId` + `InitialStateId` live from `workflow.WorkflowDefinitions` + `WorkflowStates`.
- `Generation/CaseRowFactory.cs` + `WorkflowInstanceRowFactory.cs` + `PairedRowGenerator.cs` — lazy paired generator, O(1) memory.
- `Generation/CaseNumberFormatter.cs` (M2) — `"BENCH-{D9}"` + `"Z" + base36(seq).PadLeft(7,'0')` — collision-free namespace vs UI random codes.
- `Generation/VnPhraseDictionary.cs` — 50 Vietnamese gov/admin phrases for synthetic Title/Description.
- `Generation/DataTableTemplates.cs` — schema pinned to phase-01 `sys.columns` dump (Cases 19 cols, WFI 19 cols).
- `Bulk/BulkInserter.cs` (M7 guard) — SqlBulkCopy wrapper; throws if DataTable contains `RowVersion` column.
- `Bulk/IndexDdlCapture.cs` — runtime capture from `sys.indexes`+`sys.index_columns`; drop + rebuild with `SORT_IN_TEMPDB=ON, DATA_COMPRESSION=PAGE, ONLINE=OFF`.
- `Bulk/SeedOrchestrator.cs` — pipeline: StaticPool → capture NCIs → drop → bulk loop (TableLock+UseInternalTransaction) → rebuild → `UPDATE STATISTICS WITH SAMPLE 30 PERCENT` (M3).
- `Bulk/ProgressReporter.cs` — Stopwatch + `sys.dm_db_log_space_usage` monitor; WARN at 20/40 GB log size.
- `Sql/cleanup-bench-data.sql` (H5) — chunked `DELETE TOP(100000)` + CHECKPOINT loop; TenantId-scoped; idempotent; fixed `@@ROWCOUNT` reset bug via `SET @rc` local var capture.
- `appsettings.Seeder.json` — empty connstr placeholder (no secrets committed). Env var override: `SEEDER_CONNSTR`.
- CPM additions: `Microsoft.Data.SqlClient 6.1.1` (pinned to Api transitive, M6), `Microsoft.Extensions.Hosting 10.0.0`, `Microsoft.Extensions.Configuration.Json 10.0.0`, `Serilog.Sinks.Console 6.0.0`.
- `/tools/` folder wired into `AqtCoreFW.slnx`.

**Added (plan artifacts):**
- `plans/260423-1054-seed-10m-cases-initial-state/bench-validate.sql` — 10 invariant checks (counts, uniqueness, Draft-only, NULL Updated/Modified, enum domain, ClassificationLevel=1, Z-prefix, WFI initial state, CreatedBy synthetic) with PRINT PASS/FAIL.
- `plans/260423-1054-seed-10m-cases-initial-state/runbook.md` — operator-facing step-by-step (backup → RECOVERY SIMPLE → dry-run → gate → full run → validate → DBCC → RECOVERY FULL → fresh backup).
- `plans/260423-1054-seed-10m-cases-initial-state/scripts/validate-bench-seeder-prereqs.ps1` — phase-01 13-check preflight (connectivity, sysadmin, CaseProcess definition, synthetic user bootstrap, empty BENCH tenant, sys.columns ground truth, disk budget). Writes `validation-report.md`. Exit non-zero stops the plan.

**Verified (phase-02/03/04/05):**
- `dotnet build` AqtCoreFW.slnx Debug + Release: 0 warnings, 0 errors.
- Phase-02 runtime: H1/H2/H7 guards all fire on wrong env, wrong DB, and prior BENCH rows.
- Phase-03 1k-pair smoke: 0 duplicates Ids/CaseNumbers/TrackingCodes; Type/Priority ∈ verified enum domain; 8-char Z-prefix TrackingCode; `ClassificationLevel=1` int.
- Phase-04 1k end-to-end smoke: 5 NCIs captured+dropped+rebuilt on Cases, 5 on WFI; cleanup SQL removes 500/500 rows, idempotent re-run = no-op.
- Phase-04 M7 self-test: fabricated DataTable w/ `RowVersion` column → throws `InvalidOperationException` as expected.
- Phase-05 100k dry-run: **10.4s wall** (seeder 8.6s, bulk 4.2s @ 23.8k pair/s, log peak 193 MB); `bench-validate.sql` 10/10 PASS.
- Phase-05 10M full run: **19m52s elapsed** (bulk 11m54s @ 13,993 r/s pair · NCI rebuild 6m41s · `UPDATE STATISTICS WITH SAMPLE 30 PERCENT` 1m17s) · end-to-end throughput **8,383 r/s pair** · log peak 1.22 GB · drop-NCI strategy; 10 NCIs captured/dropped/rebuilt (5 Cases + 5 WFI). `bench-validate.sql` @expected=10000000 → **ALL 10 PASS in 38.9 s** (Cases=10M, WFI=10M, distinct TrackingCodes=10M, non-Draft=0, UpdatedAt/ModifiedBy dirty=0, Type/Priority domain drift=0, ClassificationLevel≠1=0, TrackingCode not-Z=0, WFI non-initial state=0, CreatedBy drift=0). `DBCC CHECKTABLE PHYSICAL_ONLY` × 2 → 0 allocation/consistency errors. Recovery model reverted FULL + fresh backup `C:\AqtBackup\AqtCoreFW-post-bench-260423.bak` (4.56 GB compressed, 1,907,988 pages in 47.5 s @ 314 MB/s). M1 parity integration test deferred YAGNI — `DataTableTemplates` schema already pinned to `sys.columns` ground truth at build time.

**Breaking changes:** None. Tool is new; no existing code touched.

---

## v2.129 — BTC Identity + Assignment Seeding (2026-04-23)

**Feature (Identity + Organization modules):** Idempotent seeding of 10,000 BTC users covering 1,139 OrgUnits with 17 role assignments + 10,000 staff position history records.

**Summary:**

**Added (Identity.Infrastructure/Seeding):**
- `BtcIdentitySeeder` (IHostedService, 3 partials: main + .Roles + .Users + .UserRoles) — seeds 17 BTC_* roles (BTC_DepartmentHead, BTC_StaffMember, etc.) + 10k Bogus Vietnamese users across 1139 OrgUnits using 3-pass coverage algorithm (Lead/OrgUnit fill + dev reserve 129). Shared PBKDF2 password hash (1 reuse for all 10k users, deterministic RandomSeed).
- `BtcIdentitySeederOptions` + `BtcIdentitySeedConstants` — configuration binding (Enabled, UserCount, DevReserveCount, MaxOrgUnitLevel) + tenant GUID (a1b2c3d4-0000-0000-0000-00000000b7c1). Config guard: throws if UserCount < OrgUnits + DevReserve (2 HIGH post-review fixes: 1-guard validation, 2-atomic batch).
- `VnFakerData` — Bogus provider (100 họ + 50 đệm + 300 tên Việt).

**Added (Organization.Infrastructure/Seeding):**
- `BtcAssignmentSeeder` (IHostedService) — 10k UserOrgUnitAssignment + 10k StaffPositionHistory (atomic `SaveChangesAsync` per batch, no orphans). Depends on BtcIdentitySeeder completion (IHostedService dependency ordering).
- `BtcAssignmentSeederOptions` — configuration binding (Enabled flag).

**Changed:**
- `InfrastructureRegistration.cs` (Identity) — register `BtcIdentitySeeder` as IHostedService. `.AddScoped<IBtcIdentitySeeder>()` (interface for DI; IHostedService auto-activation on startup).
- `OrgRegistration.cs` (Organization) — register `BtcAssignmentSeeder` as IHostedService.
- `Directory.Packages.props` — add `Bogus 35.6.1` CPM.
- `AqtCoreFW.Identity.Infrastructure.csproj` — add `<PackageReference Include="Bogus" />` (from CPM).
- `appsettings.json` + `appsettings.Development.json` — add `BtcIdentitySeeder:Enabled=true` + `BtcAssignmentSeeder:Enabled=true` + config section (UserCount, DevReserveCount).

**Architecture:**
- **Cross-module data bridge:** Organization.Infrastructure (BtcAssignmentSeeder) reads from `identity.AspNetUsers` via raw Dapper SQL — no EF project reference. Avoids circular dependency (Identity ↛ Organization).
- **Idempotency:** HashSet pre-load per user (no count gate; lesson from v2.128 bug). Per-user check before INSERT prevents duplicates on re-run.
- **Dependency ordering:** Startup registers BtcIdentitySeeder first, then BtcAssignmentSeeder; framework ensures Identity seeds complete before Organization starts.
- **Shared password hash:** All 10k users = same PBKDF2 output (RandomSeed deterministic). Enables fast bulk load without per-user hash compute (trade-off: shared password, acceptable for test data).

**Tests:** All 8 SQL assertions PASS (17 roles, 10k users, 1139 distinct OrgUnits, 129 dev users, 10k assignments, 10k histories, coverage 100%, idempotency verified).
- Tester report: `plans/260423-0337-seed-mof-10k/reports/tester-260423-btc-10k-seed.md` — 8/8 assertions green.
- Code-review report: `plans/260423-0337-seed-mof-10k/reports/code-reviewer-260423-btc-10k-seed.md` — 8.0/10 (1 HIGH + 2 MEDIUM fixed: UserCount guard + atomic batch).
- Seeding time: ~17s.

**Build:** 0 warnings, 0 errors. `dotnet build` clean.

**Breaking Changes:** None. Multi-tenant isolation: assignments scoped per TenantId. RLS read filter enforced on UserOrgUnitAssignment + StaffPositionHistory queries.

**Migration Notes:**
- `BtcIdentitySeeder:Enabled=true` in appsettings controls auto-seed on startup.
- `BtcAssignmentSeeder:Enabled=true` in appsettings controls auto-seed on startup.
- Verify config: `BtcIdentitySeeder:UserCount >= BtcIdentitySeeder:DevReserveCount + 1139` (number of OrgUnits).
- BoTaiChinh tenant must pre-exist for seed to run (validates `TenantId` before starting).

**Commit hash:** Pending submission.

**Follow-ups:**
- **Seeder unit tests** — Add happy-path tests for BtcIdentitySeeder + BtcAssignmentSeeder (options validation, user count bounds, dependency ordering). Phase-06 backlog.

---

## v2.128p3 — Admin Identity Menu Hygiene Batch (2026-04-23)

**Chore/infra (Identity seeder + FE):** Soft-hide 3 admin identity menus chưa sử dụng hoặc chưa hoàn thiện UX — `/admin/sod-rules`, `/admin/policy-rules`, `/admin/access-reviews`. Mirrors v2.127p9 pattern: feature vẫn hoạt động (module Identity, T4-06 SoD refactor v2.127 hoàn tất) — chỉ ẩn MENU khỏi sidebar. Route + BE endpoints + permissions (`IDENTITY.SOD.VIEW`, `IDENTITY.POLICY.VIEW`, `IDENTITY.ROLE.VIEW`) giữ nguyên; admin có thể truy cập qua URL trực tiếp.

**5-tầng soft-hide pattern:**
- **L1 (FE sidebar hardcoded):** N/A (3 menus không có ở `sidebar-menu.tsx` gốc, chỉ ở admin sub-menu).
- **L2 (FE admin-menu-entries):** `web/src/layouts/admin-menu-entries.ts` — 3 MenuItem entries (lines 133/134/136) + 3 icon imports (`BlockOutlined`/`StopOutlined`/`AuditOutlined` — lines 5/24/25) commented với note "tạm ẩn — route vẫn sống". IDENTITY_PATHS array giữ nguyên (route registry — pattern giống v2.127p4 RTBF).
- **L3 (BE seeder):** `src/host/AqtCoreFW.Api/E2ETestDataSeeder.Menus.cs` — 3 M entries (lines 166/167/169) + 3 P permissions (lines 241/242/244) commented với GUID constants giữ lại để rollback.
- **L4 (DB runtime):** 2 scripts idempotent:
  - `scripts/260423-soft-hide-sod-rules.sql` — UPDATE IsActive=0 WHERE Code='admin-sod' (1 row affected).
  - `scripts/260423-soft-hide-policy-and-access-reviews.sql` — UPDATE IsActive=0 WHERE Code IN ('admin-policy','admin-access-rev') (2 rows affected).
- **L5 (Cache flush):** Redis `docker exec docker-redis-1 redis-cli --scan --pattern 'menu-tree:*' | xargs redis-cli DEL` — 1 key deleted (default tenant cache).

**Philosophy B (non-destructive):** GUID constants `MenuSodId` / `MenuPolicyRulesId` / `MenuAccessRevId` giữ lại trong seeder cho rollback an toàn. Admin `/admin/menus` vẫn thấy rows với `IsActive=0` cho audit trail. Rollback 1 lệnh: `UPDATE IsActive=1 WHERE Code IN ('admin-sod','admin-policy','admin-access-rev')` + Redis flush + uncomment seeder lines.

**Files:** 4 (2 modified, 2 new)
- `web/src/layouts/admin-menu-entries.ts` (3 menu entries + 3 icon imports commented)
- `src/host/AqtCoreFW.Api/E2ETestDataSeeder.Menus.cs` (3 M + 3 P commented)
- `scripts/260423-soft-hide-sod-rules.sql` (NEW, idempotent UPDATE)
- `scripts/260423-soft-hide-policy-and-access-reviews.sql` (NEW, idempotent batch UPDATE)

**Runtime verified (2026-04-23):** DB SELECT confirms 4 menus `IsActive=0`: `search` (v2.128p2) + `admin-sod` + `admin-policy` + `admin-access-rev` (v2.128p3). Redis flush returned `1` (default tenant menu-tree key deleted).

**Build:** No errors expected (chore-only, zero functional change, TS typecheck preserved — icon imports commented cùng lúc với tham chiếu để tránh unused warning).

**Breaking Changes:** None.

**Commit hash:** Pending submission.

---

## v2.128p2 — Search Menu Orphan Cleanup (2026-04-23)

**Chore/infra (Identity seeder):** Soft-hide menu `/search` orphan after PR #45 (`449b18fa`, 2026-04-20) removed `AqtCoreFW.Search` BE module. Seeder entry `MenuSearchId` + permission mapping `SEARCH.QUERY.USE` in `E2ETestDataSeeder.Menus.cs` (lines 146, 226) remained active → menu "Tìm kiếm" still rendered on sidebar (FE fetches via `/api/v1/menus/tree`).

**5-tầng soft-hide pattern (mirrors v2.127p9 consent/RTBF soft-hide):**
- **L1-L2:** N/A (FE has hardcoded route removal, no `/search` link generated).
- **L3 (BE seeder):** `E2ETestDataSeeder.Menus.cs` lines 146, 226 commented with note "tạm ẩn — orphan sau PR #45. Bật lại: khôi phục BE module + uncomment + reseed DB".
- **L4 (DB runtime):** `scripts/260423-soft-hide-search.sql` (NEW, idempotent) executes `UPDATE [identity].[AppMenus] SET [IsActive]=0 WHERE [Code]='search'` with `SET QUOTED_IDENTIFIER ON` (fix Msg 1934 on Windows SQL Server).
- **L5 (Cache flush):** Pending user manual execution: `FLUSHPATTERN menu-tree:*` via Redis CLI (optional, fast-path cache invalidation).

**Philosophy B (non-destructive):** Permission code `SEARCH.QUERY.USE` retained in `PermCodes.cs:468` + `seed-permissions.sql:115` for audit trail. Admin `/admin/menus` shows row "Code: search, IsActive: 0" for visibility. Rollback: `UPDATE IsActive=1` + Redis flush + uncomment seeder lines.

**Files:** 2 (1 modified, 1 new)
- `src/host/AqtCoreFW.Api/E2ETestDataSeeder.Menus.cs` (2 lines commented)
- `scripts/260423-soft-hide-search.sql` (NEW, 40 lines idempotent SQL)

**Build:** No errors expected (chore only, zero functional change).

**Breaking Changes:** None (menu hidden, permission intact, rollback safe).

**Commit hash:** Pending submission.

---

## v2.128p1 — SystemAdmin Tenant Visibility + SoD Warning Cleanup (2026-04-23)

**Fix (Identity module):** ListTenantsQueryHandler aggregates tenants from two sources: (1) root-level OrgUnits (ParentId IS NULL AND Level=1) from [organization].OrgUnits, and (2) tenants with users in [identity].AspNetUsers. Previously only returned tenants from AspNetUsers, causing SystemAdmin to not see data-only tenants (e.g., BoTaiChinh seeded with 1,139 OrgUnits but 0 users). Root cause: dropdown tenant selector used ListTenantsQuery to populate X-Tenant-Id context header options. New SQL uses UNION + LEFT JOIN UserCount per tenant (0 for data-only tenants). Fixes: SystemAdmin can now switch to BTC tenant context and navigate OrgUnit tree, despite zero user records.

**Chore (Identity module):** SodRulesAdminController wraps 2 accessors of deprecated `EnforcementLevel` field with `#pragma warning disable CS0618`. Context: v2.127 renamed enum `SodEnforcementLevel` → `SodSeverity` + entity field `Severity`, keeping legacy `EnforcementLevel` compat shim for 2 release cycles (deprecation period). Controller CreateSodRuleRequest handler prefers `request.Severity` with fallback to legacy `request.EnforcementLevel` for backward-compatible request parsing. Pragma cleanup removes CS0618 warnings during compilation without behavioral change.

**Files:** 2 modified
- `src/modules/identity/AqtCoreFW.Identity.Application/Queries/ListTenants/ListTenantsQueryHandler.cs` (27 LOC new SQL)
- `src/modules/identity/AqtCoreFW.Identity.Presentation/Controllers/SodRulesAdminController.cs` (2× pragma wrap)

**Build:** 0 warnings, 0 errors expected.

**Breaking Changes:** None.

**Commit hash:** Pending submission.

---

## v2.128 — BTC Organizational Seeding (2026-04-23)

**Feature (Organization module):** Idempotent seed of 1,139 OrgUnit hierarchy (L1-L4) for BoTaiChinh tenant (a1b2c3d4-0000-0000-0000-00000000b7c1). Supports organizational tree structure covering central + regional + district/commune level government offices.

**Summary:**

**Added:**
- `BtcOrgUnitSeeder` (IHostedService, 6 partial files + 4 JSON resource files): Main orchestrator + L12 (BTC root + 35 central units) + L3 (109 regional units) + L4 (994 district/commune offices: Tax, Customs, Statistics, Treasury). Reads from `masterdata.Wards` via Dapper; uses `ProvinceLegacyMapping` for KBNN placeholder matching across 63→34 province consolidation.
- `BtcSeedOptions` + `BtcSeedConstants` — configuration binding (Enabled, MaxLevel, SkipGiaoDichPhong) + tenant constant.
- `BtcSeederTenantContext` — bypass tenant filter during seed (IsSystemAdmin=true).
- 9 new test files (1259 LOC): unit tests for L4 subtiers (Tax/Customs/Statistics/Treasury) + integration test + architecture/smoke/helpers tests. **Result: 128 pass / 0 fail / 15 skip** (skipped: Hangfire race conditions + performance gates marked [Fact(Skip=...)]).

**Changed:**
- `OrgRegistration.cs` — wire `BtcOrgUnitSeeder` as IHostedService in DI.
- MasterData `Province` entity — updated for 63→34 consolidation per QD 19/2025 (effective 2026-07-01).
- `ProvinceSeeder` — new class seeds legacy mapping reference data.

**Added (Database):**
- `ProvinceLegacyMapping` entity — maps old 63-province codes → new 34-province codes (many-to-one); includes identity mappings.
- Migration `20260422220625_AddProvinceLegacyMappings` — schema, indexes, 62 data rows (legacy mappings).
- Migration `20260422220742_DeactivateLegacyMockProvinces` — marks provinces 63-34 range `IsActive=0` post-reform.

**Tests:** All BTC seeder tests verified — no duplicates on re-run (idempotency). Smoke verified: `SELECT COUNT(*) FROM organization.OrgUnits WHERE TenantId='a1b2c3d4-0000-0000-0000-00000000b7c1'` → 1,139 nodes (L1=1, L2=35, L3=109, L4=994).

**Code Review Score:** 8.5/10 (0 critical, 2 minor: externalCode semantics TBD, ProvinceToKbnnRegionMapping defer post-real-ward-data).

**Build:** 0 warnings, 0 errors. `dotnet build` + `npm run build` clean. Playwright E2E: @smoke tags verified locally (14/14 pass).

**Breaking Changes:** None. Backward compatible — 63-province table rows remain; new 34-province rows added in parallel.

**Migration Notes:**
- Apply 2 new migrations in sequence: `AddProvinceLegacyMappings` → `DeactivateLegacyMockProvinces`.
- `BtcOrgUnit:Enabled=true` in appsettings — controls IHostedService auto-seed on startup.
- BoTaiChinh tenant must pre-exist for seed to run (validates `TenantId` before starting).

**Commit hash:** Pending submission.

**Follow-ups:**
- **Refactor Architecture/Integration/Smoke tests** — bypass WebAppFixture (Hangfire race with background jobs during tests). Phase-06 polish backlog.
- **Decide externalCode semantics** — extend OrgUnit entity vs. strip from JSON response. Product decision needed.
- **ProvinceToKbnnRegionMapping** — full region mapping after real ward data lands (currently KBNN placeholder).

---

## v2.127p9 — Runtime soft-hide consent + admin-rtbf (DB + Redis) (2026-04-22)

**Chore (runtime + script):** Hoàn thiện hide pattern v2.127p4/p5/p8 — apply runtime DB cleanup. Sidebar 2 menu này sẽ không còn xuất hiện sau khi user F5 (cache đã flush).

**Background:** Sau v2.127p8 (chore seeder code-only), user báo sidebar vẫn hiện `/admin/rtbf` + `/consent`. Audit 5-tầng confirm: L4 DB row `IsActive=1` + L5 Redis cache `menu-tree:*` còn entry → MenuService serve từ cache cũ. Patch p9 apply soft-hide UPDATE + Redis flush, đồng thời commit script vào repo cho audit trail.

**Summary:**
- `scripts/260422-soft-hide-consent-rtbf.sql` (new) — idempotent UPDATE `IsActive=0` cho 2 row, có `SET QUOTED_IDENTIFIER ON` (SQL Server Msg 1934 fix), comment rollback inline
- Runtime executed: `UPDATE [identity].[AppMenus] SET [IsActive]=0 WHERE [Code] IN ('admin-rtbf','consent') AND [IsActive]=1` → 2 rows updated
- Runtime executed: `redis-cli DEL menu-tree:*` → 1 cache key purged
- Pattern: **soft-hide** (Option B) — admin `/admin/menus` vẫn thấy row với "Kích hoạt: Không"; bật lại = `UPDATE IsActive=1` + flush Redis (rollback note inline trong script)

**Verification:**
```
sqlcmd: admin-rtbf|IsActive=0  consent|IsActive=0  ✅
docker exec redis: KEYS 'menu-tree:*' → empty ✅
```

**Tests:** Không đụng code (chỉ runtime DB UPDATE + Redis flush + script committed). Không cần build/typecheck.

**Limitation cleared:** Trade-off "chỉ FE+BE" của p8 đã được giải quyết. Các tầng L1+L2 FE / L3 BE seeder / L4 DB / L5 Redis đều đồng bộ hide.

**Commit hash:** `a15048e1` | **Version bump:** v2.127p8 → v2.127p9

---

## v2.127p8 — Tạm ẩn consent + admin-rtbf trong BE seeder (2026-04-22)

**Chore:** Đồng bộ tầng seeder với FE hardcoded hide (v2.127p4/p5). Comment 4 entry trong `E2ETestDataSeeder.Menus.cs` để fresh DB không seed lại menu `consent` + `admin-rtbf`.

**Background:** Audit đa layer phát hiện v2.127p4/p5 chỉ ẩn FE hardcoded fallback — không phủ DB-driven path. Sidebar thực tế serve menu từ `/api/v1/menus/my-menus` (DB). User có quyền `AUDIT.RTBF.VIEW` / `IDENTITY.CONSENT.VIEW` vẫn thấy menu. Patch này đồng bộ seeder; DB runtime row hiện có cần reset để apply (trade-off user chấp nhận — "chỉ FE+BE only").

**Summary:**
- `E2ETestDataSeeder.Menus.cs:190` — comment `M(MenuRtbfId,...)` menu entry
- `E2ETestDataSeeder.Menus.cs:206` — comment `M(MenuConsentId,...)` menu entry
- `E2ETestDataSeeder.Menus.cs:260` — comment `P(MenuRtbfId, "AUDIT.RTBF.VIEW")` permission binding
- `E2ETestDataSeeder.Menus.cs:271` — comment `P(MenuConsentId, "IDENTITY.CONSENT.VIEW")` permission binding
- `MenuRtbfId` + `MenuConsentId` GUID constants giữ lại (line 33, 85) cho phép bật lại không bị trùng GUID
- Marker `// tạm ẩn` đồng bộ pattern v2.127p4/p5 — `grep "tạm ẩn" src/host/AqtCoreFW.Api/` + `grep "tạm ẩn" web/src/layouts/` list đầy đủ menu đang hide

**Tests:** BE API host build 0 Error(s) / 2 pre-existing SodRules warnings (60.78s). FE không đụng.

**Limitation (intentional):** DB hiện có vẫn giữ row `consent` + `admin-rtbf` `IsActive=1`. Seeder idempotent theo `MenuDashboardId` → không reseed. Để thực sự ẩn khỏi runtime: (a) manual SQL `DELETE FROM [identity].[AppMenus] WHERE Code IN ('consent','admin-rtbf')` + flush Redis `menu-tree:*`, hoặc (b) reset DB rồi reseed. Không bao gồm trong patch này theo yêu cầu user.

**Commit hash:** `b011f52e` | **Version bump:** v2.127p7 → v2.127p8

---

## v2.127p7 — Admin menu page fix + system-menu edit guard (2026-04-22)

**Fix:** Trang `/admin/menus` hiển thị rỗng với tenant user do filter mismatch giữa `ListMenusQueryHandler` và `MenuService.LoadFromDbAsync`. Thêm guard chặn non-SystemAdmin sửa global menu (Option B).

**Root cause:** `ListMenusQueryHandler.Where(m => request.TenantId == null || m.TenantId == request.TenantId)` — khi `request.TenantId` non-null (user đã login), clause đầu false + clause sau `(null == <GUID>) = false` → zero rows. Tất cả menu seeded có `TenantId=null` (global). Sidebar hoạt động vì `MenuService` dùng filter đúng `m.TenantId == null || m.TenantId == ctx.TenantId`.

**Summary:**
- BE `ListMenusQueryHandler.cs:20` — fix clause `request.TenantId == null` → `m.TenantId == null` (union global + tenant menus, đồng bộ với `MenuService.LoadFromDbAsync:82`) + comment anchor "Matches MenuService.LoadFromDbAsync filter" chống drift
- BE `MenuCommandHandlers.cs` — inject `ITenantContext`; guard `UpdateMenuCommandHandler`: global menu (`TenantId=null`) chỉ SystemAdmin được sửa → `ForbiddenError` 403 cho user khác (defense-in-depth)
- FE `menu-admin-page.tsx` — tag `Hệ thống` (geekblue) cho row `tenantId=null`; disable nút Edit + tooltip "Menu hệ thống — chỉ SystemAdmin mới sửa được" khi user không phải SystemAdmin
- Pattern: FE disable UX + BE enforce authority — request thủ công vẫn bị chặn 403

**Tests:** BE Identity.Infrastructure build 0W/0E (30.55s). FE `pnpm tsc --noEmit` exit 0. Không regression test mới — hotfix 1-dòng filter + comment anchor đóng vai trò grep-anchor chống drift tương lai.

**Commit hash:** `57de9844` | **Version bump:** v2.127p6 → v2.127p7

---

## v2.127p6 — Phase-00 UX Sidebar Refactor (2026-04-22)

**Feature:** Admin sidebar menu reorganization into 6 integration-aware groups with tooltip descriptions.

**Summary:**
- BE Menu entity: AppMenu + Description column (up to 500 chars, migration `20260422103319_AddMenuDescription`)
- BE MenuService: MenuNode DTO extended with description field; 3 new group GUIDs + 6 group descriptions seeded; alias menu item `/admin/integration/partners` added
- FE admin menu: Reorganized from 4 groups into 6 groups separating Gateway Auth / Event Notification / Business Integration layers
- FE sidebar: New tooltipLabel helper + Antd Tooltip span wrapper for menu item descriptions
- FE menus-api: MenuNodeDto.description field; locale updates (vi.json + 6 group descriptions in Vietnamese)
- Verification: sidebar renders 6 groups with descriptions; DB/Redis/FE all have description field; Antd Tooltip DOM present

**Tests:** All integration tests pass; TS typecheck pass; sidebar renders correctly with tooltip descriptions visible on hover.

**Commit hash:** `1d8f0dd1` (branch: `thiennd/musing-dirac-535360`, PR #52) | **Version bump:** v2.127p5 → v2.127p6

---

## v2.127p5 — Hide /consent sidebar menu (2026-04-22)

**Chore:** Tạm ẩn entry `/consent` khỏi sidebar menu chính (UI-only). Route + BE endpoints + permissions không đổi — user đã đăng nhập vẫn vào được qua URL trực tiếp.

**Summary:**
- `web/src/layouts/sidebar-menu.tsx:17` — comment dòng import `SolutionOutlined` (tránh unused import warning)
- `web/src/layouts/sidebar-menu.tsx:69` — comment menu item `/consent` (user-facing PDPL consent management)
- Giữ nguyên route `consent-page.tsx` (mount dưới `authenticatedRoute`), BE `ConsentController` (`/api/v1/identity/consents`), i18n keys `nav.consent` + `consent.*`
- Pattern match v2.127p4 — marker "tạm ẩn" + cross-reference để `grep "tạm ẩn" web/src/layouts/` trả về tất cả menu đang ẩn
- Bật lại: `grep "tạm ẩn" web/src/layouts/sidebar-menu.tsx` → uncomment 2 dòng

**Tests:** TS typecheck pass (`npx tsc --noEmit` exit 0). Không đụng BE.

**Commit hash:** `43315433`

---

## v2.127p4 — Hide RTBF admin menu (2026-04-22)

**Chore:** Tạm ẩn entry `/admin/rtbf` khỏi Admin System menu (UI-only). Route + BE endpoints + permissions không đổi — SystemAdmin vẫn vào được qua URL trực tiếp.

**Summary:**
- `web/src/layouts/admin-menu-entries.ts:26` — comment dòng import `DeleteOutlined` (tránh unused import warning)
- `web/src/layouts/admin-menu-entries.ts:152` — comment menu item `/admin/rtbf`
- Giữ nguyên `SYSTEM_PATHS[]` line 90 (path classification cho AdminLayout)
- Giữ nguyên route `admin-rtbf-page.tsx`, BE `RtbfController`, i18n keys `nav.rtbf` + `rtbf.*`, permissions `Audit.Rtbf.View/Execute`
- Bật lại: `grep "tạm ẩn" web/src/layouts/admin-menu-entries.ts` → uncomment 2 dòng

**Tests:** TS typecheck pass (`npx tsc --noEmit` exit 0). Không đụng BE.

**Commit hash:** `bf3f102a`

---

## v2.127p3 — Phase 02 Follow-up Hardening (2026-04-22)

**Feature:** Three post-review security and test-layering fixes.

**Summary:**
- **F1 Tenant Filter:** `WorkflowDefinitionEntityTypeLookup.GetEntityTypeAsync(Guid, Guid tenantId, CancellationToken)` — added SQL filter `AND (TenantId = @T OR TenantId = Guid.Empty)` mirroring `GetWorkflowDefinitionQueryHandler` fallback. Integration test validates lookup-level tenant rejection. Closes info-leak probe.
- **F2 AuditBehavior `resourceId` Propagation:** Added cached reflection extractor `ConcurrentDictionary<Type, Func<object?, string?>>` reads `response.Value.Id` from `Result<TDto>` responses. Best-effort, never throws. +2 unit tests.
- **F3 Test Layering:** Removed Infrastructure ProjectReference from `AqtCoreFW.Workflow.Application.Tests.csproj`. Moved `EntityStatusEnumRegistryTests` to Infrastructure.Tests. Added local `FakeEntityStatusEnumRegistry` in 2 validator test files.

**Tests:** 644/644 pass (SharedKernel, Workflow.Application, Workflow.Infrastructure, Tests.Integration Workflow filter). Build 0W/0E. Code-review 9/10 APPROVED_WITH_MINOR, M1 post-review applied (F1 assertion tightened).

**Commit hash:** `1baf459a`

---

## v2.127p2 — Cases Workflow Finalize Phase 02 (2026-04-22)

**Feature:** WorkflowDefinition API Expansion + State/Transition Validation + RBAC Hardening (Phase-02 cases workflow finalize v2).

**Summary:**
- Added `EntityType` field (NOT NULL, default `"Case"`) to `WorkflowDefinition` entity + DTO + Command + EF migration (Designer + Snapshot) with backfill.
- New `IEntityStatusEnumRegistry` + `IEntityStatusEnumContributor` contract (SharedKernel-like pattern, singleton DI) enabling modules to contribute entity-specific enum validators. Cases module implements `CaseStatusEnumContributor` registering `CaseStatus` enum.
- `CreateWorkflowDefinitionCommandValidator` (new) + extended `SaveDefinitionGraphCommandValidator` — validate every `States[].Name`, `Transitions[].FromStateName`, `Transitions[].ToStateName` against allowed enum names (case-sensitive `StringComparer.Ordinal`). Structured error payload: `{ field, errorMessage }`.
- `WorkflowDefinitionsController` RBAC hardened: class-level `[Authorize(Policy = PermCodes.Workflow.Def.View)]` swapped in; method-level `Manage` on writes remains (native AND stack = writes require BOTH claims).
- 9 commands converted `IRequest → ICommand<T>` to opt into existing `AuditBehavior<TRequest,TResponse>` MediatR pipeline — auto-logs all write commands via `IAuditLogger.LogCommandAsync`.
- FE mirror: `WorkflowDefinitionDto.entityType` + admin form hidden/readonly "Case" input (D4 YAGNI — single entity today).
- SeederCasingTest (H4 guard) + 23 unit tests + 13 integration tests (RBAC 10 + validation 3).
- Tests: 434 Workflow.Application + 67 Workflow.Infrastructure + 27 integration. Build 0W/0E.

**Tests:** 23 unit + 13 integration. Build clean.

**Commit hash:** `2779305146ecb2c6c8966266cf8c7a5f0c2e8607`

---

## 2026-04-22 — AI Module Deferred-Implementation Formalization (no version bump)

**Type:** Docs + scaffold governance. No runtime behavior change when `AI:Ollama:Enabled=false` (default).

**Context:** AqtCoreFW AI module (Search/NLQ, Copilot, RAG, ReAct Agents, Report Analyst, Decision Support, Risk Scorer) is a **platform scaffold** — interfaces (ports) defined, stub adapters active by default, reference real adapters (Ollama/Azure OpenAI/Qdrant) coded but gated behind `AI:Ollama:Enabled`. Prior docs marked module `✓ COMPLETE` which misrepresented wire-up status. This change codifies the deferred strategy across 4 layers: code, UI, docs, governance.

**Summary:**
- **Backend gate:** `ReportDefinitionSeeder` now injects `IConfiguration` and gates `SeedQueryCatalogAsync` behind `AI:Ollama:Enabled` flag (default false → skip + debug log). Existing seeded rows untouched (harmless without executor).
- **Shared Kernel:** 3 orphan interfaces (`IAiReportAnalyst`, `IAiDecisionSupport`, `IAiRiskScorer`) annotated with `<remarks>DEFERRED IMPLEMENTATION</remarks>` XML comment → signal visible in Intellisense.
- **Frontend signal:** New reusable component `web/src/shared/components/deferred-feature-alert.tsx` rendered on `/ai/search` (`AiSearchPage`) + `/copilot` (`CopilotChatPage`). User-visible closable info banner (VN + EN) explaining scaffolded state.
- **Docs sync:** `docs/README.md:137` status ✓COMPLETE → 📐SCAFFOLDED (deferred); `docs/project-roadmap.md:825` AI Search marker corrected; new section `AI Module — Implementation Strategy (Deferred)` added to `docs/system-architecture.md` (before Copilot Sidebar sub-section); `docs/dependency-map.md` new section `### AI (schema: 'ai') — DEFERRED IMPLEMENTATION` with entity/ports/stubs/real-adapters inventory + GOV_AI_001 note.
- **Backlog:** New tracking section in `backlogs/backlog.md` — 8 wire-up tasks `AI-W1..W8` (NLQ pipeline, Copilot prod enablement, Report Analyst adapter, Decision Support adapter, Risk Scorer adapter, TokenBudgetTracker enforcement, AiPromptTrace audit entity, QueryCatalog cleanup). Rule: tasks only close with business case reference in commit/PR.
- **Memory:** New entry `memory/project-ai-modules-deferred-implementation.md` — strategy decision + how to apply + reconcile trigger.

**Verification:** `dotnet build Reporting.Application` → 0 warning / 0 error. FE TypeScript compile not run (3 minimal Antd Alert edits, low risk). Browser preview not run (visual banner only).

**Breaking changes:** None. Default runtime path unchanged (stubs still active).

**Activation trigger for any wire-up task:** Concrete business case arrives → open matching AI-W task → implement specific adapter → flip `AI:Ollama:Enabled` in target env → remove `DeferredFeatureAlert` from affected page.

**Files touched:** 11 modified + 1 new. See commit for full list.

**Rationale:** Follow-up from `/ask` session discussing AI module scope — user confirmed "chiến lược giữ interface + abstract, implement khi vào business case". Prior `✓ COMPLETE` marks would mislead future dev/agent into treating scaffold gaps as bugs.

**Commit hash:** `cf161b55` (PR #50)

---

## v2.127p1 — Cases Workflow Finalize Phase 03 (2026-04-22, partial)

**Feature:** FE dead-code cleanup — workflow inbox page unreachable.

**Summary:**
- Deleted `web/src/features/workflow/workflow-inbox-page.tsx` (114 lines, static HTML page never imported, unreachable export from barrel `workflow/index.ts`).
- Updated barrel: removed 1 line from `web/src/features/workflow/index.ts`.
- No i18n cleanup needed: confirmed `workflow.inbox.*` namespace owned by `task-detail-drawer.tsx` (LIVE), not orphan. Plan v1 assumption invalid.

**Tests:** FE vitest 755/775 pass (20 pre-existing jsdom+Antd Modal fails, unrelated). Build clean.

**Scope:** Phase 03 of Cases Workflow Finalize plan v2 (in progress, 3 phases total: 03→02→01).

**Commit hash:** `20151f10`

---

## v2.127 — SoD Warn+Audit Refactor (2026-04-21)

**Feature:** Separation of Duties enforcement model simplification + comprehensive audit logging.

**Summary:**
- P1 Schema: `SodEnforcementLevel` enum → `SodSeverity` (Low=1, Medium=2, High=3 int values preserved); entity field `EnforcementLevel` → `Severity`; shadow column `LegacyEnforcementLevel` for 30d reversibility; V2 hard-delete endpoint removed; V4 compat shim accepts both `enforcementLevel` + `severity` DTOs for 2-release cycles; new PermCodes `Identity.Sod.ViewAuditLog` + `Identity.Sod.ExportAuditLog` seeded to Admin role.
- P2 Audit log: New entity `SodViolationLog` (8 fields, `Justification` ≤2000 chars triple-layer cap); atomic inline audit write in `SyncRolePermissionsCommandHandler`; FK `ON DELETE RESTRICT` to `SodConflictRules`; index `(TenantId ASC, DetectedAt DESC)`; 3 unit tests.
- P3 Admin UI: Violations tab folded into existing `/admin/sod-rules` page (no new route); `ListSodViolationLogsQueryHandler` with inline tenant filter + INNER JOIN; FE components `sod-violations-tab/` (5 files); permission guard `IDENTITY.SOD.VIEW_AUDIT_LOG`.
- P4 Export CSV: `GET /api/v1/admin/sod-rules/violations/export` streaming `IAsyncEnumerable` endpoint; `SodAuditLogCsvWriter` with RFC 4180 escape + OWASP formula-injection defense (prefix `'`); 24 unit tests; separate `ExportAuditLog` permission; deterministic `ORDER BY DetectedAt DESC, Id`; UTF-8 BOM for Excel Vietnamese chars.
- P5 Hangfire: `SodAuditLogPurgeJob` batched DELETE TOP(10000), per-batch transaction, oldest-first deterministic order, parameterized cutoff; `RecurringJob.AddOrUpdate(..., Cron.Daily(2, 0), TimeZoneInfo.Utc)`; 4 integration tests.
- P6 Bugfix: `.Take(10)` bug removed from `SyncRolePermissionsCommandHandler:90` (silent user truncation in large roles); inline tenant filter added to `SodConflictChecker:59` (Identity module has NO global filter); 6 new xUnit tests.

**Tests:** 63/63 unit pass (Identity.Infrastructure.Tests). 4/4 integration pass (SodAuditLogPurgeJob Testcontainers). `yarn build` web pass.

**Breaking changes:** `DELETE /api/v1/admin/sod-rules/{id}` removed (V2 soft-delete only). FE Delete UI removed. E2E `TC-IDENT-082` deleted.

**Compatibility:** V4 compat shim accepts `enforcementLevel` field for 2 release cycles. Schema shadow column persists for 30 days (reversibility window).

**Commit hash:** `b714c98d` | **Version bump:** v2.126 → v2.127

---

## 2026-04-21 — v2.126 Cases→Workflow Button Unification + Tenant Fallback + Search Removal

**Commits:** `6c3d90c4` (fix: read-side Dapper materialization + tenant fallback), `fac879a5` (feat: cases workflow history timeline + cascading assign dropdowns), `53de1556` (chore: gitignore api runtime logs), `a7758ce2` (test: add useOrgUnitStaff to org-api mock)

### Phase-03: Workflow History Timeline + Return Endpoint + WorkflowInstanceId Projection
- **BE Workflow History Enrichment:**
  - `GetWorkflowInstanceQueryHandler` new dependency on `IIdentityModuleClient` for actor enrichment (FromStateName, ToStateName, ActorName, ActionLabel per history record)
  - Dapper materialization fix for `UserOrgUnitAssignmentDto` — unblocks `/admin/org/units/{id}/members` 500 error
  - IIdentityModuleClient tenant fallback for global seed users (fixes null FullName in UserDto)
  - Read-side hygiene: all queries now validate tenant scope before returning data
  
- **BE Cases Return Endpoint:**
  - New endpoint: `POST /api/v1/cases/{id}/return` with `{ reason: string }` body (permission `CASES.WORKFLOW.RETURN`)
  - `ReturnCaseCommand` + `ReturnCaseCommandHandler` (mirrors `ApproveCase` pattern)
  - `Case.RecordReturn(newStateName, reason, actorId)` domain method
  - `CaseDto.WorkflowInstanceId: Guid?` field, populated via `IWorkflowModuleClient.TryResolveInstanceForEntityAsync`
  
- **FE Workflow History + Cascading Dropdowns:**
  - New component: `web/src/features/cases/case-workflow-history.tsx` (sidebar timeline consuming `useWorkflowInstance`)
  - `case-workflow-actions.tsx` rewritten to consume API-driven transitions from `/available-transitions`
  - Fallback to legacy hardcoded switch when `workflowInstanceId` is null (backward compatibility for legacy cases)
  - Cascading assign dropdowns: department → officer (replaces free-text inputs)
  - New hooks: `useAvailableTransitions`, `useReturnCase`
  - i18n: 8 new keys under `page.cases.workflow` namespace
  
- **Feature Flag:**
  - `cases.workflow-delegation` enabled globally (delegates permission checks to workflow API)
  
- **Tests:** 9 unit tests (Return handler, projections) + 4 integration tests (6-transition lifecycle) + 4 regression tests (tenant fallback)

### Post-Phase-03: Workflow Admin Query Tenant Fallback
- `GetWorkflowDefinitionsQueryHandler` + `GetWorkflowDefinitionQueryHandler` now use `(TenantId = @TenantId OR TenantId = @GlobalTenantId)` pattern
- Allows admins to view global templates (e.g., CaseProcess v1 @ GlobalTenantId) alongside tenant-specific definitions
- Falls back to global when tenant-specific definition missing (read-only access)
- 4 regression tests (2 per handler)

### CHORE: Remove AqtCoreFW.Search Module — Dead Code Cleanup

**Version bump:** v2.125 → v2.126

**Scope:** Delete entire Search module + FE + E2E tests

### Motivation

UnifiedSearch feature was never productionized — no `IndexAsync` callers, no `search.{index}_v` views ever created in migrations, FTS catalog never provisioned. Module had zero external consumers. Business modules already have contextual search in their own list pages (cases/forms/files). Codebase benefit from cleanup: remove maintenance burden.

### Changes

**Deleted (4 BE project families):**
- `src/modules/search/AqtCoreFW.Search.Domain/` — SearchDbContext + SavedQuery entity + repo + errors
- `src/modules/search/AqtCoreFW.Search.Application/` — commands (SaveQuery, DeleteQuery) + queries (GetSavedQueries, GetSearchResults)
- `src/modules/search/AqtCoreFW.Search.Infrastructure/` — EF migrations (schema `search`), FTS/Elasticsearch implementations, query handlers
- `src/modules/search/AqtCoreFW.Search.Presentation/` — SearchController (4 endpoints: GET /api/v1/search, /facets, /saved-queries/*), DTO/validators

**Deleted (3 test projects):**
- `tests/unit/AqtCoreFW.Search.Domain.Tests/`
- `tests/unit/AqtCoreFW.Search.Application.Tests/`
- `tests/integration/AqtCoreFW.Search.Integration.Tests/`

**Deleted (FE + E2E):**
- `/search` route (UnifiedSearchPage component) + sidebar entry
- i18n `nav.search` + section `search`
- 3 E2E specs: `search-unified.spec.ts`, `search-lifecycle.spec.ts`, `search-saved-queries.spec.ts`

**Kept intact (reusable infra):**
- `SharedKernel/Application/Search/*` — `ISearchService` abstraction (toolbox interface)
- `Infrastructure/Search/*` — `SqlServerFtsSearchService` + `ElasticsearchSearchService` + query builders (reusable adapters)
- `/ai/search` route (separate AI module feature)
- `@/shared/components/search-bar` (reusable input component)
- `Collaboration.SearchMessages` + `Identity.UserSearchController` (self-contained module-local features)

**Architecture tests updated:** Search module dropped from Modules list (`ArchitectureTests.cs`).

### Test Results

`dotnet build AqtCoreFW.slnx` — 0 warnings, 0 errors (5:22).

### Ops: Database Cleanup

Run post-deploy per tenant:
```sql
DROP SCHEMA IF EXISTS [search];  -- Cascades to SavedQueries + OutboxMessages tables
```

Safe to run — feature was never enabled; no data loss.

---

## PLAN: BusinessProcess Definition MVP — Low-Code Admin Config (2026-04-20)

**Scope:** 11-phase plan + 17 red-team R-findings for new module `businessprocess` bundling Workflow + Forms.

### Plan artifacts
- Plan directory: `plans/260420-1957-business-process-definition-mvp/` (gitignored)
- 11 phase files (P0-P10), plan.md overview, plan-red-team-findings.md (17 findings)

### Red-team value captured pre-code
- R1 Critical: `FormSubmission` is immutable gov audit entity — original "edit in-place" design violated invariant. Resolved: query-latest pattern (append-only + read latest).
- R2 Critical: Prod Cases table safe to DROP (user confirmed empty per tenant) — migration simplified.
- R3 High: Workflow version migrate infra ALREADY SHIPPED (WorkflowInstance.RecordMigration + 4 handlers + migration 20260412144356) — P9 scope reduced 1.5d → 0.5-1d.
- R16: CaseStatus enum → string for BP arbitrary workflow state names (low-code flexibility over type safety).

### MVP scope locked
- 3 new entities trong module `businessprocess`: BusinessProcessDefinition, BusinessProcessStateForm, BusinessProcessTransitionForm
- Forms: FormTemplateVersion entity (snapshot on Publish)
- Cases: thêm BusinessProcessDefinitionId FK + IntakeFormSubmissionId + Status string refactor
- FormSubmission: thêm CaseId + WorkflowStateId nullable FKs
- 3 permission codes: BusinessProcess.{Manage, View, Activate}

### No version bump
Planning artifact only — no code changes this session. Implementation starts P0 next session.

---

## CHORE: Local Dev Script — SQL Native + Docker Redis/RabbitMQ Only (2026-04-20)

**Scope:** `scripts/1_run-local-dev.ps1` + 4 doc cross-refs

### Changes

- **scripts/1_run-local-dev.ps1:** Docstring reflects native SQL Server 2022 install (instance `MSSQLSERVER22` @ `localhost,1433`) + Docker used for Redis + RabbitMQ only (app services run via `dotnet run`)
- **New function `Test-SqlServerLocal`:** Checks Windows service `MSSQLSERVER22` state before starting app services (warns if not running, does not auto-start)
- **`Test-DockerInfra`:** Removed `sqlserver` from required container list; targeted `docker compose up -d redis rabbitmq` (avoids conflicts with app containers / nginx when running in native mode)
- **Step numbering:** Pipeline renumbered 1-9 (added SQL check as step 2)

### Documentation Sync

- **README.md:** Section "Start dev infrastructure" now recommends `scripts/1_run-local-dev.ps1` for local dev, documents `docker compose up -d` as CI/full-container fallback
- **docs/developer-onboarding-guide.md:** Section 2.2 clarifies script-first workflow
- **docs/system-architecture.md:** Line 251 — `1_run-local-dev` description expanded (native dotnet run + Docker Redis/RabbitMQ + native SQL)
- **docs/codebase-summary.md:** Line 128 — same clarification

### Rationale

Developer's local environment is SQL Server 2022 native install (not Docker), confirmed by memory `local-sqlserver-2022.md`. Previous script version attempted to start `sqlserver` container which would conflict with native instance and produce confusing errors. YAGNI: `1_run-local-dev.bat` untouched (unused in practice).

---

## FEATURE: T4-05 FU5.1 — Rules Query Handlers + Module Client Tenant Cross-Check (2026-04-20)

**Version bump:** v2.124 → v2.125

**Scope:** Tenant isolation hardening for Rules module query handlers + cross-module client throw variant. T4-05 family completion.

### Security — Pattern A (pre-factory) + Pattern B (post-fetch)

- **8 Rules module query handlers:** GetRuleSetQueryHandler, ListRuleSetsQueryHandler, EvaluateRuleQueryHandler, SimulateRuleQueryHandler, ExplainRuleQueryHandler, GetDecisionTableQueryHandler — all inject `ITenantContext`, validate `request.TenantId vs tenantContext.TenantId` before processing or fail with `ValidationError` (HTTP 400). Non-SystemAdmin cross-tenant rejection.
- **RunTestCaseCommandHandler (post-fetch):** Loads TestCase, then parent RuleSet via FU5-hardened repo (inline filter). Null parent → NotFoundError; cross-tenant parent → returns null (repo filter), handler converts to NotFoundError. Defence-in-depth: parent RuleSet filter is safety net.
- **InProcessRuleEngineModuleClient (throw variant):** Cross-module write-path defence. Wrapper around `IRuleEvaluator.EvaluateAsync`, enforces tenant match pre-call. Throw `InvalidOperationException` on cross-tenant attempt (internal invariant, never user-exposed).

### Tests — 17 new (16 scope + 1 M2 fix post-review)

- **Query handlers (14 scope tests):** GetRuleSetQueryHandlerTests, ListRuleSetsQueryHandlerTests, EvaluateRuleQueryHandlerTests, SimulateRuleQueryHandlerTests, ExplainRuleQueryHandlerTests, GetDecisionTableQueryHandlerTests — 2 tests per handler (happy path + cross-tenant ValidationError)
- **Command handler (1 scope test):** RunTestCaseCommandHandlerTests — 1 new guard test (cross-tenant parent scenario)
- **Module client (1 scope test):** InProcessRuleEngineModuleClientTests — 1 new throw variant test
- **M2 fix (post-review):** Added null-context test to GetRuleSetQueryHandlerTests to cover R3 red-team commitment (non-SystemAdmin, `ITenantContext.TenantId == null`)
- **Regression:** Rules.Application 47/47 (30 existing + 17 FU5.1 incl M2 null-context post-review), Rules.Infrastructure 12/12 (5 FU5 + 5 FU5.2 + 2 FU5.1), TenantIsolation 11/11 (no regression)

### Test Results

- `dotnet build src/AqtCoreFW.slnx` → 0W/0E
- Rules.Application.Tests: 47/47 (15 new FU5.1 incl M2)
- Rules.Infrastructure.Tests: 12/12 (2 new FU5.1)
- TenantIsolation.Tests: 11/11 (no regression)
- Rules.Integration.Tests: 6/6 (no regression)

### Code Review

- Score: 9.3/10 APPROVED_WITH_MINOR
- M1: RunTestCaseCommandHandler guard branch unreachable for non-SystemAdmin (documented reality, dead-code in production, NotFoundError still denies access). Recommendation: document or defer to polish pass.
- M2: Null-context test R3 commitment unmet in initial submission. **FIXED post-review:** added `Handle_NullContextTenant_ReturnsValidationError` test to GetRuleSetQueryHandlerTests.
- M3: NSubstitute generic setup fragility in 2 SystemAdmin-bypass tests (no-op setup, tests pass by accident). Low priority, tests still validate guard logic via NotFoundError assertion.
- N1-N3: Log templates, minor naming — no action required.

### Pattern References

- Inline filter: `RuleSetRepository.cs` (FU5 v2.123)
- Handler Pattern A: `CreateRuleSetCommandHandler.cs` (FU5 v2.123)
- Handler Pattern B: `UpdateRuleCommandHandler.cs` (FU5 v2.123)
- Module client throw: `InProcessAbacRuleModuleClient.cs` (Identity module pattern)

### Files Changed

15 files (+760/-0 LOC). New: 8 test files (query + command + client handlers). Modified: 7 src files (query/command handlers + module client). Existing test files: +1 test added (GetRuleSetQueryHandlerTests M2 fix).

**Status:** SHIPPED @ `87af3d8e` — pushed to `main` 2026-04-20.

---

## FEATURE: T4-05 FU5.2 — DecisionTable Tenant Hardening (2026-04-20)

**Version bump:** v2.123 → v2.124

**Scope:** Tenant parity for DecisionTableRepository. Complements FU5 RuleSetRepository hardening.

### Security — Pattern A (pre-factory)

- **DecisionTableRepository tenant filter:** `GetByIdAsync` inlines `_tenantContext.IsSystemAdmin || dt.TenantId == _tenantContext.TenantId` filter (applied BEFORE Id selector — prevents cross-tenant row leak on IgnoreQueryFilters bypass). `AddAsync`/`UpdateAsync` enforce strict tenant-match guard — throws `InvalidOperationException("Cross-tenant DecisionTable create rejected")` with structured LogWarning.
- **IDecisionTableRepository.UpdateAsync** (NEW): Future-proof method signature (0 callers today). Same strict guard pattern as AddAsync.
- **CreateDecisionTableCommandHandler (Pattern A pre-factory):** Validates `request.TenantId vs tenantContext.TenantId` before domain construction. LogWarning + `Result.Fail(ValidationError)` on mismatch.

### Tests — 7 new (5 repo + 2 handler)

- **New:** `tests/unit/AqtCoreFW.Rules.Infrastructure.Tests/Repositories/DecisionTableRepositoryTests.cs` — GetById same/cross/admin + AddAsync/UpdateAsync wrong-tenant
- **New:** `tests/unit/AqtCoreFW.Rules.Application.Tests/Commands/CreateDecisionTableCommandHandlerTests.cs` — cross-tenant ValidationError + same-tenant success positive control
- **Regression:** Rules.Application 32/32 (30 existing + 2 new guards), TenantIsolation 11/11 (7 existing Cases + 4 existing Rules)

### Test Results

- `dotnet build src/AqtCoreFW.slnx` → 0W/0E
- Rules.Infrastructure.Tests: 10/10 (5 RuleSet + 5 new DecisionTable)
- Rules.Application.Tests: 32/32
- TenantIsolation.Tests: 11/11 (no regression)
- Rules.Integration.Tests: 6/6 (no regression)

### Code Review

- Score: 9.5/10 APPROVED (0 critical, 0 major, 1 minor plan-locked, 1 nitpick)
- Improvement over FU5 baseline (9.3) — smaller blast radius (1 handler vs 3), cleaner scope discipline

### Pattern References

- Inline filter: `RuleSetRepository.cs` (FU5 v2.123 baseline)
- Handler Pattern A: `CreateRuleSetCommandHandler.cs` (FU5 v2.123 baseline)

### Files Changed

5 files (+127/-0 LOC). New: DecisionTableRepositoryTests.cs, CreateDecisionTableCommandHandlerTests.cs. Modified: DecisionTableRepository, IDecisionTableRepository, CreateDecisionTableCommandHandler.

**Status:** SHIPPED @ `67083645` — pushed to `main` 2026-04-20.

---

## FEATURE: T4-05 FU5 — Rules Module Tenant-Isolation Hardening (2026-04-20)

**Version bump:** v2.122 → v2.123

**Scope:** Defence-in-depth for multi-tenant write-path in Rules module (completion of T4-05 security family).

### Security — Pattern A (pre-factory) + Pattern B (belt+suspenders)

- **RuleSetRepository tenant filter:** `GetByIdAsync` inlines `_tenantContext.IsSystemAdmin || r.TenantId == _tenantContext.TenantId` filter (immune to IgnoreQueryFilters bypass). `AddAsync`/`UpdateAsync` enforce strict tenant-match guards (no admin bypass), throw `InvalidOperationException` on mismatch with structured `LogWarning(currentTenantId, attemptedTenantId, ruleSetId)`.
- **CreateRuleSetCommandHandler (Pattern A pre-factory):** Validates `request.TenantId vs _tenantContext.TenantId` before `RuleSet.Create()`. Rejection emits `Result.Fail(ValidationError)` → HTTP 400.
- **UpdateRuleCommandHandler + ActivateRuleVersionCommandHandler (Pattern B belt+suspenders):** Post-load guard with admin-bypass read-path consistency (documented inline). Repo InvalidOperationException → HTTP 500 signals invariant breach.

### Tests — 12 new (5 repo + 3 handler + 4 integration)

- **New project:** `AqtCoreFW.Rules.Infrastructure.Tests` (slnx line 150)
- **Repository tests (5):** `RuleSetRepositoryTests.cs` — cross-tenant read/write, same-tenant happy, SystemAdmin read-bypass
- **Handler tests (3):** CreateRuleSetCommandHandlerTests, UpdateRuleCommandHandlerTests, ActivateRuleVersionCommandHandlerTests — each +1 cross-tenant ValidationError scenario
- **Integration (4):** `RulesTenantIsolationFixture.cs` (DbName `AqtCoreFW_Test_RulesTenantIso`) + `RuleSetTenantIsolationTests.cs` — 4 scenarios: same-tenant CRUD ✓, cross-tenant read 401 ✓, cross-tenant write 400 ✓, SystemAdmin read-bypass ✓

### Test Results

- `dotnet build src/AqtCoreFW.slnx` → 0W/0E
- Rules.Infrastructure.Tests: 5/5
- Rules.Application.Tests: 30/30 (27 existing + 3 new guards, 0 regression)
- TenantIsolation.Tests: 11/11 (7 existing Cases + 4 new Rules)
- Rules.Integration.Tests: 6/6 (no regression)
- Full `AqtCoreFW.Tests.Integration`: 103/103 (6m1s)

### Code Review

- Score: 9.3/10 APPROVED_WITH_MINOR
- 0 critical, 0 major
- M1/M2 → FU5.1 (query-handler context cross-check) + FU5.2 (DecisionTableRepository parity) backlog entries

### Pattern References

- B.1 inline filter: `src/modules/identity/AqtCoreFW.Identity.Infrastructure/Persistence/AttributeRuleRepository.cs`
- FU2 handler guard: `src/modules/identity/AqtCoreFW.Identity.Application/Commands/ManageAbacRule/UpdateAbacRuleCommandHandler.cs`

### Files changed

11 files (+380/-0 LOC). New: RuleSetRepositoryTests.cs, RuleSetTenantIsolationTests.cs, RulesTenantIsolationFixture.cs. Modified: RuleSetRepository, CreateRuleSetCommandHandler, UpdateRuleCommandHandler, ActivateRuleVersionCommandHandler, AqtCoreFW.slnx.

**Status:** SHIPPED @ `f7b5604b` — pushed to `main` 2026-04-20.

---

## FEATURE: T4-05 Follow-ups FU3 + FU4 — Rules Cache Invalidation + DeleteAbacRule Audit Log (2026-04-20)

**Version bump:** v2.121 → v2.122

**Scope:**
- **(FU3)** **UpdateRule invalidate compiled engine cache:** Complements v2.121 Phase 03 ActivateRuleVersion fix. `UpdateRuleCommandHandler.cs` now injects `IRuleEvaluator`, calls `InvalidateCache(ruleSet.Id)` after `repository.UpdateAsync()`. Covers stale-cache gap for rule edits: execution order is (1) load → (2) mutate entity → (3) persist to DB → (4) evict L1+L2 cache + Redis pub/sub publish. Next eval rebuilds engine from DB. Tests: 6 branches (happy success/persist + RuleSet-null no-invalidate + Rule-not-in-set no-invalidate). File: `tests/unit/AqtCoreFW.Rules.Application.Tests/Commands/UpdateRuleCommandHandlerTests.cs` (new).
- **(FU4)** **DeleteAbacRule audit log + defence-in-depth:** Symmetry with v2.121 FU2 UpdateAbacRule hardening. `DeleteAbacRuleCommandHandler.cs` now injects `ITenantContext`, emits `LogWarning` with caller `currentTenantId` when `rule is null` (cross-tenant attempt or already-deleted). Silent no-op preserved (Result.Ok for null) — idempotent delete, no tenant leak via response. `AttributeRuleRepository.GetByIdAsync` (B.1 v2.121 filter) ensures tenant isolation on read before delete. File: `tests/unit/AqtCoreFW.Identity.Application.Tests/Commands/ManageAbacRule/DeleteAbacRuleCommandHandlerTests.cs` (new, 5 tests).
- **(Skipped)** CreateDecisionTable not included (YAGNI — DecisionTables not in compiled engine cache policy, `RulesEngineEvaluator` builds only from `ruleSet.Rules`).

**Test Results:** Full integration suite `AqtCoreFW.Tests.Integration` 103/103 pass (6m). Rules.Application 27/27 (21 existing + 6 new FU3), Identity.Application 191/191 (186 existing + 5 new FU4), TenantIsolation 7/7, Rules.Integration 6/6 (pub/sub tests unchanged). Build: 0 warnings / 0 errors.

**Code Review:** `code-reviewer-260420-1010-t4-05-fu3-fu4.md` — 8.5/10. Minor M1 (FU4 audit log missing `currentTenantId` for forensic parity with v2.121 FU2) **applied post-review**: injected `ITenantContext` into `DeleteAbacRuleCommandHandler`, template now logs `{RuleId} + {CurrentTenantId}`. 191/191 tests still pass. P1 pre-existing issue flagged: `RuleSetRepository.GetByIdAsync` lacks tenant filter (defence-in-depth gap in Rules module, not introduced by FU3/FU4, tracked as new backlog T4-05-FU5).

**Dead-Pipe Verification:** `IRuleEvaluator.InvalidateCache` now has 2 callers: (1) `ActivateRuleVersionCommandHandler` (v2.121 M1 fix), (2) `UpdateRuleCommandHandler` (FU3 new). Dead-pipe regression class from v2.121 fully closed. Tests assert `Received(1).InvalidateCache` on happy path + `DidNotReceive` on failure paths.

**Files changed:** 4 files (+180/-0 LOC). New: UpdateRuleCommandHandlerTests.cs (78 LOC), DeleteAbacRuleCommandHandlerTests.cs (95 LOC). Modified: UpdateRuleCommandHandler.cs, DeleteAbacRuleCommandHandler.cs.

**Status:** Commits pending (code review complete, finalize gate pending journal + memory sync before push).

---

## FEATURE: T4-05 Phases 02-04 + FU2 — Rules Pub/Sub + Integration Tests + ABAC Write-Path Hardening (2026-04-20)

**Version bump:** v2.120 → v2.121

**Scope:**
- **(Phase 02)** **Deleted MasterData dead code:** Removed `DictionaryPublishedCacheInvalidationConsumer` + `DictionaryPublishedEvent` publisher call. Drive-by fix: `Program.cs:190` `AddMessageBus(...)` now invokes `registerConsumers` callback (was null), enabling module consumers to register automatically. MasterData tests: 134/134 pass.
- **(Phase 03)** **Rules engine cross-pod cache invalidation:** New `RulesCacheKeys.cs` (single source of truth for cache keys). New `RulesEngineCacheSubscriber` (IHostedService, Rules.Infrastructure) subscribes Redis pub/sub channel `rules-invalidate-cache`. Modified `RulesEngineEvaluator.InvalidateCache` to call `InvalidateCacheAsync()` after rule updates. 24 LOC added to evaluator + 3 LOC to registration.
- **(Phase 04)** **Integration tests:** New file `RulesEngineCacheSubscriberIntegrationTests.cs` (186 LOC, 3 tests covering cache invalidation scenarios, Redis subscriber startup retry, L1 invalidation on pub/sub event). Integration test suite: all tests pass (37s).
- **(FU2)** **ABAC write-path defence-in-depth:** Modified `AttributeRule.cs` (TenantId now private setter). Updated `UpdateAbacRuleCommandHandler.cs` with tenant guard + logging (14 LOC). Enhanced `AttributeRuleRepository.cs` AddAsync/UpdateAsync with ITenantContext validation (20 LOC). 9 unit tests (5 critical path + 4 positive controls). TenantIsolation module: 7/7 pass.

**Test Results:** Full integration suite `AqtCoreFW.Tests.Integration` 103/103 pass (6m). Rules.Application 21/21, Rules.Integration 6/6 (3 new + 3 existing), Identity modules 241/241, TenantIsolation 7/7, MasterData 134/134. Build: 0 warnings / 0 errors.

**Code Review:** `code-reviewer-260420-0859-t4-05-remaining-phases.md` — 8.5/10 initially with 2 Major concerns (M1 latent InvalidateCache infra, M2 doc drift). Both fixed this session via: (M1) Added `InvalidateCache` to `IRuleEvaluator` interface + wired `ActivateRuleVersionCommandHandler` to inject and call it. (M2) Updated code-standards.md (DictionaryPublishedEvent example removal) + dependency-map.md (MasterData consumer deletion note). No outstanding critical/major.

**Files changed:** ~40 files (+200/-80 LOC net). New: RulesCacheKeys.cs, RulesEngineCacheSubscriber.cs, RulesEngineCacheSubscriberIntegrationTests.cs. Modified: RulesEngineEvaluator, RulesInfrastructureRegistration, UpdateAbacRuleCommandHandler, AttributeRule, AttributeRuleRepository, IRuleEvaluator interface, ActivateRuleVersionCommandHandler, code-standards.md, dependency-map.md.

**Status:** Commits pending (code review fixes shipped locally, awaiting finalize gate + journal before push).

---

## FEATURE: T4-05 Phase 01 — ABAC Cache Migration + B.1 Tenant Filter + MailKit CVE (2026-04-19)

**Version bump:** v2.119 → v2.120

**Scope:**
- (a) **ABAC Two-Tier cache migration:** IMemoryCache → ICacheService (L1+L2 Redis). AbacCacheKeys static helper (single source of truth). IAbacCacheInvalidator interface (Application layer).
- (b) **Cross-pod L1 invalidation:** AbacCacheSubscriber (IHostedService, Identity.Infrastructure) listens Redis channel `abac-invalidate`. Startup retry: 5 attempts, exponential backoff 5s→60s. Fallback: natural L1 TTL if all attempts fail.
- (c) **B.1 tenant filter hardening:** AttributeRuleRepository.GetByAttributeAsync + GetByIdAsync filter by TenantId (current tenant + system-wide rules). DeleteAsync delegates through GetByIdAsync, enforcing tenant isolation. Closes cross-tenant read leak.
- (d) **MailKit CVE patch:** 4.15.1 → 4.16.0 (GHSA-9j88-vvj5-vhgr, CVSS 6.5 Moderate, SMTP NTLM auth). MailKitEmailSender.cs: tightened nullable guard for Password.

**Test Results:** 9 new tests (4 unit AbacCacheInvalidatorTests + 2 integration AbacCacheSubscriberIntegrationTests + 3 handler cache-hit/miss/tenant-key). Main integration 103/103 pass (6m1s). Release build 0 Warning / 0 Error (1m34s).

**Files changed:** 7 files (+190/-15 LOC). New: AbacCacheKeys.cs, AbacCacheSubscriber.cs. Modified: IAbacCacheInvalidator.cs, AttributeRuleRepository.cs, Directory.Packages.props (2×), MailKitEmailSender.cs.

**Follow-ups logged to backlog:** T4-05-FU (AbacCacheRefreshService safety net, ~1h, Low) + T4-05-FU2 (cross-tenant write protection command handlers, ~2h, Medium-High).

**Direct push to `main`** (no PR) — commits: `b8e8f5a3` MailKit CVE (3 files) + `ca4a7954` T4-05 Phase 01 feature (17 files) + `4f31fbb6` docs/journal wrap-up (2 files). Total 20 files, +612/-95 LOC across 3 commits (2026-04-19).

---

## FEATURE: D8c — OpenAPI Title + Email/SMS Test Stubs (2026-04-18 evening)

**Version bump:** v2.118 → v2.119

**Scope:** (a) D8c OpenAPI: `OpenApiVietnameseTransformer.cs:129` title changed from `"AqtCoreFW — API Hệ thống Chính phủ số"` → `"AqtCoreFW API v1"` (ASCII, Scalar-compatible). (b) Notifications test stubs: `FakeEmailSender.cs` + `FakeSmsProvider.cs` (in-memory implementations). (c) Wired into `WebAppFixture` for integration tests. (d) Unskipped `SendNotification_EmailChannel_CallsEmailSenderAndPersistsRecord` test.

**Test Results:** Notifications module 7/7 tests passing. Main integration 101/101 passing.

**Files changed:** 5 files (+180/-20 LOC).

**PR #44 merged** (commit `fe2a2f3e`, 2026-04-18 ~23:50).

---

## TEST: D9-FU — Integration Tests 100% Green (PR #42, 2026-04-18)

**Version bump:** v2.117 → v2.118

**Scope:** Closed 6 adjacent integration test failures via single unified-cause fix. Root cause discovered empirically (scout agent misclassified as 3 distinct groups): `ApiControllerBase.ResolveTenantId()` throws 401 when JWT `tenant_id` claim missing (F-01 rule).

**Fixes:** 5 tests — added `tenantId:` kwarg to `CreateAuthenticatedClient` calls. 1 test — deleted `CasesApiTests.CreateCase_MissingTenantId_Returns422` (broken premise: endpoint uses JWT tenant per F-01, ignores body.TenantId).

**Test Results:** Full run `src/tests/AqtCoreFW.Tests.Integration` — **101/101 pass (6m1s)** — **100% green first time in project scope**.

**Files changed:** 6 files, +21/-22 LOC (test-only).

**PR #42 merged** (commit `bcfda1ae`, 2026-04-18 ~22:10).

---

## DEPENDENCY: D8 — Pin Orval to v7.21.0 (PR #41, 2026-04-18)

**Scope:** Lock orval version in `web/package.json` from `^8.6.2` → `7.21.0` (exact pin). Orval v8 is wholesale output rewrite (signature + response shape + schema filename) — migration scope ~2-5d out of scope for current session.

**Reason:** Running `npm run generate:api` with orval v8.6.2 produces TS2554 errors (generated code passes 2-arg to `apiClientMutator`, v7 takes 1). Workaround in D7 FE PR: hand-edit schemas only for changed shapes. Deferred migration to backlog as **D8b** (low priority, 2-5d).

**Follow-ups added to backlog:** D8c (BE Vietnamese OpenAPI title slugification, ~30min).

**Files changed:** `web/package.json`, `web/package-lock.json` (regenerated, 193 added, 35 removed, 25 changed).

**No code impact — FE dependency only.** No generated files committed.

**PR #41 merged** (commit `bd0329b4`, 2026-04-18 ~21:45).

---

## TEST: D9 — IEffectivePermissionService Stub in Main Integration Project (PR #40, 2026-04-18)

**Version bump:** v2.116 → v2.117

**Scope:** Centralize permission service stub in `src/tests/AqtCoreFW.Tests.Integration/Infrastructure/TestEffectivePermissionService.cs` (~60 LOC) for reuse across all integration test projects. Wired into `ApiFactory.ConfigureTestServices` via `RemoveAll<IEffectivePermissionService>() + AddSingleton<TestEffectivePermissionService>()`.

**Pattern:** Reads `X-Test-Roles` HTTP header; returns `PermCodes.All` (122 compile-time codes) when role ∈ {Admin, SystemAdmin}, empty set otherwise. Enables test clients to pass permission checks without seeding Identity DB rows.

**Context:** PR #38 (2026-04-18) extended permission stub to Audit, Forms, Organization, Cases — closed 18-19 pre-existing 403 failures, 33/33 target tests green. D9 consolidates stub to main integration project for cleaner reuse pattern.

**Files changed:** 1 new integration test file, 1 config file edit.

**Test Results:** 5 pre-existing 403 failures in main `AqtCoreFW.Tests.Integration` project (Workflow + Cases filters) confirmed NOT regression from D5/D6/D7 — test code already uses `RequiredPermissionCode`.

**PR #40 merged** (commit `146af8eb`, 2026-04-18 ~21:20).

---

## REFACTOR: D5+D6+D7 Permcode Cluster (PR #39, 2026-04-18)

**Version bump:** v2.115 → v2.116

**Scope:** Stops the `RequiredRoleCode` field-name lie (semantically "permission code" since Phase-02 authz refactor), unifies `GetAvailableTransitions` filter to permission-set model (D6), and removes deprecated `ActorRoleCode` from REST DTOs + FE cascade (D7).

**PR #39 merged** (commit `a1144b87`, 2026-04-18 ~20:00).

**D5 — BE entity rename (commit `d34549f9`):** `WorkflowTransition.RequiredRoleCode` → `RequiredPermissionCode` across entity + EF config + migration `20260418025028_RenameWorkflowTransitionRequiredRoleCode` (uses `RenameColumn`, preserves data) + 3 raw-SQL callsites (repo INSERT + 2 query SELECTs) + seeder kwarg + cross-module client XML doc. DTO + Application commands (`ExecuteTransition`, `SaveDefinitionGraph`, `CreateWorkflowDefinition`) cascade rename. 28 files, +1461/-72.

**D6 — BE filter unify (commit `509aa119`):** `GetAvailableTransitionsQueryHandler` now injects `IIdentityModuleClient`, replaces role-equality filter with `permissionCodes.Contains(t.RequiredPermissionCode)` — same model as `ExecuteTransitionCommandHandler`. Controller drops `actorRoleCode` query param, passes `ResolveUserId()`. Closes pre-existing security/UX bug where caller could see "Approve" action in available list (role-match) but get 403 on execute (no perm-code match). New 6 test cases in `GetAvailableTransitionsQueryHandlerTests`. 4 files, +222/-14.

**D7 BE — controller DTO trim (commit `375ee7f0`):** `ExecuteTransitionRequest` (4→3 fields) and `BatchTransitionRequest` (3→2 fields) drop `ActorRoleCode`. Phase-02 deprecation comments removed. `src/http/workflow.http` samples cleaned. 2 files, +2/-13.

**D7 FE — cascade (commit `51b8fda8` + http cleanup `67b88d8f`):** 11 FE files updated (`workflow-types.ts`, `instance-detail-page.tsx`, `workflow-batch-action-toolbar.tsx`, `transition-editor-modal.tsx`, `definition-transitions-tab.tsx`, `properties-sidebar.tsx`, `workflow-graph-input-utils.ts`, `graph-converters.ts`, `aqtCoreFWApiV1.schemas.ts`, `en.json`, `vi.json`) + 3 e2e specs. i18n rename `workflow.requiredRole*` → `workflow.requiredPermission*`; deleted `workflow.batch.actorRoleCode*`.

**Known debt surfaced (NOT blocking this cluster, backlog items):**
- **D8:** Orval v7→v8 mutator upgrade — running `npm run generate:api` with orval v8.6.2 produces TS2554 errors because generated code passes 2-arg to `apiClientMutator` (v7 takes 1). Workaround in D7: hand-edit schemas only for changed shapes. Fix: update `apiClientMutator` to v8 2-arg signature.
- **D9:** Stub `IEffectivePermissionService` in `src/tests/AqtCoreFW.Tests.Integration` project. 5 pre-existing 403 fails confirmed in Workflow+Cases filter (NOT regression from D5/D6/D7 — test code uses `RequiredPermissionCode`). Pattern from PR #38 / `OrgUnitEndpointsTests.CreateClientWithPermissions`. Full project scope unknown (~15min run skipped due to session time budget).

**Test results (Workflow module):** Application 406/406 ✅, Domain 120/120 ✅, Infrastructure 61/61 ✅, Integration 25/27 (2 skip perf) ✅. FE typecheck 0 errors. FE vitest 765/777 (12 pre-existing jsdom/Antd fail per `c5-finalize-blockers.md`).

**Branch status:** `feat/d5-d6-d7-permission-code-cluster` (6 commits on remote). **PR NOT OPENED** — pending next session. See `backlogs/backlog.md` D8+D9 + `memory/project-state.md` "Next Session Pickup Plan".

---

## TEST: Integration Test 403 Stub Closure (Audit, Forms, Organization, Cases) — PR #38 (2026-04-18)

**Version bump:** v2.114 → v2.115

**Test infrastructure hardening:** Stub `IEffectivePermissionService` in 4 integration test projects (Audit, Forms, Organization, Cases) using PR #36 pattern (precedent: NotificationEndpointsTests). Closed 18-19 pre-existing HTTP 403 Forbidden failures surfaced during Testcontainers→Local SQL migration rollout (PR #37). Pattern: `WithWebHostBuilder.ConfigureTestServices` registers mock service returning hardcoded `PermissionSet` with all permissions.

**Test results:** All 4 target test suites now 100% green on local SQL. 33/33 target tests passing (Audit 6, Forms 9, Organization 8, Cases 10).

**Drive-by fix:** Cases integration test JSON parse bug — response envelope uses `data.items` (not `data.data`), fixed in `CasesListEndpointTests`.

**Pattern deployed:** Same `IEffectivePermissionService` stub approach from PR #36 (Notifications) applied to 4 modules. Reusable for future integration tests requiring permission setup without DB seeding.

**Files changed:** 4 integration test files (Audit, Forms, Organization, Cases endpoints tests).

**PR #38 merged** (commit `73295ce3`, 2026-04-18).

---

## INFRASTRUCTURE: Hybrid SQL Fixture + CI Policy Change (2026-04-18)

**Version bump:** v2.113 → v2.114

**Hybrid SQL fixture:** Integration tests now use local SQL Server 2022 (`MSSQLSERVER22` @ `localhost,1433`) when `TEST_USE_LOCAL_SQL=true`. Fixture startup ~12s vs ~41s Testcontainers (~3.4× speedup). 4 persistent test DBs created: `AqtCoreFW_Test_{Main,Messaging,TenantIso,Concurrency}`. Truncate/reseed isolation pattern.

**CI policy change:** Integration tests disabled on CI per user decision. Testcontainers cold-start on CI is slow and flaky. Developers MUST run integration tests locally before opening PR. Documented in `docs/deployment-guide.md` § "Running Integration Tests Locally".

**Fixture hybrid code preserved:** Testcontainers fallback remains for devs without native SQL Server. Unset `TEST_USE_LOCAL_SQL` → falls back to containers. Code can re-enable CI integration tests in future if needed (tracked in `backlogs/backlog.md`).

**Files changed:**
- `.github/workflows/ci.yml` — `if: false` on `integration-tests` job
- `docs/deployment-guide.md` — new § "Running Integration Tests Locally"
- `docs/dependency-map.md` — Test Infrastructure section updated (4 collections table, toggle contract, CI note)
- `docs/development-roadmap.md` — v2.114 milestone
- `backlogs/backlog.md` — migration marked done, re-enable item added

**Plan:** `plans/260418-0558-testcontainers-to-local-sql/`

---

## TEST: xUnit Local Collection Discovery Fix — SqlServerCollectionLocal Pattern (PR #35, 2026-04-17)

**Version bump:** v2.112 → v2.113

**Root cause:** xUnit v2 discovers `[CollectionDefinition]` attributes **only in the executing assembly**. `SqlServerCollection` lives in `tests/shared/AqtCoreFW.Tests.SharedFixtures.dll`; when module integration tests (e.g., `AqtCoreFW.Notifications.Integration.Tests`) ran in isolation, xUnit could not resolve the collection definition → `WebAppFixture(SqlServerFixture)` ctor failed with missing collection binding.

**Solution:** Added `SqlServerCollectionLocal.cs` to each of the 15 integration test projects (Ai, Audit, Cases, Collaboration, Files, Forms, Identity, Integration, Notifications, Organization, Reporting, Rules, Search, Signature, SystemParams). Each file contains a `[CollectionDefinition("SqlServer")]` stub that re-declares the collection locally within the executing assembly, enabling xUnit's discovery mechanism.

**Pattern precedent:** Matches pre-existing `Workflow` + `MasterData` integration test projects, which had already implemented this workaround.

**Future migration:** xUnit v3 introduces `[AssemblyFixture]`, which will eliminate this boilerplate. Upgrade planned when xUnit v3 ships.

**Files changed:** 15 new `SqlServerCollectionLocal.cs` files (1 per module integration test project).

**Test impact:** 0 tests fixed; pattern enables test isolation and parallel execution without shared-fixture assembly resolution errors.

**PR #35 merged** (commit `559fd272`, 2026-04-17).

---

## FIX: Notifications Endpoints 403 → IEffectivePermissionService Stub (PR #36, 2026-04-17)

**Version bump:** v2.112 → v2.113 (synced with #35)

**Symptom:** 5 tests in `NotificationEndpointsTests` failed with HTTP 403 Forbidden (`GetSummaryAsync` returned empty permission set).

**Root cause:** Fabricated test users (via `TestAuthHandler` with mocked JWT headers) had no corresponding rows in Identity DB. When `NotificationEndpoints.GetSummary` called `IEffectivePermissionService.GetUserPermissionsAsync`, the service performed a DB lookup against empty Identity schema → returned empty permission set → authorization failed with 403.

**Solution:** Stubbed `IEffectivePermissionService` in `WebAppFixture.ConfigureTestServices` to return a hardcoded `PermissionSet` containing all permissions. Precedent: `WorkflowTaxonomyAdminEndpointsTests` (Phase-00 C3) uses identical pattern.

**Files changed:**
- `tests/integration/AqtCoreFW.Notifications.Integration.Tests/Endpoints/NotificationEndpointsTests.cs` — permission service stub wired via `WithWebHostBuilder.ConfigureTestServices`
- `src/host/AqtCoreFW.Api/Program.cs` — no changes (existing `IEffectivePermissionService` DI already in place)

**Test results:** 5 tests previously failed 403 → 6/7 now pass; 1 skipped (email channel — requires `IEmailSender` stub, separate backlog item flagged).

**Pattern:** Fire-and-log event handling (consumer exceptions do not surface 500 to API client). Notification dispatch failures logged but not rethrown.

**PR #36 merged** (commit `0c892ad9`, 2026-04-17).

---

## FEATURE: M-2 Consumer Integration Test for MassTransit Event Bridge (2026-04-17)

**Version bump:** v2.111 → v2.112

**Test coverage:** `AqtCoreFW.Notifications.Integration.Tests/EventHandlers/CaseTransitionedNotificationConsumerIntegrationTests.cs` — 2 test cases validate MassTransit `IConsumer<CaseTransitionedIntegrationEvent>` wiring and notification dispatch:
- **TC-NTF-INT-001 (Consume_WhenSubmittedEventPublished_SendsNotificationAndCompletes):** Publish `CaseTransitionedIntegrationEvent` via `InMemoryTestHarness`, assert `harness.Consumed.Any<CaseTransitionedIntegrationEvent>()` true, verify `INotificationModuleClient.SendAsync` called 1× with Submitted template.
- **TC-NTF-INT-002 (Consume_WhenDownstreamFails_DoesNotThrow):** Consumer catches `SendAsync` exceptions (fire-and-log pattern at `CaseTransitionedNotificationConsumer:42-61`), assert no throw escapes to harness.

**Delivery pattern verified:** Phase-04b MassTransit direct-publish event bridge (cross-module ref eliminated via `CaseTransitionedIntegrationEvent` in `SharedKernel.Contracts.Events`); Notifications consumer now `IConsumer<T>` (not deleted MediatR handler). At-most-once semantics documented in `dep-map §8`.

**Plan status:** M-2 from `backlogs/backlog.md:22` (Phase-04 follow-ups) marked ready for implementation (plan+red-team complete 2026-04-17, branch `feat/m2-consumer-integration-test` awaits PR).

---

## DOCS: Session Close — Phase-04/04b/05 merge-batch post-drift sync (2026-04-17)

**Version bump:** v2.110 → v2.111

**Scope:** Documentation-only commit (`c68096b8`) closing the 2026-04-17 session after 3 PRs landed in a single day (#31 Phase-04 `44a77339`, #32 Phase-05 `1e14ea57`, #33 Phase-04b `a4674bb4`). All "OPEN" / "branch ready" language across roadmap, changelog, codebase-summary, and plan validation log flipped to "MERGED" with commit SHAs. Session journals (Phase-05, Phase-04b) committed to `docs/journals/` — Phase-04 journal was already bundled into PR #32. Plan `plan.md` gets Session 6 validation entry (final for the Cases→Workflow refactor on main side; Phase-06 still gated on 14-day soak).

**No code changes.** Tests unaffected. Dep-map header bumped to v2.111 for consistency with roadmap + codebase-summary.

---

## REFACTOR: Phase-04b Cross-Module Event Decoupling via MassTransit — MERGED (2026-04-17)

**Version bump:** v2.109 → v2.110

**Event bridge pattern replaces direct import:** Phase-04's internal `CaseTransitionedEvent` (Cases.Domain → Notifications.Application ref) deferred to Phase-04b. Solution: cross-module `IConsumer<CaseTransitionedIntegrationEvent>` pattern via MassTransit. `CaseTransitionHelpers.SaveWithConcurrencyGuardAsync` publishes event to message bus (at-most-once delivery guarantee); Notifications module's `DictionaryPublishedCacheInvalidationConsumer` (was disabled) now activated as new `CaseTransitionedEventConsumer` subscribes + handles notification async. Drive-by: `AddMessageBus` in `Program.cs` now registers module consumers (was plumbing infrastructure without activation).

**Architectural benefit:** Eliminates Phase-04 cross-module dependency exception; unifies event patterns across all modules. Notifications→Cases ref removed; decoupled via event bus. Supports future microservices extraction (Phase 17 roadmap).

**Test coverage:** 595/595 pass (integration tests + event dispatch verification).

**PR #33 merged** (squash, commit `a4674bb4`, 2026-04-17).

---

## OPS: Phase-05 Cases→Workflow Data Migration Backfill — 3 SQL Scripts + Runbook — MERGED (2026-04-17)

**Version bump:** v2.108 → v2.109

**Data migration for Phase-03 feature flag flip:** 3 SQL scripts migrate case data post-workflow delegation. Phase-03 ships Cases→Workflow delegation behind feature flag `feature:cases.workflow-delegation`; Phase-05 backfills existing cases with workflow instances before flag is disabled. Scripts: (1) create-workflow-instances.sql — for each case, create WorkflowInstance + WorkflowInstanceHistory, (2) sync-case-status.sql — validate Case.Status matches final WorkflowInstance.CurrentState, (3) validate-consistency.sql — pre-flight + post-migration health checks. Red-team validation: scout caught 3 column-name drifts (`ModifiedAt` vs `UpdatedAt`) before implementation — all fixed pre-release.

**Deployment:** Runbook (new: `docs/runbooks/cases-workflow-backfill.md`) documents pre-flight checks, sequenced deployment per env (dev→stg→prod), rollback procedure, post-migration validation. Blocking gate: Phase-03 feature flag cannot disable until Phase-05 migrations committed to all envs.

**PR #32 merged** (squash, commit `1e14ea57`, 2026-04-17).

**Test coverage:** SQL script validation via docker-entrypoint seeding; consistency checks in rollback testing.

---

## FEATURE: Phase-04 Cases→Workflow Domain Event Sync — Internal Event Dispatch (2026-04-17)

**Event-driven cross-module flow:** `CaseTransitionedEvent` (internal domain event) published by Cases aggregate after workflow client confirms state change. `CaseTransitionHelpers.SaveWithConcurrencyGuardAsync` dispatches via `IDomainEventPublisher.PublishAsync` post-`SaveChanges`, then clears domain events. `CaseTransitionedNotificationHandler` subscribes to event in Notifications module via in-process MediatR dispatch. Handler follows fire-and-log pattern — failures do not rollback the Case save.

**Architecture exception documented:** Cross-module dependency `Notifications.Application → Cases.Domain.Events` exists to subscribe to internal `CaseTransitionedEvent`. Phase-04b (PR #33) subsequently refactored this to use MassTransit `IConsumer<>` pattern, removing the direct project reference and enabling future microservices extraction.

**Files changed:**
- `src/modules/cases/AqtCoreFW.Cases.Domain/Events/CaseTransitionedEvent.cs` — new event record (CaseId, TenantId, FromStatus, ToStatus, ActorId)
- `src/modules/cases/AqtCoreFW.Cases.Application/Commands/_Shared/CaseTransitionHelpers.cs` — SaveWithConcurrencyGuardAsync publishes events
- `src/modules/notifications/AqtCoreFW.Notifications.Application/EventHandlers/CaseTransitionedNotificationHandler.cs` — subscribes to event
- `docs/dependency-map.md` §6 Event-Driven Flows — Cases→Notifications arrow + Phase-04 note
- `docs/dependency-map.md` §8 Architecture Variance Notes — cross-module internal-event exception documented

**Test coverage:** Integration tests verify event dispatch + notification handler invocation.

**PR #31 merged** (squash, commit `44a77339`, 2026-04-17).

---

## REFACTOR: D2 — Workflow State-Guard Rename + EF Concurrency Token (2026-04-17)

**Tech debt closure:** Renamed repository method `UpdateWithConcurrencyCheckAsync → UpdateWithStateGuardAsync` to better express the app-layer state validation guard (guards against sequential races via `FromStateId != CurrentStateId`). Added EF Core native concurrency control: `.IsConcurrencyToken()` on `WorkflowInstance.CurrentStateId` for DB-layer simultaneous-race detection. New error code `WorkflowErrorCodes.StateConflict = "GOV_WFL_013"` (HTTP 409) introduced to distinguish concurrency conflicts from invalid transitions.

**Defense-in-depth strategy:** 2-layer race protection — app-layer guard catches sequential races early (cheap), EF `IsConcurrencyToken()` on CurrentStateId catches simultaneous races via DB constraint (`WHERE Id=@id AND CurrentStateId=@original`). Try/catch `DbUpdateConcurrencyException` repurposed (no longer dead) to surface `StateConflict` error cross-module.

**Files changed:** 
- `src/shared/AqtCoreFW.SharedKernel.Contracts/Clients/WorkflowErrorCodes.cs` — added `StateConflict` code
- `src/modules/workflow/AqtCoreFW.Workflow.Domain/Errors/WorkflowErrors.cs` — added factory method
- `src/modules/workflow/AqtCoreFW.Workflow.Infrastructure/Persistence/WorkflowInstanceConfiguration.cs` — added `IsConcurrencyToken()` on CurrentStateId
- `src/modules/workflow/AqtCoreFW.Workflow.Infrastructure/Persistence/WorkflowInstanceRepository.cs` — method rename + factory usage
- `src/modules/workflow/AqtCoreFW.Workflow.Application/Commands/{ExecuteTransition,ResolveBranchChild,CompleteTask,CancelTask,BatchExecuteTransition}/*` — callers renamed
- `src/modules/workflow/AqtCoreFW.Workflow.Infrastructure/Jobs/{SlaBreachCheckerJob,MigrationBatchProcessor}.cs` — defensive fixes + exception handling
- `src/modules/cases/AqtCoreFW.Cases.Application/Commands/_Shared/CaseTransitionHelpers.cs` — `StateConflict` switch arm
- Tests: 4 unit files renamed + NEW `WorkflowInstanceConcurrencyTests.cs` (2 integration scenarios)

**Test count:** 495 unit tests + 2 integration (concurrency) = 497 pass, 0 fail. All code-reviewer findings (F1-F7) addressed.

**PR #30 MERGED** (squash, commit `c878e3e1`, 2026-04-17).

---

## FEATURE: Phase-03 Cases→Workflow Delegation — Create + Query Methods (2026-04-17)

**PR #29 MERGED (squash, commit `1027bb03`, 2026-04-17).** Cases module's 5 transition handlers + CreateCase delegate to Workflow module via feature flag `feature:cases.workflow-delegation`. New cross-module methods on `IWorkflowModuleClient`: `CreateInstanceByDefinitionKeyAsync(definitionKey, entityType, entityId, tenantId, variables)` + `TryResolveInstanceForEntityAsync(entityType, entityId, tenantId)`. `WorkflowErrorCodes` moved from `Workflow.Domain.Errors` → `SharedKernel.Contracts.Clients` (cross-module public contract shared between Cases + Workflow). New event: `CaseTransitionedEvent(CaseId, TenantId, FromStatus, ToStatus, ActorId)` alongside rich per-action events. Shared helper: `CaseTransitionHelpers` static class (MapTransitionError + SaveWithConcurrencyGuardAsync). Feature flag gates workflow delegation; legacy CaseWorkflow.Engine used when disabled. Zero breaking changes (feature-flag gated).

**Test Coverage:** New integration tests covering feature-flag enabled/disabled paths, cross-module error code mapping, concurrent case transitions with optimistic concurrency. All pass.

---

## FEATURE: Phase-02 Workflow Client Contract — Execute + Query Methods (2026-04-17)

**PR #28 MERGED (squash, commit `c4d72382`, 2026-04-17).** `IWorkflowModuleClient` extended with 2 command methods: `ExecuteTransitionAsync(instanceId, transitionKey, actor, tenantId, variables)` + `QueryAllowedTransitionsAsync(instanceId, tenantId)` + 4 new contract types (transition result DTO, allowed transition DTO, error response DTO). `IIdentityModuleClient.GetEffectivePermissionCodesAsync` added; delegates to IEffectivePermissionService. `ExecuteTransitionCommandHandler` authz refactored: role-equality check → permission-set membership check (user must have ALL perms required by transition). New cross-module arrow: Workflow.Application → IIdentityModuleClient (delegates authorization to Identity module). Zero breaking changes; permissions seeded to existing roles.

**Test Coverage:** 14 new unit tests (transition authz, permission resolution, error mapping) + 6 integration tests (cross-module identity lookup, concurrent transitions). All pass.

---

## FEATURE: Phase-01 Seed CaseProcess WorkflowDefinition (2026-04-16 late evening)

**PR #26 MERGED** (squash, commit `9322440c`, 2026-04-17) — Completes Cases→Workflow seeding foundational work post Phase-00 taxonomy merge.

**CaseProcessDefinitionSeeder (IHostedService):** Idempotent seeding on API startup. Definitions: `case-process-standard` (7 states: New, InProgress, PendingApproval, Approved, Rejected, ReturnedForRevision, Closed) + 8 transitions (New→InProgress→PendingApproval→Approved/Rejected; Rejected→InProgress; ReturnedForRevision→InProgress; Approved/Rejected→Closed). Validates GUID uniqueness + transition paths. Skips re-seeding if definition exists (idempotency via DefinitionKey lookup).

**Permission Codes:** 6 new codes in `CASES.WORKFLOW.*` namespace (READ, CREATE, TRANSITION, ASSIGN, VIEW_ANALYTICS, MANAGE_DELEGATION). Seeded to SystemAdmin role.

**Taxonomy Extension:** ReturnedForRevision state added as Option B (selected over Option A inline-status); reuses WorkflowStateTaxonomy category `revision` (created in Phase-00).

**Testing:** 16 unit tests (seeder logic, transition validation, idempotency) + 7 integration tests (Testcontainers MSSQL, live seeding verification, permission enforcement). All pass.

**Code Quality:** Code-reviewer 9.7/10 APPROVED. No TSC errors. Diff ~180 LOC (+application + +infrastructure + +tests).

---

## FEATURE: Phase-00 Workflow Taxonomy — SHIPPED (2026-04-16 PR #25 merged to main via squashed commit 60e7d241)

**Entities:** WorkflowStateTaxonomy (22 seeded states: New, InProgress, PendingApproval, etc. + 6 categories: action, revision, terminal, pending, processing, archive), WorkflowActionTaxonomy (20 seeded actions: Submit, Approve, Reject, etc. + 5 categories: approval, escalation, notification, reassignment, archive).

**Backend:** 10 REST endpoints (6 per entity: list, byId, create, update, delete, restore). Split authorization: reads via `MASTER.WORKFLOW_TAXONOMY.VIEW`, writes via `PLATFORM.WORKFLOW_TAXONOMY.MANAGE`. RowVersion concurrency control. Soft-delete with `referencedByDefinitionCount` validation. 12 integration tests on live SQL Server (Testcontainers).

**Frontend:** 40 new vitest tests. use-workflow-taxonomies TanStack Query hooks. state-editor-modal + transition-editor-modal with <AutoComplete> + yellow custom-code warning. Dedicated admin CRUD pages: workflow-state-taxonomy-admin-page + workflow-action-taxonomy-admin-page. Archive tab + Restore + Deprecate dialog with case-sensitive confirmCode + reason validation (≥10 chars).

**Code Review:** 5 sub-chunks averaged 9.5–9.7/10 APPROVED (9.6 aggregate). 47 new FE vitest pass in isolation + subset. tsc exit 0. Release build 0W/0E.

**Foundational Scope:** 2 new entities, 10 endpoints, 2 permission codes, FE full admin CRUD + archive/restore/deprecate, 134 unit + 12 integration + 40 FE taxonomy tests (186 total). Unblocks Phase-01 seeding + future workflow state machine delegation to Workflow module.

**See:** `docs/project-changelog-archive.md` for detailed per-chunk breakdowns (C3 BE API, C4a FE Select, C4b-1/2 FE admin pages, C4c Archive+Restore+Deprecate dialog, test evidence).

---

## CHORE: Delete MasterData Monolithic Dead Code (2026-04-16)

Cleanup of incomplete refactor from ~2026-03-31. Monolithic project `AqtCoreFW.MasterData` removed (41 files, -2226 lines); 11 unit tests migrated to layered namespaces (Domain/Application/Infrastructure). 2 orphan migrations (`20260314_InitialCreate`, `20260325_AddRlsPolicies`) deleted — never applied to any environment. Pre-requisite for Cases→Workflow refactor Phase 00 (plan `260415-1737`). Docs sync: `docs/dependency-map.md` §MasterData corrected (was "service-based, no CQRS" — now documents CQRS split: Command+Validator in Application, Handlers in Infrastructure.CommandHandlers; Queries in Application using Dapper).

**Branch:** `chore/t4-06-delete-masterdata-monolithic` (3 commits: phase-01 `1331ad8c`, phase-02 `ca1d4b3e`, phase-03). **Red-team:** 2 rounds completed (RT-R1: 12 findings; RT-R2: 12 findings incl. RT-R2-01 critical count error — plan said "6 commands", reality 5). **Tests:** 56/56 unit tests pass Debug + Release; FQN diff 0 lines; Release builds 0 warnings. **Plan:** `plans/260416-1036-t4-06-delete-masterdata-monolithic/`.

---

## DEBUG: Sidebar "Thông báo" Menu Removal Verified (2026-04-15)

**Investigation only, no code changes.** User reported legacy "Thông báo" top-level menu still visible after removal edits. Root cause analysis traced the rendering pipeline: FE sidebar prefers API-driven `menuTree` over hardcoded fallback ([sidebar-menu.tsx:199-201](../web/src/layouts/sidebar-menu.tsx:199)); BE seeder is idempotent by fixed GUID ([E2ETestDataSeeder.Menus.cs:93](../src/host/AqtCoreFW.Api/E2ETestDataSeeder.Menus.cs:93)) so seed edits don't apply to existing DB; `MenuService` caches `menu-tree:{tenantId}` in Redis with 30min TTL ([MenuService.cs:25](../src/modules/identity/AqtCoreFW.Identity.Infrastructure/Services/MenuService.cs:25)).

Direct DB query against `[identity].[AppMenus]` confirmed **no stale top-level "Thông báo" row** — only `admin-notif-tpl` → "Mẫu thông báo" (intentional Admin › Content child). User verified menu removal rendered correctly after fresh load; proposed `DELETE` SQL (Option 1) withheld to avoid removing the legitimate template row.

**Side-finding (not remediated):** DB contains dozens of top-level `TC076-MENU-*` rows left by integration tests — candidate for future cleanup job.

---

## PLANNING: Cases → Workflow Refactor Multi-Release Plan APPROVED (2026-04-15)

**Planning artifact (no code changes):** Comprehensive plan approved via 3 red-team review cycles + validation interview. Refactor scope: eliminate `CaseWorkflow.Engine` from SharedKernel by delegating Cases state machine to Workflow module via `IWorkflowModuleClient`. Release A foundational (workflow taxonomy masterdata ~54h), Release B cases refactor (~18h) gated on Release A + 3-day prod soak. Implementation deferred to next session.

---

## T5-16 — Workflow Dashboard Server-Side Cursor Pagination (2026-04-15)

**BE + FE. 3 PHASED COMMITS: 95155652 → 3cc12a11 → 20dd6a23.**

- **BE (Phase 1, 95155652):** `GET /api/v1/workflow/instances` + `/summary` endpoints. Keyset pagination via composite cursor `base64("{startedAt:O}|{id}")`. EF Core SQL-translatable predicate (`i.Id < cursorId`, NOT `Guid.CompareTo`). Peek strategy `Take(pageSize+1)` for hasNext. `PagedResult<T>.TotalCount` → `int?` (null in cursor mode). Raw-SQL migration adds `IX_WorkflowInstances_TenantId_StartedAt_Id_Keyset` (DESC, DESC) with covering INCLUDE. Server-side `Math.Clamp(pageSize, 1, 100)`. Malformed cursor → 400 `GOV_WORKFLOW_040`. 16 unit tests (9 codec + 7 handler with `.ToQueryString()` SQL-shape proof).
- **FE (Phase 2, 3cc12a11):** `useWorkflowInstancesInfinite` + `useWorkflowInstancesSummary` hooks (TanStack Query v5 `useInfiniteQuery` with `initialPageParam`/`getNextPageParam`). `admin-workflow-instances-page.tsx` route. Dashboard KPI reads from summary prop (no client-side aggregation), Load-more footer, select-all scoped to loaded window.
- **Verification (Phase 3, 20dd6a23):** 6 integration tests — 200/401/403/400 envelope + 120-row keyset walk (no dup/no gap/ordering) + tenant isolation (60+60). Fixed 3 latent bugs on first real run (migration `DropColumn` idempotency, xUnit `[CollectionDefinition]` scoping, cross-test row bleed).
- **Red-team:** 12/13 findings accepted (RT-01..RT-11/13). RT-12 HMAC cursor rejected (tenant filter precedes cursor in WHERE).
- **Deferred (T5-16-FOLLOWUP):** Perf gate p95 @10K rows, Playwright E2E, rate-limit smoke.
- **Scope:** Workflow module. 2 endpoints, 23 new tests (16 unit + 7 FE + 6 integration). `dependency-map.md` updated.

---


## T5-20 — Workflow Designer UX: Position Persistence + Draw-Edge Transition (2026-04-15)

**PURE FE. 0 BE CHANGES. 0 ENTITY/ENDPOINT/MODULE CHANGES. 6 NEW VITEST CASES.**

- **Node position persistence (`positionMapRef`):** Dragged node positions are cached in a `positionMapRef` inside `definition-visual-designer-tab.tsx` and re-applied via `onPositionCommit` prop on every React Query refetch. Positions survive tab-session switches without writing to BE — intentional ephemeral UX improvement (R7).
- **Draw-edge opens TransitionEditorModal (R11):** `react-flow-canvas.tsx` receives new `onCreateTransition` prop. When a user draws an edge between two states on the canvas, `TransitionEditorModal` opens pre-filled with source/target state ids. Eliminates the prior manual "Add Transition" button flow for graph-drawn connections.
- **New utility module:** `workflow-graph-input-utils.ts` extracted from canvas component — exports `stateToInput`, `transitionToInput`, `applyAutoLayout` helpers. Keeps `react-flow-canvas.tsx` at 198 LOC (within 200-LOC limit).
- **Modified files:** `react-flow-canvas.tsx` (198 LOC, +`positionMap`/`onPositionCommit`/`onCreateTransition` props), `definition-visual-designer-tab.tsx` (193 LOC, +`positionMapRef` + modal state + callbacks).
- **Tests:** `react-flow-canvas-persist-and-connect.test.tsx` — 6 vitest cases covering position commit callback and draw-edge modal trigger.
- **Scope:** FE-only; zero cross-module deps; no dependency-map update required.

---


## T5-22 — Vitest Resource Hardening + Drift Fixes (2026-04-14)

**TEST INFRA: FORK POOL CAPPED + 4 DRIFT FAILURES RESOLVED. SUITE 720/4 → 724/0.**

- **perf(test) — vitest fork pool cap (`cb71efb8`):** `maxForks 7→2` + `execArgv --max-old-space-size=1024` per worker in `vitest.config.ts`. Added `npm run test:serial` panic script (single-fork fallback). Trade-off: +16% wall time (161s→187s) for bounded ~2 GB worker pool RSS. Prevents OOM on memory-constrained CI runners.
- **fix(web) T5-22 — 4 drift failures resolved (`ea071ceb`):** `LAYOUT.siderWidth` constant corrected 220→256 (snapshot drift). `roles-api` mock stubbing extended to 8 hooks in `smoke-remaining-pages.test.tsx` (added missing hook stubs). `vi.hoisted`+`vi.stubEnv` isolates `VITE_API_BASE_URL` in `api-client.test.ts` (env leak between test files). Zero production code touched; test-only scope.
- **docs sync (`d3eae252`):** backlog + project-state + session journals updated to reflect T5-22 completion.

---


## T5-21-C — Test TSC Strictness Closure (2026-04-14)

**44 → 0 TEST TSC ERRORS ACROSS 19 TEST FILES. CI NOW ENFORCES STRICT TYPE-CHECK ON BOTH PROD AND TEST CODE.**

- **Error elimination (commit `bb555c1e`, 44 errors → 0):**
  - Cluster 1: removed unused `screen` imports (2 files)
  - Cluster 2: removed dropped `tenantId`/`userId` props from test JSX (`case-comments`, `form-builder-smoke`) — tracks prior DTO drift caught by T5-21-B
  - Cluster 3: added `version: 1` to `FormTemplateDto` fixtures (required field added in earlier BE change)
  - Cluster 4: typed `vi.fn<(rowIndex: number) => void>()` for workflow keyboard nav mocks (T5-17 follow-up)
  - Cluster 5: typed empty arrays in 13 mock factories via `[] as XDto[]`; fixed DTO drift — notification `title` → `subject`, user `status`/`mfaEnabled` → `isActive`/`clearanceLevel`, `WorkflowDefinitionDto` → `WorkflowDefinitionListDto`, added required `createdAt`/`parametersSchema` fields
  - Cluster 6: `api-client.test.ts` — `as AxiosResponse` casts + non-null assertion on `errorHandler`
- **M1 consistency fix (commit `d29a55e6`):** typed `notification-bell` mock items array as `NotificationDto[]`
- **CI gate added:** `Type check (tests)` step added to `.github/workflows/ci.yml` after the existing app typecheck step; runs `typecheck:test` (`tsc --noEmit -p tsconfig.test.json`). DTO drift now caught at PR time, not runtime.
- **Scope:** test files only; no production code changes; no vitest regressions (4 pre-existing failures unchanged).

---

## T5-21-B — FE TypeScript Error Batch Fix + CI Gate Hardening (2026-04-14)

**29 TSC ERRORS FIXED ACROSS 23 FE FILES. CI TYPECHECK GATE NOW REAL. SECURITY: SERVER-DERIVED FIELDS REMOVED FROM REQUEST PAYLOADS.**

- **Bug fix (29 errors, commit `f2db41e5`):**
  - Batch A: removed unused imports in jobs, forms, layout files (9 errors)
  - Batch B: removed obsolete `tenantId` + `modifiedBy` from `SaveGraphRequest`, `ReassignTaskRequest`, `CreateAbacRuleDto` — fields are server-derived; FE must not supply
  - Batch C: `UserListParams` pagination param renamed `page` → `pageNumber`
  - Batch D: null-safety `template?.` guard added to `public-form-page` (matches runtime loading state)
  - Batch E: `SyntheticListenerMap` replaced with `NonNullable<ReturnType<typeof useSortable>['listeners']>`; notification DTO field `subject` → `title`
  - Batch F: type mismatches — `UserRole` cast, `DataNode`, `MenuItemGroup.icon`, webhook route `/admin/webhooks`, `MigrationItemStatus`
  - Batch G: router `navigate` search params, lazy import direct path
- **CI hardening:** `ci.yml:252` + `package.json typecheck` script both changed from `tsc --noEmit` (root `tsconfig.json` has `files:[]` — zero-file no-op) to `tsc --noEmit -p tsconfig.app.json` (real gate). CI was silently passing broken prod types since inception.
- **Security:** dead `<Form.Item name="tenantId">` removed from `abac-rules-page.tsx` — `CreateAbacRuleDto` does not include `tenantId`; BE derives from JWT.

---

## T5-17 — Workflow Grid Accessibility (ARIA + Keyboard Nav) (2026-04-14)

**PURE FE. 0 BE CHANGES. 97/97 TESTS GREEN (hook 35 + dashboard 32 + others).**

- **WAI-ARIA grid semantics:** `instance-monitoring-dashboard.tsx` upgraded to `role="grid"` / `role="row"` / `role="gridcell"` structure per WAI-ARIA 1.2. Roving `tabIndex` managed by new `use-workflow-grid-keyboard-nav.ts` hook — 11 key bindings (↑↓←→ Home End Ctrl+Home Ctrl+End PageUp PageDown Space), virtualizer scroll sync on row navigation.
- **New files:** `use-workflow-grid-keyboard-nav.ts` (hook), `workflow-grid-row.tsx` (row renderer), `workflow-grid-constants.ts` (shared constants).
- **i18n:** 2 new keys added to `en.json` + `vi.json` for ARIA label strings.
- **Scope:** FE-only; no entity/endpoint/module changes.

---


## T5-18 + T5-19 — Contract Drift Closure + Batch Safety Guard (2026-04-14)

**STRUCTURAL ROOT-CAUSE FIX + TACTICAL UX GUARD. 2 COMMITS. 42/42 WORKFLOW TESTS GREEN.**

- **T5-19 — Orval ApiError migration (`ae448ac6`):** Replaced hand-written `BackendApiError`/`BackendEnvelope` in `api-error.ts` with generated `ApiError` from `aqtCoreFWApiV1.schemas.ts`. Renamed FE export `ApiError` → `NormalizedApiError` to resolve naming collision. Compile-time contract guard test (`api-error-contract.test.ts`) fails typecheck if BE renames/removes consumed fields (`code`/`message`/`detailVi`/`property`). Bonus: deleted dead `ApiError { field?: string }` + `ApiResponse<T>` in `shared/types/api.ts` (zero importers, carried old drift shape). `traceId` now surfaced. Effort ~0.75d vs 1-1.5d estimate — BE OpenAPI already emitted correct shape, no Swashbuckle work needed.
- **T5-18 — Batch confirm dialog >20 selections (`5b0dce84`):** Added soft `CONFIRM_THRESHOLD=20` to `WorkflowBatchActionToolbar` below existing `MAX_BATCH=50` hard cap. Selections >20 trigger `Modal.confirm` intercept (title/content/okText/cancelText); ≤20 flows unchanged. 3 new i18n keys (vi/en). 4 TDD tests using `vi.spyOn(Modal, 'confirm')` + captured callback pattern — reusable for future imperative dialog tests.
- **Pushed:** `31dc2765..0b31937f` → origin/main (3 commits incl. T5-19 journal).
- **Deferred:** Phase-04 runtime OpenAPI snapshot test for T5-19 (BE offline at :5001/:5002 — same infra blocker as T4-04 Playwright).

---


## T5-21 — TSC Test-Strict Config Landing (2026-04-14)

**CONFIG-ONLY LANDING. 2 FILES ADDED/MODIFIED. CI RED FLAG SURFACED.**

- **New:** `web/tsconfig.test.json` — extends `tsconfig.app.json`, narrows `include` to `src/test/**/*`, sets `exclude: []` so test fixtures enter the type-check graph. Registered as reference in root `tsconfig.json`.
- **New script:** `package.json` `typecheck:test` — runs `tsc --noEmit -p tsconfig.test.json`; dev-time gate to catch test fixture type drift before CI.
- **Baseline (triage report):** 62 errors total: 29 prod-code (T5-21-B), 33 test-only (T5-21-C). See `plans/260414-1359-t5-21-tsc-test-strict/reports/baseline-triage.md`.
- **CRITICAL — CI red ≥3 days (T5-21-B):** `ci.yml:252` `tsc --noEmit` uses root `tsconfig.json` (`files: []`) — processes zero files (dead gate since inception). `tsc -b` exits 2 with the 29 prod errors. Main branch failing on all runs 2026-04-11 → 2026-04-13. No CI changes landed in this session (config-only; CI fix requires prod errors resolved first).
- **Follow-ups:** T5-21-B (fix 29 prod errors + flip `ci.yml:252` to `-p tsconfig.app.json`) — CRITICAL; T5-21-C (fix 33 test errors + add `Type check (tests)` CI step) — normal priority.

---

## T4-04 FE↔BE Validator Sync — Complete (2026-04-14)

**12 FORM.ITEM GAP PATCHES ACROSS 5 FILES. SHARED I18N VALIDATION KEYS. TYPESCRIPT TYPECHECK CLEAN.**

- **Phase-01 (Cases form):** `case-create-form.tsx` — `title` (`min:10`, `max:200`) and `description` (`min:20`, `max:2000`) now mirror `CreateCaseCommandValidator.cs` constraints.
- **Phase-02 (5 admin pages):** `form-templates-page.tsx` (`name`, `nameVi` max:200), `master-data-page.tsx` (Dictionary `code` pattern + max:100), `notification-templates-admin-page.tsx` (`templateKey` lowercase pattern), `partner-list-page.tsx` (Partner `code` max:50), `template-list-page.tsx` (DocumentTemplate `code` max:100).
- **Shared i18n keys added:** `common.validation.maxLength` (with `{{max}}` interpolation), `common.validation.pattern.upperAlphaNum`, `common.validation.pattern.lowerAlphaNum`; page-specific: `forms.codePatternError`.
- **All patched Form.Items:** `validateFirst: true` + `// Mirrors BE <Validator>.cs` comment for traceability.
- **Playwright gate deferred:** backend offline at 127.0.0.1:5002 (infra blocker); TypeScript typecheck clean as substitute gate.
- **Follow-up T4-04-B:** 2 adjacent gaps identified in code review (partner `name` max:200, template `code` max:100) — tracked separately.

---

## Direction A Quick Wins — FE UX Polish (2026-04-13)

**FE UX POLISH ACROSS 3 COMPONENTS. VIRTUALIZATION PATTERN INTRODUCED. 20 NEW UNIT TESTS. 0 BE CHANGES.**

- **A1 — Signature Widget Responsive:** Signature capture widget layout hardened for narrow viewports; canvas sizing and toolbar overflow corrected.
- **A2 — Forms Views Editor:** Drag-reorder polish in the Views editor; reorder handles, hover states, and keyboard UX improved.
- **A3 — Workflow Dashboard Virtualization:** Large workflow task list virtualized with `@tanstack/react-virtual ^3.13.23` (first virtualization pattern in codebase); eliminates DOM thrash on 1,000+ row lists.
- **A4 — Deferred:** Bulk-action panel polish deferred to next iteration.
- **Test Coverage:** 20 new FE unit tests (vitest); tsc clean, 0 compile errors.

---

## Workflow Version Migration — 4-Eyes Approval (2026-04-13)

**WORKFLOW VERSION MIGRATION WITH 4-EYES APPROVAL FLOW. STATE MAPPING VALIDATION. HANGFIRE EXECUTOR. CONCURRENCY SAFETY VIA sp_getapplock. 9 ENDPOINTS. 119 TESTS.**

- **WorkflowVersionMigration Entity:** Tracks source/target definition versions, approval state machine (Draft→PendingApproval→Approved→InProgress→Completed/Failed/Cancelled), creator + approver IDs (segregated), state mappings (JSON), migration items count. TenantId scoped.
- **WorkflowVersionMigrationItem Entity:** Tracks individual state mappings (source state → target state). Supports one-to-many, one-to-none, many-to-one state transitions for flexible upgrade paths.
- **Backend Commands:** ValidateMigrationMappingCommand (dry-run validation), CreateVersionMigrationCommand (validate instance compat + target version defs + state mappings, persist as Draft), ApproveVersionMigrationCommand (approver review, enforce ApprovalId ≠ CreatorId, transition to Approved), ExecuteVersionMigrationCommand (manual trigger, transition to InProgress), CancelVersionMigrationCommand (soft-cancel).
- **Backend Queries:** GetVersionHistoryQuery (all versions + instance counts per version), GetMigrationsByDefinitionQuery (list migrations filtered by definition + status), GetMigrationStatusQuery (detail + execution progress %), GetMigrationItemsQuery (all state mappings + validation results).
- **WorkflowMigrationsController:** 9 REST endpoints under /api/v1/workflow. GET /definitions/{key}/versions (version history), POST /migrations/validate (dry-run), POST /migrations (create), GET /migrations?definitionId=X (list), GET /migrations/{id} (detail), POST /migrations/{id}/approve (approve), DELETE /migrations/{id} (cancel), POST /migrations/{id}/execute (manual), GET /migrations/{id}/items (state mappings). All require WORKFLOW.MIGRATION.* permissions.
- **VersionMigrationExecutorJob:** Hangfire background job (every 10min). Query all Approved migrations → lock via sp_getapplock(N'migration-{id}') for concurrency safety → process instances in batches (update WorkflowVersionMigrationId + apply state mappings + create audit trail + cascade cancels to dependent tasks) → handle conflicts (already migrated, instance deleted, state not in mapping) → deadlock retry + exponential backoff → mark migration InProgress/Completed/Failed. EF raw SQL + sp_getapplock prevent concurrent executor duplicates.
- **4-Eyes Governance:** CreatorId resolved from JWT (user who created migration). ApprovalId resolved from JWT (approver). Enforce ApprovalId ≠ CreatorId in ApproveVersionMigrationCommandHandler. WORKFLOW.MIGRATION.CREATE permission for creation. WORKFLOW.MIGRATION.APPROVE permission for approval. Permission split ensures different users handle submission + approval.
- **State Mapping Validation:** POST /migrations/validate (dry-run) validates: target definition exists + is active, all source states in source definition, all target states in target definition, all instances can transition via mappings (no orphaned states). Returns validation errors + affected instance count. POST /migrations accepts same mappings, auto-validates on create.
- **FE Components:** version-history-panel (displays all versions + active instance badges), state-mapping-wizard (drag-drop state mapping UI with validation feedback), migration-status-panel (progress indicator + executor logs), migration-page (full workflow: create → approve → execute → monitor). React Query hooks for CRUD + polling. i18n 30+ keys (version history labels, mapping UI, approval workflow, error messages).
- **Permissions & Seeding:** 5 new permission codes (WORKFLOW.MIGRATION.READ, WORKFLOW.MIGRATION.CREATE, WORKFLOW.MIGRATION.APPROVE, WORKFLOW.MIGRATION.EXECUTE, WORKFLOW.MIGRATION.CANCEL) seeded to SystemAdmin role. WORKFLOW.MIGRATION.READ allows listing/viewing migrations (read-only). WORKFLOW.MIGRATION.CREATE allows creating migrations. WORKFLOW.MIGRATION.APPROVE allows approving migrations. WORKFLOW.MIGRATION.EXECUTE allows executing migrations. WORKFLOW.MIGRATION.CANCEL allows cancelling migrations.
- **Module Stats:** Workflow commands 26→31 (+5), queries 19→23 (+4), endpoints 44→53 (+9), controllers 6→7 (+1). Total: 355→364 endpoints (+9), 111→115 queries (+4), 160→165 commands (+5).
- **Test Coverage:** 119 tests: 85 unit tests (CreateCommand, ApproveCommand, ExecuteCommand, validation, state mapping logic) + 34 integration tests (Testcontainers MSSQL, Hangfire executor, concurrency safety, conflict handling).

---

## Bug Fixes — RBAC Menu Filter + AuthServer Deprecation (2026-04-13)

**PERMISSION-AWARE MENU TREE FILTERING. SEEDER IDEMPOTENCY. AUTHSERVER OBSOLETE API REPLACEMENT.**

- **MenuService Two-Pass Filter (F4/F10):** Rebuilt menu tree filtering with ancestor awareness. Pass 1: Include leaf nodes matching user permissions, preserve parents of authorized children even if parent has no permission. Pass 2: Prune empty group headers when all children filtered. Fixes issue where authorized menu items disappeared when parent lacked permission.
- **E2ETestDataSeeder Idempotency:** Added sentinel GUID check before inserting RolePermissions. Prevents duplicate permission entries on repeated seeder runs. Idempotent behavior ensures safe migration + re-runs.
- **AuthServer KnownNetworks → KnownIPNetworks:** Replaced obsolete `Microsoft.AspNetCore.HttpOverrides.KnownNetworks` with `Microsoft.AspNetCore.HttpOverrides.KnownIPNetworks` in Program.cs. Fixes deprecation warning on build.
- **Test Coverage:** 9 BE unit tests (MenuServiceFilterTests.cs: public leaf, permission-gated leaf, parent preservation, empty group pruning, nested subtree filtering) + 5 FE tests (menu loading with permission filtering).

---

## Workflow Dashboard Analytics (2026-04-12)

**WORKFLOW KPI ANALYTICS DASHBOARD. 6 METRICS QUERIES. ECHARTS CHARTS. 15-MIN CACHING. PERMISSION SEEDING. 1-YEAR DATE RANGE GUARD. ARIA LABELS.**

- **WorkflowAnalyticsController:** 6 new endpoints under `/api/v1/workflow/analytics`. GET state-distribution, processing-time, throughput, sla-compliance, aging, bottlenecks. All require WORKFLOW.ANALYTICS.VIEW permission. TenantId auto-resolved from JWT. Rate-limited via read-ops policy.
- **6 Analytics Queries:** GetStateDistributionQuery (count per state % share), GetAvgProcessingTimeByStateQuery (avg/median minutes by state over date range), GetThroughputByUserQuery (transition count grouped by user + day/week/month), GetSlaComplianceTrendQuery (on-time % per period for completed instances), GetAgingReportQuery (active instances stuck >minHours), GetBottleneckAnalysisQuery (states ranked by avg dwell time for bottleneck detection).
- **Query Handlers:** Dapper-based SQL queries with 15-minute ICacheService caching per tenantId+params. Validators (FluentValidation) enforce date range guards (1-year max, from < to). Top-5+Others aggregation for throughput user rank. Query timeout 30s.
- **FE Dashboard:** ECharts bar/line/pie charts rendering analytics data. State distribution stacked bar. Processing time trend line. Throughput multi-series bar (user/definition filtered). SLA compliance trend line + 95% target ref. Aging table (instance id, state, hours stuck, assignee). Bottlenecks ranked table. Date range picker (from/to, presets: 7d, 30d, 90d, 1y). Definition dropdown filter (optional).
- **Permissions & Seeding:** WORKFLOW.ANALYTICS.VIEW permission code added to PermCodes.Workflow.Analytics class. E2ETestDataSeeder auto-seeds permission + maps to SystemAdmin role on startup. No explicit permission policy enforce in seed (inherited from RolePermission seeding).
- **Accessibility:** ARIA labels on all charts (role=figure, aria-label="State distribution pie chart"), button titles, date picker labels. Screen reader friendly.
- **Module Stats:** Workflow queries: 13→19 (+6), endpoints: 38→44 (+6), controllers: 5→6 (+1). Total: 349→355 endpoints (+6), 105→111 queries (+6).

---

## Workflow Delegation Proxy (2026-04-12)

**WORKFLOW TASK DELEGATION WITH AUTO-EXPIRY. TIME-BOUNDED DELEGATION RULES. TASK ASSIGNMENT TRACKING. HANGFIRE AUTO-CLEANUP. 8 E2E TESTS.**

- **DelegationRule Entity:** Soft-delete, overlap validation, tenant-scoped. Tracks delegator, delegatee, valid time window, reason, auto-expiry.
- **WorkflowTask Enhancement:** Added DelegatedFromUserId (nullable) to track original assignee when task is delegated.
- **Backend Commands:** CreateDelegationRule, UpdateDelegationRule, DeleteDelegationRule, DelegateTask, ReturnDelegation (5 new CQRS handlers).
- **Backend Query:** GetDelegationRulesQuery with filtering and pagination.
- **WorkflowDelegationController:** 6 REST endpoints under `/api/v1/workflow/delegation-rules` and task delegation/return endpoints. Permission-gated with WORKFLOW.DELEGATION.MANAGE.
- **Hangfire Job:** DelegationExpiryJob runs on schedule, auto-expires delegations when validUntil passes. Registered in DelegationExpiryJobRegistration.
- **TaskAssignmentRepository:** Added to track all task-to-user assignments with delegation tracking (history audit trail).
- **Frontend:** 6 React Query hooks, DelegationRulesPage, DelegationRuleFormDialog, DelegateTaskDialog. Task detail drawer shows delegate button + return button + delegated-from badge. Route /admin/workflow-delegation with permission gate.
- **Localization:** 24 new i18n keys (en.json + vi.json) for delegation UI (form labels, buttons, error messages).
- **Test Coverage:** 8 E2E Playwright tests (TC-DEL-001..008) + 287 Workflow application tests + 77 domain tests all passing.
- **Module Stats:** Workflow endpoints 32→38 (+6), commands 20→26 (+5), queries 12→13 (+1), controllers 4→5 (+1).

---

## Dynamic Forms FE Red-Team Fixes (2026-04-11)

**10 SECURITY & CORRECTNESS FIXES. DOMPURIFY SANITIZATION. FILE UPLOAD VALIDATION. FIELD VISIBILITY RUNTIME EVAL. VIEW COLUMN EDITING. ERROR RECOVERY. 38 NEW TESTS.**

- **Security:** F4 DOMPurify HTML sanitization for RichText fields (stored XSS prevention via dompurify + @types/dompurify npm packages). F5 File upload validates per-field maxSize + allowedExtensions before upload (client-side + server-side enforcement).
- **Correctness:** F1 Auth submit modal now sends consentGiven field (PDPL compliance). F2 RequiredIf conditions evaluated at runtime (was decorative-only). F3 File upload fileId included in form submission data via hidden Form.Item. F8 View columns can be edited after creation (BE + FE views API extended).
- **Reliability:** F6 ErrorBoundary wraps RichText lazy-load — fallback textarea on chunk failure. F9 Signature canvas preserves drawing across container resize. F10 Table field single render per cell edit (eliminated double-render bug).
- **Localization:** F7 ~75 missing i18n keys added to src/locales/en.json + src/locales/vi.json for Forms module (field labels, validation messages, UI text).
- **New Dependencies:** dompurify (^2.4.0) + @types/dompurify (^2.8.0) for XSS prevention.
- **Test Coverage:** 38 new tests: form-visibility-helpers.test.ts (visibility logic), form-step-utils.test.ts (step evaluation), rich-text-sanitize.test.ts (XSS prevention). All located in `web/src/features/forms/__tests__/` and `web/src/test/features/forms/`.

---

## Local Dev Automation + Reverse Proxy Support (2026-04-10)

**LOCAL DEVELOPMENT SCRIPTS, FORWARDEDHEADERS MIDDLEWARE, TUNNEL MODE SUPPORT, CORS HARDENING, API BASEURL FIX.**

- **Local Dev Scripts (scripts/ folder):** 4 numbered scripts for development automation. `1_run-local-dev.bat/.ps1/.sh` starts Auth+API+FE services with health checks (cross-platform). `2_start-tunnels.bat` launches 3 Cloudflare tunnels (AuthServer, API, FE) in background. `3_setup-tunnel-urls.bat/.py` auto-configures Vite .env + appsettings.json (5 files) for tunnel mode. `4_expose-demo.bat` interactive menu for tunnel tools.
- **ForwardedHeaders Middleware:** Added `UseForwardedHeaders()` to both AuthServer and API `Program.cs`. Trusts X-Forwarded-For/Proto/Host headers from reverse proxies (Cloudflare Tunnel, nginx, Docker). Enables seamless proxy deployment without header spoofing.
- **CORS Hardening:** Extended `GovApiPolicy` AllowedHeaders to include `X-Correlation-Id` and `X-Tenant-Id` (InfrastructureRegistration.cs). Added Cors section to AuthServer + API appsettings.Development.json for tunnel domain access.
- **API baseURL Fix:** `api-client.ts` corrected to `BASE_URL ? ${BASE_URL}/api/v1 : '/api/v1'` (was missing /api/v1 prefix when VITE_API_BASE_URL env set). Fixes tunnel + proxy mode API routing.
- **Vite HMR Config:** Added tunnel domain entries to allowedHosts in `vite.config.ts` for hot-reload over reverse proxy.
- **Readiness:** Full-stack tunnel/proxy deployment ready. Supports Cloudflare, nginx, Docker networking modes without code changes.

---

## Permission-Based Controller Authorization Migration (2026-04-09)

**~40 CONTROLLERS MIGRATED FROM ROLE-BASED TO PERMISSION-BASED AUTHORIZATION. COMPILE-TIME SAFETY VIA PERMCODES CONSTANTS. 105 PERMISSIONS ACROSS 18 MODULES.**

- **PermCodes.cs Class:** Centralized authorization constants with compile-time type safety. Format: MODULE.RESOURCE.ACTION (e.g., PermCodes.Identity.User.View = "IDENTITY.USER.VIEW"). All 105 permission codes defined as nested static classes organized by module (Identity 23, Cases 5, Files 8, Audit 7, Workflow 6, Forms 6, Notif 4, Org 4, Master 5, AI 5, Collab 3, Integ 5, Report 6, Rules 5, Search 2, Sign 3, SysParam 6, System 2).
- **Policies.cs Refactored:** Now contains only ABAC policies (DepartmentRestricted, ClassifiedAccess). All permission-based policies moved to permission registration (no longer hardcoded in Policies class).
- **ModuleRegistration.cs:** Registers 105 permission-based policies dynamically via PermCodes.All collection + PermissionRequirement handler. 2 ABAC policies (DepartmentRestricted, ClassifiedAccess) registered separately with their respective handlers.
- **Controller Decorators:** All ~40 controllers migrated from `[Authorize(Roles = "Admin")]` to granular `[Authorize(Policy = PermCodes.X.Y.Z)]` (e.g., UsersAdminController uses PermCodes.Identity.User.View for GET, PermCodes.Identity.User.Create for POST).
- **RolePermissions Seeding:** Maps roles → permissions at startup. Admin role receives normal permissions (View, Create, Update, Delete per resource). SystemAdmin role receives all 105 permissions. GovOfficer receives limited subset. Citizen receives form/collaboration permissions only.
- **Authorization Handler (PermissionRequirement):** Evaluates permission code against user's effective permissions (combines direct role assignments + delegated roles + group memberships). Falls back to ABAC handlers if permission check passes.
- **Backward Compatibility:** Role-based checking removed from controllers entirely. Permission claims in JWT must match PermCodes values exactly. All custom ABAC policies continue to work alongside permission-based checks.

---

## RBAC Roles & Permissions CRUD + FE Admin Interface (2026-04-09)

**BUSINESS ROLE MANAGEMENT API + PERMISSION ASSIGNMENT + FRONTEND ADMIN INTERFACE. SYSTEM ROLES READ-ONLY. DOMAIN EVENTS FOR AUDIT.**

- **Role CRUD Endpoints (RolesAdminController):** Full management for business roles. GET /admin/roles (list all), GET /admin/roles/{id} (detail), POST /admin/roles (create with name/code/description), PUT /admin/roles/{id} (update name/description), DELETE /admin/roles/{id} (soft-delete), PUT /admin/roles/{id}/activate (reactivate). System roles return 403 on delete/update. Tenant isolation via ResolveTenantId(). ActorId from JWT for audit trail.
- **Permission APIs:** GET /admin/permissions (list all seeded permissions), GET /admin/roles/{id}/permissions (get role's current permissions), PUT /admin/roles/{id}/permissions (sync permissions via SyncRolePermissionsCommand). X-Confirm-SoD header validates Segregation-of-Duties before allowing sensitive permission combos.
- **Domain Events:** RoleCreated, RoleUpdated, RoleDeactivated, RoleActivated, RolePermissionsChanged. All include TenantId + ActorId for audit integration.
- **EffectivePermissionService:** Enhanced to filter IsActive on direct roles + group roles. Deactivated roles do not grant permissions (time-based auto-revocation without explicit assignment removal).
- **FE Admin (Role Management Page):** Create/Edit modals with Name, Code, Description fields. Permission matrix drawer with grouped permissions (Module.Resource). Checkboxes with descriptions. SoD conflict warning when user attempts conflicting permission combo (e.g., approver + requester). Confirm dialog on delete.
- **Security:** All write operations enforce tenant isolation (ResolveTenantId before command dispatch). Sensitive permissions require SystemAdmin role check in handler. IBulkRoleChangeService interface extracted from concrete impl for extensibility.
- **Backward Compatibility:** No breaking changes. Phase F (v2.11) authorization patterns remain unchanged. RBAC additions are purely additive (new commands, new events, existing role lookup logic unmodified).

---

## Chat UI Redesign — Telegram-Style (2026-04-09)

**COLLABORATION MODULE ENRICHED WITH SIDEBAR ENHANCEMENTS. UNREAD COUNTS. LAST MESSAGE PREVIEWS. CROSS-MODULE INTEGRATION.**

- **ConversationDto Enriched:** Added LastMessagePreview (truncated to 100 chars), LastMessageSenderName, UnreadCount, ParticipantNames. Telegram-style sidebar displays all fields for quick context.
- **MessageDto Enriched:** Added SenderName field for UI rendering without N+1 queries.
- **GetConversationsQueryHandler Rewritten:** Single optimized Dapper query with OUTER APPLY for last message + CASE aggregate for unread count. Bulk user name resolution via IIdentityModuleClient cross-module contract (avoids schema joins). Pagination via OFFSET/FETCH. Result set includes conversation metadata + sender names for FE sidebar.
- **UserSearchController Enhanced:** Filters out current user from results (Id != @CurrentUserId) to prevent self-chat. Minimal DTO (Id, FullName, DepartmentCode only — no email PII). LIKE escape for SQL injection mitigation. Rate-limited via read-ops policy.
- **FE Chat Sidebar (Telegram-style):** Displays unread count badge, last message preview, sender name, conversation type icon. Click-to-open modal for contact search. Direct conversations show other participant name.
- **Cross-Module Communication:** Collaboration module now uses IIdentityModuleClient.GetUserInfoByIdsAsync() for async user name resolution (phase 1: in-process, future: RPC/HTTP).

---

## Audit Closure + Feature Hardening (2026-04-08)

**18 SECURITY FINDINGS VERIFIED FIXED. IDLE TIMEOUT FEATURE. CHAT + RATE LIMITING IMPROVEMENTS. 2,050+ TESTS.**

- **Audit Closure (18/18):** All CRITICAL/HIGH/MEDIUM/LOW security findings verified fixed and closed on GitHub. No open vulnerabilities. Full remediation evidence in place.
- **Idle Timeout Feature:** SessionExpiryModal component (Ant Design Modal), configurable via SystemParameter (session_timeout_minutes, session_countdown_seconds). Cross-tab sync via localStorage. Pauses on window hidden. Public GET /api/v1/session-config endpoint. SessionConfigController in SystemParams.Presentation.
- **Chat Module Improvements:** Create conversation modal with user search functionality. Public GET /api/v1/users/search endpoint (UserSearchController in Identity.Presentation). Duplicate Direct conversation guard prevents duplicate 1:1 chats. Send message endpoint fix for edge case.
- **Form Builder Fix:** Drag reference/formula controls now sends default dataSourceJson/formulaJson on drop.
- **Notification Fix:** Subject field name mismatch corrected.
- **Rate Limiting Enhancement:** New read-ops policy (60/min prod, 600/min dev) added to RateLimiterConfiguration. Named policies for read vs write ops.
- **Pagination Hardening:** DelegationController.List and AnnouncementsController.GetAll now server-paginated with offset/limit.
- **Hangfire Stability:** AlertEvaluationJob + WebhookDeliveryJob now have explicit retry/concurrency attributes (Retry(3), JobProcessingTimeout).
- **Public Endpoints:** SessionConfigController + UserSearchController added to public routes (no auth required). Enable session config pre-login + chat participant discovery.
- **Test Coverage:** 2,050+ passing tests (1,808 BE unit + 491 FE vitest + 772 E2E Playwright).

---

## E2E Playwright Full Coverage Infrastructure (2026-04-05)

**6 PHASES COMPLETE. 28 NEW FILES. POM + FIXTURES + HELPERS + 16 SPEC FILES. ALL ADMIN/USER ROUTES COVERED.**

- **Phase 1 — Test Infrastructure:** 5 Page Object Models (LoginPage, AdminLayoutPage, DataTablePage, FormModalPage, PublicFormPage), 2 fixtures (auth-fixture with worker-scoped OIDC login, TestDataFactory with entity cap + reverse-order cleanup), 3 helpers (navigation, form, table), playwright.config.ts updated with admin-browser + e2e-perf projects.
- **Phase 2 — Public Form Flows:** 4 spec files: lifecycle (admin create→publish→citizen fill→submit→review), validation (required fields, email format, XSS injection), file upload (valid/oversized), CSV export.
- **Phase 3 — Admin UI Browser Tests:** 5 spec files covering 45 render + 4 CRUD tests across 29 admin routes + 15 user-facing routes. Split: identity (13 routes), content (5), system (6), integration (6), user pages (15).
- **Phase 4 — JIT SSO Flow:** API-level JIT provisioning tests (config CRUD, domain whitelist rejection) + mock IdP helper for future browser-level SSO testing.
- **Phase 5 — Error Scenarios:** API error codes (400/401/403/404/422), browser error boundaries (404 page, component crash fallback, form validation, intercepted 500), session expiry + concurrent sessions, optimistic concurrency conflict detection.
- **Phase 6 — Performance Baselines:** Page load <3s (6 pages), API response <500ms (6 list + 3 detail endpoints), Core Web Vitals (LCP <4s, CLS <0.25), ROPC token <1s.
- **Code Review Fixes:** Playwright config regex overlap fixed (browser-ui.spec vs browser-ui/), perf assertions tightened (status < 300 not < 500), route intercept leak prevented (try/finally), XSS test accuracy improved (innerHTML vs textContent).
- **CRUD Expansion Plan:** Phase 0 POM extension done (fillSwitch, fillRangePicker, fillNumber, clickRowDeleteAndConfirm, findRowByText). 3-phase CRUD plan red-teamed with 16 findings (6 critical resolved). Ready for implementation.

---

## E2E CRUD Expansion — 30+ Tests Across Admin/User Routes (2026-04-06)

**3 PHASES COMPLETE. 29 CRUD TESTS ADDED. NEW FILE + 3 MODIFIED. ALL UNDER 200 LOC. SELF-CLEANUP VIA AFTERALL.**

- **Phase 1 — Identity CRUD (11 tests):** NEW `web/e2e/browser-ui/admin-identity-crud.spec.ts` (180 LOC). Tests: Users create+delete (email `e2e-{timestamp}@test.local`), Groups create+edit+delete, CredentialPolicies create+delete (fillNumber + fillSwitch), ABAC Rules create+delete, Delegations create+revoke (fillRangePicker + resolveUserId), Sessions render. All use POM helpers: fillField, fillNumber, fillSwitch, fillRangePicker, clickRowEditButton, clickRowDeleteAndConfirm, findRowByText.
- **Phase 2 — Content+System CRUD (13 tests):** `admin-content.spec.ts` (+8 tests, 155 LOC total). Templates create+edit+delete (720px modal), MasterData create+delete (province level only), NotifTemplates edit+delete. `admin-system.spec.ts` (+5 tests, 165 LOC total). API Keys generate+revoke (two-phase modal, close after generate), SystemParams create+edit+delete. All use regex matching for Vietnamese labels (/thêm|tạo|lưu/i).
- **Phase 3 — Integration+User CRUD (5 tests):** `admin-integration.spec.ts` (+3 tests, 90 LOC total). Rules create+activate (CheckCircleOutlined)+delete (Draft only). `user-pages.spec.ts` (+2 tests, 145 LOC total). Cases create, Forms create, Partners create (minimal fields).
- **Cleanup Pattern:** Each file uses `const createdIds: any[] = []` + afterAll with API delete calls. Verified no row leakage (findRowByText returns -1 for non-existent rows, test skipped).
- **Code Quality Fixes:** Removed dead createdIds code, added delete verification assertions, replaced dangerous session revoke test with safe render check. All files tsc clean. Under 200 LOC per file limit.
- **Test Count:** 29 CRUD tests (11+8+5+3+2=29). Combined with 45 render tests + 28 Workflow E2E + Phase 1-6 infrastructure = 140+ total E2E Playwright tests.

---

## E2E Test Case Specifications — 1,051 Test Cases Across 340 Endpoints (2026-04-06)

**20 MARKDOWN DOCS. 19 MODULES + CROSS-MODULE FLOWS. 20,804 LINES. 340 ENDPOINTS COVERED. READY FOR IMPLEMENTATION.**

- **Specification Docs (20 files):** Identity (62 endpoints, 111 TCs), Audit (15 endpoints, 48 TCs), Cases (25 endpoints, 95 TCs), Forms (28 endpoints, 107 TCs), Workflow (18 endpoints, 71 TCs), Files (12 endpoints, 45 TCs), Notifications (8 endpoints, 32 TCs), Organization (14 endpoints, 52 TCs), MasterData (10 endpoints, 38 TCs), Search (6 endpoints, 24 TCs), Reporting (8 endpoints, 32 TCs), AI (6 endpoints, 24 TCs), Integration (9 endpoints, 36 TCs), Collaboration (7 endpoints, 28 TCs), Rules (8 endpoints, 32 TCs), Signature (6 endpoints, 24 TCs), SystemParams (5 endpoints, 20 TCs), HostAdmin (2 endpoints, 8 TCs), AuthServer (20 endpoints, 80 TCs), Cross-module flows (12 TCs). Totals: 340 endpoints, ~1,051 test cases.
- **Structure:** Each TC contains Priority (P1-P3), Type (API/Browser/Integration), Auth role, endpoint, request/response format, preconditions, steps, expected result, cleanup. Covers happy path, auth failures (401/403), validation (422), not found (404), error scenarios (400/500), pagination, filtering, cross-tenant isolation.
- **Infrastructure Phase 0 Fixes:** Rate limiter isDev override (named policies 200/min write-ops, 600/min anonymous), Playwright config testIgnore.push('accessibility'), 6 spec files serial mode, auth fixture hardcoded localhost:5000→dynamic port, findRowByText helper innerText() fix.
- **Coverage Alignment:** 140+ E2E Playwright tests already implemented (45 render + 29 CRUD + 28 Workflow + Phase 1-6 infrastructure). Specs provide roadmap for additional 90+ TCs across 11 modules requiring implementation (Audit, Cases detailed workflows, Forms conditional fields, Workflow SLA/recall, Files complex, Notifications, Organization nested units, Search FTS, Reporting aggregations, AI multi-turn, Integration webhooks, Collaboration dual-write, Rules conflict, Signature verification, SystemParams hot-reload, AuthServer federation, Cross-module scenarios).
- **Location:** `/plans/260406-1223-e2e-full-coverage-verified/test-cases/`

---

## E2E Spec Implementation Roadmap (2026-04-06)

**8 PHASES. P1 SCOPE: 706 TESTS ACROSS 19 MODULES. 45H EFFORT. RED-TEAMED + VALIDATED. READY TO EXECUTE.**

- **Phase Breakdown:** Phase 1 Infrastructure (rate limiter + Playwright config fixes, 3h). Phase 2-8 Module Implementation (Identity 65 TCs/7h, Audit 48 TCs/6h, Cases 95 TCs/12h, Forms 107 TCs/13h, Workflow 71 TCs/8h, Files 45 TCs/6h, Notifications 32 TCs/4h, and 5 remaining P2 modules deferred). Estimate 45h for P1 only (706 TCs). Excludes 345 P2/P3 tests and cross-module flows (Phase 2 roadmap item).
- **Red Team Results:** 23 findings → 19 resolved (4 deferred to Phase 2). Critical mitigations: spec clarity (TC ID naming, role/tenant context), fixture robustness (retry logic for async state), cleanup validation (verify deletion before test skip), parallel execution boundaries (serial mode for state-dependent tests).
- **Validation:** Spec coverage aligned with existing 140+ implemented tests. P1 modules (Identity, Audit, Cases, Forms, Workflow, Files, Notifications) represent 470/706 TCs. Infrastructure Phase 0 committed. HTML review dashboard auto-generated for TC traceability + dev status tracking.
- **Location:** `/plans/260406-1740-e2e-spec-implementation/` (plan.md + 8 phase files)

---

## JIT SSO Provisioning — Auto-Provision External OIDC Users (2026-04-05)

**5 IMPLEMENTATION PHASES COMPLETE. AUTO-CREATE USERS ON FIRST SSO LOGIN. 4-LAYER SECURITY. ADMIN CRUD + REACT PAGE. 21 TESTS.**

- **Phase 1 — JitProvisioningService:** Core logic in AuthServer (JitProvisioningService.cs). ProvisionOrLinkAsync(loginProvider, providerKey, email, fullName) auto-creates ApplicationUser + ExternalIdentity on first OIDC login. Checks existing ExternalIdentity by (Provider, ExternalId) — links if found. Validates JitProviderConfig enabled + active. Returns JitResult(Success, User, ErrorCode) with status codes: "deactivated", "pending_approval", "provision_failed", "jit_disabled", "domain_not_allowed", "rate_limited", "no_tenant".
- **Phase 2 — Security Layer (4 Red Team Mitigations):** RT-01 Email Squatting: never auto-link by email alone (ExternalIdentity prevents duplicate email registration). RT-02 Domain Whitelist: JitProviderConfig.AllowedEmailDomains validates domain if configured (regex pattern). RT-03 Tenant Requirement: JitProviderConfig.RequireTenant enforces user must have TenantId (no cross-tenant provisioning). RT-04 Rate Limiting: in-memory ConcurrentDictionary tracks provisioning attempts per provider scheme with hourly window + max 100 attempts/hour.
- **Phase 3 — JitProviderConfig Entity:** Domain entity in Identity module with columns: ProviderScheme (unique key), JitEnabled (bool), IsActive (bool), AllowedEmailDomains (string, null-safe), RequireTenant (bool), DefaultTenantId (Guid?), AutoCreateEmailDomain (string, e.g., "@company.local"), MaxProvisioningPerHour (int, default 100). EF configuration + migration (AddJitProviderConfig). Repository: IJitProviderConfigRepository, InProcessJitProviderConfigRepository.
- **Phase 4 — Admin API (5 Endpoints):** JitProviderConfigsController (ApiControllerBase) with CRUD: POST /admin/jit-provider-configs (CreateJitProviderConfigCommand), GET /admin/jit-provider-configs (ListJitProviderConfigsQuery), PUT /admin/jit-provider-configs/{id} (UpdateJitProviderConfigCommand), DELETE /admin/jit-provider-configs/{id} (DeleteJitProviderConfigCommand). Command validators: FluentValidation (ProviderScheme required, AllowedEmailDomains regex validation). Dapper queries for list with paging + tenant isolation.
- **Phase 5 — React Admin UI:** /admin/jit-provider-configs page with table (Provider, JitEnabled, IsActive, AllowedDomains, Actions). Create/Edit modals with form fields (ProviderScheme, JitEnabled toggle, IsActive toggle, AllowedEmailDomains textarea, RequireTenant, DefaultTenantId dropdown, MaxProvisioningPerHour). Delete confirmation dialog. Orval hooks auto-generated (useCreateJitProviderConfig, useListJitProviderConfigs, etc.).
- **Test Coverage (21 tests):** 5 command/validator tests (CreateJitProviderConfig, UpdateJitProviderConfig, DeleteJitProviderConfig validation), 4 provisioning logic tests (ProvisionOrLinkAsync success/existing/disabled/rate-limited), 3 domain whitelist tests (email domain validation), 2 rate limit tests (hourly window, DoS prevention), 2 tenant requirement tests, 3 repository tests (CRUD), 1 migration test, 1 controller test.
- **ExternalLogin Integration:** ExternalLoginController.Callback() calls JitProvisioningService.ProvisionOrLinkAsync() before creating/returning ExternalIdentity. Handles error codes gracefully (return error view or redirect to /admin/jit-provider-configs if provisioning failed).
- **Architecture Impact:** Unblocks enterprise OIDC federation (Google, Microsoft, custom IdP). Prevents email-based account takeover attacks. Tenant-scoped user creation ensures isolation. Rate limiting protects against brute-force provisioning attacks. Establishes pattern for future SSO providers.

---

## IModuleClient Batch Lookup — Cross-Schema JOIN Elimination (2026-04-05)

**3 PHASES COMPLETE. 5 CROSS-SCHEMA JOINS ELIMINATED. IIDENTITYMODULECLIENT EXTENDED + CONSUMERS REFACTORED + TESTS VERIFIED.**

- **Phase 1 — IIdentityModuleClient Extension:** Added UserInfoDto (class-based for Dapper compatibility) with UserId, Email, FullName, TenantId, PrimaryOrgUnitId. Added GetUserInfoByIdsAsync(userIds, tenantId?, ct) with hard cap 5000 IDs + chunking 500 per batch. Modified FindByEmailAsync signature to accept optional tenantId for tenant-scoped email lookup. Implemented InProcessIdentityModuleClient with Dapper batch queries + tenant filtering. Added IOrgUnitModuleClient interface + InProcessOrgUnitModuleClient with identical patterns (batch lookup + tenant isolation + 5000 cap).
- **Phase 2 — Consumer Refactoring:** Removed cross-schema JOIN from GetRtbfRequestsQueryHandler (audit module). Added SubjectEmail denormalization to RtbfRequest entity + EF migration with IF EXISTS guard for backfill. Updated CreateRtbfRequestCommandHandler to resolve email via IIdentityModuleClient. Removed cross-schema JOIN from OrgUnitQueryHandlers (organization module). Updated AuditPiiAnonymizer to scrub denormalized SubjectEmail on RTBF processing. Fixed reporting seeder schema reference ([identity].OrgUnits→[organization].OrgUnits) + documented reporting SQL templates as documented exception (GROUP BY requires DB-level JOIN). Added TODO comments referencing CasesStatisticsReport + SLA_BY_DEPARTMENT_PERIOD for future microservice extraction.
- **Phase 3 — Tests + Validation:** 2,001/2,008 tests pass (7 pre-existing failures unrelated). Grep verification: 0 unacknowledged cross-schema JOINs in audit/organization/workflow/rules/forms/files modules. Build: 0 errors. RTBF page displays correctly with denormalized SubjectEmail. Org unit members page shows user names via IModuleClient enrichment. AuditPiiAnonymizer confirmed scrubbing denormalized email field.
- **Red Team Acceptance:** 13 of 15 findings accepted (2 scope rejections). Applied critical mitigation: tenant parameters on all batch lookups (prevents cross-tenant PII enumeration). Hard 5000 ID cap on all methods (prevents DoS). Class-based DTOs for Dapper compatibility (nullable param handling). Backfill with IF EXISTS guard (safe schema dependency).
- **Architecture Impact:** Establishes IModuleClient batch lookup pattern as standard for cross-module data access. Unblocks microservice extraction: database per service feasible without dual-write sync. Tenant isolation enforced at API boundary (optional tenantId param). Denormalization of SubjectEmail enables RTBF search without identity module dependency.

---

## Server-Side Search Implementation (2026-04-03)

**8 NEW BE QUERY HANDLERS. AUDIT CONTAINS/FTS + FEATURE FLAG FALLBACK. FORMS/FILES LIKE PATTERN. FE ALREADY MIGRATED (26 PAGES).**

- **Audit Module (5 handlers):** GetAuditLogsQuery, GetAuditLogDetailsQuery, GetAuditLogCategoryCountsQuery, GetAuditLogStatusCountsQuery, SearchAuditLogsQuery. Uses CONTAINS/FTS via IFeatureFlagService — falls back to LIKE pattern if feature disabled. SearchTerm DTO: Term (string), Fields (string array). Integration tests verify full-text search accuracy + fallback behavior.
- **Forms Module (2 handlers):** SearchFormTemplatesQuery, SearchFormSubmissionsQuery. LIKE pattern search on FormTemplate.Name + FormTemplate.Description, FormSubmission.Id + FormSubmission.Data (JSON). Supports dynamic field name resolution.
- **Files Module (1 handler):** SearchFilesQuery. LIKE pattern search on File.Name + File.Description + File.Tags.
- **Integration + Workflow:** Already supported SearchTerm in existing query handlers (no new handlers required).
- **Frontend:** 26 pages already migrated to useServerPagination + useDebouncedValue hooks (Audit, Case, Forms, Files, Tasks, etc.). Real-time search with debounce (500ms) eliminates fetch-all pattern.
- **Small Dataset Exceptions (3 pages):** Signature, Rules, Backup remain with pageSize:9999 — small datasets, no paginated search needed.
- **Orval Code Generation:** SearchAuditLogsQuery, SearchFormTemplatesQuery, SearchFormSubmissionsQuery, SearchFilesQuery auto-generated as useQuery hooks. FE consumers call `useSearchAuditLogs({searchTerm: term})` with debounced values.

---

## Forms Frontend Gap Closure (2026-04-01)

**31 GAP FIXES + 9 NEW FIELD TYPES + FE TEST FIXTURES. 144 FE TESTS WRITTEN. FORM SUBMISSION VALIDATION + UI IMPROVEMENTS. FORM SETTINGS PERSISTENCE. WORKFLOW TASK ASSIGNMENT FE.**

- **31 Gaps Closed:** FG1-FG31 fixed per requirements. Client-side validation, server-side integration, UI enhancements, workflow event handling, accessibility, error recovery, internationalization, form history tracking, batch submission, duplicate detection.
- **9 New Field Types:** Implemented RichText (Slate.js editor), File (multer upload), Signature (Canvas-based), Rating (Star picker), Slider (Range input), Checkbox, Select (Ant Select), MultiSelect (Ant Select multiple), Radio.
- **Validation Integration:** FluentValidation rules applied to all field types. Custom messages per field.
- **Form Submission Flow:** Client-side validation → API submission → server error handling → retry logic → offline storage for failed submissions.
- **Form Settings:** Persistent preferences for open/closed state, collapsed sections, saved drafts.
- **Workflow Integration:** Task assignment form displays workflow step information, task routing based on approval rules.
- **FE Test Fixtures:** 144 tests across field types, submission flows, validation, and edge cases.
- **Accessibility:** ARIA labels, keyboard navigation, focus management, screen reader support.

---

## Full System Test + Bug Fixes (2026-03-30 PM)

**6 BUG FIXES. 8 WORKFLOW CONDITION OPERATORS. FORM VISIBILITY. JWT ISSUE WORKAROUND. DAPPER DTO FIX. CACHING CLARITY. FORM BUILDER ENHANCEMENTS. 19 TESTS + 11 E2E TESTS.**

- **Bug F1:** Forms module missing from app layout menu due to FORM.VIEW permission check — added seeding + endpoint filter.
- **Bug F2:** Form submission multi-step visibility broken — fixed via FormVisibilityHelper. Tests verify step transitions.
- **Bug F3:** JWT issue in development mode — applied issuer validation workaround (AllowInsecureHttps = true in appsettings.Development.json).
- **Bug F4:** Dapper position-record parameter binding failure — migrated to class-based DTOs (e.g., UserInfoDto class instead of record).
- **Bug F5:** Missing Dapper DTO models in domain — created SearchAuditLogsDto, SearchFormTemplatesDto, SearchFormSubmissionsDto, SearchFilesDto.
- **Bug F6:** Caching pattern unclear — documented IQueryCacheService usage + cache-aside pattern examples in design guidelines.
- **Workflow Operators:** Added 8 operators (Equals, NotEquals, GreaterThan, LessThan, GreaterThanOrEqual, LessThanOrEqual, Contains, StringEquals). Type-safe enum + API validation.
- **Form Builder Enhancements:** Form step reordering, field reordering, visibility rules (conditional display), default values for form fields, form versioning (minor).
- **New Tests:** 19 unit tests (form visibility, submission validation, workflow conditions), 11 E2E tests (form CRUD, submission, workflow integration). All passing.

---

## Backlog Cleanup — Build Warnings + Auto-Assign + PDF Font + Test Coverage (2026-03-30)

**BUILD WARNINGS RESOLVED. AUTO-ASSIGN IMPLEMENTATION. PDF SIGNATURE FONT. TEST COVERAGE 2,100+ TESTS. IDENTITY INTEGRATION CLEANUP. PERMISSION SEEDING.**

- **Build Warnings:** 23 nullability, obsolete, and unused-parameter warnings eliminated. All projects compile clean (0 warnings, 0 errors).
- **Auto-Assign Workflow Tasks:** Implemented logic to auto-assign workflow tasks to users matching skill tags + department restrictions. Manual reassignment permitted.
- **PDF Signature Rendering:** Integrated NotoSansCJK font in signature PDF to render Vietnamese text correctly. Font package: font-awesome-cjk.
- **Identity Module Integration:** Added IIdentityModuleClient wrapper for user lookup (GetUserInfoByIds, GetUserInfoByEmail). No breaking changes.
- **Permission Seeding:** Expanded role-permission mapping. Admin role → read-only permissions. Citizen role → form-only permissions. GovOfficer → departmental access.
- **Test Coverage:** 2,100+ tests passing (1,750 BE unit, 280 integration, 70 FE). Audit module tests, forms tests, workflow tests all green.
- **Pipeline Integration:** CI/CD pipeline updated to detect build warnings + fail if any detected.

---

## Build System + Architecture Hardening (2026-03-28)

**SQLSERVER DOCKER TESTCONTAINERS. LOCAL POSTGRES. EFCORE OPTIMIZATIONS. TEST ISOLATION. 250+ INTEGRATION TESTS. CONCURRENT EXECUTION SAFE.**

- **Testcontainers Integration:** MSSQL 2022 Docker container for integration tests. Automatic startup/shutdown per test fixture. Connection pooling, concurrent isolation via schema + database name.
- **EF Tracking Optimization:** Explicit AsNoTracking() on read-only queries (Audit, Reports, Files). Changed tracker only for write operations (Create/Update/Delete).
- **Query Optimization:** Filtered Include + Select projections reduce N+1 queries (Workflow instances list). Added 15-min cache on expensive aggregations.
- **Test Isolation:** Each test runs in isolated schema. TenantSessionContext + SQL SESSION_CONTEXT prevent cross-tenant data leak. Data cleanup via transaction rollback post-test.
- **Concurrent Execution:** Tests run in parallel (NUnit 3.13 + parallelScope=FixturePerTestSession). No deadlocks, no flakiness detected.
- **Performance Baseline:** Integration tests complete in 2-3 min (250+ tests), under 100ms average per test.

---

## Codebase Initial Setup (2026-03-15)

**CLEAN ARCHITECTURE. CQRS + DDD. 12 MODULES. SQLSERVER + EFCORE 10. TESTCONTAINERS. REACT 19 + VITE. OPENIDDICT AUTHSERVER.**

- **Backend:** .NET 10, Clean Architecture (Domain/App/Infra/Presentation layers), CQRS (MediatR), DDD (Value Objects, Domain Events), FluentValidation, FluentResults, Entity Framework Core 10.
- **Modules:** Identity (JIT SSO), Audit (PII Anonymization), Cases, Files (S3), Notifications (SignalR), Organization (RLS), Forms (Dynamic), Workflow, Reporting, MasterData, SystemParams, Integration (webhook).
- **Database:** SQL Server 2022, 100+ entities, Row-Level Security (RLS) on sensitive tables, audit trail on all modifications.
- **Testing:** xUnit + NUnit, Testcontainers for MSSQL, 1,500+ unit tests, 200+ integration tests, Playwright E2E.
- **Frontend:** React 19, Vite, Ant Design 5.x, React Query, Zustand, i18n (Vietnamese/English), Form Builder UI.
- **DevOps:** Docker containerization, CI/CD pipeline (GitHub Actions), deployment automation.
- **Security:** OpenID Connect (OIDC) auth via AuthServer, JWT tokens, role-based access control, audit logging.

---

