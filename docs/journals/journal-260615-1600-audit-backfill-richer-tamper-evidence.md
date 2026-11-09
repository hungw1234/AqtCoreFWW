# Audit Backfill Richer Tamper Evidence

**Date**: 2026-06-15 16:00
**Severity**: High
**Component**: Audit module (chain backfill, forensic event enrichment)
**Status**: Resolved
**Commit**: `cc7aa82c` (3 ahead, NOT pushed)

## What Happened

Triaged a 4-follow-up T4 backlog row and extracted one genuinely code-actionable item: enriching the `chain-backfill` audit entry with caller IP, correlation ID, and per-tenant chain-head signatures before/after. The single backfill record is the ONLY forensic control over a privileged signature rewrite (SystemAdmin can rewrite all historical signatures under `audit_mutate_mode='signature'` with RLS bypass). Previously it recorded only row count; now it captures evidence of what changed across tenants.

## The Brutal Truth

The audit record was sterile — just a number. A SystemAdmin can invoke the backfill endpoint, rewrite 10,000 signatures silently, and the audit entry would show `{"rowsReSignedTotal": 10000}` with no visibility into *which* tenants, what chain-heads were before, or where the request came from. If a future forensic investigator runs queries on audit events, they'd see the magnitude but not the surface area (which tenants affected). In a multi-tenant SaaS, that's a blind spot.

The frustration is that this is follow-up work after the main 4-phase chain fix shipped. The temptation was to scope-creep all 4 items; instead, the right move was to audit the backlog row empirically and let the user decide what's actually actionable.

## Technical Details

**Backlog Triage (Before Coding):**
- Row listed "4 T4 follow-ups" from the prior chain-fix session
- Empirical audit: 2 were already closed in production (not visible in backlog), 1 was a compliance/PO scope decision (fingerprint version 3 schema expansion = tamper-evidence policy change, not code), 1 was genuinely open code-ready (this one)
- Surfaced the split rather than barelling into all 4

**Enrichment Design:**

1. **Caller context capture** — IP + correlationId via `IHttpContextAccessor`, same pattern as main `AuditService`. Captured at HTTP boundary (backfill POST `/audit/chain/backfill`), passed explicitly to the job handler.

2. **Per-tenant evidence snapshot** — `RunSigningPassAsync` (shared by incremental + backfill paths) now returns `SigningPassResult(Count, PreHead, PostHead)` instead of bare long. Pre-head read from prior anchor BEFORE applock reset, same transaction. Post-head read after signing complete. Both serialized to JSON `DataSnapshot` column via System.Text.Json.

3. **Backward compatibility** — kept `IHmacChainBackfiller.BackfillAllTenantsAsync` signature unchanged (Task<long>); added private `BackfillAllTenantsWithEvidenceAsync` returning rich tuple; public method delegates `.TotalRows` only. Zero contract break. 

4. **Optional parameters** — `triggeredByIp`/`correlationId` optional (`= null`) on `RunBackfillJobAsync` — semantically nullable + keeps existing actor-only integration test valid. Real enqueuer always passes them, so no silent-wire risk.

**Forensic Value:**
- An investigator can now query: "Which tenants were re-signed in the 2026-06-15 16:00 backfill?" (scan DataSnapshot JSON)
- "What was the chain-head before/after?" (transition captured)
- "Which IP initiated it?" (actor identity + IP, correlated with session logs)

## What We Tried

1. **Scope gate on all 4 T4 items** — read backlog row verbatim, probed empirical state (grep, test runs, migrations), categorized as 2 closed / 1 gated / 1 ready
2. **Kept contract stable** — public method signature unchanged, new enrichment hidden behind private delegate + tuple return inside
3. **Reused IHttpContextAccessor pattern** — same as main AuditService, reduces code variance

## Root Cause Analysis

**Why the prior backfill record was sterile:**
- Backfill endpoint was new (this session). Designed to record *that* a backfill ran + quantity.
- Forensic use-case (which tenants? chain-heads before/after?) wasn't explicitly modeled.
- Caller identity + request IP + per-tenant snapshots = high-value detail, but required explicit capture at POST boundary + composition down to the signing loop.

**Why scope-creeping all 4 items would have been wrong:**
- Item 2 (FINGERPRINT-V3) is a compliance/PO decision (expand tamper-evidence by versioning fingerprints), not code
- Items 3–4 (ops tasks) are zero-code (config wiring, rotation schedule, prod deployment readiness)
- Mixing decision + code + ops = context-switch tax; user picks priority once triage is clear

## Lessons Learned

1. **Triage before cooking.** A 4-item backlog row looks like 4 code tasks until you audit empirically. Probe state (migrations, tests, configs), categorize (closed / gated / code / ops), surface the split, let the user decide scope.

2. **Forensic controls are high-value but easy to forget.** The backfill endpoint is a privileged operation; the audit record is the ONLY trail. Enriching it with caller IP + per-tenant evidence is small-scale TDD (3 new test cases) with asymmetric payoff (future incident investigation).

3. **Review fold-in on asymmetry.** Reviewer caught: correlationId was captured into the column but missing from the JSON blob, while callerIp was duplicated. Folded correlationId into DataSnapshot for a self-contained record. Lesson: forensic records should be internally complete (don't assume column names survive RTBF scrub or schema evolution).

4. **Optional parameters for backward compatibility.** Marking `triggeredByIp`/`correlationId` as nullable kept an existing test valid (actor-only invocation from a fixture). Real code always passes them; the nullable default is a safety valve, not a design goal.

## Next Steps

**Immediate (done, not pushed):**
- Enrich `DataSnapshot` with pre/post chain-head + caller IP + correlationId
- API host 0W/0E (`-p:TreatWarningsAsErrors=true`)
- Audit Domain 188 + App 54 + Infrastructure 20 + Integration 61 (incl 6 new evidence test cases) + TenantIsolation prod-least-priv 4/4 all green
- Code review APPROVE_WITH_NITS (nits folded)
- Tester 262/262 passing

**Follow-up filed (T4, advisory):**
- `AUDIT-BACKFILL-EVIDENCE-RTBF-SCRUB` — anonymizer scrubs DataSnapshot for self-RTBF; consider excluding chain-backfill rows or persisting chain-heads to a separate immutable column (out of scope for this slice)

**Not code (user decides):**
- `FINGERPRINT-V3-WHO-WHERE` (compliance/PO: expand tamper-evidence schema)
- `AUDIT-ADMIN-CONN-PROD-WIRING` (ops: rotation schedule for elevated connection string)
- `AUDIT-CHAIN-BACKFILL-PROD-PO` (ops: prod backfill run post-cutover, SLA per QĐ742)
