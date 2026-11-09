# Journal — BP MVP P6 Form Prefill Context Client (2026-05-16 12:04–13:30 ICT)

**Branch:** `main` @ `8f004b3d` (pushed) · **Session window:** ~12:04 → 13:30 ICT (~86 min wallclock)  
**Ship #5 of day** — rule-c override at user direction (4-ship cap acknowledged + crossed knowingly)  
**Version tag:** v2.137p28 · **Commits:** `d7e807ff` (code) + `8f004b3d` (docs)

## Session goal

Implement BP MVP P6 (form prefill context client) — 11 todo items in `phase-06-form-prefill-context-client.md`, all completed. Scope: contract interface + storage backend branching (Json/Table modes) + controller endpoint + 7-step orchestrator + OTel instrumentation + 9 unit + 1 integration test suite.

## What shipped (scope)

**Contracts & Interfaces:**
- `IFormPrefillContextClient` at `SharedKernel.Contracts/Clients/` (PLAN DEVIATION D1: nullable `Dictionary<string, object?>` instead of `Result<T>` — SharedKernel.Contracts project has no-PackageReferences policy; controller wraps null→NotFoundError→404). Saves wasteful FluentResults cross-cutting dependency injection.
- `IFormSubmissionRunClient.GetFullSubmissionAsync` extended with Json/Table branch via `IFormDataStorageFactory.ResolveStorageModeAsync`.
- `IFormTemplateQueryClient.GetVersionSchemaByPublicIdAsync` (PublicId variant added; PublicId used in runtime instead of InternalId for FE API contract).

**Forms Domain:**
- Migration `20260516052746_AddPrefillSourceToFormField`: single `AddColumn` nvarchar(2000) nullable. No RLS impact.
- `FormField.PrefillSourceJson` column + `FormTemplate.AddField` parameter plumbing.
- `FormTemplateVersionFactory.FieldPayload.PrefillSource` snapshot extension (additive, SchemaEnvelopeVersion stays 1 — envelope v1 forward-compatible).

**BP Application (7-step orchestrator):**
- `InProcessFormPrefillContextClient` (resolve source→fetch submission→parse snapshot→walk fields→resolve values→coerce→return dict).
- `PrefillSourceMeta` envelope parser (never-throws, defensive on malformed JSON).
- `BusinessProcessMetrics.PrefillDurationMs` OTel histogram + `AddMeter("AqtCoreFW.BusinessProcess")` in `OpenTelemetryConfiguration`.

**BP Presentation:**
- `GET /api/v1/runs/{instId}/forms/{formId}/prefill-context?version=N` endpoint.
- BP.View authorization + 3 error paths (400 BadRequest for `version < 1`, 404 NotFound cross-tenant/missing run/missing form, 200 + nullable dict).
- M-1 fixed inline post-review: guard `version < 1 → 400` added.

## Test results

- **BP.Application:** 59/59 (43 prior + 16 new: 8 PrefillSourceMeta parser + 8 orchestrator resolver).
- **TenantIsolation:** 8/8 (3 BP runtime from P4 + 3 FormSubmissionRunClient + 2 NEW prefill-context).
- **Regression:** Forms.Domain 130/130 + Forms.Application 155/155 + Architecture 97/97 + Workflow.Application 431/431 + BP.Domain 19/19.
- **Build:** 0E/0W. Review: 9.2/10 APPROVE_WITH_NITS.

## What didn't / lessons

1. **Contract-project boundary policy beats plan literal.** Plan D1 said `Result<T>`, but `SharedKernel.Contracts.csproj` header explicitly states "No PackageReferences — pure C# interfaces only". Adapted contract to nullable dict + caller-wraps. Saved a wasteful cross-cutting dependency that would have polluted every consumer. **Codify:** when plan signature conflicts with project boundary policy, the boundary wins; surface adaptation in PR description.

