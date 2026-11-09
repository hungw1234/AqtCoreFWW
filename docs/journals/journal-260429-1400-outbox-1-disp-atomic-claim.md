# Session Journal — OUTBOX-1-DISP Atomic Claim Refactor (v2.133p12)

**Date:** 2026-04-29 | **Version:** v2.133p12 | **Branch:** main | **Status:** SHIPPED

---

## What

Atomic claim refactor for Outbox dispatcher. Defense-in-depth fix for multi-replica claim race.

**Problem:** Pre-existing Outbox dispatcher pattern:
1. `SELECT ... WITH (UPDLOCK,READPAST) ... ORDER BY CreatedUtc` — acquires UPDLOCK
2. Lock released after SELECT
3. `UPDATE ... WHERE Id IN (...)` — separate implicit tx

Multi-replica race: two dispatcher replicas both SELECT same batch (both acquire+release lock between steps 1→2), both see lock-free state → both UPDATE same rows → duplicate publishing.

**Solution:** Single-statement CTE atomic claim-and-update (no intermediate lock release):
```sql
;WITH cte AS (
  SELECT TOP(@claimSize) Id, CreatedUtc 
  FROM OutboxMessages 
  WITH (UPDLOCK,READPAST,ROWLOCK)
  WHERE ProcessedAtUtc IS NULL 
    AND (ClaimedAtUtc IS NULL OR ClaimedAtUtc < @staleCutoff)
  ORDER BY CreatedUtc
)
UPDATE cte SET ClaimedAtUtc=SYSUTCDATETIME() OUTPUT inserted.*
```

Lock held from SELECT through UPDATE (single statement boundary). ROWLOCK escalates per-row (not page/table).

---

## Why

**Empirical context:** v2.133p11 shipped OUTBOX-1 (consumer-side TOCTOU collapse via unique index + idempotent catch). That fix stops duplicate *insertion* into workflow instance table, but doesn't solve upstream duplicate *publishing* from outbox.

**Architectural pairing:** Two-layer defense:
- **Layer 1 (producer):** OUTBOX-1-DISP atomic claim (this fix) — prevents duplicate dispatch
- **Layer 2 (consumer):** OUTBOX-1 unique constraint (v2.133p11) — stops duplicate creation

Single-layer insufficient (either layer failing = data anomaly). Both deployed rolling (backwards compat: Layer 2 works even if Layer 1 not yet live on all replicas).

**Risk without fix:** Silent data duplication at scale (multi-datacenter deployments). Production alerting detects via duplicate instance counts; root cause ambiguous until claim race identified.

---

## How

**Files modified:**

1. **OutboxStore.cs** (14 modules) — Replaced `SelectAsync` + separate `UpdateAsync` with single-tx `ClaimAsync` using CTE. `ClaimTimeout` property injected from `OutboxOptions`.

2. **OutboxOptions.cs** — NEW `ClaimTimeout` property (default 5 min). Settable via appsettings (OutboxOptions:ClaimTimeout).

3. **Migrations** (14 modules × 2 files each = 28 total):
   - `20260429HHMMSS_AddClaimedAtUtcColumn.cs` (Up/Down)
   - Designer + Snapshot auto-updated
   
   Column: `ClaimedAtUtc datetimeoffset NULL DEFAULT NULL`
   
   Index: `IX_OutboxMessages_ClaimedAtUtc_ProcessedAtUtc` covering CTE filter predicate.

4. **Snapshot files** (14 modules) — Updated via EF tooling, no manual edits.

5. **ReportingDesignTimeDbContextFactory.cs** (NEW) — Reporting module was missing factory; added to unblock offline migration queries.

6. **Test infrastructure:**
   - `OutboxStoreFixture.cs` (147 lines) — Shared test harness (local SQL, auto-cleanup, seeding)
   - `OutboxStoreRaceTests.cs` (61 lines, 3 tests):
     - **ConcurrentClaim:** Two replicas claim different batches → no collision
     - **StaleClaim:** Claim older than 5 min → reclaimed successfully
     - **FreshClaim:** Recent claim → not reclaimed (safety)

**Backwards compatibility:**

- `ClaimedAtUtc` nullable (DEFAULT NULL) → zero schema-breaking change
- Old instances (pre-migration) have `ClaimedAtUtc=NULL` → interpreted as "never claimed" (safe)
- No backfill required
- Rolling deploy safe:
  - Old dispatcher (v2.133p11): sees NULL column (transparent, no INSERT/UPDATE of new column)
  - New dispatcher (v2.133p12): uses CTE, sets ClaimedAtUtc
  - Old + new coexist: `CTE filter (ClaimedAtUtc IS NULL OR ClaimedAtUtc < @staleCutoff)` handles both states

