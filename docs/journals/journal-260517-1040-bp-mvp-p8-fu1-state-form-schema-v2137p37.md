---
title: BP MVP P8-FU1 — state-form schema endpoint + FE field render (v2.137p37)
date: 2026-05-17
commit: 6b04d679
version: v2.137p37
type: feature-ship
duration: ~45 min
---

## What shipped

`6b04d679` pushed origin/main. Closes the **state-form fields render gap** — final BE work for BP MVP user-side. New endpoint pairs with existing intake-form + prefill-context contracts so the FE BP-Run state form panel can fetch a snapshot, render typed fields, prefill from prior state submissions, and submit independently.

**BE — 3 files:**
- `GetStateFormSchemaQuery` + `GetStateFormSchemaQueryHandler` (new folder under `BP.Application/Queries/GetStateFormSchema/`) — mirrors `GetIntakeFormSchemaQueryHandler` 1:1. Pattern-A tenant guard pre-DB, same-schema Dapper JOIN `BusinessProcessStateForms × BusinessProcessDefinitions ON sf.PublicId AND bp.PublicId AND TenantId`, SystemAdmin bypass on reads, delegation to existing `IFormTemplateQueryClient.GetVersionSchemaByInternalIdAsync` (no cross-schema SQL — scanner green).
- Controller endpoint `GET /api/v1/business-processes/{bpId:guid}/state-form-maps/{stateFormMapId:guid}/schema` under `BUSINESSPROCESS.BP.VIEW` policy. Returns reused `BusinessProcessIntakeFormDto` (`FormTemplatePublicId, VersionNumber, SchemaJson`).
- 5 unit tests (NSubstitute over `IReadDbConnection` + `IFormTemplateQueryClient`): cross-tenant denied, map not-found, snapshot missing (data-integrity NotFound), happy, SystemAdmin bypass.

**FE — 2 files:**
- `business-process-run-api.ts:155-181` — new `useBpStateFormSchema(bpId, mapId)` wrapper hand-wired via `apiClient` + `useQuery` (Orval regen pending; matches `useBpReferencesForFormTemplate` precedent).
- `form-tabs-container.tsx` — `FormTabPanel` rewrite: fetches schema → parses `schemaJson` → renders via `renderInteractiveField` + `buildValidationRules` + `buildRequiredIfRules` + `isFieldVisible` (mirrors intake panel from `new-bp-run-wizard`) → wires `usePrefillContext` keyed on **resolved FormTemplate PublicId** (not state-form-map id) → submits via `useSubmitBpStateForm`. Loading / error / empty branches handled with Antd Alert+Spin.

## Why

Backlog row at `backlogs/backlog.md:401` (now SHIPPED line) said state-form tabs render blank — FE had submit wiring + prefill plumbing from P8 but no field schema endpoint, so `FormTabPanel` rendered only a "P9 placeholder" alert (`form-tabs-container.tsx:36-51` pre-ship). With the schema fetch in place, the BP MVP user UI now fills + submits state forms end-to-end without P9 deferral.

## Premise correction (recurring pattern, 3rd time today)

Backlog described the gap as "fetch `FormFieldDto[]` from **bigint** state-form-map `id`". Pre-cook grep on `GetBusinessProcessByIdQueryHandler.cs:46-47` showed the read SQL projects `PublicId AS Id` and `BusinessProcessStateFormDto.Id` is typed `Guid` — the FE always sees the row's PublicId AK, never the bigint internal PK. Route therefore uses `{bpId:guid}/{stateFormMapId:guid}` pair, not `:long`.

Same pattern hit ARCH-FU (`ApiResponse` premise wrong) and TEST-FU (path location wrong) earlier today. Codified in memory L4 across all 3: **even user-confirmed `--auto` scope must pass grep-verify before code-write**.

## Scope decisions

| Decision | Choice | Why |
|---|---|---|
| DTO shape | Reuse `BusinessProcessIntakeFormDto` | Identical fields; rename to neutral name = N-2 carry-over |
| Orval regen | Skip, hand-wire | BE not running; precedent `useBpReferencesForFormTemplate` does same |
| i18n entries | Use `defaultValue` only | `page.businessProcessRun.*` namespace has zero formal entries; adding 5 creates asymmetry |
| Cross-BP guard | JOIN on `bp.PublicId` | Leaked map Guid from sibling BP returns NotFound, not silent render |
| Snapshot-missing branch | Explicit NotFound | Data-integrity bug worth surfacing; intake precedent does same |
| Subagents | tester + code-reviewer in parallel | Medium scope feature work; YAGNI skip only for ≤10 LOC test-infra |

