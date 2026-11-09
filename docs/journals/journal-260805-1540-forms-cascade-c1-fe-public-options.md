# Forms Cascade C1 (FE) — Public Form Reference-Field Anonymous Routing

**Date**: 2026-08-05 15:41
**Severity**: Low
**Component**: Forms / FE public-form renderer, public API client
**Status**: Shipped + merged to main (`5976aa2f0`); part of `FORMS-CASCADE-PUBLIC-EXTERNAL-INROW` C1 slice. C1 complete.

## What Happened

Public forms rendered `RefFieldSelect` (cascade-aware reference fields: EnumRef, InternalRef, ExternalRef) against authed `/forms/templates/{id}/fields/{fieldId}/options` (Bearer-required endpoint). Anonymous visitors → 401 → cascading public forms were broken. C1 slice (preceded by BE P01 `ec36c3e22` + P02 `4f1c9bdb7`) routes public forms to a new anonymous `/public/forms/{code}/fields/{fieldKey}/options` endpoint, enabling cascade to work on public forms that reference a dependency chain.

## The Gap (Pre-Existing)

The authed hook `useFieldOptions` hardcoded a request to the Bearer endpoint. Public forms passed neither credentials nor an alternative path. When a public form contained a cascading reference field (e.g., Ward → District → Province), clicking the parent field attempted to fetch options and got 401 → the child stayed empty. The fix is both a leak patch (BE public-surface controller didn't exist) and a feature enablement (cascade never worked on public forms).

## Technical Details

**5 FE files, 260 lines added + 8 removed:**

1. **`form-public-api.ts`** — new hook `usePublicFieldOptions(code, fieldKey, parentValues?, enabled?)`: uses `publicClient` (axios, no Bearer token), routes `/public/forms/{code}/fields/{fieldKey}/options`, mirrors authed hook's query-key namespace (`public-field-options`; disjoint from `forms/fieldOptions` → no cache collision), encodes `p[filterParamName]=value` querystring params matching authed hook + BE handler.

2. **`builder/ref-field-select.tsx`** — optional `publicCode?` prop. Both hooks called unconditionally every render (rules-of-hooks) with mutually-exclusive `enabled` flags (`enabled && !isPublic` vs `enabled && isPublic`); active query = `isPublic ? publicQuery : authedQuery`. Cascade watch/clear logic untouched — watches parent values, gates fetch until all parents set, clears stale child on parent change.

3. **`builder/form-field-renderer.tsx`** — `renderInteractiveField` gained optional `publicCode?`, threaded to BOTH `RefFieldSelect` sites: EnumRef cascade branch AND Internal/ExternalRef branch. Missing a branch = that field-type still 401s → both threaded and tested.

4. **`public-form-page.tsx`** — passes `publicCode={code}` into renderInteractiveField.

5. **`builder/ref-field-select-public-cascade.test.tsx`** — NEW, 4 tests: (1) route selection (public enabled/authed disabled), (2) regression (authed enabled/public disabled — critical backward axis), (3) cascade fire with parent-value propagation, (4) clear-on-parent-change (originally redundant, strengthened by review to assert actual field-value → `undefined` after parent switch).

## Verification

Tests 4/4, forms suite 96/96, `tsc --noEmit` clean. Code review 0 critical/high/medium (1 LOW: test-4 redundancy caught and fixed).

## Decisions

- **DRY**: reused authed hook's `p[${k}]` encoding + query-key shape (short when no parents) rather than inventing a new contract.
- **Rules-of-hooks**: both hooks always called, disabled one never fetches, instead of a conditional single-hook call.
- **Pre-existing observation flagged** (not introduced): `publicClient` baseURL is `(VITE_API_BASE_URL) || '/api/v1'` whereas `apiClient` appends `/api/v1` to env base — with an absolute `VITE_API_BASE_URL` the public client would omit `/api/v1` and 404. Shared Phase-0 public-surface code; unaffected on relative-proxy deploys. Track separately.
- **antd Form.useWatch constraint** (memory `feedback-antd-usewatch-tracks-registered-fields-only`): only sees fields registered as Form.Item → cascade test renders parent as a real Form.Item.

## Next Steps

C1 slice complete. Remaining in `FORMS-CASCADE-PUBLIC-EXTERNAL-INROW`:
- **H2** (Phases 04–05): Vault-authed external DataSource + reject authed-external on public form.
- **M1** (Phases 06–07): in-row TableField cascade validator + FE per-row.
- **H1** (deferred): Internal Handler (vacuous, no real implementation path).

Plan dir: `plans/260805-1409-forms-cascade-public-external-inrow/` (gitignored).

---

**Unresolved questions:** none. C1 shipped and merged.
