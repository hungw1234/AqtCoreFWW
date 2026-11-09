# Sóng-0 Phase-2 Scope Audit → Table-Mode Forms Silent Breakage Surfaced

**Date**: 2026-07-08 10:15  
**Severity**: Critical  
**Component**: Forms.Application (SubmitFormCommandHandler), Forms.Infrastructure (FormMaterializationService), Forms.Presentation (TableField DTO)  
**Status**: Resolved

## What Happened

`/aqt-next` recommended Sóng-0 Phase-2 (plan `260706-1237-song0-foundation`) as next scope. Orchestrator performed scope-audit per INVARIANT 260522-A (plan written 07-06 targeting `SubmitFormCommandHandler`, touched 07-07 by `3e1cd660`). Audit revealed: Phase-2's core deliverable (C2 cross-module-write atomicity fix + rollback tests) was already shipped incidentally in Phase-1 slices 1B-1/1B-2. Residual scope: write-path consistency inventory artifact + Table-mode rollback test. User rejected "pivot to Phase-3" and insisted "finish Phase-2 100%." Chose full Table-mode harness option (~1.5h integration rebuild).

Full harness discovered **4 pre-existing production defects** silently breaking Table-mode forms end-to-end since Phase-07 entity retier (7 months):

1. **[BLOCKER B]** `FormMaterializationService` materialization loop stuck `Materializing` forever: dynamic BI views queried `FormSubmissions s ON s.Id = v.SubmissionId` (bigint vs uniqueidentifier), operand-type clash swallowed by service. Join should use `s.PublicId`.
2. **[BLOCKER C]** `FormDataPreprocessor` passed raw `JsonElement` to Dapper scalar submission read → crash. Fix: normalize scalar JsonElement→CLR before Dapper.
3. **[MEDIUM A]** No HTTP PUT/POST path to author TableField child fields; DTO lacked `ParentFieldId`. Forms.Presentation + SubmitFormCommandHandler missing wiring.
4. **[MEDIUM D]** `SubmitPublicFormCommandHandler` never wrote TableField child rows (anon public Table-mode silently dropped them). Parity gap vs authenticated handler.

User chose fix-all (not file-and-defer). Code-review during integration run discovered D mid-test, approved same-session. All 4 verified via fresh Testcontainers + unit assertions.

## The Brutal Truth

Table-mode was **completely non-functional** end-to-end in production (no user ever exercised it successfully; all attempts materialized forever or crashed). This survived since Phase-07 because:

1. **No E2E test drove it.** Unit tests for SubmitFormCommandHandler passed (mocked IFormFieldRepository + dispatcher). Integration tests were missing the materialization + public-anon variant coverage.
2. **"Table mode works locally" was false.** Forms.Integration had 0 tests exercising real FormMaterializationService (background job) + BI dynamic view loop. Seeder created Table-template definitions but nobody tried to actually submit them.
3. **The lighter options would have missed this entirely.** A "skip Table-mode test" or "unit-only Table-mode test" would have shipped Phase-2 showing 100% green. The ONLY reason defect B/C surfaced is because the full harness called FormMaterializationService synchronously (materializationService.MaterializeFormSubmissionAsync(‥)) and forced the BI view join + Dapper scalar path to execute. Happy-path unit mocks hid everything.

This is exactly what thorough testing is for. The user's choice to do the full harness was the difference between "feature shipped broken" and "feature shipped fixed."

## Technical Details

### Defect B: SubmissionId Type Clash
BI dynamic view:
```sql
FROM dbo.FormSubmissions s
INNER JOIN @SubmissionBag v ON s.Id = v.SubmissionId
-- s.Id = bigint (T2), v.SubmissionId = uniqueidentifier (public addr)
-- Operand-type mismatch
```
FormMaterializationService caught exception, logged (level=Information, swallowed), materialization remained `Materializing` → all subsequent submissions + reads stuck.

Fix: `ON s.PublicId = v.SubmissionId`

### Defect C: JsonElement Crash
`FormDataPreprocessor.PreprocessAndMapScalarSubmissionAsync` received raw JsonElement from `IFormFieldRepository.GetScalarFieldDataAsync` (unstructured dict), passed to Dapper handler without normalization. Dapper has no JsonElement handler → TypeHandlerCache miss → runtime crash.

Fix: JsonElement.TryGetProperty(scalar-key).GetString() → CLR scalar before Dapper.

