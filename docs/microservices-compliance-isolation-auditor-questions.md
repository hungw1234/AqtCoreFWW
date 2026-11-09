# Compliance Isolation — Questions for the GOV Auditor (FU-A)

**Status:** Reviewed & send-ready (2026-07-16) — for the user to put to the GOV compliance auditor · **Date:** drafted 2026-07-13, reviewed 2026-07-16
**Feeds:** [ADR-012 amendment](./adr/012-activate-microservices-strangler-extraction.md) · SSOT
[`microservices-transformation-roadmap.md`](./microservices-transformation-roadmap.md)
**Why this exists:** the answer is the single gating input that decides whether ANY service needs *physical*
extraction for compliance — or whether logical isolation on the shared monolith DB suffices. Every downstream
wave-sequencing and extraction decision waits on it.

---

## 1. One-paragraph context for the auditor

The platform stores Vietnamese-GOV-regulated data (NĐ68 e-signatures, HMAC-chained audit trail, personal
data under consent). It is currently a **modular monolith**: 20 SQL schemas in **one database**, one
application, with **Row-Level Security** enforcing per-tenant isolation. We are deciding whether compliance
requires splitting services into **separate databases / physical instances**, or whether **logical isolation
inside the shared database** is sufficient. The distinction is expensive (separate deployables, DBs, ops) so
we need the auditor's requirement stated precisely.

## 2. What we have already established (so the auditor answers the *right* question)

- **Row-level tenant isolation is in force today** (RLS FILTER/BLOCK on `SESSION_CONTEXT('TenantId')`).
- **Cross-service *read* isolation is *achievable* on the shared database** — we empirically proved
  (C3 spike, 2026-07-13) that a SQL login granted only one schema **can** read that schema but is **denied**
  (`SqlException 229`) reads of another schema on the *same* database, as a non-`db_owner` login. So
  "service A cannot read service B's data" is deliverable **without** a separate database — via per-service
  schema-scoped logins.
- **Important honesty caveat:** that isolation is *achievable by configuration*, **not currently applied**.
  Today the whole app runs under **one** login (`aqtcore_app`) that spans all 20 schemas, and the dominant
  read path (Dapper) uses one shared connection. Applying per-service logins is a real (non-trivial)
  engineering task on this codebase — but it is a **configuration/wiring change on the existing database**,
  at a small fraction of the cost of extracting services onto separate databases/instances.

**Therefore the only open question is what compliance requires *beyond* read-isolation** — because
read-isolation itself does not need a database split.

### 2b. Scope fence — what this question does and does NOT cover

This asks **only about compliance obligations.** Independent DB-tier **scaling / HA / throughput** is a
**separate** decision on a **different gate** (load-evidence, not compliance) and is explicitly **out of scope**
for the auditor's ruling. The two must not be conflated:

