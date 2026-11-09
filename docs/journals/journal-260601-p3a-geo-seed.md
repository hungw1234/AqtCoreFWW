# P3a Geo Seed Shipped — Two-Model Discovery + Anti-Transcription Parser

**Date**: 2026-06-01 21:52
**Severity**: Medium
**Component**: MasterData / Seeding
**Status**: Resolved

## What Happened

Completed P3a (geo-seeding slice) after a one-session prep+cook pair. Shipped `AdministrativeUnitSeeder` + 387-unit 2-level hệ-34 dataset (HN 126 / HCM 164 / ĐN 94 + Hoàng Sa + Côn Đảo), dropped CaseTypes seeding, expanded JobTitles 4→17. Commit `3c7bb84d`, unpushed. Build 0E, MasterData 15/15 real-SQL, review APPROVE-WITH-NITS. User overrode fatigue (21:26, second override same day after P2 completion).

## The Brutal Truth

The fatigue wasn't warranted. Prep (read-only, 30m) surfaced two load-bearing discoveries that would have become painful mid-cook: (1) the FE address widget reads legacy `District`/`Ward` entities, not `AdministrativeUnit` (new model was behavior-invisible until someone re-pointed the widget code); (2) the treasury seeder (`OrgTreeSeeder.Treasury`) reads `masterdata.Wards`, so retiring Ward breaks BTC KBNN placeholder generation. Without prep, both surface as "it doesn't work" at test/demo time.

The parser decision (extract ward names from markdown tables, never type them) saved a 21:00 transcription nightmare. 388 Vietnamese diacritic names at fatigue-peak is exactly where silenthand-typo creeps in.

HCM count mismatch (164 not 168) required asking the user to confirm a deviation from their stated number. The rule ("don't silently change user-picked numbers") landed right — user confirmed 164 is correct after seeing the dedup reasoning.

## Technical Details

**Scout discovery:** `MasterDataController.cs:42,55,68` → addresses cascade endpoints read `District`+`Ward` via `provinces/{c}/districts` + `districts/{c}/wards` routes. Endpoint `admin-units` exists but widget never calls it (`address-field-widget.tsx` hardcodes 3-level Tỉnh→Huyện→Phường, zero-awareness of AdminUnit).

**Treasury blocker:** `OrgTreeSeeder.Treasury.cs:47` — Dapper loop over `masterdata.Wards` to generate placeholder org units for BTC L4 KBNN wards. Retiring Ward = no-op placeholder generation.

**Parser mitigation:** `research/generate-admin-units-json.py` extracted 388 unit names **directly from source markdown** (tables for HN/ĐN, prose for HCM), never transcribed. Assertion loop per-city count FAIL-LOUD before JSON write. Fidelity verified by reviewer: 384/384 ward names are exact source substrings.

**HCM dedup:** source lists "Dầu Tiếng", "Long Hòa", "Thanh An", "Minh Thạnh" twice each (once under phường, once under xã) with source's own "[xem ghi chú]" flag. Dedup to 4 distinct → 164 total. 168 would mean 8 rows under one province with identical names (invalid schema assumption).

Error message if hand-transcribed: None, because parser enforced per-city counts before write. Manual-type risk: silent diacritic mutation + off-by-one on count (no assertion without running).

## What We Tried

(1) Prep-before-cook vs. open-cook directly on S3 framing — chose prep.
(2) Two-model splitting vs. re-leveling District/Ward in place — advisor + user confirmed path (A) per S3 ("seed AdministrativeUnit, re-point widget").
(3) Hand-extraction of 388 ward names vs. parser + source-markdown — chose parser.

## Root Cause Analysis

Why did prep earn its keep? Because the framing "seed hệ-34 to AdministrativeUnit" omits the fact that nothing *reads* AdministrativeUnit on the live form. The decision to migrate *to* a model presupposes that model is live; pre-build verification (i)-(iv) surfaced the split obligation: model change (P3a, invisible) + consumer migration (P3b, breaking).

Why HCM 164 not 168? Source ambiguity on classification (phường vs. xã, QĐ19/2025 boundary cases). Dedup is data-clean; 168 would require duplicating rows. User confirmed this is correct after reasoning. The rule ("ask before silently changing numbers") applied correctly.

Why parser over hand-type? At 21:00 on fatigue override, transcription error is not hypothetical — it's the most likely failure mode for a 388-row manual paste. Parser asserts per-city count, preventing a silent off-by-one. Parser cannot guarantee diacritic fidelity against source without reviewer check, but it eliminates the "fat-finger" class of errors entirely.

## Lessons Learned

**1. Prep-before-cook is not busywork.** Two-model coupling + Ward/treasury binding would have surfaced as test failure or "form renders blank" mystery. Reading endpoints + entity consumers *first* turns vague architecture into concrete constraints.

**2. FAIL-LOUD assertions catch misses grep won't.** Per-city ward count (3 cities, ASSERTED in parser before JSON write) caught the HCM dedup ambiguity empirically. A manual JSON file would need reviewer to hand-count 384 rows; the parser makes it fail the build if counts don't match expectations.

**3. Don't transcribe at fatigue-peak.** A 388-row diacritic paste at 21:00 is a social debt waiting to happen. Invest 10m in a parser that forces source-fidelity. Reviewer verified 384/384 names are source substrings; that's non-vacuous coverage a hand-count wouldn't guarantee.

**4. User-picked numbers are sticky; don't silently adjust.** Rule worked correctly: HCM 168 stated → found 164 in source via dedup → asked user via `AskUserQuestion` → user confirmed 164 is correct. This avoided a silent regression if auto-rounding to nearest 10 had seemed harmless.

**5. Ward survives as a dependency, not optional.** Treasury coupling wasn't obvious until concrete source code read (`OrgTreeSeeder.Treasury.cs:47`). "Retire legacy geo model" looked free until the Dapper loop became visible. Now P3b is gated on resolving 5 open questions, including Ward's fate.

## Next Steps

- **PUSH commit `3c7bb84d`** (origin + bitbucket) — currently unpushed.
- **P3b gated on 5 open questions** (widget re-point mechanism / District/Ward retirement / admin-UI tabs / treasury re-point / orphan JSON keys) + live orphan-district probe (`SELECT COUNT(*) FROM FormSubmission WHERE SubmissionData LIKE '%"district"%'`).
- **P4 Workflow Defs interleavable** — depends on P2 only, not P3. Can start in parallel after push.
- **Ward ↔ AdminUnit L2 alignment** — resolve in P3b cook; if treasury re-points to AdminUnit, Ward fully retires; if Ward stays dormant, it's a carry-forward technical debt.

**Linked:** [[monday-260601-p2-3c-tenant-assignment-and-legacy-delete]] (P2 complete, same day) + [[saturday-260530-reseed-all-modules-demo-data-plan]] (parent plan) + [[phase-04a-a-invariant-closure-260522]] (260522-A backlog-audit basis).
