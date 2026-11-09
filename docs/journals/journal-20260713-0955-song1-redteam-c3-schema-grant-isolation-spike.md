# Journal — Sóng-1 Red-Team → C3 Schema-GRANT Isolation Spike (2026-07-13)

**Session:** `/aqt-next` → `/ck:plan red-team Sóng-1` → gating decisions → `/ck:plan --fast` → `/ck:cook`.
**Outcome:** a red-team of the not-yet-authored S4 extraction plan caught a wave-justification collapse; a 0.5d spike
then empirically disproved the compliance driver. 2 test files, SSOT/backlog/changelog updated. 0 `src/` change.

## What happened

1. **aqt-next** surfaced the standing #1 priority: `/ck:plan` Sóng-1 (S4 extraction). Instead of authoring, the user
   chose to **red-team the plan-in-embryo first** (roadmap SSOT + scout §6 skeleton — no `plan.md` existed yet).
2. **Red-team (4 hostile lenses, code-reviewer ×4):** 30 raw → 16 accepted (5 Critical). Controller-verified the
   boldest claims by grep before trusting them:
   - NĐ68 signing engine (142 `.cs`) is in `identity`/S1, **not** `files`/S4 → S4's "crypto-pentest" driver
     mis-attributed (C1).
   - `search` is a **deleted** module (`git log` → PR#45 `449b18fa`), not the "empty scaffold" the roadmap §2 **and**
     the prior 07-10 scout both claimed (M1).
   - ADR-003:32,88 already **designed** per-schema login isolation; 0 production DENY grants exist (C3).
   - `TenantPropagationHandler` already exists; `AuditLogAppendedConsumer` is a noop; 0 gRPC (H6/C4/AD5).
3. **Gating decisions (user):** C5 Sóng-1 deferred · C3 run the schema-GRANT spike first · C1 signing-ownership
   deferred · H6 → **decision #9** transport = HTTP + extend `TenantPropagationHandler` (not gRPC).
4. **C3 spike (`--fast` plan → cook):** added `files_svc_app` — a non-`db_owner` login granted only
   `SELECT ON SCHEMA::[files]` — to the existing `RlsPolicyPresenceFixture` (which already migrates all 14 schemas
   into one DB = the decision-#2 topology), plus `SchemaGrantIsolationSpikeTests` (3 facts). **3/3 pass:** reads
   `[files]`, DENIED `[audit]` (SqlException 229), `IS_MEMBER('db_owner')=0`.

## Verdict

**Schema-scoped GRANT on the shared monolith DB already delivers auditor-demonstrable read-isolation** — the exact
"service A cannot read service B's data" guarantee ADR-012 claims requires extraction. So **compliance-isolation does
NOT justify extracting S4** for the shared-instance tier; the same guarantee is a monolith login-config change at ~1%
of the cost. Recorded as a recommendation (defer S4-for-compliance, reserve extraction for S1/S2) — it reverses a
locked 2026-07-06 decision, so it is **pending the user**. Residual (per-service backup / TDE keys / blast-radius /
DBA-role / resource isolation) → GOV auditor question.

## Lessons

- **Red-team the plan-in-embryo BEFORE authoring.** 4 reviewers caught 2 SSOT factual errors + a justification
  collapse for a fraction of the cost of writing P0–P4 and discovering it mid-implementation.
- **A spike's harness must dodge the trap it investigates.** This spike could have been a false-green if it ran as
  `db_owner` (`IS_MEMBER('db_owner')=1` short-circuits permission checks — the same H4/SA5 class the red-team flagged).
  The mandatory non-vacuous guard (`IS_MEMBER('db_owner')=0`) + the A/B positive-read leg close every false-green path.
- **Verify subagent claims that contradict your own prior reads.** Two reviewers said `search` was deleted, which
  contradicted the scout report I had just relied on — `git log -S` confirmed the reviewers, not the scout.

## Follow-ups (pending user)

1. Auditor-delta question → GOV (the only thing that could re-justify S4 extraction).
2. Decide: defer S4-for-compliance + reserve physical extraction for S1/S2 (reverses locked decision).
3. Optional cheap win: apply per-service schema-scoped logins to the monolith now (isolation without extraction).
