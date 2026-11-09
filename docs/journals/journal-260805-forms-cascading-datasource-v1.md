# Forms Cross-Field Cascading Data-Source (v1 Slice) — 8-Phase TDD Cook

**Date**: 2026-08-05
**Severity**: Medium
**Component**: Forms / Data-source resolver, builder, FE field renderer
**Status**: Shipped + merged to main (`04c75105a`); pushed bitbucket + origin. Row DONE `cfac973a5`.

## What Happened

Backlog row `FORMS-CASCADING-DATASOURCE`: a child field's option-list filtered by one-or-more parent fields' selected values. Schema: `DataSourceJson.dependencies:[{dependsOnFieldKey, filterParamName}]` — empty/absent list is the old static path, so it's a backward-safe discriminator. v1 scope = AUTHED-only, TOP-LEVEL fields, External-static + keyed-Enum sources. Deferred to `FORMS-CASCADE-PUBLIC-EXTERNAL-INROW`: anonymous-public, external-Vault-auth, Internal-multi-param, in-row TableField cascades.

## Technical Details

**P1 spike** locked the schema via source-verified symmetry map (INVARIANT 260522-A discipline): confirmed `FormFieldRepository.cs:20` filters by Id only, and the real central gap was `GetFormFieldOptionsQuery` having no parent-values slot — not the schema itself. Two spike corrections: keyed-Enum resolved via config-driven `parentFilterMap` (not a cross-module attribute — user decision), and validation moved to PUBLISH-time whole-graph check rather than per-AddField.

**P2 — validation.** `CascadeDependencyValidator` (Forms.Domain): Kahn topo-sort catches cycles, self-dependency, dangling refs. Runs in `FormTemplate.Publish/Republish`; `DeactivateField` gained an `EnsureNotDependedOn` reject-delete guard. GOV_FRM_040-043. 7 tests, no regression (Domain 185 / App 378).

**P3 — resolver.** `IDataSourceResolver.ResolveAsync` gained optional `parentValues` across 3 impls (Internal accepts-and-ignores, per spike decision H1). External split into testable statics `BuildRequestUrl` / `BuildCacheKey`. **Caught a real bug**: `UriBuilder.Query`'s setter un-escapes then re-escapes, mangling a space inside an already-escaped value — an injection-char test caught it. Fixed by switching to `Uri.GetComponents(UriFormat.UriEscaped)` + `Uri.EscapeDataString` for manual query construction. Cache-key is parent-aware but byte-identical when no parents present (no cache-key drift for the static case). Enum path uses `ApplyParentFilter` (parentFilterMap intersection, fail-safe-empty on mismatch). 13 tests; needed `InternalsVisibleTo` for the new statics.

**P4 — API.** `GetFormFieldOptionsQuery` gained optional `ParentValues`; handler forwards to resolver. Endpoint takes `[FromQuery] Dictionary<string,string> p` (`?p[filterParamName]=value`), stays under the existing class-level `[Authorize]`. 3 tests.

**P5 — FE fetch/watch.** `useFieldOptions(templateId, fieldId, parentValues?, enabled?)` puts parentValues in the query-key so parent changes auto-refetch. `RefFieldSelect` watches declared parents (`Form.useWatch([], form)` precedent), gates the fetch until all parents are set, and clears stale child value on parent change — clearing cascades down a multi-level chain. Pure helpers in `cascade-dependencies.ts`. 12 tests. **Gotcha**: `Form.useWatch([], form)` only tracks fields actually registered with the form instance — a parent set via `setFieldValue` without being rendered as a real `Form.Item` is invisible to it. Test had to render the parent for real to see updates.

**P7 — builder UI.** `CascadeDependenciesEditor` — parent picker + filter-param name + add/remove, with a soft cycle-warning reusing the same adjacency-walk as the BE Kahn validator. `cascade-builder.ts` pure helpers. 15 tests.

