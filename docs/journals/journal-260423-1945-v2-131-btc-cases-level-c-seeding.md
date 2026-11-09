# v2.131 — BTC Cases Level-C Seeding (10M shipped)

## What shipped
Extended `CasesBenchSeeder` to populate 10M realistic BTC Level-C Cases alongside existing 10M BENCH Draft cases (prefix carving `BENCH-%` / `BTC-%`). Zero destructive impact on BENCH dataset. All 7 post-seed SQL validation assertions passed.

## Metrics

| Metric | Value | Target |
|--------|-------|--------|
| Total execution | 61m34s | 90m |
| Bulk insert throughput | 3,607 r/s avg (11,177 peak) | 2,000+ r/s ✓ |
| NCI rebuild (10 idx × 20M) | 11m43s | 15m ✓ |
| Distribution max deviation | 0.013% | 0.5% ✓ |
| Post-seed validation assertions | 5/5 PASS | 5/5 ✓ |
| WFI.CompletedAt match (Closed) | 499,053 exact | match ✓ |
| Transaction log peak | 23 GB | 20 GB (warn, continued) |
| Unit tests | 35/35 PASS | 35/35 ✓ |

## Technical decisions worth remembering

1. **Batch size = throughput slope**: Dry-run 50k batch yielded 1.64k r/s; full 100k batch hit 3.6k r/s (2.2× gain). Default seeder batch exists for a reason.

2. **CreatedAt temporal window**: BTC users + OrgUnits only exist from 2026-04-22/23. Tight window nearly blocked FK integrity. RNG seeding happened day-of; avoid backdating user/org creation.

3. **WFI.CompletedAt = Closed only**: DB IsFinal=1 truth. Approved/Rejected intermediate states stay NULL. Honored in Level-C blueprint patch-in-place design.

4. **SLA fallback dict seeder-side**: DB SlaHours NULL on all 7 CaseProcess states → YAGNI on migration. Populated from StaticPool hardcoded dict; no schema change.

5. **Fast-path RNG guard preserves determinism**: `_useDistribution=false` skips sampler, preserves MD5 baseline (BENCH bit-for-bit). Regression test verified pre/post refactor.

## Surprises & lessons

**Dry-run misjudgment**: 1.64k r/s on 50k batch looked like 5× below floor. Almost filed optimization blocker. Full 100k batch run vindicated decision — recovered to 3.6k r/s. Lesson: test at scale, not sample batch size.

**Tempdb auto-grow worked**: Started 571 MB free; 10-index NCI rebuild + SORT_IN_TEMPDB auto-grew without OOM. 664 GB disk headroom absorbed spill. Infrastructure resilient.

**Monitor regex brittleness**: Progress pattern `[1-9],?[0-9]{0,3},000,000` missed batch-cadence milestones (1.1M/2.1M/3.1M, not round millions). Trivial fix but revealed log format assumption risk.

## Next steps

Manual UI login test pending (separate user task). Plan.md status=complete. All 7 artifacts synced (dependency-map, roadmap, changelog, backlog, project-state, MEMORY). Ready for git-manager → v2.131 commit.

**References:**
- Phase execution: `plans/260423-1713-btc-cases-level-c-seeding/phase-06-level-c-seed-and-validate.md`
- Code: `src/AqtCoreFW.Tools.CasesBenchSeeder/` (6 new fields, 8 CLI flags, 5 validation asserts)
