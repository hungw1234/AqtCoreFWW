# Auth-IAM Verdict, Launch Feature-Set Decided, 2 P1 Stable-Launch Blockers Shipped

**Date**: 2026-07-16 22:13
**Severity**: Medium
**Component**: Identity/Auth (advisory), Roadmap/Backlog (scope decision), Ops (backup automation + CI smoke gate)
**Status**: Resolved (all 3 threads)

## What Happened

Three separate asks in one session, all landing on the same theme: **stop asking "replace/upgrade/what's missing" before checking if it already exists.**

1. User asked whether to replace the auth server with Keycloak or "upgrade to full IAM." Advisory verdict: **keep OpenIddict.** It's already a mature domain-specific IAM — OIDC/PKCE, MFA, 6-phase RBAC/ABAC, SoD, per-tenant `CredentialPolicy`, VN PKI 3-tier CCCD-signing, HMAC audit-chain, 63 migrations of PDPL/NĐ68/QĐ742 compliance baked in. Keycloak rip-and-replace = 4-8mo, HIGH risk, breaks the audit-chain, tenant↔realm mismatch, destabilizes the stable-first phase for a net-negative outcome. No code change — advisory only, recorded in memory.

2. The open "launch feature-set = USER-TBD" gate on Definition-of-Stable got resolved via `/ak-cook` that turned into a scope decision. 3-source scout (BE: 414 endpoints/15 modules/0 stubs/2234+ tests; FE: 85 routes/90+ SDK; SRS: 73 use-cases) proved the system is **already feature-complete**. The real work was scope-DOWN (pick v1.0 module set), not scope-UP (build more). Decided: v1.0 = 9 modules (Identity/Workflow/Forms/Files/Audit/MasterData + Notifications/Organization/Integration), defer AI/Collaboration/BusinessProcess/Reporting/Rules to v1.1, Digital Signature full 3-tier stays ON, single-tenant-first soak. Committed `9962fbc9` on `docs/launch-feature-set-v1-decided`.

3. Shipped the 2 real P1 stable-launch blockers on `feat/p1-stable-backup-and-ci-smoke` (PR #121):
   - **P1-STABLE-BACKUP-AUTOMATION** (`571ba695`): Hangfire recurring job daily 02:00 + config-driven 30d retention + pure `BackupRetentionPruner` (6 boundary tests) + restore runbook.
   - **P1-STABLE-INTEGRATION-TEST-CI-GATE** (`9a836fc9`): new `smoke-tests` job in `ci.yml`, runs the 7-class `Category=Smoke` startup suite against a SQL service-container. Chose this (Path B) over re-enabling the full flaky 197-test suite.
   - Code review: 0 Critical/0 High, 6/6 red-team CONFIRMED-SAFE, M1 (RetentionDays<1 self-destruct prune) + L1 (narrow file-delete catch) fixed in-slice. Build 0W/0E, 6/6 unit green. CI-green not yet observed live (runs on PR trigger).

## The Brutal Truth

Two of three threads this session were "we almost built/replaced something that already existed." That's expensive if it happens after the fact instead of before. The Keycloak ask could have burned 4-8 months rebuilding compliance machinery that was already sitting there working. The launch-feature-set ask could have burned who-knows-how-long adding modules when the actual job was picking which of the already-built ones to turn on for v1.0. Both were caught at the advisory/scout stage, which is the cheap stage. That's the win — but it's only a win because scout-first was actually done instead of skipped. The backlog's own audit-effort estimate (M for backup automation) was also wrong — right-sized to S once someone actually looked and found `DatabaseBackupService` logic already existed; only the recurring-job wiring was missing.

## Technical Details

- Backup automation: `BackupRetentionPruner` pure class, 6 boundary tests (0-day, negative, exact-boundary retention). Hangfire recurring job registered at 02:00 daily.
- CI smoke gate: `smoke-tests` job added to `ci.yml`, `Category=Smoke` filter, SQL Server as a GH Actions service-container (not Testcontainers — CI-native).
- Review findings fixed in-slice: M1 — `RetentionDays < 1` config would prune everything including the just-made backup (self-destruct); L1 — file-delete catch block was too broad, narrowed.
- A stale `.git/index.lock` blocked git-manager mid-session. It correctly refused to `git reset --hard` to clear it (per the standing incident where a hard reset previously wiped 6 tracked files — see memory `feedback-never-reset-hard-to-clear-index-lock`). User manually ran `rm -f .git/index.lock` to unblock. Discipline held, no data loss.

## What We Tried

- For the auth question: didn't jump to "here's a Keycloak migration plan" — ran the advisory skill against the actual OpenIddict implementation first (migrations, RBAC phases, PKI tiers) before answering.
- For launch feature-set: didn't start drafting new module specs — ran a 3-source scout (BE endpoints, FE routes, SRS use-cases) to establish what already exists, THEN made the include/defer call.
- For backup automation: didn't accept the backlog row's "M" effort estimate at face value — scouted the codebase, found `DatabaseBackupService` already had the backup logic, re-scoped to just the missing automation wiring (S).

## Root Cause Analysis

Not a failure this time — a caught-before-it-happened. The pattern that keeps recurring (per memory `verify-fe-consumer-before-be-build`, `feedback-plan-aspirational-vs-factual`, `feedback-git-log-s-never-shipped-oracle`) is that requests and backlog rows are framed as if the target doesn't exist yet, or effort is claimed without an empirical check. This session is three more data points for the same lesson, just on the advisory/planning side instead of the implementation side.

## Lessons Learned

- "Replace X with Y" and "what's missing for X" are both claims that need falsifying BEFORE scoping work, not after. Scout first, always.
- Backlog/audit effort estimates are hypotheses, not facts. Empirical scout can move M→S (or the reverse) — trust the repo over the row.
- Stale `.git/index.lock` is not a green light for `git reset --hard`. The refusal-and-ask pattern is now proven twice; keep it.

## Next Steps

- CI-green on the `smoke-tests` job needs to actually be observed on PR #121 before calling P1-STABLE-INTEGRATION-TEST-CI-GATE fully closed — currently unverified live.
- User owns: confirm v1.0 module scope decision (`9962fbc9`) is final before further roadmap work assumes it.
- Backup restore runbook (shipped in `571ba695`) should get a dry-run rehearsal before first prod cutover, not just code review.
