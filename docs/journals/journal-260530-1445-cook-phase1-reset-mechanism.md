# Journal — Reset All Mechanism (Phase 1): Build + Review, Runtime Deferred

**Date:** 2026-05-30 ~14:45
**Type:** Cook Phase 1 (build + review ONLY; user deferred runtime verification).
**Commit:** `1dc09ce6` local main, **unpushed**. 3 files: `scripts/reset-all.ps1` + `docs/dev-data-reset-runbook.md` + `.gitignore`.
**Artifacts:** Plan `plans/260530-0803-reseed-all-modules-demo-data/`, phase-01 complete code; 2 research files (BNV structure + geography sourcing).

## What

Build + review the hard-reset orchestrator (`reset-all.ps1`) and runbook that:
- Env-guards (dev-only, DB=AqtCoreFW, docker local).
- Stops ports (5002 Auth, 5001 API) and optionally wipes DB (`DROP DATABASE` / `docker compose down -v`).
- Vault re-seed, build, start Auth (Identity+Messaging+OpenIddict migrations), start API (15 DbContexts + all seeders).
- Poll 4 literal completion markers (pinned from seeder source, not paraphrased).
- Detect 2-restart abort-marker convergence (Identity abort → chop log → restart API once).
- Verify: Tenant/Users/OrgUnit row counts + tabular summary in Vietnamese.
- Runbook: 2 modes, gotchas, demo-seeder env convention, troubleshooting.

No database touched yet (user will `/clear && /cook` when ready). Status: code complete, review pass, convergence logic proven-correct vs seeder source code — but zero runtime proof (not run at all).

## Code quality & decisions

**Code review: 8.5/10** (0 Critical/High found). 4 fixes applied mid-review:

1. **Clamp identity-wait budget:** script waits up to 60s for "Identity migrations complete in …" but seeder is v3.0 standard (fast, ~2-5s cold). Set floor to 8s, bail earlier if found, cap 30s total (was uncapped timeout trap).

2. **Env-guard teeth:** NOT just string-match `Development` — check **locality** empirically: DB connection string hardcoded `AqtCoreFW`, docker exec to local sqlserver confirm, block if env ≠Development (throw-silent =  risky fail-open). NOW: throw early + loud.

3. **UTF-8 BOM mandate:** Initial version UTF-8 no-BOM → PS 5.1 `Get-Content -ReadCount 0` + `[ScriptBlock]::Create()` mis-decode tiếng Việt (quotes/braces garbled) → `ParseFile` error false-positive. Added `[System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)` manually to avoid PS encoding trap. Verification: reparsed artifact (BOM or no-BOM) in 5.1 & 7 both clean. **NOW: save UTF-8 WITH BOM** to future-proof against PS default.

4. **Abort-marker log-wipe load-bearing:** After "no BTC-SEED users yet" abort-marker, script clears logs + restarts API. Log-wipe is **NOT optional** — stale abort marker masks the actual issue on second run, infinite retry loop. Added clamped comment: "DO NOT remove; abort-marker stale on retry without wipe."

**Poll-marker verification:** 4 markers pinned LITERAL (not paraphrased):
- `BtcOrgUnitSeeder.cs:58` → `All phases complete` (regex exact).
- `BtcIdentitySeeder.cs:63` → `complete in` (substring OK, non-ambiguous).
- `BtcAssignmentSeeder.cs:179` → `complete:` (uppercase, not lowercase `complete` on line 77 abort).
- `MasterDataSeeder.cs:46` → `complete:` (matches both identity-style).
Lesson: **Never poll a paraphrased marker from a plan; verify source FIRST** (260529-A empirical-probe, reinforce 260522-A slice-start audit).

**2-restart convergence algorithm:** Proven correct vs seeder state machine:
- Boot 1: empty DB → seeders run to completion → `All phases complete` found → exit 0.
- Boot 2 (if user manually resets): resume-skip (idempotent seeders, drift-WIPE only BTC users → resume on existing state) → hoit tụ same marker → exit 0.
- Edge: seeder abort → log clears → retry API → retry Identity or timeout.

## Process meta — lessons

**Brutal-honesty surface:** Build-only mode splits success-criteria. Code compiles & passes review ≠ runtime-verified. Reported status **DONE (code/design/review pass)** but explicitly **RUNTIME UNPROVEN** — did not claim convergence-polls work or verify row counts until user actually runs it. Temptation to over-claim "done" on clean build + green review; resisted. Truth: design-correct, never-run.

**Advisor catch (sourcing risk):** Poll-marker sourcing must be EMPIRICAL (grep source) before code, not paraphrased from plan. Initial draft paraphrased "seeders log completion" → advisor flagged "polls will hang" → re-sourced actual literal strings from .cs files → added `[regex]::Escape` to handle edge chars. Saved a silent hang after ship.

**User-deferred runtime:** Session scope = build phase ONLY. User will run in separate session after `/clear`. Explicit non-claim: unresolved questions on **scale** (BNV tree count) + **sourcing** (real geography data) carried to Phase 2/3; not silenced.

## Sourcing P2/P3 (researcher parallel, DONE_WITH_CONCERNS)

Delegated BNV structure + geography sourcing to 2 researchers in parallel.

**P2 — BNV real org tree (`research/bnv-org-structure-source.md`):**
- Planned ~100-200 units was overshoot. Real Bộ Nội Vụ post-2023 reform: ~23-30 units cấp 2 (10 Vụ + 5 Cục + 3 Ban/VP/Thanh tra + ~5 sự nghiệp per NĐ-CP 25/2025).
- **NOT scale-equal BTC** (no regional chi-cục network, much smaller).
- **Impact:** Phase 2 must revise tree from ~100-200 → ~23-30 nodes to match reality. Concern: user initially accepted inflated estimate; resourcing reality corrects scope upward in design-time (preferred) not runtime-fail (bad).

**P3 — Địa giới hệ-34 (3-city phường/xã) (`research/dia-gioi-3-thanh-pho-source.md`):**
- HN: 126 phường/xã · HCM: 168 · ĐN: 94 (per NQ-UBTVQH15, all have diacritics).
- **4 unresolved questions for user:**
  1. HCM — seed all 168 or ~75 legacy (pre-2023)?
  2. ĐN — seed all 94 or ~16 legacy?
  3. Hoàng Sa/Côn Đảo special islands — include?
  4. Need ĐVHC codes (administrative codes) for each ward?
- **Concern:** researcher passed questions back (DONE_WITH_CONCERNS); awaiting user direction before P3 impl.

## Unresolved questions (carry to next session)

1. **BNV scale finalization:** User approval on ~23-30 units vs plan's ~100-200. Sourcing complete but tree design pending.
2. **HCM/ĐN geography split:** User chooses full modern (168/94) vs legacy subset (75/16) for demo DB.
3. **Special territories:** Hoàng Sa/Côn Đảo inclusion decision.
4. **ĐVHC code sourcing:** Needed for reporting/integration? Decide P3 scope.
5. **Env var naming:** `Seed__Demo__Enabled` vs per-seeder section (e.g., `DemoSeeder__Enabled`) — standardize in P2 once first demo-seeder code lands.
6. **JobTitles DRY:** `BtcPositions` (15 entries) vs incoming JobTitles — share source or duplicate (decide P3).

## Next session

- User runs `reset-all.ps1 -Force` on dev box (docker + sqlserver up).
- Verify actual Tenant/Users counts match seed design.
- If 2-restart flake appears: debug log + poll timing.
- Once Phase 1 runtime-proven: proceed Phase 2 (BNV tree generalization + P2 seeder).
- **PUSH** when user green-lights runtime success (not pre-emptively).
