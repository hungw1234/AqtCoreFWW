# Journal — Audit-Chain Signed Tail Watermark (Cell #7 slice-1)

**Date:** 2026-07-15 · **Track:** Microservices completeness-matrix cell #7 "Audit-chain under distribution"
**Ships:** 1 (2-phase, code + tests + docs) — `MSVC-CELL7-TAIL-WATERMARK`
**Plan:** `plans/260715-1010-msvc-cell7-audit-chain-tail-watermark/` (2 phases)

---

## What & why

Redirected here from slice-1B (`HttpOrgUnitModuleClient` vertical) after scout revealed 1B pulled in a hidden service-auth + anti-spoof dependency and was speculative dead-code (no extraction scheduled). Cell #7 was the non-speculative alternative: a **live compliance correctness gap** — "a lost/removed audit event is an invisible gap; `VerifyAsync` only catches gaps between assigned SequenceIds."

**Design-first paid off.** A `kongming` design pass (verified the code itself) killed the obvious "outbox reconciliation" strategy — `audit.OutboxMessages` is an *outbound same-transaction* outbox (row exists ⟺ audit row committed), so it can't see ingestion loss. It reframed the gap into distinct threats: **L1** silent ingestion swallow (`AuditBehavior` catch), **L2** tail-truncation of signed rows, **L4** a *new* latent time-bomb (purge punches mid-chain holes → `VerifyAsync` false-tamper forever after ~2027). Chose **L2** for slice-1 (additive, no request-path touch), filed L1/L4 as follow-ups.

## What shipped

**`audit.ChainWatermarks`** — one HMAC-signed row per tenant recording the chain's `LastSequenceId` + `ChainHead`, keyed under the same HMAC key as the chain. Upserted at the end of every signing pass (incremental + backfill + 0-row heal) inside the existing per-tenant applock. `VerifyAsync` (both EF and raw-ADO paths, via one shared `EvaluateWatermark` helper) now reads the watermark **before** streaming and fails on: tampered watermark HMAC, missing watermark on a non-empty chain, or `maxSeenSeq < LastSequenceId` (tail truncation). Rollout self-heals via the existing chain-sweep (extended to enqueue watermark-missing signed chains).

**Closes L2:** a key-less DB-write attacker deleting the newest K rows of an otherwise dense+valid chain previously verified GREEN. Now → `IsValid=false`, "Tail truncation".

## The load-bearing correction

The advisor brief said "read watermark *after* streaming." The planner traced the signer's commit order (rows autocommit one-by-one, then the watermark, all inside the applock) and proved reading-after **false-positives the truncation check on every concurrent append** (a pass completing mid-verify pushes `LastSeq > maxSeenSeq`). Corrected to **read-before-stream** → `maxSeenSeq >= LastSeq` race-free for the common append case. Encoded as a code comment (the invariant, not a finding ID) at both call sites.

## Verification

- Build **0W/0E** (`-warnaserror`). `HmacChainTailWatermark` 5/5, `HmacChainTailTruncation` 6/6, **HmacChain regression 29/29**, `HmacChainProdLeastPrivilege` 5/5 (non-`db_owner`), `RlsPolicyPresence` 50.
- **RED-first proved empirically** (not assumed): stubbing `EvaluateWatermark` to `null` made `TailTruncation_DetectedByWatermark` FAIL (verify wrongly GREEN on a 5→3 truncation) → un-stub → PASS. The gap and the test's non-vacuity are both demonstrated.
- Code-review **APPROVE_WITH_NITS** — 0 Critical/High code-level. M1 (entity comment mis-attributed the single-NULL invariant to the DB index — it's the applock; filtered index excludes NULLs) + L1 ("L2" labels in test comments) fixed in-slice.

## Honesty — what verify-GREEN now attests, and what it does NOT

The single most important compliance deliverable. Attests: "the chain's tail extends at least to a key-sealed SequenceId." Does **NOT** attest (documented, routed to follow-ups / auditor questions, must not be over-read):
- **F1 watermark replay** — an attacker who captured an earlier valid watermark can truncate back to it and replay (needs external anchor / append-only history).
- **M2 total-erasure** — deleting all signed rows *and* the watermark row passes via the empty-chain branch, indistinguishable from a never-used tenant.
- **L1 ingestion-loss** (slice-2/D), **L4 purge holes** (urgent follow-up), key/Vault compromise.

## State after & follow-ups

- **`MSVC-CELL7-INGESTION-SWALLOW-FIX`** (slice-2, strategy D, ready) — kill the `AuditBehavior:59-63` silent swallow with durable retry + alert. Closes L1 (headline ingestion-loss).
- **`MSVC-CELL7-PURGE-CHAIN-REANCHOR`** (🔴 URGENT, L4) — purge×verify false-tamper time-bomb; needs a GOV auditor chain-epoch decision. Action: verify prod `AuditRetentionPolicies` for any policy <365d.
- **H1 ops carry (non-code):** prod `ConnectionStrings:AuditAdmin` must stay `db_owner`-capable — else the rollout-heal enumeration hides named-tenant chains (RLS FILTER), leaving dormant named chains un-healed → verify check-(b) false-fail. Verify is authoritative only post-rollout (active chains self-heal on next append). Same ops requirement the existing backfill/health-check already assume.
- 2 auditor questions appended to `docs/microservices-compliance-isolation-auditor-questions.md` (FU-A pending).

## Lessons reinforced

- `feedback-red-team-before-implement` / design-first — a `kongming` pass that reads the code killed a whole misframed strategy (outbox reconciliation) before any code, and surfaced a latent bug (L4) unrelated to the slice.
- `feedback-reverify-plan-before-execute` — the planner corrected the advisor's read-order (a real race), and Phase 01 caught the filtered-index nuance (EF excludes NULLs from a nullable unique index).
- `feedback-sa-dbowner-false-greens-rls-assertions` — the AC6 non-`db_owner` test is what makes a missing GRANT surface in CI (SqlException 229) instead of prod.
- Compliance honesty: name every residual next to what you close, so verify-GREEN is never over-read as "complete audit trail."

## Unresolved questions

- Prod `AuditRetentionPolicies` — any policy <365d already? (decides L4 urgency; not checked this session.)
- Auditor: chain re-anchor/epoch after purge acceptable? In-DB HMAC watermark sufficient vs external anchor (F1/M2)?