### Defect A: ParentFieldId DTO Gap
`CreateTableFieldCommand` DTO omitted `ParentFieldId`. Forms.Presentation controller had no PUT endpoint to author child fields. Public + authenticated submit paths both lacked wiring. Table-structure authorship blocked entirely.

Fix: Added `ParentFieldId` (Guid?) to DTO; controller PUT+POST routes; validation.

### Defect D: Public Handler Child-Row Silent Drop
`SubmitPublicFormCommandHandler.Handle` looped `submission.TableRows` → created row entities → called `_repository.AddAsync(row)` BUT never called `_repository.SaveAsync()` for child rows (only parent submission). Authenticated variant had the save call. Public anon submits committed only parent row; child rows discarded.

Fix: Parity with authenticated handler (table-row save in same UoW).

### Test Coverage
- Forms.Application.Tests: 320/320 (no red)
- Forms.Integration.Tests: 37/37 (fresh Testcontainers, FormMaterializationService harness + BI view join + public anon path)
- Code-review: APPROVE_WITH_NITS, 0 sibling blast-radius (repo-wide grep for `SubmissionId` join + JsonElement-to-Dapper + public-handler pattern), 3 follow-ups filed same-session.

## Root Cause Analysis

**Why did 4 production defects survive 7 months in Table-mode?**

1. **No integration test drove the real path.** Unit mocks (IFormFieldRepository, IBus) returned happy-path data; nobody called FormMaterializationService (background job, not invoked by unit harness).
2. **Public-anon variant was never tested.** SubmitPublicFormCommandHandler is a separate class; code-duplication (copy-paste from authenticated handler) introduced mid-07 but parity was never verified.
3. **BI dynamic view was untested.** `FormDataPreprocessor` was mocked in unit tests; real SQL + Dapper type-handling never exercised.
4. **Lighter testing strategies don't find this.** A "test the happy path" or "unit-test only the new logic" approach would have shipped green. Only exhaustive E2E (sync FormMaterializationService + real DB + BI view + Dapper scalar + public anon) forced the defects to manifest.

## Solution & Verification

**All 4 defects fixed + verified same-session:**

1. **Defect B:** SQL join corrected in BI dynamic-view query.
2. **Defect C:** JsonElement normalization added before Dapper scalar handler.
3. **Defect A:** DTO + controller routes added (backwards compatible, no breaking change).
4. **Defect D:** UoW save call added to public handler (parity).

**Verification:**
- Fresh Testcontainers run (cold SQL): Forms.Integration 37/37
- Forms.Application unit: 320/320
- Code-review (independent): APPROVE_WITH_NITS
- No sibling occurrences in blast-radius grep (SubmissionId join, JsonElement patterns, public-handler duplication)

**No migration, no DTO versioning (additive field), no FE breaking change, no public-contract break.**

## Lessons

1. **Scope-audit before implementation is non-negotiable (INVARIANT 260522-A).** This caught Phase-1 rework risk; prevented scope-creep. BUT it's insufficient alone — audit says "what to do," not "will it work." The real shield was...

2. **Thorough integration testing finds what unit mocks hide.** The user chose the full harness (1.5h) over lighter options. That choice directly surfaced a completely-broken production feature that unit-level + happy-path testing never would have found. The lesson: when uncertain between "ship now" and "test exhaustively," exhaustive testing is the right bet. It paid off 4× in one session.

3. **Code duplication (copy-paste) creates parity gaps under stress.** Public vs authenticated handlers both handle Table-mode submits; the delta was one missing `SaveAsync()` call. Duplication is a radar for missed test coverage. Future: consider extracting shared command logic into a base or composition, even if it's 10 lines.

4. **Integration tests must exercise ALL code paths.** Table-mode is a feature; if there's a separate public-anon handler, both need harness coverage. (This is implicit in "test the feature," but defect D shows how easy it is to forget a variant.)

## Next Steps

1. Follow-ups filed + closed same-session (3 medium-priority items around related edge-cases in child-row handling).
2. Backlog debt: "Table-mode forms integration test harness" → promoted to recurring gate (check this on every Forms.Application change).
3. Microservice extraction note: Forms module isolated enough for extraction; BI dynamic view is internal-only (zero external consumers found). Phase-5 extraction is unblocked.

---

**Status**: DONE  
**Summary**: Audit found Phase-1 rework risk; user insisted on full Phase-2 completion → full harness test surfaced 4 pre-existing production defects in Table-mode forms (B/C blockers + A/D medium). All fixed + verified same-session. Journal at `C:\AqtCoreFW\docs\journals\journal-260708-1015-song0-phase2-audit-table-defects.md`