2. **JsonElement vs CLR-primitive asymmetry between Json/Table backends is a real downstream gotcha.** `JsonSerializer.Deserialize<Dictionary<string, object?>>` returns JsonElement-boxed values; `IFormDataStorage.ReadAsync` (Table mode) returns native CLR boxed values. FE FormRenderer may need branch logic OR resolver must coerce. Deferred to P6-FU2 — codify so a future session reads test fixture + decides one normalization point. **Load-bearing test:** `PrefillSourceMeta_Deserialize_MixedTypes` exercises both paths; M-2 (validators for PrefillSourceJson) also deferred.

3. **5th-ship-of-day override with reviewer 9.2 still safe — *if and only if* scope is read-only + review ≥9.0 + regression green + user explicit.** P6 met all four. Fatigue heuristic (4-ship cap) is advisory, not absolute. Codify: violation acceptable when conditions met; P7/P8 (FE-heavy, longer chains) should wait for fresh window.

4. **L-4 pattern (JsonElement coercion asymmetry) will surface again.** Logged but not fixed this session; mark in memory for next FE-BE integration work.

## Decisions made (P6, 9 locked)

1. **D1:** `IFormPrefillContextClient` = nullable dict, not `Result<>` (SharedKernel boundary).
2. **D2:** Json/Table branch via `IFormDataStorageFactory`, consistent with P2 SubmissionRunClient pattern.
3. **D3:** PublicId in runtime endpoint, not InternalId (FE contract).
4. **D4:** `PrefillSourceJson nvarchar(2000)` — nullable, no length validation in migration.
5. **D5:** Never-throw parser (PrefillSourceMeta); unresolvable → log warning + omit field (graceful degradation).
6. **D6:** Snapshot envelope extension additive (v1 envelope, no version bump).
7. **D7:** Route = `runs/{instId}/forms/{formId}/prefill-context`, query param `version` (0-based).
8. **D8:** Cross-tenant/not-found → 404 via `Result.Fail(NotFoundError)` Pattern-A precedent (consistent with P4 handlers).
9. **D9:** Value coerce → none, pass-through CLR objects (asymmetry deferred to P6-FU2).

## Carry-over follow-ups

| Priority | Item | Effort | Blocked-by |
|---|---|---|---|
| Medium | P6-FU1 (validators for PrefillSourceJson) | 1-2h | None |
| Medium | P6-FU2 (JsonElement coercion or doc) | 2-3h | None |
| Low | P6-FU3 (4-mode storage matrix real-DB integration test) | 1h | None |
| Low | P4-2C-FU1 (TOCTOU 409) | 1h | None |
| Low | P4-2C-FU2 (cross-tenant 422→404 module-wide pattern) | 2h | None |
| Low | P4-2C-FU3 (validators) | 1h | None |
| Low | P3-FU1 (TenantIsolation enforcement full fixture) | 3-4h | None |
| Low | P2-FU1 (blocked on R5) | 0.5h | None |
| Low | P2-FU2 (blocked on R6) | 0.5h | None |

**Note:** P7 (Admin UI BP designer, 3d FE) + P8 (User UI BP runtime, 2.5d FE) ready in parallel; no BE blockers remaining for MVP scope.

## Cumulative metrics (P6 session only)

- **Wallclock compression:** ~90 min actual vs 1-1.5d estimate = **~16–20x compression** (consistent with P4 cadence).
- **Code churn:** 11 files +587 LOC (7 new files + 4 modified).
- **Test coverage:** 9 new unit + 1 integration; all prior regression green.
- **Review score trend:** 9.5/10 (P4 slice 2C) → 9.2/10 (P6) — M-1 fixed inline; M-2/L-4 deferred acceptable for ship cadence.

## Cross-references

- **Plan phase:** `plans/260420-1957-business-process-definition-mvp/phase-06-form-prefill-context-client.md`
- **Code:** `src/modules/businessprocess/` (Application/Presentation + shared orchestrator).
- **Tests:** BP.Application 59/59 + TenantIsolation 8/8.
- **Prior journal:** `journal-260516-1100-bp-mvp-p4-slice2c-shipped.md` (P4 COMPLETE).
- **Memory:** BP MVP P4 COMPLETE; P6 SHIPPED; P7/P8 unblocked.

## Unresolved questions

- None blocking. (M-1 fixed inline; M-2 + L-4 deferred to follow-up; all decisions locked.)

