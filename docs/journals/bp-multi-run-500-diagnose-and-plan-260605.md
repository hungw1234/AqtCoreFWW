# Session Journal — BP 2nd-run 500: diagnose → plan (no ship)

**Date:** 2026-06-05 · **Type:** debug + plan (zero code commits) · **Branch:** main (HEAD `788a77b1`, ahead=1 pre-existing)

## Session goal
Run the dev stack, then chase a user-reported 500 on BP "Start Run" (curl repro of `POST /business-processes/{id}/runs` → `GOV_SYS_000`). Outcome was a fully-vetted implementation plan, not a code fix — user routed `/fix redesign` → `/ck:plan`.

## What worked
- **Stack up** via `/aqt-runapp`: Auth 5002 + API 5001 + FE 5173, Gateway=Standalone, Development. Vault dev container had lost secrets (in-memory) → re-seeded via `vault-init`.
- **Root cause nailed empirically** (source + live DB + FE): the 500 is `SqlException 2601` on unique filtered index `UX_WorkflowInstances_Active_Entity` (EntityType,EntityId,TenantId WHERE CompletedAt IS NULL). FE sends `entityId=bpId` (a load-bearing display-name contract, `new-bp-run-wizard.tsx:174` + `bp-run-detail-page.tsx:36`) → only one active run per BP per tenant is ever possible. `WorkflowInstanceRepository.SaveChangesAsync:215-227` catches only `DbUpdateConcurrencyException`, not 2601 → bubbles to 500. Confirmed a live active row exists (PublicId `019E8E64-CDD3-...`, CompletedAt NULL, created 2026-06-03).
- **Plan produced + hardened** through 3 gates: planner (advisor×2, chose Option B' over B/A) → `/ck:plan validate` (independent claim re-verify + consistency sweep) → `/ck:plan red-team` (4 hostile reviewers). Final: `plans/20260605-1339-bp-multi-run-dossier-id/`, 7 phases ~3.0d.

## What didn't / lessons
- **Validate caught a real cross-phase contradiction the planner left behind:** when the plan upgraded the index from Option B (`EntityType <> 'BusinessProcess'`) to B' (`DossierId IS NULL`), phase-01 was reconciled but phase-03 prose still said non-BP `DossierId == EntityId`. Under B' that would EXCLUDE non-BP rows from the index → silently reintroduce the OUTBOX-1 race the index exists to prevent. The code step was already correct; only the prose/success-criterion was stale. Lesson reinforced: a whole-plan consistency sweep must read every phase, not trust the edited one — superseded decisions hide in unedited files.
- **Red-team's highest-value catch = test completeness, not architecture.** The approach was sound (MARS rollback safe, B' NULL semantics correct, blast-radius fully mapped, generator real), but: down-migration falsely claimed reversible (1505 once multi-run data exists), the OUTBOX-1 keystone test was unowned + about to drift + polluting a shared fixture, and AC6 (double-submit) had zero automated test despite being the entire reason phase-06 exists.
- **A double-click footgun surfaced only by tracing the OLD behavior:** the unique index was an *accidental* dup-run backstop (2nd run = 500). Removing it for multi-run means double-click/refresh silently creates 2 runs. FE sends neither `X-Request-Token` nor `X-Idempotency-Key` today → added phase-06.

## Decisions made
- **Identity model = C/B' (per-run `DossierId`, auto-gen Guid v7), enabler-only** — user explicitly scoped it as a future-proof column, not a dossier feature. entityId stays = bpId (FE contract intact, ~0 FE churn beyond the token header).
- **phase-03 → Approach B** (mint DossierId in the BP handler, thread through the contract) over Approach A (mint in shared client keyed on `EntityType == "BusinessProcess"`) — eliminates dependency on a `"BusinessProcess"` literal that lives in 3 un-shared copies; literal drift can no longer break the fix.
- **Down-migration = fail-loud non-destructive** (recommended; cook to confirm vs CTE-collapse which deletes run data).
- **Vault BNV pepper:** runtime-seeded into the running dev Vault to unblock now (ephemeral); durable fix filed as a backlog row.

## Test status
No code → no test run this session. Plan encodes 6 acceptance criteria with real-SQL (Testcontainers) tests, incl. the DB-level OUTBOX-1 collision assertion and the AC6 double-submit test.

## Next session
1. `/cook plans/20260605-1339-bp-multi-run-dossier-id/` (7 phases, Down=fail-loud) — the BP-500 fix.
2. `VAULT-INIT-MISSING-BNV-PEPPER` — 1-line durable fix (`vault-init.sh:28`), ~5min.
3. Pre-existing housekeeping: 1 unpushed docs commit `788a77b1` + uncommitted `backlogs/backlog.md` + untracked `parallel-branch-runtime-plan-redteam-260604.md` (carried from prior session; this session also touched backlog.md — bundle into one docs commit + push).
4. Still-open epic decision: `WF-PARALLEL-RUNTIME-ENABLEMENT` appetite (30min/2-3d/3-4wk).
