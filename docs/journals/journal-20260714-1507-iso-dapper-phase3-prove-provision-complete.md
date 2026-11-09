# Journal — ISO-DAPPER Phase 3 (prove + provision + document) — increment code-complete

**Date:** 2026-07-14 (afternoon) · **Branch:** main · **Row:** ISO-DAPPER-KEYED-DI-INCREMENT (Phase 3 of 3)
**Plan:** `plans/260714-1021-iso-dapper-keyed-di-audit-read-isolation/` (2/3→3/3) · **Follows:** journal-20260714-1354 (Phase 1-2 wiring)

## What shipped (Phase 3 — proof + ops artifact + docs)
**3a — Prove + Guard (tests, all green):**
- `AuditBoundaryDapperIsolationTests` **5/5** — the security oracle: a non-`db_owner` constrained login (files_svc_app analog) reading `[audit]` via the Dapper sink is DENIED (SqlException 229) on the standard opener **AND both SESSION_CONTEXT bypass openers** (`QueryWithAuditAdminBypassAsync`, `QueryFirstOrDefaultWithPublicFormsBypassAsync`), even with `IsSystemAdmin:true` — so only the DB GRANT explains the denial. Positive leg (`[files]` read OK) + `IS_MEMBER('db_owner')=0` = non-vacuous. **This empirically proves the red-team F1/F2 bypass-hole is closed** (the bypass flag changes RLS visibility, not which login opens the socket). Independently re-run.
- `AuditKeyedReaderAllowlistTests` (M1 from code-review) — closed-allowlist source sweep: exactly the 12 audit-capable files carry `[FromKeyedServices(ReadConnectionKeys.Audit)]`, no others; a comment-discriminator fact makes it mutation-proof. Sole CI backstop for a dropped/added key.
- Extended `AuditBoundaryEfWiringGuardTests` (F8) — separate `src/shared` array for the 3 folded shared-infra registrations + a fact asserting Messaging stays on Default. Full Architecture.Tests **174/174**.

**3b — Provision + Document (artifact + docs, ops-gated):**
- `scripts/260714-nonaudit-login-provisioning.sql` — prod NonAudit login: GRANT DML+DDL on the **19** non-audit schemas (DBA decision 2026-07-14: DML+DDL keeps inline EF `MigrateAsync` working), explicit DENY `[audit]`, non-vacuous verify. Rich runbook header: **migration-vs-login ordering invariant** (provision grants → publish Vault KV `ConnectionStrings__NonAudit` → THEN point app at NonAudit) + rollback + placeholder password (no secret committed).
- ADR-012 reworded: audit read-isolation now covers **EF + Dapper + Hmac**, **enforced only when provisioned (opt-in) — NOT a blanket "COMPLETE"**; standing exceptions ledger (UoW-active reads, the 2 identity SecurityHealth keyed reads → identity stays audit-capable, admin/db_owner/backup/seeders).
- L1 (code-review) — 3 shared-infra `?? throw` messages corrected ("NonAudit or Default").
- Docs set: changelog + backlog (row → 🟢 CODE-COMPLETE/OPS-ACTIVATION-PENDING, **not closed**) + 2 follow-up rows filed + completed.md milestone + project-roadmap + microservices-transformation-roadmap (SSOT) reconciled (docs-manager also fixed 2 stale "next=Sóng-1" drift lines predating the 07-13 S4-deferral).

## State & honest scope
Increment is **code-complete**; the row stays OPEN pending **ops activation** (a DBA runs the provisioning script + publishes the Vault key — not a code task). Opt-in: dormant until then, zero behavior change. Monolith-side audit read-path repoint (EF+Dapper) done. NOT "every `[audit]` read closed" — the standing exceptions remain by design.

## Follow-ups filed
- `ISO-AUDIT-MODULECLIENT-SECURITYHEALTH-REFACTOR` (T4) — route the 2 identity reads via `IAuditModuleClient` so identity drops its `[audit]`-capable credential.
- `ISO-NONAUDIT-PROD-FAILFAST-HEALTHCHECK` (T4) — startup warning + degraded-isolation health signal when Production && NonAudit unset (was a Phase-3 acceptance criterion, deferred as its own slice — a prod behavior addition deserving separate care).

## Lessons reinforced
- [[feedback-sa-dbowner-false-greens-rls-assertions]] — the oracle's `IS_MEMBER('db_owner')=0` + `IsSystemAdmin:true` isolate the DB-permission fact from every gate; the bypass-opener legs are the part that could have false-greened.
- [[feedback-test-local-sql-warm-reuse-and-false-red-modes]] — oracle cross-checked warm-reuse vs fresh Testcontainers (5/5 both) before trusting.
- [[feedback-verify-subagent-reports]] — re-ran the oracle independently + grep-verified the docs/backlog edits after each subagent.