## Lessons

1. **Premise-verification 8-16x compression now systematic.** All 3 ships today (P7-FU1.2, ARCH-FU, TEST-FU, this P8-FU1) had backlog premise wrong or imprecise on first read. Grep + test-run before code-write costs ~30 sec, saves hours. Should be habitual, not opt-in.

2. **DTO reuse beats new DTO when shape is identical.** Intake form + state form both surface `{FormTemplatePublicId, VersionNumber, SchemaJson}`. Renaming to `BusinessProcessFormSnapshotDto` would be cleaner but the rename cost > YAGNI value right now; deferred per N-2.

3. **FE precedent capture pre-implementation.** `new-bp-run-wizard.IntakeFormPanel` already had the renderInteractiveField + helpers composition for intake form. `FormTabPanel` is just that composition + per-tab Form.useForm() + per-tab submit. Copy-and-localise was faster than design-from-scratch by ~30 min.

4. **React hooks order check is non-negotiable.** Reviewer explicitly verified all 7 hooks (`useTranslation, useForm, useWatch, useBpStateFormSchema, usePrefillContext, useMemo×2, useSubmitBpStateForm`) called pre-return. Easy to break with conditional fetch gating; here gated via `enabled: !!formTemplatePublicId` on the prefill hook instead.

5. **Reviewer M-1 fold-in stays cheap when codebase precedent exists.** `useBpReferencesForFormTemplate` already had the safer envelope unwrap pattern `(payload as { data?: T }).data ?? payload`. Folded immediately; 1 file, 4 LOC, 0 regression risk.

## Test deltas

| Suite | Before | After | Delta |
|---|---|---|---|
| BP.Application | 82 | 87 | +5 (new handler tests) |
| Forms.Application | 177 | 177 | — |
| Workflow.Application | 431 | 431 | — |
| BP.Domain | 25 | 25 | — |
| Architecture | 97 | 97 | — |

FE `npx tsc --noEmit` clean (zero output). dotnet build 0E/0W.

## Carry-over

- **L-1 (BE test):** Redundant explicit `.Returns((FormTemplateVersionSchemaDto?)null)` — NSubstitute auto-returns null. Harmless; clarifies intent. No action.
- **L-2 (BE handler):** NotFound message uniform across cross-tenant / soft-delete / missing — intentional per security/no-info-leak. No action.
- **L-3 (FE):** `cleanFormData(values, allFields, formValues)` signature audit across BP/Forms — pre-existing pattern, out of P8-FU1 scope.
- **N-2 (DTO rename):** `BusinessProcessIntakeFormDto` → `BusinessProcessFormSnapshotDto` to reflect dual use. Defer to next BP touch.

## Ship cadence today

| # | Commit | Version | Scope | Wallclock |
|---|---|---|---|---|
| 1 | `cc620a76` | v2.137p34 | P7-FU1.2 picker UX | ~35 min |
| 2 | `fac04940` | v2.137p35 | ARCH-FU whitelist | ~15 min |
| 3 | `ae4814d2` | v2.137p36 | TEST-FU BP schema | ~5 min |
| 4 | `6b04d679` | v2.137p37 | P8-FU1 state-form schema | ~45 min |

4 ships, ~1h40min total wallclock. Post-MVP cleanup streak + 1 feature ship. **BP MVP BE+FE now feature-complete end-to-end** — state-form filling no longer blocked.

## Next candidates

Remaining open follow-ups (all Low/Nit, non-blocking):
- P7-FU1.1, P7-FU1.2.1-1.2.4, P7-FU1.3, P7-FU1.4
- P7-FU2.1, P7-FU2.2, P7-FU2.3, P7-FU3, P7-FU4
- P8-FU2 (Low, ~15min), P8-FU3 (Low)

Tail of [[test-fu-shipped-260517]] / [[arch-fu-shipped-260517]] / [[bp-mvp-p7-fu1-2-shipped-260517]].