**P7-FU (from code review #1).** keyed-Enum had no authoring UI in v1 — user chose to build it now rather than defer: `EnumParentFilterMapEditor` lets authors define `parentValue → [allowed enum values]` directly in the builder. +9 tests.

## Code Review

general-purpose subagent, 8.5/10, 0 critical/high findings. 3 findings, all addressed:
1. keyed-Enum missing builder UI → built (P7-FU).
2. Latent edit-flow bug: clear-propagation would wipe a pre-loaded child value on the first watch's `undefined → snapshot` transition — added a first-watch seed guard. Not reachable today (all current call sites start blank) but would have bitten template-edit UX later.
3. Misleading round-trip comment in External resolver — fixed.

Verified correct on review: backward-compat (empty deps = old path unchanged), SSRF (host/scheme un-alterable by parent values, guards run on the final built URL), cache-key correctness, fail-safe Enum filtering, cycle validator, FE loop-safety, and the 6-site symmetry map from P1.

## Final Gate

BE 617/617 (Domain 185 + App 378 + Infra 54), FE forms 204/204, typecheck 0, lint 0, builds clean. Contract doc: `plans/260804-1701-.../reports/from-spike-to-planner-cascade-contract.md`.

## Lessons Learned

1. **`UriBuilder.Query`'s setter is not safe for pre-escaped values.** It round-trips through decode/encode and corrupts already-escaped characters. Build the query manually with `Uri.GetComponents(UriFormat.UriEscaped)` + `Uri.EscapeDataString` when the value is pre-escaped.
2. **`Form.useWatch([], form)` only sees registered fields.** A `setFieldValue` on a field that isn't rendered as a real `Form.Item` is invisible to watch — affects both prod semantics (unrendered parent fields won't trigger cascade) and test realism (must render the parent to exercise the watch path).

## Next Steps

1. ✅ **DONE** — merged `feat/forms-cascading-datasource` → main (merge `04c75105a`, non-ff).
2. ✅ **DONE** — backlog row `FORMS-CASCADING-DATASOURCE` marked DONE on main (`cfac973a5`).
3. Feature ships gated post-v1.0 (shipped now under explicit user request, ahead of the standing launch-feature-set gate).

## Merge + wrap (2026-08-05 PM, same session)

Merged to main and pushed. Two things worth recording:

1. **Near-duplicate backlog row caught at the merge gate.** `aqt-next` (reading branch-state memory) reported `FORMS-CASCADE-PUBLIC-EXTERNAL-INROW` as "not yet filed" → a row was filed on the branch. But main already carried BOTH cascade rows since 08-04 (`2c056ef3a`, landed on main after the branch tapped off), and main's deferred row was MORE detailed (real red-team C1 root-cause: `FormFieldRepository.cs:20` lacks tenant/template scope). Removed the branch duplicate before merge; kept main's richer row. Lesson → memory `feedback-aqt-next-memory-stale-vs-main-truth`: on a feature branch, memory = branch-truth and cannot see commits that landed on main post-tap — `git show main:backlogs/backlog.md | grep <ROW-ID>` before filing.
2. **Pushed to 2 of 3 remotes.** `bitbucket` (synodus/v26grdnb63-be-corebasev2, org-official) + `origin` (thiennd212/AqtCoreFW GitHub personal), both fast-forward. Skipped `oracle` (thiennd212/AqtCoreFW_Oracle) — it diverges 1432 local / 61 remote, a separate branch; never push main there.

Then branch cleanup: deleted 16 merged-into-main local branches via `git branch -d` (Git refuses non-merged, so 16/16 clean = all truly in main). Kept 4 unmerged (`dev`, `docs/launch-feature-set-v1-decided`, `feature/foundation-perf-surgery`, `feature/phase-04b-workflow-followup`) + 1 worktree.

---

**Unresolved questions:** none. Cascade v1 fully landed on main + pushed (bitbucket + origin); deferred scope tracked under `FORMS-CASCADE-PUBLIC-EXTERNAL-INROW` (already on main). No carry-over.