- A **"no compliance requirement"** answer here does **not** forbid a future scale-driven database split — it
  only removes *compliance* as the driver. (As of 2026-07-16 the platform is pre-production with **no load
  numbers**, so no scale split is justified yet either — that gate is simply unmet, not the auditor's concern.)
- A **"yes"** on any §4 property makes *compliance* the driver for the named domain, independent of load.

We need the answer scoped this way so we can attribute each future split to the correct driver in ADR-012.

## 3. The core question

> **Does the GOV compliance obligation require any of the properties in §4 below — each of which a separate
> database / physical instance provides but a shared-database per-schema login does NOT — or is the
> requirement satisfied by "service A cannot read service B's data" (achievable on the shared database)?**

## 4. The residual properties — the actual decision drivers

For each: does compliance *require* it? A "yes" on any row re-justifies physical extraction for the affected
service and must be written into ADR-012 as the real driver (replacing the now-satisfied read-isolation).

| # | Property | Shared-DB per-schema login | Separate DB / instance | Ask the auditor |
|---|----------|----------------------------|------------------------|-----------------|
| a | **Backup / restore / PITR granularity** | One backup unit for the whole DB; cannot restore one service independently | Independent backup + point-in-time-restore per service | Must a service's data be independently backup-able / restorable for compliance? |
| b | **Encryption-at-rest key separation (TDE)** | One TDE key for the whole DB | Per-database key | Does compliance require *separate encryption keys* per service/data-domain? |
| c | **Blast-radius on credential / `sa` compromise** | A leaked shared-instance login (or elevated-context SQL injection) can reach every schema on that instance | A compromise is contained to one instance's data | Does compliance require that a single credential/instance compromise cannot expose all regulated domains at once? |
| d | **DBA / operator role separation** | DBAs of the shared instance can see all data | Per-instance operators, separable duties | Does compliance require segregation-of-duty so that no single operator can access all regulated data? |
| e | **Resource / noisy-neighbor isolation** | Shared CPU/IO | Per-instance resource governor | (Usually a *performance*, not compliance, driver — confirm it is not a stated obligation.) |

**Precision to extract per "yes": separate *database* vs separate *instance*.** These are different costs and
the properties split across them — so for any "yes", ask the auditor for the **minimum sufficient boundary**:

- **Separate database, same instance** (cheaper — own backup unit + own TDE key, still one SQL Server host)
  already satisfies **(a) backup granularity** and **(b) TDE key separation**.
- **Separate instance / host** (more expensive — own login surface, own DBAs, own blast-radius) is what
  **(c) credential blast-radius** and **(d) DBA segregation-of-duty** actually need; a separate DB on a shared
  instance does **not** contain an `sa`/host compromise.

So a "yes" on (a)/(b) implies *separate DB*; a "yes" on (c)/(d) implies *separate instance*. Record which, not
just "physical split" — it changes the target topology materially.

## 5. Decision framework — map each answer to the architecture

| Auditor answer | Implication | Action |
|----------------|-------------|--------|
| **Only read-isolation** ("A cannot read B") | No physical split needed for compliance | Apply per-service schema logins on the monolith (the deferred **FU-C** — real work, but shared-DB). Extraction, if ever, becomes scale/HA-driven, not compliance-driven. |
| **Blast-radius (c) and/or key separation (b) required** | Read-isolation is insufficient; a leaked login must not reach all domains | Physical/logical-DB separation *is* re-justified for the domains named. Sequence extraction of **S1** (auth authority) / **S2** (HMAC audit) first — decision #2 already puts them on physical instances. |
| **Backup (a) / DBA-separation (d) required** | Per-service backup/ops boundary needed | Separate databases (not necessarily separate instances) per named domain; write into ADR-012 as the driver. |
| **Resource (e) only** | Not a compliance driver | Handle as capacity planning; no compliance-mandated split. |

**Key consequence to communicate internally:** even the "only read-isolation" answer is **not free** — it
still requires shipping FU-C (per-service logins), which the 2026-07-13 red-team showed is a genuine
foundation slice (it must repoint the shared `DatabaseOptions`/Dapper connection abstraction and handle
cross-cutting schemas), not a trivial config toggle. So the auditor's answer decides *which* non-trivial
path we invest in, not *whether* there is work.

## 6. Suggested framing for the conversation

Lead with what is already true (tenant RLS in force; cross-service read-isolation achievable on the shared
DB) so the auditor evaluates the *delta*, not the baseline. Then walk §4 row by row and record a
yes/no/"depends" per property. A single "yes" on (a)–(d) is sufficient to re-open physical extraction for the
named domain; all "no" confirms the shared-DB isolation path.

## 7. Open questions (for the user, before/after the auditor meeting)

1. **Who is the auditor and what is the artifact form** — a formal written requirement, or a review meeting?
   (Determines whether we need this as a formal memo.)
2. **Governing instrument per property (anchor the question).** For a crisp auditor answer, each §4 "yes"
   should cite the specific obligation it flows from — do not ask "does compliance require X" in the abstract.
   Confirm with the auditor / legal **which instrument governs each property**: e-signature integrity (NĐ68
   e-signing), personal-data protection (the PDP decree), audit/log retention + tamper-evidence, and any
   sector-specific data-residency / duty-segregation rule. Leave the citation blank until legal confirms —
   **do not fabricate clause numbers.** This turns each row into "under [named obligation], is X required?"
3. **Scope of "regulated data"** — does the obligation attach to all 20 schemas, or specific domains
   (audit chain, signing, personal data)? The answer may make the question per-domain, not global.
4. **After the answer:** update ADR-012's driver, then either plan the FU-C re-scope (read-isolation path)
   or the S1/S2 extraction wave (physical-isolation path). Do not author either until the answer lands.

---

## 8. Addendum (2026-07-15) — audit-chain distribution questions (completeness-matrix cell #7 slice-1)

Filed alongside `MSVC-CELL7-TAIL-WATERMARK` (signed per-tenant chain tail-watermark, closes L2 key-less
tail-truncation on the HMAC-chained audit log). Two residual questions this slice's honesty-review
surfaced, separate from the physical/logical-DB question in §3–§5 above but on the same GOV audit track:

1. **Chain re-anchor / epoch after retention purge (L4).** `AuditLogPurgeJob` deletes the oldest rows of a
   tenant's HMAC chain once `AuditRetentionPolicy.RetentionDays` elapses. Once a purge reaches into a chain
   that has prior activity, the per-row dense/hole check in `VerifyAsync` will report a permanent
   false-tamper — it cannot distinguish "purged" from "attacker deleted a middle segment." **Question:** is
   a chain re-anchor / epoch model (the watermark becomes a new, auditable chain-start at the purge
   boundary) acceptable for compliance, or does full-history tamper-evidence need to survive retention
   purges?
2. **Is in-DB HMAC watermark protection sufficient, or does the chain need an external anchor?** The new
   tail-watermark (`[audit].[ChainWatermarks]`) is HMAC-signed with the same key as the chain itself, stored
   in the same database. It closes key-less tail-truncation (L2) but NOT: (F1) watermark-replay — an
   attacker who captured an older valid watermark can truncate the chain to that point and replay it,
   passing every check; (M2) total-erasure — deleting every signed row AND the watermark row verifies
   GREEN via the empty-chain-OK branch, indistinguishable from a tenant that never logged. Both require an
   anchor outside the database's own trust boundary (e.g., WORM storage, a Trusted Timestamp Authority, or
   periodic external publication of the chain head) to close. **Question:** does GOV compliance require
   defense against a Vault/key-compromised attacker (who could forge a new valid watermark), or is the
   current in-DB HMAC boundary (same trust tier as the chain itself) an accepted risk?

Same escalation channel as §7 above — record yes/no/"depends" per question; a "yes" on either reopens
scope for a follow-up slice (external anchor integration / chain-epoch design).
