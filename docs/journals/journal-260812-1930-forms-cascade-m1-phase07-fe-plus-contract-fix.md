# Journal 260812-1930 - Forms Cascade M1 Phase-07 FE + Contract Gap Fix

**Date:** 2026-08-12 19:30 ICT
**Branch:** `feat/forms-cascade-public-external-inrow`
**Slice:** M1 Phase-07 finalize - includes reviewer-uncovered contract gap fix
**Effort:** ~1.5d (Phase-07 impl + 2 code-review rounds + BE contract addition + 4 test-writing sessions)

## What shipped

**Phase-07 FE core (per-row cascade wiring in TableFieldWidget):**
- NEW `web/src/features/forms/builder/row-ref-field-select.tsx` (~110 LOC) - row-scoped variant of `RefFieldSelect`, reads parent values directly from a `rowValues` prop instead of `Form.useWatch` (which is blind to plain-`useState` row values - `feedback-antd-usewatch-tracks-registered-fields-only`).
- MODIFIED `table-field-widget.tsx` - 3 Ref-type dispatch cases (EnumRef/InternalRef/ExternalRef) with cascade-vs-static branching; `hasCascadeDependencies` hoisted per-column (avoids N-row × M-column JSON.parse thrash).
- MODIFIED `form-field-renderer.tsx` - thread `templateId` + `publicCode` down to TableFieldWidget.

**Reviewer round-2 findings resolved in-slice:**

- **C-1 - BE `ParentFieldId` contract gap** (reviewer round-1 blocker; round-2 verified fixed): FE `TableFieldWidget` previously discovered TableField child columns via `fieldKey.startsWith("parent.")` prefix. But `AddFormFieldCommandValidator` regex forbids dots in FieldKey (`^[a-zA-Z_][a-zA-Z0-9_]{0,127}$`), so production data never matches - the ENTIRE cascade dispatch was unreachable dead code. Fix: BE `FormFieldDto` +`long? ParentFieldId = null` (backward-safe positional append cuối record); 2 handlers (`GetFormTemplate`, `GetPublicForm`) - `FormFieldRow` +`ParentFieldId` property, SELECT list adds `ParentFieldId`, `MapField` passes `ParentFieldId: f.ParentFieldId`; FE `FormFieldDto` +`parentFieldId?: number | null`; widget discovery switched to `String(f.parentFieldId) === String(field.id)` with prefix fallback for legacy fixtures.

