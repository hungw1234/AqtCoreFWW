# Journal — S4-Defer Decision + FU-C Red-Team → FU-A Auditor Pivot

**Date:** 2026-07-13 (session 2, ~11:06–12:54 ICT) · **Branch:** `main` · **Commits:** `fe9ec9bf`, `34d6333d` (both pushed)

## Session goal

`/aqt-next` at session start surfaced the microservices #1 track sitting at a decision gate (C3 spike had
disproved the S4 compliance-extraction driver, "pending user"). Goal: resolve the gate, then advance the
next microservices step.

## What worked (verified outcomes)

- **S4-defer decision recorded** (`fe9ec9bf`). User chose "defer S4-for-compliance, reserve physical
  extraction for S1/S2". Recorded across SSOT roadmap + an ADR-012 dated amendment (immutable-record
  pattern, not rewrite) + backlog + changelog. Flipped every "pending user" marker → DECIDED.
- **FU-A deliverable produced** (`34d6333d`) — `docs/microservices-compliance-isolation-auditor-questions.md`:
  the GOV auditor-delta question + a 5-property residual table + a decision-framework mapping each possible
  auditor answer to the architecture. Honest caveat baked in: read-isolation is *achievable* via schema-GRANT
  but *not currently applied* on the monolith (one `aqtcore_app` login + Dapper on a global connection).
- **Red-team caught a broken plan before any code** — the highest-value outcome. See below.

## What didn't / lessons

- **FU-C ("apply per-service logins to the monolith") was NOT a cheap win.** A `/cook` invocation scouted it,
  found it plan-sized (not cook-sized), routed to `/ck:plan`; a 4-lens red-team of the resulting 4-phase plan
  returned 16 accepted findings with **2 controller-verified plan-killers**:
  1. Rewiring the 13 EF module registrations does NOT isolate reads — the dominant read path is **Dapper on
     the global `DatabaseOptions.ConnectionString`** (`DapperReadDbConnection.cs:32,63,178`), untouched by the
     plan. The C3 spike proved isolation on a *fabricated* login connection, never on the app's real read path.
  2. The schema map was wrong — **20 schemas, not 14**; real names `collab`/`config` (not
     "collaboration"/"systemparams"); plus ownerless cross-cutting schemas (`messaging` dedup ledger,
     `hangfire`, `gateway`, `backup`, `webhooks`) that no service owns.
  Also: Outcome-A (per-service write isolation) is architecturally unreachable — the UoW opens its connection
  before enlistment, and audit(S2)/consent(S1) co-transact onto forms(S3) connections.
- **Lesson — the real obstacle is a SharedKernel coupling point:** `DatabaseOptions.ConnectionString` (global,
  single-valued) is depended on by the Dapper read path + `CrossModuleUnitOfWork` + the outbox dispatcher. Any
  future isolation/extraction work must address this abstraction first, not rewire per-module registrations.
  Worth remembering when the real S1/S2 extraction wave arrives.
- **Lesson — red-team the plan-in-embryo pays again.** `[[feedback-red-team-before-implement]]` ROI held: 4
  reviewers for ~30 min prevented a 4-phase implementation built on two source-false premises.

## Decisions made

- **Defer S4-for-compliance; reserve physical extraction for S1/S2** (reverses locked 2026-07-06 S4-first).
- **Defer FU-C** — the red-team *was* the "scout-size before cook" gate the backlog asked for; it proved
  FU-C is real foundation work, and its read-isolation claim is already *demonstrated* by C3. Gated on FU-A.
- **Pivot to FU-A** — draft the auditor question first; if the auditor needs any residual physical property
  (backup / TDE / blast-radius / DBA / resource), the whole schema-GRANT approach is moot.

## Test status

No code changed this session (governance + planning + docs only). Build/test not run. The C3 spike's 3/3
isolation tests (previous session, `af25b036`) remain the empirical basis.

## Next session (carry-over)

1. **FU-A (user action):** put the auditor-questions doc to the GOV auditor. Two inputs needed to finalize
   the doc (§7): who the auditor is + artifact form; and whether "regulated data" spans all 20 schemas or
   specific domains.
2. **FU-B** (first-extraction-wave re-sequencing) — gated on FU-A.
3. **FU-C** re-scope (read-path-only, repoint Dapper/`DatabaseOptions`, shared login for cross-cutting
   schemas, fail-closed) — only if the auditor answer confirms read-isolation is the target.
4. **Backlog housekeeping** (non-urgent): 648 lines, marginally over the 600 optimize threshold; 5 closed
   "Newly filed" sections could archive to `completed.md` in a dedicated pass.
