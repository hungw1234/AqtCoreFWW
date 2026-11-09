# Journal — ISO-DAPPER keyed-DI audit read-isolation (plan + red-team + Phase 1-2)

**Date:** 2026-07-14 (afternoon) · **Branch:** main · **Row:** ISO-DAPPER-KEYED-DI-INCREMENT (partial — Phase 2 of 3)
**Plan:** `plans/260714-1021-iso-dapper-keyed-di-audit-read-isolation/` (gitignored)

## What shipped (this commit — Phase 2 wiring, 19 src files, opt-in-safe)
Closes the **standalone-Dapper** leg of the audit read-isolation boundary the EF-first slice left open. Default-deny keyed DI:
- **`ReadConnectionKeys.Audit`** const (new) — single-sourced key, no magic-string drift.
- **`DapperReadDbConnection`** parametrized: optional `connectionStringOverride` + `ConnectionString` property used by ALL 3 openers (standard + both SESSION_CONTEXT bypass variants) → per-registration login. UoW branch untouched.
- **`InfrastructureRegistration.cs:82`** — default unkeyed `IReadDbConnection` → `NonAudit` (opt-in `NonAuditConnectionString.Get`); keyed `"audit"` → `Default`.
- **12 audit-capable consumers** annotated `[FromKeyedServices(ReadConnectionKeys.Audit)]` (9 audit handlers + `InProcessAuditEvidenceClient` + 2 identity SecurityHealth handlers).
- **3 shared-infra EF contexts** (Alerting/ApiKey/Webhook) folded onto NonAudit; **Messaging kept on Default** (AuthServer-coupled — registered+migrated by AuthServer, DML-only login would crash its inline DDL migration).

**Verify:** full solution 0W/0E (142 projects); code-review CLEAN (0 Critical/High, all 7 red-team points + census PASS); regression 652 green (Audit 56 + Identity 426 + Architecture/wiring-guard 170). **Opt-in-safe:** with NonAudit unprovisioned (dev/test/CI) both keyed+unkeyed resolve Default → zero behavior change; isolation dormant until the prod login lands.

## How we got here (process — the valuable part)
`aqt-next` → `/ck:plan` → **`/ck:plan red-team` (3 hostile reviewers, 14 findings applied)** → `/cook` Phase-1 census → `/ak:cook` Phase-2.

**Red-team paid for itself before any code** — caught, on plan-only:
1. A genuine **self-contradiction** — "parametrize the conn string" vs "keep bypass openers verbatim" (they read the exact field). Left literal → NonAudit default reader could reach `[audit]` via a bypass. Fixed by the `ConnectionString` property funneling all 3 openers.
2. **2 boot-crash vectors the plan missed** — Messaging is AuthServer-coupled (folding it → DDL under DML-only login → boot crash); DML-only login + inline `MigrateAsync` needs a migration-vs-login ordering runbook.
3. **False precedent** — `[FromKeyedServices]` was unused in the repo (`grep`=0); the cited exemplar keys impl TYPES, not per-key conn strings → the mechanism was genuinely unproven. Resolved empirically: net10.0 build 0W/0E + code-review confirmed the container honors the attribute (type-based `InProcessAuditEvidenceClient` reg + MediatR handler reg).

**Phase-1 census resolved 4 findings empirically** — bypass-polarity (all audit-admin-bypass callers are audit-module → keyed→Default; all public-forms-bypass callers are forms→NonAudit reads `[forms]` not `[audit]`); audit-key set closed at 12; Messaging AuthServer-coupling confirmed; one construction site (a test).

## Carry-forward → Phase 3 (fresh session)
- **Isolation oracle (real):** non-`db_owner` test resolving the unkeyed reader through real `AddInfrastructure` on a constrained login → DENY `[audit]` (SqlEx 229), keyed reads `[audit]`, `IS_MEMBER=0`. Existing fixtures set Default=sa → needs a constrained-login override fixture (not `WebAppFixture`).
- **M1 (code-review):** reflection-allowlist test asserting all 12 keyed consumers resolve keyed — sole CI backstop; MUST land before prod provisioning (unit tests `new()` bypass the attribute).
- **M2 + fork #2 (DBA):** prod NonAudit login grant matrix — GRANT all ~19 non-audit schemas incl. apikey/alerting/webhook, DENY `[audit]`; **decision: DML+DDL (inline-migrate works) vs DML-only+SKIP_STARTUP_MIGRATIONS**. Migration-vs-login ordering runbook + rollback.
- Wiring-guard shared-infra extension (separate `src/shared` array — the `Reg()` helper is `src/modules`-only); ADR-012 reword to **opt-in / NOT blanket-complete** (UoW-active + 2 identity keyed reads stay audit-capable); prod fail-fast when Production && NonAudit unset; **L1** cleanup (3 stale "Default required" throw messages); docs set + backlog close.

## Lessons reinforced
- [[feedback-red-team-before-implement]] — 3 lenses on plan-only caught a contradiction + 2 boot-crash vectors + a false precedent (ROI paid).
- [[feedback-isolation-seam-must-cover-all-invariant-paths]] — the bypass openers were the "all paths" the seam had to cover; the `ConnectionString` property closes them structurally.
- [[feedback-verify-subagent-reports]] — grep+git+spot-read verified the 16-file sweep before trusting.
