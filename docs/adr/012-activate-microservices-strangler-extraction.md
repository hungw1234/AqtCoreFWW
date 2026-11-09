# ADR-012: Activate Microservices Strangler Extraction

**Date:** 2026-07-10
**Status:** Accepted — supersedes ADR-001
**Deciders:** AqtCoreFW Architecture Team

> **Amendment 2026-07-13 (user decision) — compliance-isolation driver refined; S4-for-compliance deferred.**
> The C3 schema-GRANT spike (`plans/reports/from-c3-spike-to-planner-schema-grant-isolation-verdict-report.md`,
> 3/3 pass, non-vacuous) proved that a non-`db_owner` login granted only `SELECT ON SCHEMA::[files]` reads its own
> schema but is DENIED cross-schema `[audit]` reads **on the current shared monolith DB**. That is the exact
> "service A cannot read service B's data" property this ADR's Context (below) and the "Stay a modular monolith"
> alternative-rejection cite as **only** achievable by extraction. It is **not** — schema-scoped GRANT delivers it on
> the monolith at ~1% of the extraction cost. **Consequence:** read-isolation alone no longer justifies a runtime
> split. The extraction driver is narrowed to the *residual* physical-instance properties schema-GRANT cannot give —
> per-service backup/PITR, separate TDE keys, blast-radius containment on credential/`sa` compromise, DBA-role
> separation, resource-governor isolation. Those are genuine drivers for **S1** (auth authority) and **S2** (HMAC
> single-writer), which decision #2 already places on physical instances — **not** for S4, whose sole cited
> compliance driver was the now-satisfied read-isolation. **User decision 2026-07-13:** defer S4-for-compliance;
> reserve physical extraction for S1/S2. A residual GOV auditor-delta question (do the obligations require any of the
> residual properties, or only read-isolation?) is the sole condition that could re-justify S4 — still open, not yet
> put to the auditor. This amends, not repudiates, the ADR: the strangler transformation and the 5-service target
> stand; only the S4-first wave order and the "extraction is the only way to demonstrate isolation" premise are
> corrected. The Context and Alternatives sections below are read subject to this amendment.

