# Journal — Forms orphan-workflow-link-retry Phase 06 (FE admin page)

**Date:** 2026-08-04 · **Branch:** main · **Ships:** `9c5e1e440` (feat) + `dd845affd` (docs) → pushed origin/main

## What shipped

Phase 06 of plan `260803-2219-forms-orphan-workflow-link-retry`: the admin **"Pending
Workflow Links"** FE page consuming the Phase-05 BE endpoints, plus a small BE DTO extension.

- **FE:** new feature dir `web/src/features/forms/pending-workflow-links/` (page + hand-written
  RQ hooks + types + index), route `/admin/pending-workflow-links`, sidebar entry in
  Content & Workflow gated by `FORMS.WFLINK.MANAGE`, i18n vi/en. Renders all 4 statuses;
  Retry-now (+ un-shelve) / Shelve row actions. 5 Vitest tests (FE-1..FE-4 + gate) green.
- **BE:** `PendingWorkflowLinkListItemDto` gained `TemplateName`, JOINed via
  `COALESCE(NULLIF(t.NameVi,''), t.Name)` in `ListPendingWorkflowLinksQueryHandler`; DTO-shape
  guard test updated to include it (symmetry).

## Decision that shaped the slice

The plan (written 2026-08-03, before Phase 05 actually shipped) promised a "Template **name**"
column, but the shipped Phase-05 DTO carried only `FormTemplateId` (Guid). User chose to **add
the name to BE first** (BE↔FE symmetry) rather than render a bare Guid. That turned a pure-FE
phase into a small BE contract extension — clean because the query handler already `INNER JOIN`ed
`FormTemplates` (1:1 on PK), so it was a column add, not a new join or row-multiplication.

## What the re-verify caught (INVARIANT 260522-A)

Scouting the *shipped* Phase-05 artifacts against the plan's assumptions surfaced 3 drifts before
any FE code was written:
1. **DTO drift** — plan listed a `templateName?` field that did NOT exist; real DTO had 11 fields
   incl. `ResolvedBy`. (→ mirror the real shape, then add `TemplateName` deliberately.)
2. **Route param** — retry/shelve use `{submissionId:guid}`, not a generic `{id}`.
3. **Route base** — controller is `api/v1/forms/workflow-link-pending`; the FE `apiClient`
   auto-prefixes `/api/v1`, so hooks use `/forms/workflow-link-pending`.

Trusting the day-old plan verbatim would have shipped a broken FE contract. The 15-min contract
re-verify against source paid for itself.

## Verification

- Code-review (subagent): 0 critical/high/medium, 2 optional low (both addressed: added the
  AC-FE-4 status-filter behavioral test; trimmed dead mock scaffolding).
- Tests: 565 green (5 new page + 30 layout + 168 forms regression + 362 BE), 0 fail.
- Blast-radius `TemplateName`: single DTO consumer, count query untouched, JOIN pre-existed.

## Remaining

Phase 07 (backfill script + runbook + test-hardening M-3 + doc-drift fix) — not a launch blocker;
the auto retry job already runs.
