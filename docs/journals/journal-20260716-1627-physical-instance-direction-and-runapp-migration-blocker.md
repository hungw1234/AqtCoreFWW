# Physical DB-Instance-per-Service Direction + Runapp Migration Blocker

**Date**: 2026-07-16 16:27
**Severity**: Medium (direction-setting advisory + a dev-infra blocker; no production incident)
**Component**: microservices transformation strategy; dev stack startup (`AqtCoreFW.Api` migration)
**Status**: Advisory recorded; runapp API blocked (unfixed by user choice)

## Session goal

Post-ship (2 cell-#10 ships committed+pushed earlier: `48af0fad`, `6c02d8ad`), the user pivoted to strategy: evaluate physical isolation vs monolith+schema-GRANT, then decide the service→DB/instance→schema partition, then try to run the dev stack.

## What worked

- **Consultation delivered** the honest 2-option comparison; surfaced that read-isolation (the only proven-needed property) is satisfiable on the shared DB (C3), so only FU-A properties (b) TDE / (c) blast-radius re-justify separate *instances*.
- **User decision captured**: pursue **DB-instance-per-service**, driver = **independent DB-tier scaling** (a *scale* driver, not compliance). Plan-first; execution gated on FU-A.
- **Source-verified partition map** written → `plans/reports/microservices-db-instance-schema-partition-map-260716-1602-for-fu-a-auditor-review-report.md`. 20 schemas (15 module + 5 infra), 3 truly-ownerless (`messaging`/`hangfire`/`backup`), `messaging` replicate-per-DB = mandatory foundation. Corrected inferences via source: `consent`∈`identity` (not its own schema), `collaboration`→`collab`, `systemparams`→`config`, `[identity]` is a reserved keyword.

## What didn't / lessons

- **`/aqt-runapp` — API blocked at startup migration.** Auth (5002) + FE (5173) came up fine; Vault re-seeded (dev in-memory had lost secrets). But `AqtCoreFW.Api` runs EF migrations on boot and `UX_FormTemplate_PublicCode` hit its **intentional** pre-flight guard (`THROW 50000`): dev DB has `demo-intake-form` as a public Code in 2 tenants → cross-tenant public-Code uniqueness violated. Not a regression — the guard is the public-forms global-uniqueness arc working as designed. User chose to stop rather than mutate/nuke the dev DB.
- **Lesson (scale vs compliance are different gates).** The user's "execute after FU-A" gate is a *compliance* gate; the stated driver is *scale* (load-evidence gate). Conflating them risks either waiting on the wrong signal or over-building. Recorded as open-question #5 on the direction row.
- **Lesson (data-tier scale ≠ service extraction).** Independent DB scaling is a data-tier property achievable without app-tier extraction; and splitting any DB breaks `CrossModuleUnitOfWork` shared-connection atomicity → forces the outbox/saga substrate regardless. Neither path is free — FU-A/scale-evidence decides *which* investment, not *whether*.

## Decisions made

- **Direction (in-flight, not locked):** DB-instance-per-service, audit-first recommended, execute-gated on FU-A. Reverses the 07-13 defer in *intent* only.
- **Recommendation on record:** do NOT provision 5–6 instances upfront (premature for a pre-production, single-team, self-hosted SQL Server 2022 stack — no cloud elastic escape hatch). Peel **S2 audit** first (write-heavy, unbounded, already event-decoupled), prove the pattern (per-DB `messaging` + Hangfire + backup/HA), expand on measured load.
- **Did NOT author Sóng-1** — blocked on open scope Qs + FU-A (correct restraint; user chose plan-first).

## Test status

N/A this arc (advisory + run). Earlier code ships this session: cell #10 slice-3 84/84 + retry-guard 180/180, both green + mutation-proven.

## Next session

1. **FU-A** — user puts the GOV auditor question (`docs/microservices-compliance-isolation-auditor-questions.md`) to the auditor. Sole gate for execution.
2. **Answer partition open-Qs** (instance-vs-DB, all-5-vs-audit-first, messaging replicate-vs-shared, load profile + cloud) — see backlog `MSVC-PHYSICAL-INSTANCE-PARTITION-DIRECTION`.
3. **Triage the dev-DB dupe** (`FORMS-DEV-DB-DUPLICATE-PUBLIC-CODE-MIGRATION-BLOCK`) — is it seeder-created (real seeder-bug) or stale data? Fix (demote or reseed) before the API can run locally again.