- **C-3 - sibling-row isolation zero test witness** (reviewer round-1 blocker + round-2 phantom-test callout): initial C-3 test used `dispatchEvent(new Event('input'))` on antd `Input` which React swallows (controlled-input tracker bypassed) + `if (onChange.mock.calls.length > 0) { ... }` conditional gate = pass even when widget never emitted. Round-2 rewrite: `fireEvent.change(row0Province, { target: { value: 'DN' } })` (uses React's internal `nativeInputValueSetter` correctly) + UNCONDITIONAL `expect(onChange).toHaveBeenCalled()` first, then row-1 ward preservation asserted across EVERY emission (belt-and-suspenders loop).

- **H-A - `activeFields` double-render pre-existing, slice-activated** (reviewer round-2 High): `form-submit-modal.tsx`, `public-form-page.tsx`, `form-preview-modal.tsx` all filter `activeFields` by `isActive` only - after `parentFieldId` shipped on the wire (C-1 fix), TableField child columns would render TWICE (top-level Form.Item + inside TableFieldWidget's Table) with colliding antd Form.Item names. Fix: 3 renderers add `&& f.parentFieldId == null` guard + defense-in-depth `renderInteractiveField` early-return on `field.parentFieldId != null`.

- **H-B - C-3 phantom conditional** (reviewer round-2 High): see C-3 rewrite above.

**Polish (reviewer round-1 H-2/L-1/defensive gate):**
- `hasDeps` hoisted per-column instead of per (row × render).
- Defensive `enabled && !isPublic && Boolean(templateId)` on authed `useFieldOptions` (prevents empty-templateId GET).
- Eslint-disable comment carries rationale ("only rerun on parent-signature change; value/onChange via fresh closure").

**Test evidence (final):**
- BE Domain 200/200 + App 384/384 + Infra 69/70 (1 known contention flake `FormulaSandboxTests.EvaluateAsync_ReferencesAbsentField_SubstitutesZero`, isolated re-run 307ms PASS - `feedback-contention-flake-family-shared-regex-timeout-root`) + Integration new `FormFieldDtoParentFieldIdWireTests` 1/1 (real HTTP round-trip witnesses `parentFieldId=TableField.Id` on child row, null on top-level).
- FE Forms suite 213/213 (was 210 baseline + 3 new: id-based discovery, 2-row sibling isolation, backward-safe legacy prefix path).
- FE `tsc --noEmit` exit 0.
- Grand total 867/867.

## What was NOT shipped (filed as backlog)

- `FORMS-CASCADE-INROW-INTERNAL-PARENT-FILTER` (v1.1) - InternalRef in-row cascade child value only rejected if outside ENTIRE Internal domain (narrower cross-parent tampering passes silently); vacuous today because `ResolveInternalDataSourceDefaultHandler` returns `[]`. Filed 2026-08-12 from Phase-06 slice-6b review H-1.
- `FORMS-CASCADE-INROW-GUARD-N-PLUS-1-DEDUP` (v1.1) - `InRowCascadeMembershipValidator.ValidateAsync` iterates rows × cascade-children sequentially, no dedup; 500-row × 3 InternalRef = 1500 sequential MediatR queries. Filed 2026-08-12 from Phase-06 slice-6b review H-2.
- `FORMS-FORMFIELDDTO-POSITIONAL-CONTRACT-TEST-DRIFT` (v1.1) - `FormsApiContractTests.cs:17` uses positional args; safe today (Phase-07 appended), latent trap if future param inserted mid-record. Filed 2026-08-12 from Phase-07 round-2 review M-A.
- `FORMS-GET-TEMPLATE-PREFILL-SOURCE-JSON-PROJECTION-GAP` (investigate) - both GET template handlers omit `PrefillSourceJson` from `new FormFieldDto(...)`; pre-existing but Phase-07 made it visible. Filed 2026-08-12 from Phase-07 round-2 review M-B.

## Lessons captured (feedback candidates for MEMORY)

1. **Reviewer-uncovered contract gap can inflate slice scope 3-5x.** The Phase-07 core FE (RowRefFieldSelect + widget dispatch) was ~200 LOC. Round-1 review flagged the FE was shipping into an unreachable BE contract (C-1). Fixing that added BE DTO/2 handlers/FE type/widget rewrite + integration test = another ~150 LOC + full ceremony. Slice ballooned from ~1.25d planned to ~1.5d actual. Signal: when reviewer flags "dead code because contract X blocks it", check whether X can change in-slice or slice should ship-and-file-followup - depends on user-visibility of the dead-code state.

2. **`Form.useWatch` invisible to plain-state = separate component, not shared abstraction (yet).** The `RowRefFieldSelect` copy-paste from `RefFieldSelect` (~90% duplication) is INTENTIONAL - the two hooks (`useWatch` vs. plain `rowValues` prop) read from fundamentally different state sources. Extracting the shared cascade-options logic into a `useRefOptionsCascade` hook would DRY the two but adds a per-hook signature parameter with only 2 call sites - premature. Filed M-1 backlog row for v1.1 when the count justifies extraction.

3. **antd Input `dispatchEvent('input')` is a phantom-test smell.** React's controlled-input tracker only fires the synthetic onChange when the value change came through the internal `nativeInputValueSetter` (which `fireEvent.change` uses correctly). A raw `dispatchEvent(new Event('input'))` bypasses the tracker → React swallows the event → test that expected onChange sees zero calls and (if conditional) passes silently. Rule: for controlled-input assertions in JSDOM, always use `fireEvent.change` or `userEvent.type`, and NEVER wrap the assertion in `if (mock.calls.length > 0)`.

4. **Change-Impact Symmetry 4 axes caught the H-A blast BEFORE user did.** Round-2 reviewer explicitly ran the Downstream axis on the new `parentFieldId` DTO field → found 3 top-level renderers that filter `isActive` only → predicted double-render. Manual QA would have caught it too but only after ship. This is the 5th confirmed instance where the 4-axis discipline caught a "shipped one place, forgot the mirror site" pattern before ship.

## Commit sequence

Slice 6a (Phase-06 slice A - validator extension): `e0f35c132`
Slice 6b (Phase-06 slice B - membership guard): `1787f31b2`
Slice 7 (Phase-07 finalize + contract fix): pending this journal

## Next

Ship-slice: merge branch `feat/forms-cascade-public-external-inrow` → main via single PR carrying C1 (public path) + H2 (external Vault auth) + M1 (in-row cascade). All 3 dimensions have been TDD-shipped and code-reviewed. Estimate: 15min PR ceremony + CI watch.
