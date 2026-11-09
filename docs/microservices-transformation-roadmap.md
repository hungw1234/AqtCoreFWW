# Microservices Transformation Roadmap

**Last updated:** 2026-07-16 · **Status:** Foundation complete (Sóng-0); **Sóng-1 (S4) extraction DEFERRED 2026-07-13 (user decision)** — the C3 spike disproved the compliance driver, so S4-for-compliance is off the wave path; physical extraction is reserved for S1/S2 (see the 2026-07-13 update below) · **monolith-side audit read-isolation (EF+Dapper) CODE-COMPLETE 2026-07-14, ops-activation-pending** (see §5 below) · **completeness-matrix cell #3 slice-1A (cross-module contract-purity arch-guard) SHIPPED 2026-07-15, test-only** (see §4/§5 below) · **completeness-matrix build-now CODE track EXHAUSTED 2026-07-16** — cell #10's retry-config integrity guard closed the last cheap unblocked item; sole remaining microservices gate = **FU-A** (non-code, GOV auditor question) (see §4/§8 below) · **Priority:** ⬇️ **DEMOTED 2026-07-16** — superseded by "ship a stable modular monolith for launch" (#1); microservices sequenced **post-stable-launch** + gated on FU-A + production load-evidence (was 🥇 #1 user 2026-07-10) · **Branch:** `main`
**Mandate ADR:** [ADR-012 — Activate Microservices Strangler Extraction](./adr/012-activate-microservices-strangler-extraction.md) (supersedes [ADR-001](./adr/001-modular-monolith-over-microservices.md))
**Backlog anchor:** `MSVC-5SVC-TARGET`, `MSVC-WAVE0-*` in `backlogs/backlog.md`

> **⚠️ 2026-07-13 red-team + gating decisions (user).** A 4-lens red-team of the Sóng-1 plan-in-embryo (16 findings, all accepted — `plans/reports/from-red-team-to-planner-song1-s4-extraction-adjudication-report.md`) showed S4's compliance-isolation justification is unproven and its boundary is mis-scoped. **User decisions 2026-07-13:**
> - **C3 (RESOLVED 2026-07-13 by spike — verdict `plans/reports/from-c3-spike-to-planner-schema-grant-isolation-verdict-report.md`):** the schema-GRANT spike PASSED — a non-`db_owner` login granted only `SELECT ON SCHEMA::[files]` reads `[files]` but is DENIED `[audit]` (SqlException 229) on the current shared monolith DB, non-vacuously. **Read-isolation proven ⇒ compliance no longer justifies extracting S4** for the shared-instance tier (same guarantee at a monolith config-change cost). Residual (per-service backup / TDE keys / blast-radius / DBA-role / resource isolation) → GOV auditor question. **DECIDED 2026-07-13 (user): defer S4-for-compliance; reserve physical extraction for S1/S2** — their auth-authority / HMAC-single-writer needs are genuine physical-instance drivers that schema-GRANT cannot satisfy, unlike S4's now-satisfied read-isolation. This reverses the locked 2026-07-06 S4-first pick. The residual GOV auditor-delta stays open as the **only** condition that could reopen S4 (not yet put to the auditor). **Do not author Sóng-1 P0–P4.** Wave re-sequencing (which service becomes the first extraction wave) is a follow-up needing its own planning — S2 is still "hardest, goes late" and S1 is still "last" (§5), so "reserve for S1/S2" is a driver-priority statement, not an automatic next-wave.
> - **C5:** **Sóng-1 deferred** — do not author P0–P4 until the C3 spike resolves.
> - **C1:** signing-ownership deferred — the NĐ68 signing engine (142 `.cs`, PAdES/Viettel/USB) lives in `identity`/S1, **not** `files`/S4, so S4's "NĐ68 crypto pentest" isolation driver (§2) is mis-attributed; resolve after C3.
> - **H6 → decision #9:** cross-service transport = **HTTP + extend the existing `TenantPropagationHandler`** (not gRPC); reuse shipped code, revisit gRPC only for a measured chatty edge.
> Inline corrections folded below: `search` is a *deleted* module (not a scaffold); §3's "no gating-blocker remains" is superseded.

> **Source-of-truth note.** This document is the durable, self-contained master plan for the microservices
> transformation. It consolidates a set of session-local analysis artifacts (`plans/reports/architect-260706-0937-…master-synthesis`,
> three deep-dive reports on audit-chain / authz-PEP-PIP / consistency-substrate, and the `brainstorm-260704-1918`
> 5-service operational deep-dive). Those reports live under `plans/` (gitignored) — every load-bearing decision
> from them is captured inline here, so this file does not depend on their presence. Where a source claim was later
> corrected by red-team review, the corrected form is used (corrections are called out).

---

## 1. Mandate & Context

The platform began as a **modular monolith** (ADR-001) built strangler-ready: 16 modules, each owning its own
SQL Server schema, decoupled through SharedKernel abstractions. ADR-001 explicitly reserved a "Phase 2" option to
extract modules into separate deployables *if scale demands justified the operational cost*.

**That option is now activated — but the driver is not scale.** The decision (2026-07-06) rests on:

- **Greenfield, not yet in production.** No migration debt, no hybrid-risk, no zero-downtime constraint during the
  build. This is the ideal window to design the target correctly from the start rather than retrofit later.
- **Compliance-isolation, not horizontal scaling.** Vietnamese GOV obligations (NĐ68 signing crypto, HMAC-chained
  audit integrity, auth-authority isolation) make *physical/logical boundary isolation* — not throughput — the
  primary architectural pressure. Auditors need "this service cannot read that service's data" to be demonstrable.

**Target:** 5 coarse-grained services (below), via the **strangler-fig** pattern — monolith-side foundations first
(done), then extract one service per wave. The guiding constraint throughout: **do not build a distributed monolith**
disguised as microservices. The hard capabilities (§4) are what separate the two.

---

## 2. Target — 5 Coarse Services

Coarse-grained by design: 5 services, not 16. Each service owns a cohesive set of modules that already share
transactional and trust boundaries. `S4` is designed **"6-ready"** — `reporting` is kept as an internal split-seam
(own Hangfire queue, DbContext, read-model store, DI namespace) and promoted to a standalone `S6` only when a
concrete trigger-metric fires (§ below).

| Service | Modules | Load profile | Isolation driver |
|---------|---------|--------------|------------------|
| **S1 · Identity & Access** | `identity` (+Consent) · `organization` · **AuthServer** (OIDC host, :5002) | Latency-sensitive, high-HA | Auth authority — mandatory dedicated pentest |
| **S2 · Audit / Alert / Notify** | `audit` · `notifications` · Alerting (schema `alerting`) | Async, write-heavy, background | **HMAC chain single-writer** — integrity isolation mandatory |
| **S3 · Process & Forms** | `workflow` · `rules` · `businessprocess` · `forms` · `collaboration` | Business core, saga-heavy | Heaviest per-tenant RLS surface |
| **S4 · Documents / Sign / AI** | `files` (PAdES) · `ai` · `reporting`\* | Resource-isolated: CPU + GPU + Hangfire | NĐ68 signing — dedicated crypto pentest |
| **S5 · Edge Gateway & Config** | `integration` (YARP / ApiKey / Webhook / ErrorCatalog) · `masterdata` · `systemparams` | Thin edge + slow-changing reference data | M2M attack surface — edge hardening |

\* `reporting` = internal split-seam inside S4; promote to **S6** when the trigger fires: *P95 signing latency of S4
rises ≥ X% during the report-job window* (Hangfire burst contends for CPU). A measurable trigger, not a hunch.

**⚠️ Coverage gap — resolved 2026-07-13 (red-team correction).** The repo shows **16** module directories but the
map assigns **15**. **`search` is not an *unmapped* module — it is a *deleted* one** (removed in PR #45 `449b18fa`,
2026-04-20, "remove AqtCoreFW.Search module (dead code)"; only stale `obj/` build artifacts remain — 0 hand-written
`.cs`, 0 `.csproj`, journal `journal-260421-0949-remove-search-module.md`). The earlier "empty scaffold, never built"
framing was wrong. It is **not** an open mapping question: the correct action is to drop the stale artifacts
(`git rm` if tracked). The deferred `/ai/search` + `/copilot` FE routes are a *separate* future AI-module feature
(→ S4/AI if/when built), not a resurrection of the old business-search module. Does not affect any wave.

### Cross-service call edges (post-split)

| Edge | Direction | Mechanism |
|------|-----------|-----------|
| Identity read (role/group, chatty) | S3 → S1 | gRPC + cache (`ICacheService` already present) |
| OrgUnit subtree (Workflow → OrgUnit) | S3 → S1 | gRPC — **cross-service** (red-team C4 corrected the earlier "internalized" claim) |
| Files (Forms/Workflow → Files) | S3 → S4 | gRPC |
| Signing ceremony (session → cert → attach) | S3 → S1 → S4 | **async stateful saga, forward-recovery** (decision #7) |
| Audit write | every service → S2 | event (`AuditLogAppendedIntegrationEvent`, already prepared) |

---

## 3. Locked Architectural Decisions (2026-07-06 — 8/8, + #9 2026-07-13)

These gate the design of every downstream wave. **Superseded 2026-07-13:** the 2026-07-06 claim "no open
gating-blocker remains" no longer holds — the Sóng-1 red-team **reopened two gating items** (C3 compliance-isolation
*proof*, C1 signing-ownership) and **added decision #9** (transport). See the 2026-07-13 update near the top.

