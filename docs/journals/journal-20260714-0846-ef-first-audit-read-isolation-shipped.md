# Journal — EF-First Audit Read-Isolation Shipped (ISO-EF-FIRST-AUDIT-BOUNDARY)

**Date:** 2026-07-14 · **Branch:** main · **Ships:** 3 commits — `a5e890ad` (oracle test), `4b74114e` (impl, 18 files), `3c1515eb` (docs sync). Local, not pushed. · **Plan:** `plans/260713-1720-logical-isolation-connection-resolver-seam` (gitignored), executed.

## Session goal
`/ak-cook "what to do next?"` → microservices #1 track: decision-tier gated on FU-A (auditor, human-paced), but the **execution-tier** slice `ISO-EF-FIRST-AUDIT-BOUNDARY` was validated + ready + auditor-independent. User chose to cook it, approach **A (fallback-to-Default, opt-in)**. Execute the 07-13 EF-first plan end-to-end.

## What shipped
- **Helper `NonAuditConnectionString.Get(config)` = `NonAudit ?? Default`.** The 14 non-audit module EF DbContext registrations route through it → a least-privilege login denied `[audit]`, fallback keeps dev/test/CI booting unchanged (opt-in prod isolation). Audit module stays on `Default` (owns `[audit]`); `HmacChainService` already reads on the audit-owner `Default` (no change).
- **Oracle `AuditBoundaryEfIsolationTests`** — EF analog of the C3 raw-ADO spike (`SchemaGrantIsolationSpikeTests`). Reused the fixture's existing `files_svc_app` login (GRANT `[files]` only) → EF read `[files]` OK, EF read `[audit]` DENIED (SqlException 229), `IS_MEMBER('db_owner')=0`. Proved the capability with **zero new provisioning**.
- **Arch-guard `AuditBoundaryEfWiringGuardTests`** — asserts 14 registrations route through the helper (count==14, non-vacuous) + audit stays on Default.
- **ADR-012 amendment** — read/write asymmetry + documented exceptions + honest deferred gaps.

## What worked
- **Plan re-verify before execute paid off but returned zero drift** — every file:line anchor the 07-13 plan cited (`DapperReadDbConnection.cs:94-97`, `CrossModuleUnitOfWork.cs:155`, `HmacChainService.cs:369/810/849`, `AuthServer/Program.cs:229/336`) matched current source exactly. Fresh plan (18h old) held.
- **Reusing the C3 fixture login as the EF oracle** collapsed the plan's "provision a login" Phase-2 crux into a characterization test that was GREEN immediately — the DB capability was already proven by C3; the real deliverable was the app WIRING, which the arch-guard now guards.
- **Approach A (fallback-to-Default) made the F3 AuthServer risk vanish** — AuthServer calls the module registrations; with no `NonAudit` configured it falls back to `Default` → boots unchanged. The plan's "must register in both roots or login 500s" concern was dissolved by the opt-in fallback, not by editing AuthServer.

## What didn't / lessons
- **A full-project test run is not the oracle — again.** `Startup.SmokeTests` reported 5 API-host boot failures (parallel AND sequential). Each failing test **passed in isolation**. Proved they were NOT mine via a **git-stash A/B**: the pre-Phase-2 clean tree failed the *identical* 5 tests → pre-existing warm-DB pollution (`feedback-test-local-sql-warm-reuse-and-false-red-modes`). Lesson reinforced: on a full-run red, re-run isolated + A/B the clean tree before believing it's a regression. HARD-GATE-NO-SIDE-EFFECTS demands the empirical A/B, not reasoning.
- **The code-reviewer caught a scope-honesty gap I'd made** — I isolated the 14 module SERVICE contexts but left 3-4 shared-infra EF contexts (Alerting/ApiKeys/Webhooks/Messaging) on Default without listing them as exceptions. Fixed in ADR-012 (documented deferral, not silent). The guard + ADR must enumerate what is NOT covered, or "isolated the EF path" over-claims.

## Decisions made
- **Opt-in (approach A) over required-login (B).** Zero-break rollout; prod activates by config + provisioning. Isolation of shared-infra contexts + standalone Dapper + prod login provisioning **deferred to `ISO-DAPPER-KEYED-DI-INCREMENT`** (now unblocked).
- **L1 (`??` null-only fallback) + L2 (enumerated guard) accepted** — match existing idioms (`AuditAdmin ?? Default`, `MarsConnectionStringGuardTests`); an explicit "no-Default" guard assertion risks false-fails.

## Test status
Build 0W/0E (API + AuthServer, `-warnaserror`). TenantIsolation.Tests 46/46 · arch-guard 2/2 · helper unit 3/3 · oracle 3/3. `Startup.SmokeTests` 5 API-host fails = pre-existing (A/B-confirmed). Review: DONE_WITH_CONCERNS (0 Crit/0 High/1 Med/3 Low), all resolved or accepted-with-rationale.

## Next session
- `ISO-DAPPER-KEYED-DI-INCREMENT` (ready) — standalone-Dapper keyed-DI + shared-infra EF contexts + prod `NonAudit` login provisioning/grant-matrix runbook; completes full audit read-isolation.
- Microservices decision-tier still gated on **FU-A** (user → GOV auditor) — unchanged.
- Optional: push the 3 local commits.
