# Journal — PHASE7-TIER-D-CASCADE Re-Verify (2026-05-09 16:20 ICT)

## What changed

No source code modified this session. Verification + classification + backlog sync only.

- `backlogs/backlog.md` — PHASE7-TIER-D-CASCADE marked RESOLVED with re-verify metrics; PHASE7-TIER-D-RESIDUAL row replaced with classified 19-failure breakdown (6 buckets); PHASE7-TIER-D-LOCKOUT-VICTIM-PARALLEL marked UNBLOCKED.

## Re-verify outcome

Pre-fix Tier D baseline (2026-05-08, post-qd742-§5.2 swap): **256 PASS / 83 FAIL** (`web/test-results/.last-run.json`).

Re-run today on `main` (post `9a3607dc` source-fix): `npx playwright test --last-failed --workers=1` — **64 PASS / 19 FAIL of 83 retried**.

**Cascade-resolved: 64/83 = 77%.** Confirms admin-lockout pollution from TC-AUTH-022 was the dominant root cause. The source fix (TC-AUTH-022 + 2 change-password specs swapped admin→non-admin users) removed the dominant pollution vector, and 64 specs that depended on a clean admin token now pass.

19 residual non-cascade failures isolated. All non-cascade by elimination: they were already failing before the fix and remain failing after; the fix didn't address them and shouldn't have. Classified into 6 buckets (workflow setup chains, public-form FE rendering, MFA anonymous expectations, module list/CRUD, browser auth landing, single delegation TC-DEL-001 stale-seed) — see backlog row PHASE7-TIER-D-RESIDUAL.

## Process notes

- Dev stack boot in `--auto` mode required workaround for VBCSCompiler file lock when starting Api+AuthServer concurrently (`feedback-kill-dotnet-before-build`). Procedure: kill VBCSCompiler → `dotnet build` AuthServer alone → `dotnet run --no-build`. API boot also takes ~90s on cold cache; AuthServer +20s after that. Worth documenting as `aqt-runapp` skill enhancement vs current interactive monitor loop.
- AuthServer `/health/live` returns 404 (no such route); only `/health` exists. Readiness probe needs both endpoints to handle both hosts uniformly.
- 7-minute Tier D re-run (64+19 specs, single worker) — acceptable for `--last-failed` workflow; full parallel would be 2-3min but invites lockout-victim contention per LOCKOUT-VICTIM-PARALLEL backlog row.

## What was deferred + why

- **Full Tier D clean re-run** (no `--last-failed`) deferred to next session. Today's --last-failed run only revisited the 83 prior failures; the 256 prior passes weren't re-validated post-fix. Residual classification will sharpen once a clean run separates pollution-residue from genuine drift. Estimated 15-20min single-worker, 5min parallel (latter risky pending LOCKOUT-VICTIM-PARALLEL).
- **Drilling into 19 residuals** — each bucket needs its own investigation (workflow setup ordering, FE form route check, MFA contract diff, etc.). Not in scope for re-verify session.

## Code review

N/A — no source change. Backlog edit + journal only.

## Mistakes / corrections

- Trusted backlog claim "83 unclassified failures" without re-running first; turned out 64 of those 83 cleared automatically because the source-fix landed yesterday. Lesson: when re-verifying after a known fix, expect the failure count to drop substantially before classifying — classification before re-run wastes effort on cascade victims.
- Used wrong CWD in initial AuthServer `--no-build` background launch (relative `src/host/...` from worktree root expanded incorrectly when re-fired). Corrected to absolute `C:\AqtCoreFW\src\host\AqtCoreFW.AuthServer`. Already covered by general guidance in CLAUDE.md.

## Files modified

- `backlogs/backlog.md` (~30 lines edited)
- `docs/journals/journal-260509-1620-phase7-tier-d-cascade-reverify.md` (this file, new)

## Test impact

- Tier D re-verify: 64/83 cascade closure confirmed. 19 residuals isolated for follow-up.
- No .NET unit/integration test runs — code untouched.

## Memory implications

- Existing `feedback-kill-dotnet-before-build` validated 2nd time this month — keep.
- New observation: dev-stack boot procedure under `--auto` needs documentation. Candidate memory entry IF the pattern recurs in next 2 sessions; defer for now (single-incident YAGNI).

## Spawned backlog

None. PHASE7-TIER-D-RESIDUAL classification *is* the spawned work product — rows existed already, just reshaped.