> **Amendment 2026-07-14 — monolith-side read-isolation model (EF-first slice, backlog `ISO-EF-FIRST-AUDIT-BOUNDARY`).**
> First concrete increment of the shared-DB read-isolation path the 07-13 amendment described. Delivers the
> **audit read boundary** on the current shared instance and records the isolation model as an accepted invariant:
>
> - **Read/write asymmetry.** Isolation is applied to the *read/query* path, per module. A non-audit module's EF
>   DbContext resolves its connection string through `NonAuditConnectionString.Get` → a least-privilege `NonAudit`
>   login (denied `[audit]`) when configured, else `Default` (**opt-in**: dev/test/CI keep booting on `Default`;
>   production activates isolation by provisioning the login + setting `ConnectionStrings:NonAudit`). The audit
>   module stays on the broad `Default` login because it *owns* `[audit]`.
> - **Proof.** `AuditBoundaryEfIsolationTests` (EF path) + `AuditBoundaryDapperIsolationTests` (Dapper path, incl. both
>   SESSION_CONTEXT bypass openers) prove a non-audit service login is DENIED `[audit]` (SqlException 229) while reading
>   its own schema, non-vacuously (positive leg + `IS_MEMBER('db_owner')=0`). EF analog of the C3 raw-ADO spike
>   (`SchemaGrantIsolationSpikeTests`). Guarded by `AuditBoundaryEfWiringGuardTests` (EF module + shared-infra
>   registrations) + `AuditKeyedReaderAllowlistTests` (the closed 12-consumer keyed-reader set).
> - **Documented exceptions (NOT compliance holes).** (1) **UoW-active reads** — a coordinated cross-module operation
>   (`CrossModuleUnitOfWork`) opens ONE connection on `Default` and reads span the schemas it has authority over;
>   isolating it would break write atomicity for ~0 benefit (it dissolves into a cross-service saga at physical
>   extraction). (2) **admin / `db_owner` paths** — `HmacChainService` cross-tenant enumeration
>   (`_adminConnectionString`), `DatabaseBackupService`, `UnsignedAuditBacklogHealthCheck`, `RlsPrincipalValidator`,
>   and seeders run elevated by purpose. (3) **audit runtime reads** — `HmacChainService` signs on the audit-owner
>   connection. (4) **`MessagingDbContext`** stays on `Default` — it is registered and inline-migrated by the AuthServer
>   host, so a DML-only `NonAudit` login would crash AuthServer boot on its DDL migration (`AlertingDbContext`,
>   `ApiKeyDbContext`, `WebhookDbContext` are API-only and now run on `NonAudit`).
> - **Identity no longer reads `[audit]` directly (tightened 2026-07-14, `ISO-AUDIT-MODULECLIENT-SECURITYHEALTH-REFACTOR`).**
>   The 2 identity SecurityHealth handlers (`GetAuditIntegrityStatusQuery` + `GetAuditRetentionStatusQuery`) now read the
>   audit rollups through `IAuditEvidenceClient` (the audit module's cross-module client), so identity holds NO
>   `[audit]`-capable DB credential — the audit-owner login + audit-admin bypass live inside the audit module. The
>   audit-keyed reader allowlist is correspondingly 10 files (was 12).
> - **Read-path repoint complete; ENFORCED only when provisioned (opt-in) — NOT a blanket "COMPLETE".** The standalone
>   Dapper read path is now isolated (`ISO-DAPPER-KEYED-DI-INCREMENT`): the default `IReadDbConnection` resolves the
>   `NonAudit` login, audit-capable handlers use the keyed `"audit"` reader on `Default`, and both SESSION_CONTEXT bypass
>   openers inherit the per-registration login (proven by `AuditBoundaryDapperIsolationTests`). Audit read-isolation now
>   covers **EF + Dapper + Hmac**, but it is **enforced only when the prod `NonAudit` login is provisioned**; the standing
>   exceptions above (UoW-active reads, admin/`db_owner`/backup/seeders) remain by design.
>   Do not read "audit read-isolated" as "every `[audit]` read closed."
> - **Activation is deferred (ops).** The code is opt-in and falls back to `Default`. Activating isolation in production
>   requires running the prod `NonAudit` login provisioning script (`scripts/260714-nonaudit-login-provisioning.sql` —
>   GRANT DML+DDL on the 19 non-audit schemas, default-deny `[audit]`), publishing the Vault KV key
>   `ConnectionStrings__NonAudit`, and honoring the migration-vs-login ordering invariant documented in that script's
>   runbook header. The C3 fixture login `files_svc_app` (GRANT `[files]` only) proves the DB fact but is NOT the prod
>   login model.

## Context

[ADR-001](./001-modular-monolith-over-microservices.md) chose a **modular monolith** built strangler-ready: 16
modules, each owning its own SQL Server schema (ADR-003), decoupled through SharedKernel abstractions. ADR-001
deliberately reserved a **"Phase 2"** option — extract modules into separate deployables *"if scale demands justify
the operational cost."* This ADR **activates that reserved option**. It is an evolution ADR-001 planned for, not a
repudiation of it: the modular monolith did its job as the extraction base, and the conditions to extract now hold.

The trigger is **not** the scale threshold ADR-001 named. Two conditions changed the calculus (decided 2026-07-06):

1. **Greenfield, pre-production.** The platform is not yet in production. There is no migration debt, no hybrid-risk,
   and no zero-downtime constraint during the build — the ideal window to design the target architecture correctly
   from the start rather than retrofit it under a live load later.
2. **Compliance-isolation as the primary pressure, not throughput.** Vietnamese GOV obligations — NĐ68 signing
   cryptography, HMAC-chained audit integrity (ADR-004), auth-authority isolation — make *demonstrable data-boundary
   isolation between services* the driving requirement. An auditor needs "service A physically cannot read service
   B's data" to be true and provable. A modular monolith cannot demonstrate that; separate services with least-priv
   per-service database logins can.

ADR-001's own risk analysis (single-process bottleneck, all-or-nothing deployment, shared-infra contention) stands;
those costs are now accepted in exchange for the isolation and independent-deployability the compliance target needs.

## Decision

Adopt the **strangler-fig** transformation toward **5 coarse-grained services** (S1 Identity & Access · S2 Audit /
Alert / Notify · S3 Process & Forms · S4 Documents / Sign / AI · S5 Edge Gateway & Config), extracted one wave at a
time. The full target map, wave sequencing, locked decisions, capability gaps, and current status are maintained in
the durable master plan: **[`docs/microservices-transformation-roadmap.md`](../microservices-transformation-roadmap.md)**.

Binding architectural commitments (detail + rationale in the roadmap §3):

- **Coarse granularity.** 5 services grouping cohesive modules that already share transactional/trust boundaries —
  not one service per module. S4 is designed "6-ready" (`reporting` as an internal split-seam, promoted only on a
  measurable trigger).
- **DB-per-service, schema-per-module** (decision #1) — `CrossModuleUnitOfWork` stays valid *within* a service, so
  intra-service writes remain atomic-local with no distributed 2PC.
- **Hybrid DB topology** (decision #2) — S1 + S2 on physical instances; S3/4/5 as logical DBs on a shared instance
  with least-priv per-service logins forbidding cross-DB queries. Reversible to physical-per-service if an auditor
  requires it.
- **Monolith-side foundations first, runtime split later.** Correctness/consistency/authz foundations are built and
  proven on the monolith (reversible, valuable immediately) before any physical extraction. Sóng-0 (outbox
  at-least-once, cross-module write atomicity, authz vocabulary-ownership split) is **complete**.
- **No distributed monolith.** The five hard capabilities — sync-client conversion, cross-service saga consistency,
  authz decentralization, audit-chain-under-distribution, distributed-test substrate — are the transformation's real
  scope. Infrastructure scaffolding (mesh / umbrella Helm / CI-matrix) is correctly deferred until a wave needs it.

## Consequences

### Positive
- **Demonstrable compliance isolation** — per-service least-priv DB logins make data-boundary claims provable to an
  auditor; NĐ68 crypto (S4), HMAC audit chain (S2), and auth authority (S1) each get their own isolated blast radius
  and dedicated pentest surface.
- **Independent deployability & resource isolation** — S4 (CPU/GPU/Hangfire-heavy) scales and deploys without
  dragging the auth path; a report-job burst no longer contends with token issuance.
- **Design-correctness window** — building the target greenfield avoids the retrofit tax ADR-001 anticipated for a
  later, live extraction.

### Negative
- **Operational cost ADR-001 warned of is now incurred** — 5 deployables, per-service DBs, service mesh, distributed
  tracing, and the org/ops capacity to run them (capability #14 — not yet assessed for a single team).
- **New failure modes** — network partitions, cross-service consistency, auth-context propagation over gRPC (RLS
  currently depends on `ITenantContext` from `HttpContext`, lost at a gRPC boundary).
- **Undug gaps remain** (roadmap §8) — schema backward-compat under rolling deploy, distributed-test substrate, and
  auth-context propagation each need a dedicated round before the relevant wave.

## Alternatives Considered

| Option | Why not |
|--------|---------|
| **Stay a modular monolith** (ADR-001 status quo) | Cannot demonstrate physical data-boundary isolation to a GOV auditor; the compliance driver, unlike scale, is not optional |
| **Fine-grained microservices** (1 per module, ~16) | Explodes cross-service chatter and saga surface; splits cohesive transactional boundaries (`WorkflowInstance ⊕ FormSubmission`) that decision #1 keeps atomic-local |
| **Extract later, under production load** | Forfeits the greenfield design-correctness window; forces a zero-downtime retrofit and migration debt the current timing avoids |

## Relationship to Existing ADRs

- **Supersedes ADR-001** — activates the Phase-2 strangler option ADR-001 reserved; ADR-001 remains the historical
  record of the modular-monolith base and its rationale.
- **Builds on ADR-003** (SQL Server per-module schema) — per-schema ownership is exactly what makes clean extraction
  possible.
- **Honours ADR-004** (HMAC-chained audit log) — the audit-chain-under-distribution work (roadmap §8, deep-dive ①)
  exists to preserve chain integrity once S2 is split.
