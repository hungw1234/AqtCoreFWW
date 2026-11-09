# Journal — GEO-WARD-DATA-34-PROVINCES: seed all 34 provinces' wards (hệ-34)

**Date:** 2026-06-03 (past-midnight, after a 33-ship 2026-06-02) · **Ship:** v2.137p145 · **Commit:** `b4c6f079` (pushed origin + bitbucket, ahead=0)

## What shipped
Expanded `admin-units-wards-34.json` from 387 → **3351 rows** (34 L1 provinces + 3317 L2 wards). The 31 missing provinces (codes 04…96) now have full ward data, lifting the 3-province limit the P3b address widget inherited. Seeder code unchanged (delta-insert idempotent by `Code`); existing 387 rows byte-preserved. Test updated 3-cities → 34-provinces. No migration / no entity / no contract change.

## The real task was data-sourcing, not code
`/aqt-next` had recommended EOD wrap (33 ships, 23:21). User chose to cook this row anyway. Scout immediately surfaced the crux: the seeder + JSON pattern is trivial; the entire substance is **sourcing ~2900 ward names across 31 provinces from authoritative hệ-34 sources** — data not in the repo, which I must not fabricate for a real gov system. Surfaced 4 provisioning options; user chose "I WebSearch official sources."

## Methodology that made it safe (the lessons)
1. **Spike one province end-to-end first** (advisor). Cao Bằng: found NQ 1657 → official total 56 → extracted 56 → count-gate PASS. Proved the pipeline + locked conventions (bare names, `nameEn` = mechanical diacritic fold validated to reproduce all 386 existing nameEn, positional codes) before fanning out.
2. **Count-gate as the discriminating constraint.** Each province's extracted list must equal the NQ-stated total quoted verbatim. This separates sourced from fabricated — findability was never the problem, count-verifiability was.
3. **🔴 Do NOT trust subagent self-report — re-verify with a script.** 6 parallel researchers all returned "DONE, count-gate PASS." An independent Python re-check of `len(wards)==officialTotal` against the actual fragment files **caught 2 false-PASS truncations**: Nghệ An (claimed 130, file had 104) and Đắk Lắk (claimed 102, had 83). Re-sourced both; Đắk Lắk still came back 101 → diffed against the authoritative list → found the one dropped xã ("Buôn Đôn") → 102. **The agents' PASS claim was worthless; the script check was everything.**
4. **Reconcile the aggregate to zero, don't hand-wave.** National 3317 vs commonly-cited 3321 = −4. Confirmed 3321 counts HCM at 168; we ship 164 (the pre-existing P3a "Dầu Tiếng" dedup). 3321 − 4 = 3317 exactly → clean reconciliation, and the clean magnitude is positive evidence the other 33 counts are right.
5. **Name the verification scope honestly.** Counts + provenance-consistency + structure + nameEn-fold verified for all 34. Per-ward *name* ground-truth (direct re-fetch/diff) only for Cao Bằng + Đắk Lắk + Quảng Trị. The other 28 rest on harvester faithfulness to cited chinhphu.vn sources — count-gate is blind to hallucination-at-correct-count. Closed the row **DONE-with-residual**, not a clean DONE. Acceptable bar for T4 demo data; production-grade address accuracy would require exhaustive re-fetch.

## Verification
Build slnx 0E/0W · `AdministrativeUnitSeederTests` 4/4 real-SQL (34 L1, 3317 L2, anchors HN126/HCM164/ĐN94/CaoBằng56/NghệAn130, code uniqueness, diacritics, idempotent re-seed = 0 new) · code-review APPROVE 0 Crit/High/Med (independently re-verified old rows preserved + treasury reads separate `masterdata.Wards` table → unaffected).

## Quảng Trị spot-check scare (resolved)
A 3rd-batch spot-check looked like a name mismatch (first 5 shipped names ≠ source's first 5). Investigation: Quảng Trị merged Quảng Bình+Quảng Trị; all 5 source names were present in my data at positions 9–13 — the harvester ordered phường-first vs the NQ's xã-first. Pure ordering difference (cosmetic), not hallucination. Filed the intra-province ordering inconsistency as a known cosmetic note.

## Carry-forward
- **`GEO-WIDGET-FILTER-ACCENT-INSENSITIVE`** (T4, new): `address-field-widget.tsx:52` `filterOption` is diacritic-sensitive — "nghe" won't match "Nghệ An". Materially worse now with 34 provinces + up-to-166 wards. FE-only ~0.25d.
- Residual: 28 provinces' per-ward names not exhaustively re-fetched (named in row + changelog).
- Provenance artifacts (`research/dia-gioi-31-tinh-source.md` + `geo-fragments/`) are gitignored → provenance captured in changelog v2.137p145 + the backlog row.

## Meta
- Scout-first paid off: the "1-2d" estimate was right but the shape was "data-curation under an integrity bar," not coding. Naming that up front set the whole approach.
- The single highest-value move was distrusting 6 confident "PASS" reports and re-checking with 20 lines of Python. Tired-at-midnight + agent-optimism is exactly when independent verification earns its keep.
- Spike → fan-out → independent-verify → reconcile → spot-check is a reusable shape for any "harvest N things from external sources under an accuracy bar."