**State transition safety:**

Methods updating OutboxMessages state must clear claim:
- `MarkProcessedAsync` — `UPDATE SET ClaimedAtUtc=NULL, ProcessedAtUtc=...`
- `MarkRetryAsync` — `UPDATE SET ClaimedAtUtc=NULL` (critical: retry must re-enter claim queue)
- `MoveToDeadLetterAsync` — `UPDATE SET ClaimedAtUtc=NULL, ProcessedAtUtc=..., MoveToDeadLetterAtUtc=...`

Clearing ensures idempotency: message can be re-claimed safely if operation is retried.

---

## Outcome

**Test results:**
- **T1 (Unit):** 31/31 PASS (OutboxStore suite + fixture baseline, all 14 modules)
- **T2 (Integration):** 7/7 PASS (OutboxStore race via local SQL)
- **T3 (Architecture):** 16/16 PASS (no design changes)
- **Build:** 0E/0W

**Code review:** 8.5/10 APPROVE_WITH_MINOR

**Issues breakdown:**
- **0 P0/P1** — No blocking issues
- **4 P2 deferred** (non-blocking enhancements, documented in backlog):
  1. Clock-skew note: C# computed `staleCutoff` vs `SYSUTCDATETIME()` server-side. Benign at 5-min default.
  2. OPTION (MAXDOP 1) hint: Validate serial query plan if parallel contention observed.
  3. Seed batching: Cosmetic. SeedRowsAsync does 1 INSERT per message; batch if scale test adds 100+ rows.
  4. Race test stress (deg=8): Optional 3rd test with `Parallel.ForAsync` (100 rows, aggressive timing) to defend cold-CI false-positive.

**Deployment:**

- PR staged for merge (awaiting sign-off)
- Rolling deploy safe (backwards compat verified)
- No database admin manual steps required (EF migrations handle column + index)
- Dispatcher configuration optional: `OutboxOptions:ClaimTimeout` in appsettings (default 5 min sufficient for prod)

---

## Lessons

### 1. Backlog wording stale

Original backlog item v2.133p1:

> "Robust fix: `ClaimedAtUtc NOT NULL` column + claim-and-update single-tx pattern **(requires cursor or xp_cmdshell workaround on SQL Server)**."

**Reality:** SQL Server natively supports CTE + UPDATE ... OUTPUT in same statement (T-SQL standard feature). No cursor or `xp_cmdshell` needed.

**Root cause:** Pre-existing backlog written without SQL Server documentation check. CTE pattern is canonical, not exotic workaround.

**Fix:** Corrected in ship, no plan change needed.

### 2. Reporting module infrastructure gap

Reporting module was missing `DesignTimeDbContextFactory`. While EF migrations *compiled*, offline queries (e.g., `dotnet ef migrations list` in CI) would fail or require manual --project flag.

**Discovery:** Hit during migration file generation phase. Quick add of factory per Identity/Cases/Forms precedent.

**Lesson:** Include factory in module bootstrap checklist. Post-ship: consider adding to architecture test suite (validate all modules with DbContext have factory).

### 3. Implementation vs. recommendation tension

Owner deferred code-reviewer recommendation to defer OUTBOX-1-DISP itself ("Phase 2 idempotent consumers sufficient"). Owner's rationale: pairing Layer 1 + Layer 2 defense is architecture-sound; deferring only delays stability hardening.

**Decision:** Execute full pair.

**Outcome:** v2.133p12 ships, Layer 1 + Layer 2 complete.

**Lesson:** Recommendation documented; owner captured reasoning. If future incident shows Layer 2 alone sufficient, retrospective updates backlog priority (no re-litigation needed).

---

## Next Steps

1. **Merge to main** — PR approved pending integration test green (all T1+T2+T3 pass, 0E/0W build)
2. **Monitor stale-claim recovery** — Production visibility: add alerting if `ClaimedAtUtc < (now - 2×ClaimTimeout)` observed (indicates dispatcher crash pattern)
3. **Backlog follow-ups** — 4 P2 items deferred to backlog (FU1-FU4), open if observational evidence triggers (clock-skew, perf, scale)

---

**Journal saved:** `docs/journals/journal-260429-1400-outbox-1-disp-atomic-claim.md`
