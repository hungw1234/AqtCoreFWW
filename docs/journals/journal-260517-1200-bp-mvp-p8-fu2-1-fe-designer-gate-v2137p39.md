# BP MVP P8-FU2.1 — FE Designer-Side Multi-Form Guard

**Date:** 2026-05-17 12:00
**Duration:** ~20 min wallclock
**Component:** BusinessProcess admin UI, form-maps editor
**Status:** RESOLVED (commit `264b91a9` on origin/main)

## What Shipped

FE symmetric defense gate in `business-process-form-maps-view.tsx` `handleSave()` method. After existing required-field validation, added duplicate-transition detection before mutation:

```typescript
const duplicateIdx = transitionRows.findIndex(
  (row, idx) => row.transitionId === id && idx !== index
);
if (duplicateIdx !== -1) {
  toast.error(t("page.businessProcessAdmin.formMaps.edit.duplicateTransition"));
  return;
}
```

**Why this matters:** P8-FU2 BE guard prevents runtime silent data loss (422 if multi-form submitted). P8-FU2.1 FE gate catches the mistake at design-time before admin submits. Defense-in-depth: prevent in UI first, guard on server second.

**Files changed:** 5 (+34 LOC, -1 LOC)
- `business-process-form-maps-view.tsx` — duplicate detection + early return
- `en.json` i18n — `page.businessProcessAdmin.formMaps.edit.duplicateTransition` key
- `vi.json` i18n — Vietnamese translation "tối đa một form cho mỗi transition"
- Backlog: P8-FU2.1 retroactive row added (shipped same morning as P8-FU2)
- Changelog: entry noted FE gate delivery

**Test Results:** FE typecheck PASS (0E/0W), code-review 9.4→9.5+ APPROVED.
**Code-Review Notes:** EN phrasing nit folded inline ("1 form" → "one form" + "supports" → "can be configured with"). Reviewer also confirmed state-form asymmetry is correct per P8-FU1 spec: state forms render as tabs (multi-state-form legitimate); only transition forms accept single-payload per BE contract.

## Decisions & Rationale

**Duplicate detection method:** Array `indexOf()` simpler than Set creation. Transitionrows list small (<20 typical), no perf concern.

**Early return vs disable option:** YAGNI on dynamic option-disable (P8-FU2.1.1 future nit). Pre-save toast preserves admin mental model better than disappearing UI options mid-edit. Feedback-driven if UX concern surfaces.

**i18n key phrasing:** Matched cardinal phrasing across both languages. Vietnamese "tối đa một form" ("at most one form") mirrors English cardinal count, not imperative tone. Consistency with existing validator tone.

**Asymmetry callout in review prompt:** Explicit framing ("state-forms NOT gated because multiple state-forms per state IS supported per P8-FU1") prevented false-positive reviewer flag. Saved a re-review cycle.

## Lessons Learned

### 1. Defense-in-Depth Within 30 Min

BE guard P8-FU2 (11:00) → FE gate P8-FU2.1 (12:00). Same morning delivery. Mirrors R5/R6 delete/deactivate guard precedents: backend silences edge case, FE prevents common path. Symmetric pattern sustainable.

### 2. Fold-In Trivial i18n Nits

Review 9.4→9.5+ on cardinal phrasing alone ("1" vs "one" + "supports" vs "can be configured with"). Zero logic change, pure polish. Folded inline same-commit. Pattern: i18n localization nits don't warrant re-review gate if logic unchanged.

### 3. Asymmetry Clarification Saves Re-Review

State-form multi-instance IS supported (P8-FU1 renders tabs per state). Only transition forms single-form-gated (BE command contract). Explicit in review prompt prevented reviewer questioning coverage. Callout cost ~1 line, saved ~10 min re-review.

### 4. Backlog Retroactive Entry OK When Both Ships Same Morning

P8-FU2 ship filed P8-FU2.1 in changelog only. This session added backlog row + immediately marked SHIPPED. Pattern: when FU discovered same morning, retroactive backlog entry + ship in same session accepted. Next-day discovery would require earlier backlog filing.

### 5. Scope Estimate Accuracy

20 min wallclock vs ~25 min estimate. TSX guard + 2 i18n keys + backlog row + review fold-in. No surprises.

## Carry-Overs

**P8-FU2.1.1 (Nit, deferred):** In-row Select option-disable filter — dynamically disable transition options already selected in another row. Pure UX polish. Pre-save toast sufficient; defer until feedback surfaces.

## Unresolved Questions

None. Duplicate detection premise verified on existing `transitionRows` shape, i18n phrasing confirmed across both languages.

---

**Reflection:** Today's 9-commit cadence (6 feature ships in ~5h) sustained through micro-scope slices + premise-verification habit. P8-FU2.1 completed defense-in-depth within 30 min of BE guard. Backlog retroactive entry pattern now codified. Ship count: 9 commits, 6 features, all green.
