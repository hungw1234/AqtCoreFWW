# Journal — 2026-06-16 — Audit Chain Sweep (Dormant Backlog)

## Session Goal

Investigate and fix system-health widget reporting "Unhealthy" (audit-unsigned-backlog check failing). Ship recurring sweeper for unsigned HMAC chains that stop receiving appends.

---

## What Happened

User reported `/health/ready` returning Unhealthy. Diagnosis chain:

1. **Initial probe:** 5 health checks; one failing: `audit-unsigned-backlog`
2. **DB scan:** 4 rows with `SequenceId=0` and `HmacSignature` already 'set' (not empty). All rows TenantId=NULL (system/global chain).
3. **Dev-chain check:** Dev tenant chain: 148 rows, MaxSeq=148 — healthy.
4. **Hangfire inspection:** 257/257 jobs Succeeded; 0 stuck/failed HmacChain jobs → not a jammed signer.

**Root cause discovered:**

Audit chain signing is **enqueue-on-append only** — `EnqueueHmacChain` fires when new data is written to that chain. A dormant chain (NULL/system tenant, no new appends in 10+ minutes) never re-fires the enqueue trigger. The SequenceId=0 backlog sits unsigned indefinitely. **No recurring sweeper existed** — only RTBF and log-purge jobs ran periodically.

Stale code comment in health check claimed a "5-minute recurring schedule" that did NOT exist.

---

## The Brutal Truth

This is an embarrassing gap. We shipped incremental per-tenant signing (commit `89581e3c` on 2026-06-15) with enqueue-only logic and **never wired the trigger for dormant chains**. The health check doc comment lied about a sweeper that wasn't there, masking the real issue. The system/NULL tenant is a low-traffic backlog by design, but "low-traffic" doesn't mean "silent forever when traffic stops."

A second system (MinIO down, now managed; ClamAV now in compose) went through the same "unmanaged infra" escalation pattern this week. The lesson: **if a dependency is fire-and-forget (enqueue-only), you MUST have a background sweeper**, or it rots in production undetected.

---

## Technical Details

**The audit chain schema:**

```
audit_events
  id (PK)
  tenant_id  → NULL for system/global chain
  sequence_id (0 = unsigned; 1..N = signed)
  hmac_signature (empty initially; set after signing)
```

**Signing flow:**

- Each write appends new rows → triggers `EnqueueHmacChain(tenantId)`
- Hangfire job `IncrementalHmacChainSignerJob` reads chains with `SequenceId=0`, signs them atomically, updates both fields together
- **Gap:** if a chain stops receiving appends, the enqueue never fires again

**Health check logic:**

```csharp
// query: SELECT MAX(TIMESTAMP), COUNT(*) FROM audit_events WHERE sequence_id=0
// check: (Now - MAX_TIMESTAMP) > 10 minutes && COUNT > 0 → Unhealthy
```

The comment claimed the sweeper ran every 5 minutes. It didn't.

---

## What We Tried

1. **First hypothesis:** signer skips NULL-tenant rows via a filter predicate — incorrect. Examined `ReadUnsignedAsync` and confirmed the signer filters only on `SequenceId=0`, not tenant. The signer WOULD heal them if enqueued.
2. **Verified Hangfire state:** all signing jobs succeeded (257/257). The gap was the missing TRIGGER, not signer failure.

---

## Root Cause Analysis

**Why this gap existed:**

1. **Enqueue-on-append is a one-way gate:** fires only when new appends land. Per-tenant applock makes redundant enqueues idempotent (no fork). But it also means a dormant tenant's backlog is orphaned.
2. **No recurring sweeper:** the codebase had RTBF + log-purge jobs (recurring) but NOT a chain-health sweeper.
3. **Stale documentation:** the health check comment mentioned a "5-minute schedule" that never existed, creating false confidence.
4. **System/NULL tenant is shared state:** unlike per-tenant chains (traffic-driven), the system chain is global and gets hit by sparse operations (batch jobs, admin tasks). It has no steady append cadence.

---

## Lessons Learned

1. **Fire-and-forget requires a sweeper:** any async task triggered only by new writes needs a periodic background job to heal dormant backlogs. If you skip the sweeper, you hide failures.

2. **Read the actual query, don't assume the predicate:** initial hypothesis about filter logic was wrong because I didn't trace the actual `WHERE` clause. The fix required reading `ReadUnsignedAsync` carefully.

3. **System/NULL tenant is global shared state:** test isolation broke because a new test seeded unsigned NULL rows and didn't clean up, contaminating the existing `HmacChainPerTenantTests.NullTenantChain_VerifiesIndependently` invariant (it asserts an exact count of 2 on the system chain). **Tests that touch NULL-tenant data must clean up via `IAsyncLifetime.DisposeAsync`** — raw delete with `audit_mutate_mode='archive'` — rather than weakening other tests' invariants.

4. **Document comment debt is real debt:** a comment claiming a sweeper that doesn't exist is worse than no comment. It creates false confidence and traps future investigators.

---

## The Fix

**One mechanism solved both:**

- **Clear the stuck rows now:** `AuditChainSweepJob` (`*/5` recurring, Hangfire)
- **Stop it recurring:** same job prevents future backlog accumulation

**Implementation:**

- `AuditChainSweepJob` (new, Hangfire recurring job `/5` minutes)
- `HmacChainService.EnqueueSignForBackloggedChainsAsync` — queries chains (including NULL) with `SequenceId=0` backlog via `db_owner` admin connection, enqueues incremental sign per tenant
- Immediate nudge at startup via `IHostedService`
- Key design choice: **enqueue-only + no audit writes** → prevents backfill loops and chain forks (idempotent per-tenant applock makes redundant enqueues safe)

**On restart:** the 4 stuck NULL-tenant rows were signed immediately (SequenceId→1-4, HmacSignature→computed), health check returned Healthy.

**Test cleanup:**

- New test `AuditChainSweepJob_SignsBackloggedChains` seeds unsigned NULL rows, asserts they're signed, then **cleans up all NULL rows via raw delete + audit_mutate_mode='archive'**
- Existing test `HmacChainPerTenantTests.NullTenantChain_VerifiesIndependently` remains unchanged (invariant protected)

---

## Decisions & Trade-Offs

1. **Sweep filter scope:** `SequenceId=0` only (narrower than health check's `SequenceId=0 OR HmacSignature=''`). The divergent state (seq>0 + empty-sig) is unreachable by construction (sign always writes both together). Corruption → manual admin backfill. Documented as intentional, not a gap.

2. **Enqueue-only design held:** no writes to audit_events during sweep (prevents loop). Idempotent per-tenant applock absorbs redundant enqueues.

---

## Build & Test Status

```
Build:          0W / 0E
HmacChain.Integration:  18/18 pass
Audit.Integration.Unit: 20/20 pass
Review:         APPROVE_WITH_NITS (M1: sweep filter scope documented, not fixed)
```

---

## Related Context

- **Prior:** commit `89581e3c` (2026-06-15) — per-tenant HMAC audit chain shipped. Health check added but sweeper logic never wired.
- **Parallel issues same week:** MinIO (infrastructure not in compose), ClamAV (same unmanaged pattern).
- **Memory ref:** MEMORY.md entries on Monday/Tuesday signing phase, audit-chain fix arc.

---

## Next Steps

None immediate for this slice. Chain signing is now self-healing. Dormant tenants' unsigned backlogs clear within 5 minutes of the next scheduled sweep.

**Status:** DONE | Commit `53c098c6` | 0-ahead | Pushed origin/main
