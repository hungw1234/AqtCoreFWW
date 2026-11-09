# Journal — 2026-05-17 08:01 ICT — BP MVP P8 + P7 + P7-FU1/2/3 session bundle (v2.137p29–p33)

## Session goal
Original entry: `aqt-next` Option B (P7 + P8 fresh window). Final delivery: **5 ships in single window** — P8 user runtime UI, BE-FIX (CreateBP Guid contract), P7 admin UI reduced scope, P7-FU2+FU3 BP query client + delete/deactivate guards, P7-FU1 form-map designer with ETag concurrency. BP MVP feature-complete across BE + FE.

## Ships

| # | Commit | Version | Scope | Wallclock |
|---|--------|---------|-------|-----------|
| 1 | `e07b875e` | v2.137p29 | P8 user runtime UI — wizard, detail, transition flow | — |
| 2 | `5db45429` | (chore) | orval regen — non-BP BE additions | — |
| 3 | `a8e6c83e` | v2.137p30 | BE-FIX `CreateBusinessProcessRequest` accepts Guid PublicIds, resolves to bigint in handler | — |
| 4 | `5dfd2bf5` | v2.137p31 | P7 admin UI — list, 2-step wizard, read-only detail (reduced scope; 9 FU deferred) | — |
| 5 | `71a714b4` | v2.137p32 | P7-FU2+FU3 — `IBusinessProcessQueryClient` + delete/deactivate guards | ~45 min |
| 6 | `6cca2727` | v2.137p33 | P7-FU1 — form-map designer atomic replace-all with ETag concurrency | ~90 min |

All commits pushed `origin/main`. Working tree at close: only `web/playwright-report/index.html` (auto-generated artifact).

## What worked

### Phase A (FU2+FU3) — BP query client + guards
- New csproj `AqtCoreFW.BusinessProcess.Contracts` at `src/shared/` (SDK-only, no PackageReferences — mirrors `Forms.Contracts` / `Workflow.Contracts` layering).
- `IBusinessProcessQueryClient` 2 methods: `ListBpsReferencingFormTemplateAsync(long, Guid)` + `ListBpsReferencingWorkflowDefinitionAsync(long, Guid)` → `IReadOnlyList<BusinessProcessReferenceDto>`.
- `InProcessBusinessProcessQueryClient` Dapper same-schema UNION ALL across 3 BP tables (root + state forms + transition forms). CrossSchemaSqlScanner green.
- 2 GET controller endpoints; 2 FE pre-flight delete/deactivate modals (forms-page + workflow-admin-page + workflow-definition-detail-page).
- Reviewer score 9.0 effective 9.5+; M-1 (i18n keys absent) was false alarm — grep confirmed `refs.*` keys present at vi.json/en.json:2077.

### Phase B (FU1) — Form-map designer with ETag
- `ReplaceFormMapsCommand` atomic replace-all (D-B-1) + server-side `displayOrder` from array index (D-B-2).
- ETag (If-Match) optimistic concurrency: base64 RowVersion exposed in `BusinessProcessDetailDto`; `PreconditionFailedError` → 412. In-memory ETag pre-check + GlobalExceptionHandler maps `DbUpdateConcurrencyException` → 409 fallback.
- 9-step handler: load → ETag → resolve IDs → validate no dups → Clear*Forms → Add*Form per item → UpdateAsync → cache invalidate.
- `BusinessProcessDefinition` aggregate gains 4 collection mutators (`RemoveStateForm` / `RemoveTransitionForm` / `ClearStateForms` / `ClearTransitionForms`).
- FE dual-mode view (read-only + edit) + Edit/Cancel/Save toggle on detail page.

## What didn't / lessons

### L1 — Plan defect: T1 entity Guid-resolve impossible
Phase B-0 plan called for `TryResolveStateInternalIdAsync(Guid, ...)` but `WorkflowState` / `WorkflowTransition` are T1 entities (bigint Id, no Guid AK). Pivoted mid-flight to `GetDefinitionStructureAsync(long, Guid) → WorkflowDefinitionStructure(StateIds, TransitionIds)` for injection-attack defense (resolve-then-validate-membership), with bigint DTOs throughout. **Codify:** verify T1 vs T2 at plan-time — Guid round-trip impossible on T1.