| # | Decision | Locked choice | Consequence |
|---|----------|---------------|-------------|
| 1 | S3 DB granularity | **DB-per-service, schema-per-module** (S3 = 1 DB) | `CrossModuleUnitOfWork` stays alive → "0 saga" *within* S3 is real; `WorkflowInstance ⊕ FormSubmission` atomic-local |
| 2 | DB topology | **Hybrid** — S1+S2 on physical instances; S3/4/5 logical DBs on a shared instance + least-priv logins (no cross-DB query) | Isolation compliance where it matters (auth+audit) at ~1/5 the instance cost. *Reversible to physical-per-service if an auditor demands.* **⚠️ C3 (RESOLVED 2026-07-13):** the schema-GRANT spike PROVED per-service read-isolation on the shared instance (`files_svc_app` reads `[files]`, denied `[audit]`, non-vacuous non-`db_owner`) → logical-DB-on-shared-instance adds **no read-isolation** over a per-schema login. Compliance no longer justifies S4 extraction for this tier; only the auditor-delta (backup/TDE/blast-radius/DBA/resource) could. See the 2026-07-13 update + verdict report. |
| 3 | `dataSnapshot` after S2 split | **Envelope-encrypt into the event → S2 stores ciphertext** | Centralized audit (one auditor query); PII safe on-bus + at-rest; RTBF = crypto-shred; chain still excludes `dataSnapshot` from the fingerprint |
| 4 | Chain order | **Receive-order chain + `OccurredAt` (tamper-proof)** | No global sequencer; chain = integrity, `OccurredAt` = timing; needs document + auditor sign-off |
| 5 | Token validation | **JWKS local-validate + short token (5–15m) + revocation event** | S1 leaves the request path (service autonomy); revocation near-instant via expiry + event |
| 6 | Extraction schedule | **Build now, prioritized; greenfield, pre-production** | The former "DEFER-track" (CI-N / mesh / signing-saga) is justified to build now, not "someday" |
| 7 | Signing consistency | **Async stateful saga + idempotent forward-recovery** | session pending → signed → attached; retry-forward (never undo a PAdES signature); every step idempotent |
| 8 | Compensation (intra-service) | **Not needed — UoW handles it** (folds into #1/#7) | S3-internal writes are atomic-local; only cross-service signing uses forward-recovery |
| 9 | Cross-service transport | **HTTP + extend `TenantPropagationHandler`** (not gRPC) — decided 2026-07-13 (red-team H6) | Reuse the shipped `X-Tenant-Id` + service-Bearer handler; the propagation seam targets the typed `HttpClient`, not a gRPC factory branch. Revisit gRPC only for a measured chatty edge. Anti-spoof (server-side claim validation / mTLS) still required before any edge goes live (red-team C2/H5). |

---

## 4. Completeness Matrix — the real work boundary

14 standard microservices capabilities, graded against the current codebase. The core insight from the master
synthesis: **the codebase is already ~80% microservices-ready in the hard places** (interface-based seams,
`CrossModuleUnitOfWork` DTC-free, audit–signer decoupled, authz resolved through an abstraction + cache). The
earlier reports over-invested in the *easy, already-good* areas (mesh/helm/CI-matrix) and left the **five hard cells
empty**. Those five — not the infra — are what separate real microservices from a distributed monolith.

| # | Capability | Status | Note |
|---|-----------|--------|------|
| 1 | Service boundaries | ✅⚠️ | 5-map is sound; `search` unmapped (§2); "OrgUnit internalized" was wrong (C4 — it's cross-service) |
| 2 | DB-per-service + migration | ⚠️ | expand/contract exists; **schema backward-compat under rolling deploy is undug** |
| **3** | **Sync inter-service + auth propagation** | ❌ | 18 `I*Client` in-process → remote = the largest workstream; tenant/JWT-claim over gRPC undesigned. **Slice-1A SHIPPED 2026-07-15 (test-only):** a contract-purity arch-guard locks all 18 `I*Client` signatures remote-serialization-safe (`JsonDocument`/`JsonElement` carve-out for dynamic JSON payloads) — precondition for slice-1B's first `Remote*Client` vertical; the transport itself (decision #9, HTTP) is still unbuilt. **Slice-1B SHIPPED 2026-07-16 (client-side only):** `HttpOrgUnitModuleClient` implements the HTTP/JSON transport for all 6 `IOrgUnitModuleClient` methods, selectable via a new `OrgUnitModuleClient:Transport` config toggle (default InProcess = zero runtime change) — the reusable template for the other 17 `I*Client` seams. NO provider endpoint, NO service-to-service auth yet (both deferred to physical extraction, follow-up `MSVC-CELL3-SLICE1B-PROVIDER-ENDPOINT`, auditor-gated). |
| 4 | Async (outbox / inbox / idempotency) | ⚠️→✅ | Real bug fixed in Sóng-0 Phase-1; exactly-once for audit events still needs S2-split hardening |
| **5** | **Consistency (saga)** | ❌→⚠️ | Decision #1 makes intra-S3 atomic-local real; cross-service signing saga (#7) is the remaining build |
| **6** | **Authorization model** | ❌→⚠️ | 80% PEP/PIP already; Sóng-0 Phase-3 did the vocabulary-ownership carve-out; remote resolution + JWKS remain |
| **7** | **Audit-chain under distribution** | ❌→⚠️ | Live gap: a lost event = an *invisible* audit gap (`VerifyAsync` only catches gaps between assigned SequenceIds). **Slice-1 SHIPPED 2026-07-15:** signed per-tenant chain tail-watermark closes L2 (key-less tail-truncation). **Slice-2 SHIPPED 2026-07-15:** durable Hangfire retry (`AuditIngestionRetryJob`, via new shared `AuditEntryPersister`) closes L1 (ingestion-loss) — a synchronous audit-write failure is now enqueued to an idempotent retry instead of swallowed; only a double failure (persist AND enqueue) rethrows to a Critical log + `audit.ingestion.lost` metric. Residual F1 (watermark-replay) / M2 (total-erasure) / L4 (purge-induced false-tamper) still documented NOT closed; a NEW gap surfaced (`MSVC-CELL7-INGESTION-PII-JOBARG` — Confidential/PII fields ride unprotected in the Hangfire job-args table, compliance-blocked) → follow-ups + auditor questions filed; `MSVC-CELL7-PURGE-CHAIN-REANCHOR` (L4) stays 🔴 URGENT/auditor-gated. |
| 8 | Contract governance (Pact / version) | ✅ | Sound design (deferred) |
| 9 | Observability (OTEL / SLO / trace) | ✅ | Strong |
| **10** | **Resilience** | ⚠️ | Design exists; distributed-test substrate is being dug. **Slice-1 SHIPPED 2026-07-16:** (a) hosted-service boot-guard — `HostedServiceTestHelper` drives a real registered `IHostedService` through the app DI container so a boot-time regression (missing DI, throwing `StartAsync`) surfaces in an integration test instead of prod startup, closing the non-seeder false-green; the strip predicate was DRY-extracted to `HostedServiceStripFilter` and hardened to fail-safe-retain only the interface-pinned factory-seeder shape (a broader first cut wrongly retained Hangfire's job server — caught in review, fixed + regression-tested). (b) First distributed-fault dimension — **message duplication** proven against a claim-first at-most-once consumer (`FormSubmissionAcknowledgedConsumer`). **Slice-2 SHIPPED 2026-07-16:** second distributed-fault dimension — **timeout/transient-retry** proven against the effect-idempotent throws-to-retry consumer (`WorkflowFinalizedFormSubmissionConsumer`) through the REAL MassTransit retry machinery (test-local `Immediate(N)` policy): a transient failure converges on retry (effect applied, no fault) and a persistent failure exhausts retries into a visible `Fault<T>` (never a silent drop) — closes the false-green where the sibling unit test only proved the consumer throws via a mocked `ConsumeContext`, never driving real redelivery. **Slice-3 SHIPPED 2026-07-16:** third distributed-fault dimension — **partition/broker-outage** — proven against the real durable transactional-outbox relay (`OutboxDispatcher`, prod-driven by `OutboxProcessor`): a transient outage retains the row for retry and converges after recovery (zero loss); a persistent outage past `DeadLetterAfterRetries` lands the row visibly in `messaging.dead_letters` (never a silent drop). **Reordering RETIRED AS THEATER** this session — grounding proved `AuditLogAppendedConsumer` is a NOOP with no consumer holding cross-message ordered state, so a reordering test needs an activated projection consumer (extraction-dependent). Cross-service dedup + chaos remain extraction-gated/low-ROI. **Retry-config integrity guard SHIPPED 2026-07-16** (`MSVC-CELL10-RETRY-CONFIG-GUARD`, static-source arch-guard on `MessagingRegistration.cs` asserting `UseMessageRetry` on both transports — the last cheap unblocked cell #10 item) → **cell #10 is now FULLY monolith-exhausted** (no cheap unblocked items remain; only extraction-gated/low-ROI residuals). |
| 11 | Deploy / CI-CD | ✅ | Correct to defer (red-team) |
| 12 | Service discovery / health | ✅⚠️ | health-checks exist; discovery implicitly deferred to mesh |
| 13 | Secrets (Vault per-service) | ✅ | Sound; extraction *improves* key isolation |
| 14 | Org / ops capability (1 team) | ⚠️ | Risk noted; real capacity to run 5 distributed compliance-critical services not yet assessed |

**The five ❌/hard cells (#3 #5 #6 #7 + part of #10)** are the transformation's true scope. Sóng-0 already closed the
monolith-side portions of #4 and #6.

---

## 5. Wave Sequencing

Sequence corrected from the original report order. **Async-ready ≠ compliance-low-risk** — S2 (audit) is *not*
first; it goes late, after messaging matures, because a distributed audit chain is the hardest correctness problem.

| Wave | Scope | Status | Rationale |
|------|-------|--------|-----------|
| **Sóng 0** | Monolith-side foundation (outbox, atomicity, authz carve-out) | ✅ **DONE** (2026-07-08) | Real, reversible, unblocks every later wave. **← current position** |
| **Gating** | Lock 8 architectural decisions with GOV/auditor | ✅ **DONE** (2026-07-06, §3) | Without these, every runtime wave is speculative |
| **Sóng 1** | Extract **S4** — **deferred, no compliance driver** | ⏸ **Deferred (user 2026-07-13)** — C3 disproved the driver | Schema-GRANT gives S4's read-isolation on the shared instance (spike passed) → extracting S4 for compliance is not justified. **User decision 2026-07-13: defer S4-for-compliance; reserve physical extraction for S1/S2.** Residual GOV auditor-delta stays open as the only S4-reopener. First extraction wave now **TBD** (re-sequencing follow-up — S2 stays late, S1 last per rows below). Do not author P0–P4. |
| **Sóng 2** | Extract **S5 + S3** | ⬜ Not started | S3 opens only after decision #1 (locked) is honored in the split |
| **Sóng 3** | Extract **S2** (Audit) | ⬜ Not started | Only after outbox + dedup + auditor sign-off mature (§ deep-dive ①) |
| **Sóng 4** | Extract **S1** (Identity hub) | ⬜ Not started | Last; the Sóng-0 vocabulary-ownership carve-out makes the hub-split lighter |

> **2026-07-13 (evening) — monolith-side logical read-isolation, EF-first.** Independent of the wave extractions above: a validated plan (`plans/260713-1720-logical-isolation-connection-resolver-seam`, gitignored) isolates the `[audit]` read boundary on the shared DB via a least-privilege login (EF per-context + `HmacChainService`), proven by a non-`db_owner` test (DENY `[audit]`, SqlException 229). A prior behavior-preserving connection-resolver *seam* was **killed by red-team** — it isolated nothing (UoW-active reads → shared write conn `DapperReadDbConnection.cs:94-97`; EF + `HmacChainService` reads bypass it; adjudication in the plan's `reports/`). UoW-active + admin/db_owner reads are documented ADR-012 exceptions. This supersedes FU-C's "apply per-service schema-scoped logins" framing with a scoped, testable slice. Backlog: `ISO-EF-FIRST-AUDIT-BOUNDARY`, `ISO-DAPPER-KEYED-DI-INCREMENT`. **`ISO-EF-FIRST-AUDIT-BOUNDARY` SHIPPED 2026-07-14** (`4b74114e` impl + `a5e890ad` oracle): 14 non-audit module EF contexts route through `NonAuditConnectionString.Get` (NonAudit login denied `[audit]`, fallback Default = opt-in), proven by `AuditBoundaryEfIsolationTests` (EF DENY `[audit]` SqlEx 229) + arch-guarded.
>
> **2026-07-14 — `ISO-DAPPER-KEYED-DI-INCREMENT` CODE-COMPLETE (commit `f0ad95e3` + this session's Phase-3 proof/provisioning/ADR, commit pending), ops-activation-pending.** Completes the monolith-side EF+Dapper audit read-path repoint: the default (unkeyed) `IReadDbConnection` (Dapper) resolves `NonAudit`; 12 audit-capable consumers use the keyed `"audit"` reader on `Default` via `[FromKeyedServices(ReadConnectionKeys.Audit)]`; both SESSION_CONTEXT bypass openers inherit the per-registration login; the 3 API-only shared-infra EF contexts (Alerting/ApiKey/Webhook) fold onto `NonAudit` (Messaging stays `Default`, AuthServer-coupled, documented exclusion). Proven by `AuditBoundaryDapperIsolationTests` 5/5 + `AuditKeyedReaderAllowlistTests` (closed 12-consumer allowlist) + extended `AuditBoundaryEfWiringGuardTests`; Architecture.Tests 174/174, regression 652 green, build 0W/0E. Audit read-isolation now covers **EF + Dapper + Hmac**, but — per the corrected ADR-012 language — **enforced only when the prod `NonAudit` login is provisioned (opt-in), NOT a blanket "complete."** Prod activation artifact: `scripts/260714-nonaudit-login-provisioning.sql` (GRANT DML+DDL on the 19 non-audit schemas, DENY `[audit]`, migration-vs-login ordering runbook + rollback + Vault KV `ConnectionStrings__NonAudit`) — **running it is an ops/DBA task, not code**. Standing exceptions: UoW-active reads, admin/`db_owner`/backup/seeders. Two small follow-ups filed in `backlog.md`: `ISO-AUDIT-MODULECLIENT-SECURITYHEALTH-REFACTOR` and `ISO-NONAUDIT-PROD-FAILFAST-HEALTHCHECK` (both deferred out of this increment). **This closes the monolith-side read-isolation thread; the first physical-extraction wave stays TBD, gated on the still-open FU-A auditor question (§ backlog).**
>
> **2026-07-14 (later) — `ISO-AUDIT-MODULECLIENT-SECURITYHEALTH-REFACTOR` ✅ RESOLVED.** The "2 identity SecurityHealth keyed reads" standing exception above is now CLOSED: `GetAuditIntegrityStatusQuery`/`GetAuditRetentionStatusQuery` route through `IAuditEvidenceClient` (Audit.Contracts, +`GetIntegrityStatusAsync`/`GetRetentionPolicyStatusAsync`) instead of the keyed `[audit]` Dapper reader — identity now holds no `[audit]`-capable DB credential. New edge `Identity.Application` → `Audit.Contracts`. Allowlist 12→10. `ISO-NONAUDIT-PROD-FAILFAST-HEALTHCHECK` remains deferred.
>
> **2026-07-14 (T4) — `ISO-NONAUDIT-PROD-FAILFAST-HEALTHCHECK` ✅ RESOLVED, last ISO-* follow-up closed.** Prod-observability for the still-opt-in boundary now exists: `NonAuditIsolationHealthCheck` (Degraded, never Unhealthy, tag `audit-isolation`) + a warning-only API startup log fire when `Production && ConnectionStrings:NonAudit` is unset. Does not change enforcement — still opt-in pending ops running `scripts/260714-nonaudit-login-provisioning.sql`; makes the dormant state visible instead of silent.
>
> **2026-07-15 (later) — completeness-matrix cell #7 slice-1: signed per-tenant chain tail-watermark.** New `[audit].[ChainWatermarks]` (HMAC-signed, 1 row/tenant) upserted at the end of every signing pass (incremental+backfill+0-row heal, existing per-tenant applock); `HmacChainService.VerifyAsync` gains tail-attestation via a shared `EvaluateWatermark` helper (EF+raw-ADO parity, watermark read before the chain stream — race-free against a concurrent append). Migration `20260715045449_AddAuditChainWatermarks`. **Closes L2 tail-truncation** (a key-less DB-write attacker deleting the newest rows of an otherwise dense+valid chain previously verified GREEN; RED-first proved it empirically). Tests: `HmacChainTailWatermarkTests` 5/5 + `HmacChainTailTruncationTests` 6/6 (new) + `HmacChainProdLeastPrivilegeTests` 5/5 (+1) + `RlsPolicyPresenceTests` 50; build 0W/0E; code-review APPROVE_WITH_NITS. **HONESTY — documented, NOT closed:** F1 (watermark-replay — an attacker holding an older valid watermark truncates to it and replays), M2 (total-erasure of every signed row + the watermark row verifies GREEN via the empty-chain-OK branch), L1 (ingestion-loss, `AuditBehavior.cs:59-63` swallow), L4 (purge-induced permanent false-tamper once retention purge reaches a chain with prior activity). Follow-ups filed: `MSVC-CELL7-INGESTION-SWALLOW-FIX` (L1), `MSVC-CELL7-PURGE-CHAIN-REANCHOR` (🔴 URGENT, L4). Auditor questions appended to `docs/microservices-compliance-isolation-auditor-questions.md`. **Does not change wave-sequencing** — this is monolith-side hardening of the existing HMAC chain, orthogonal to the S1–S4 extraction waves.
>
> **2026-07-15 (later still) — completeness-matrix cell #7 slice-2: durable audit-ingestion retry closes L1.** `AuditService.LogInternalAsync` (the standalone audit-log path) now catches a synchronous persist failure and hands the frozen entry (`AuditIngestionIntent`) to a new idempotent Hangfire job `AuditIngestionRetryJob` (`[AutomaticRetry(Attempts = 10)]`, insert-then-catch-unique-key idempotency on `MessageId`, dead-letter = Hangfire Failed state) instead of the previous silent swallow (`AuditBehavior.cs:59-63`'s post-commit `catch` + `LogWarning`, no rethrow/retry). Only a double failure (persist AND enqueue) rethrows, so `AuditBehavior` now logs Critical and the new `audit.ingestion.lost` OTel counter fires (3 counters total — `fallback_enqueued`/`recovered`/`lost`, meter `AqtCoreFW.Audit`). The write itself was extracted into a new shared `AuditEntryPersister` (row+outbox+HMAC-enqueue, used verbatim by both the sync path and the retry job) whose write span deliberately ignores the request `CancellationToken` — closing the dominant real L1 trigger (a request-abort after business commit dropping the row) — and pins SESSION_CONTEXT to the row's tenant (not the ambient one) so the retry job, which runs with no ambient tenant, still persists under the prod least-privilege login. **Review-fixed in-slice (both false-green under sa/db_owner tests):** a Critical (the tenant-pin above, proven by new non-`db_owner` test `AuditIngestionRecoveryLeastPrivilegeTests`) and a Medium (insert-then-catch idempotency rather than a read-first dedup, for the same RLS-under-least-priv reason). **Verify:** Audit.Integration 82/82; TenantIsolation.Tests 56/56; Audit.Infrastructure unit 23/23; SharedKernel AuditBehavior 10/10; build 0W/0E; code-review REQUEST_CHANGES → all blockers resolved in-slice. **Deferred (compliance-blocked, NOT closed):** `MSVC-CELL7-INGESTION-PII-JOBARG` — the frozen intent carries Confidential/PII fields (`IpAddress`/`UserName`/`DataSnapshot`) as raw Hangfire job args in `[hangfire].[Job]`, outside the RLS/HMAC/retention compliance boundary; a poison (Failed) job persists that PII indefinitely there. Needs compliance-owner sign-off before a fix. **Residual, unchanged/carried:** F-C (process-crash window between business commit and fallback enqueue — closes only at physical extraction), F-G (no independent durable store — a whole-DB outage still owes no audit row on a single-DB monolith), fail-closed audit ("no audit, no action" per QĐ 742, vs. this slice's fail-open-with-recovery design) — candidate FU-A auditor question; F1/M2/L4 carried unchanged from slice-1 (above). Does not change wave-sequencing.
>
> **2026-07-15 (test-only) — completeness-matrix cell #3 slice-1A: cross-module client contract serialization-safety arch-guard.** New `CrossModuleClientContractSerializationSafetyTests` (4 facts, `tests/architecture/AqtCoreFW.Architecture.Tests/`) locks all 18 `I*Client` contracts under `src/shared/*.Contracts` to remote-serialization-safe signatures — a default-deny recursive type checker over every method's params+return, with 2 carve-outs (`object`-as-collection-value, `JsonDocument`/`JsonElement` for dynamic JSON payloads, e.g. `IRuleEngineModuleClient.EvaluateAsync`). 12 `*.Contracts` `ProjectReference` entries added to `AqtCoreFW.Architecture.Tests.csproj` so reflection can inspect every contract. The guard caught 1 real finding (the `JsonDocument` param) that manual scout missed; user decided to whitelist it rather than change the contract. `Architecture.Tests` 178/178, build 0W/0E, code-review APPROVE_WITH_NITS. **Does not change wave-sequencing** — S1/S2 physical extraction still gates on FU-A (§ backlog); this is contract-purity prep for the future slice-1B `Remote*Client` vertical (filed `MSVC-CELL3-SLICE1B-REMOTE-CLIENT-VERTICAL`, ready/unblocked).
>
> **2026-07-16 (later, test-only) — completeness-matrix cell #10 retry-config integrity guard: last unblocked item, build-now CODE track now exhausted.** New `MessageRetryConfigGuardTests` (`tests/architecture/AqtCoreFW.Architecture.Tests/`, 2-case `[Theory]`) reads `src/shared/AqtCoreFW.Infrastructure/Messaging/MessagingRegistration.cs` via `FindRepoRoot()` and asserts both transport lambdas (`UsingInMemory`/`UsingRabbitMq`) keep `UseMessageRetry` — a regression removing it would silently convert every throws-to-retry consumer to immediate dead-letter (per `MSVC-CELL10-TEST-SUBSTRATE-SLICE2`'s `TransientFaultRetryConvergenceTests`), with no prior test to catch it. RabbitMQ is prod-only (untestable without a broker), so a static-source guard is the only way to cover both transports; mirrors the existing `MarsConnectionStringGuardTests` pattern. Test-only, 1 new file, zero production change. **Verify:** build 0W/0E; new 2/2 GREEN; full `Architecture.Tests` 180/180 (no regression); mutation-proven (deleting `UseMessageRetry` from the InMemory block flips only that case RED); code-review APPROVE_WITH_NITS (0 Critical/High/Medium). **Strategic milestone:** this was the last cheap unblocked cell #10 item — cell #10 is now FULLY monolith-exhausted. Combined with hard cells #5/#6 (extraction-gated), cell #7 follow-ups (compliance/auditor-blocked), and cell #3's provider half (extraction-gated), **the completeness-matrix build-now CODE track now has NO unblocked items remaining** — the sole remaining microservices action is FU-A (the GOV auditor question, non-code, user-owned), which gates all physical extraction. Feature-dev/polish remains the only other code track. **Does not change wave-sequencing.**
>
> **2026-07-16 (client-side only) — completeness-matrix cell #3 slice-1B: first `Remote*Client` vertical, client half.** New `HttpOrgUnitModuleClient` (`src/shared/AqtCoreFW.Infrastructure/Clients/`) implements all 6 `IOrgUnitModuleClient` methods over HTTP/JSON — mirrors `InProcessOrgUnitModuleClient`'s input-normalization + `MaxBatchSize` cap (enforced client-side before the call, so the documented `ArgumentException` still fires on every transport) + empty-input short-circuits; unlike the in-process client it does NOT chunk the batch (single wire call, provider owns any chunking); `PostAsync` tolerates an empty/whitespace response body → default. New `OrgUnitClientTransport` enum {InProcess, Http} + `OrgUnitModuleClientRegistration.AddHttpOrgUnitModuleClient` (typed `HttpClient` + activates the previously-dormant `TenantPropagationHandler`; fail-fast `InvalidOperationException` when `OrgUnitModuleClient:BaseAddress` is missing under Http mode). `OrganizationInfrastructureRegistration` swaps its hardcoded `AddScoped<IOrgUnitModuleClient, InProcessOrgUnitModuleClient>()` for an internal `AddOrgUnitModuleClient(services, configuration)` toggle reading new config `OrgUnitModuleClient:Transport` (absent/unparseable → InProcess). **Default DI stays InProcess = zero runtime behaviour change.** Proven by `HttpOrgUnitModuleClientTests` (20 facts, real registration path against an in-memory TestServer stub). **Verify:** build 0W/0E; new tests 20/20; `CrossModuleClientContractSerializationSafetyTests` + `ModuleIsolationTests` 16/16 (18-contract census unchanged); code-review APPROVE_WITH_NITS (0 Critical/High; M1+L3 fixed in-slice). **Deliberately out of scope (deferred to physical extraction, gated on FU-A auditor):** the provider-side internal HTTP endpoint (`/internal/org-units/*`) and service-to-service auth (`NoOpServiceIdentityProvider` stays a placeholder) — follow-up filed `MSVC-CELL3-SLICE1B-PROVIDER-ENDPOINT`. **Does not change wave-sequencing** — nothing is reachable over the network yet; this establishes the reusable HTTP-client template the other 17 `I*Client` seams copy at extraction time.

---

## 6. Current Status — Sóng-0 Foundation (COMPLETE)

Plan `plans/260706-1237-song0-foundation-outbox-atomicity-authz-decouple/` — all 3 phases done, pushed to
`main`. **No runtime split** — all monolith-side, valuable-now, reversible.

- **Phase 1 — Outbox at-least-once done-right** (arc `e03d2a4e..2d927dce`): version-agnostic type-token
  (`IOutboxEventTypeResolver`, FullName not AQN), producer-UoW atomicity, idempotent consumers
  (`IProcessedMessageStore` claim-first dedup ledger), payload-tenant pin for background consumers, white-box
  at-least-once test. 3/4 publishers routed through outbox; `AccountAutoDisabled` kept direct at-most-once
  (recorded WONTFIX — notification-only, low materiality).
- **Phase 2 — Cross-module write atomicity + consistency inventory**: `CrossModuleUnitOfWork` hardened (H1 reset
  enlisted-context on commit/rollback, H2 reuse connection on sequential Begin); 13-write-site inventory (0
  residual atomicity gap); surfaced + fixed 3 pre-existing Table-mode Forms production defects.
- **Phase 3 — Authz vocabulary-ownership split**: the 590-line `PermCodes` monolith split into 21 per-module
  partial-class files (same namespace → 0 call-site churn across 316 sites, 130 consts byte-identical) + 3
  architecture guards (const-drift, ownership-convention, checked-⊆-grantable contract-test).

### Reusable primitives delivered (available now, monolith-side)

- `IOutboxWriter` — escape hatch for non-`IAggregateRoot` publishers (requires an active UoW).
- `CrossModuleUnitOfWork` (H1+H2 hardened) + the `ownsUow` nesting pattern.
- `IProcessedMessageStore` — consumer-side atomic claim-first dedup (`messaging.processed_messages`).
- `AmbientTenantScope` + `TenantScopeConsumeFilter<T>` — payload-tenant pin for any background consume path.
- `ScopedTenantContext` — single-tenant `ITenantContext` for non-HTTP scopes.

---

## 7. Three-Track Framework

| Track | Meaning | State |
|-------|---------|-------|
| **SHIP-NOW** | Monolith-side, extraction-independent, reversible, valuable now | ✅ Delivered as Sóng-0 (outbox, atomicity, authz carve-out, write-path inventory) |
| **GATING** | Target decisions that must be locked *before* the related wave | ✅ 8/8 locked (§3); residual auditor sign-offs deferred to go-live (§8) |
| **DEFER** | Blocked on extraction schedule — kept as corrected design-reference | ⬜ Sync-client conversion (#3), saga-substrate/signing-saga (#5/#7), CI-N matrix, umbrella Helm, mesh pilot, distributed-test substrate |

---

## 8. Residual Sign-offs & Undug Gaps

**Auditor sign-offs (do not block build; confirm before go-live):**
- Logical-DB-per-service for S3/4/5 sharing one instance (decision #2).
- Chain-order semantics — receive-order + `OccurredAt` (decision #4); document and present at audit.
- Envelope-encryption key management — Vault path + RTBF crypto-shred (decision #3); detail-design when S2 is planned.

**Undug gaps (need a dedicated round for 100% completeness; do not block the first waves):**
- **#2 Schema backward-compat under rolling deploy** — expand/contract at the *service* level exists; at the
  *schema* level (drop-column while an old pod still runs = outage) it does not. Zero-downtime cross-version migration.
- **#10 Distributed-test substrate** — resilience tests are monolith-scoped. Slice-1 (2026-07-16) closed the
  hosted-service boot-guard false-green and seeded the **duplication** fault dimension; slice-2 (2026-07-16) closed
  **timeout/transient-retry**; slice-3 (2026-07-16) closed **partition/broker-outage** against the real outbox
  relay. **Reordering retired as theater** this session (extraction-dependent — needs an activated projection
  consumer, none live today); cross-service dedup + chaos remain extraction-gated/low-ROI. The retry-config integrity
  guard (2026-07-16, `MSVC-CELL10-RETRY-CONFIG-GUARD`) closed the last cheap unblocked item → **cell #10 is now FULLY
  monolith-exhausted**; the real remaining blocker is FU-A (non-code, user-owned).
- **#3 Auth-context propagation over gRPC** — RLS depends on `ITenantContext` from the JWT; crossing a gRPC boundary
  loses `HttpContext` → propagation must be designed (couples with #6).
- **#14 Org/ops capability** — one team running 5 distributed compliance-critical services: bus-factor, on-call,
  cert-rotation, DR-per-DB not yet assessed.

---

## 9. References

- **ADR-012** — Activate Microservices Strangler Extraction (this transformation's governing decision).
- **ADR-001 / ADR-003** — the modular-monolith base and per-module-schema isolation this transformation builds on.
- **Backlog** — `MSVC-5SVC-TARGET` (strategic anchor), `MSVC-WAVE0-*` (foundation rows), `MSVC-WAVE0-DEFER-TRACK`
  (P4–P7 blocked-on extraction schedule + GOV) in `backlogs/backlog.md`.
- **Session-local detail** (under `plans/`, gitignored — key content captured inline above): master synthesis
  `architect-260706-0937`; deep-dives on audit-chain integrity, authz decentralization (PEP/PIP), and the
  consistency substrate; the `brainstorm-260704-1918` 5-service operational deep-dive (source of the §2 map).
