# Journal — QD742-WAVE5-FE Closed as RESOLVED-OBSOLETE

**Date:** 2026-05-11 23:08 ICT
**Branch:** `main` (no commits — closure-only)
**Scope:** P2 backlog row source-verify

## What happened

User asked `/cook QD742-WAVE5-FE --auto` (estimated 3-4h: build FE data-classification admin page consuming Wave 2 BE API).

Spawned 2 parallel Explore subagents to scout BE surface + FE admin patterns. Pivotal finding from FE scout: `web/src/features/data-classification/` **already exists** with full page + modal + API hooks + tests + route + menu + i18n.

Verified production-ready state:
- `tsc --noEmit` → 0 errors
- `vitest run src/features/data-classification` → 5/5 PASS (8.96s)
- BE endpoints (`GET/PUT /api/v1/admin/compliance/classification*`) match FE hooks
- DTOs aligned (`ClassificationRuleDto` ↔ `ClassificationRule` interface, RowVersion as base64 string round-trip)
- Permission gate via `COMPLIANCE.CLASSIFICATION.VIEW/MANAGE` (FE matches BE `PermCodes.cs:547-551`)

## Why the backlog row was stale

Backlog row written 2026-05-10 documenting Wave 1-4+6 ship. Wave 5 FE listed as pending follow-up. But Phase-05 supporting-UIs ship (commit range `c74ba545..806e0c74`, also 2026-05-10) bundled the classification page into the broader admin CRUD pages ship without drain-back to the QD742 Wave list.

Three accuracy nits surfaced in the row itself:
1. Permission code `AUDIT.CLASSIFICATION.MANAGE` — wrong prefix. Actual BE: `COMPLIANCE.CLASSIFICATION.MANAGE`.
2. Cited plan `phase-05-fe-classification.md` doesn't exist in the plan folder.
3. Row described `level/entityType/fieldName` columns + `overriddenLevel` modal — doesn't match shipped level-keyed schema (4 rows: Public/Internal/Confidential/Restricted, no per-field overrides).

All three nits suggest the row was written from a stale design intent, not from `PermCodes.cs` + current entity. Memory-only authorship.

## Outcome

- Decision report: `plans/reports/decision-260511-2308-qd742-wave5-fe-resolved-obsolete.md`
- Backlog row marked **RESOLVED-OBSOLETE** in place.
- Memory `verify-fe-consumer-before-be-build.md` appended with 4th occurrence + rule generalization (verify any shipped-state claim, not just BE-for-FE direction).
- **No code change. No commit needed.**

## Lesson — generalize the verify rule

Original rule (2026-05-09): "Source-verify FE consumer before cooking BE-only rows."
Generalized (2026-05-11): "Source-verify any shipped-state claim in backlog rows older than 7 days, in either direction."

4 closures in 3 days, ~30min verify replaced ~10-18h (or now ~3-4h) speculative implementation. Cumulative ROI ~25-35×. Pattern firmly validated.

## Follow-up

- **(P3, ~30min):** QD742-AUDIT-403 sits right above this row, written same day, same provenance. Worth pre-cook source-verify.
- **(P3, ~1h):** One-pass backlog hygiene sweep across rows older than 2026-05-08 — 4× ROI now demonstrated; sweep amortizes future cook decisions.

## Unresolved

None.