### L2 — C-1 audit-bypass bug (Critical, caught pre-ship)
`ReplaceFormMapsCommand : IRequest<Result>` instead of `: ICommand`. `AuditBehavior` pipeline constrained to `IBaseCommand` via `ICommand` / `ICommand<T>` — so adding `"Replace"` to `CommandVerbs` was insufficient; the command itself had to implement the interface or audit silently bypassed despite verb match. 3-LOC fix (`using AqtCoreFW.SharedKernel.Application;` + `: ICommand`). Tests verified AuditBehavior 10/10 PASS post-fix. **Codify:** new-command checklist must include `: ICommand` (not `: IRequest<>`) for audit pipeline.

### L3 — M-2 attempted fix REVERTED (layering)
Initial attempt added `using Microsoft.EntityFrameworkCore;` + try/catch `DbUpdateConcurrencyException` → `PreconditionFailedError`. Build failed: `BP.Application` has no EF reference (layering rule). Reverted; rely on GlobalExceptionHandler precedent (Identity module). Layering preserved.

### L4 — Reviewer M-1 false alarm
Reviewer claimed i18n `refs.*` keys absent. Grep proved keys at `vi.json` + `en.json:2077`. **Codify:** reviewer claims about file content should be verified via grep before acting, not auto-trusted.

### L5 — Pre-existing arch test failure unrelated
`Rule1_GuidNewGuid_DirectCall_Banned` fails at `ApiResponse.cs:14` — confirmed pre-existing since 2026-03-16 `a7919abf`. Prior 3 ships used filter `~ArchitectureTests` which didn't catch failure namespace `~Architecture`. Filed as ARCH-FU; not blocking.

## Decisions made
- **D-cross-1..5**: BP Contracts as new csproj (not in `SharedKernel.Contracts`) to preserve module ownership.
- **D-A-1**: Dapper UNION ALL same-schema (BP module owns all 3 tables; no cross-schema needed).
- **D-B-1**: Replace-all atomic semantics (not delta) — simpler client, simpler server, fewer races.
- **D-B-2**: Server-side `displayOrder` from array index — client just sends ordered list.
- **D-B-3**: ETag base64 RowVersion exposed in DetailDto — scope deviation accepted (ReplaceFormMaps mandates ETag; FE cannot supply If-Match without it).
- **D-B-4**: `"Replace"` appended to `AuditBehavior.CommandVerbs`.

## Test status
- BP.Application 78/78 PASS (post-C-1 fix)
- BP.Domain 25/25 PASS
- AuditBehavior 10/10 PASS
- Forms 155/155, Workflow 431/431 (regression)
- Architecture: 96/97 (1 pre-existing `Rule1_GuidNewGuid_DirectCall_Banned` — ARCH-FU, NOT regression)

## Carry-over (filed)
- **P7-FU1.1** (Low): FormTemplate picker `pageSize=100` cap
- **P7-FU1.2** (Medium): State/transition picker numeric fallback — BE expand DTO with display names, or new structure endpoint
- **P7-FU1.3** (Low): ReactFlow workflow visualization placeholder
- **P7-FU1.4** (Low): ETag concurrent-replace integration test
- **P7-FU2.1/2.2/2.3** (Low): Active deactivate surface, modal DRY, defense-in-depth handler
- **ARCH-FU** (Medium): `ApiResponse.cs:14` `Guid.NewGuid()` direct call — needs `IGuidGenerator` refactor of 54 callers OR rule suppression for transient request-id case
- **TEST-FU** (Low): Add `businessprocess` schema to `CrossSchemaSqlScanner.ModuleToSchemas`

## Next session
- BP MVP feature-complete across BE + FE — no MVP-scope blockers remain.
- Highest-value next-task candidates: ARCH-FU (decide refactor vs suppress); P7-FU1.2 (UX — state/transition human-readable display names).

## Unresolved questions
- ARCH-FU resolution: refactor 54 `Guid.NewGuid()` callers to `IGuidGenerator` (~2-3d) vs add allowlist exemption for `ApiResponse.cs` transient request-id (5min)? Both defensible.
- Should P7-FU1.2 bump from Low to Medium? Current UX requires admin to memorize bigint state/transition IDs in form-map designer.
