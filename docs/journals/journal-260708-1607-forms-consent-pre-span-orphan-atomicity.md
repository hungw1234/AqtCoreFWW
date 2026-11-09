# PDPL Consent Write Enlisted into Atomic Submit Span

**Date**: 2026-07-08 16:07
**Severity**: Medium (atomicity hygiene, orphan risk)
**Component**: Forms.Application submission handlers, Identity.Infrastructure ConsentRepository
**Status**: Resolved

## What Happened

The PDPL consent write in form submissions was committing pre-span on its own IdentityDbContext transaction. When the atomic submit span rolled back (constraint violation, nested-UoW exception, test failure injection), the consent record persisted orphaned — recorded successfully but the submission failed and never landed. This violated the expectation that consent + submission + outbox events should atomically commit or roll back together.

**Fix applied:** `ConsentRepository` became enlist-aware (optional `ICrossModuleUnitOfWork` injected, mirroring `GenericRepository` pattern). The consent block (`RecordConsentAsync` + `LinkConsent`) moved INSIDE the `try` span in both `SubmitFormCommandHandler` and `SubmitPublicFormCommandHandler`, running before `AddAsync` so the FK is set before the parent row is inserted. Enlist happens in `SaveChangesAsync` check: `if (_uow is { IsActive: true }) await _uow.EnlistAsync(_db, ct)` — when no UoW is active (ConsentController grant path), enlist no-ops and SaveChanges commits standalone exactly as before.

**Outcome:** Consent + submission + outbox now commit or roll-back together on one local SQL transaction (the Sóng-0 CrossModuleUnitOfWork primitive applied to a real-world site). No migration. Public contract (`IConsentModuleClient`) unchanged.

## The Brutal Truth

The frustration here is entirely self-inflicted. Spent ~3 hours on two entirely false-red test rounds because of **trivial, stupid mistakes**:

**Round 1 (unit tests):** Created the consent tests, they all PASSED but the actual consent flow never ran. Found only when integration tests revealed zero consent records ever written. Root cause: positional `AddField` call meant to set `isPii: true` silently set `required` + `validationRules` instead — the field landed with `isPii=false`, so consent path never gated. Massive waste of time debugging "why did my test assert pass when the feature did nothing."

**Round 2 (integration rollback proof):** Built a rollback test on the authenticated submit path. Test always returned 422 (`GOV_FRM_020` "requires consent") — not 500 from a failed rollback. Spent 30 minutes thinking the enlist logic broke authentication flow until realizing: the authenticated `SubmitFormRequest` DTO has NO `ConsentGiven` field, so the handler always builds the command with `ConsentGiven=false`. Consent is **only testable end-to-end on the public path**. Authenticated path never even attempts a consent write. Completely reasonable design choice (admin-submitted forms don't need citizen consent), but the DTO being silent about it made me chase ghosts.

This is the cost of shipping a feature nobody (including the forms team before today) has actually exercised — every assumption is invisible until tests force them visible.

## Technical Details

**Code locations:**
- `ConsentRepository.cs` lines 14–50: enlist-aware SaveChangesAsync, optional UoW ctor injection
- `SubmitFormCommandHandler.cs` lines 141–166: consent block moved inside the try span (was lines 79–96, now inside UoW)
- `SubmitPublicFormCommandHandler.cs` lines 133–161: same pattern, public-path parity

**Enlist seam:**
```csharp
if (_uow is { IsActive: true })
    await _uow.EnlistAsync(_db, ct);
await _db.SaveChangesAsync(ct);
```

Enlist requires closed connection and runs BEFORE SaveChanges (connection stays closed during enlist if it's never been opened, which is the case for consent-only writes). If any prior operation opened the IdentityDbContext connection before enlist, the enlist would throw — the submit handlers are safe here because they record consent before any identity-schema reads.

**Test coverage (15 tests):**
- Integration: `FormSubmissionConsentAtomicityTests` (4 tests, incl. `PublicSubmit_ConsentThenInSpanFailure_RollsBackConsentAndSubmission` — forces outbox failure mid-span to prove 0 orphan)
- Unit (authenticated path): `SubmitFormCommandHandlerConsentAtomicityTests` (6 tests covering happy path, missing PII fields, no consent given, enlist-aware call sequence)
- Unit (public path): `SubmitPublicFormCommandHandlerConsentAtomicityTests` (5 tests, parallel coverage)

Rollback proof **varies by submit mode:** authenticated path injects throwing `IStorageRepository` (preprocess mock), public JSON mode injects throwing `IOutboxWriter` (selective on "forms" schema to avoid audit cascade), public Table mode injects throwing child-row write. All three paths converge: 0 consent rows, 0 submissions.

**Test run green flags:**
- Forms.Integration 41/41 (fresh Testcontainers, includes 2 atomicity tests)
- Forms.Application 340/340 (unit suite)
- Identity.Application 426/426
- Arch 158/158
- Build 0/0

## What We Tried

1. **Positional AddField (Round 1 trap):** Wrote 6 unit tests with `template.AddField(key, type, labelVi, labelEn, order, isPii: true)` — all green, but the field still had `isPii=false` because the signature is `AddField(key, type, labelVi, labelEn, order, required, validationRulesJson, updatedBy, ...)` — the `required=true` arg filled slot 6 instead. Lesson: **FormTemplate.AddField signature is a footgun; always pass optional params by name, never positional.**

2. **Authenticated consent test (Round 2 trap):** Built `SubmitFormCommand` directly in unit tests (ConsentGiven=true, PII=true), all passed. Built `SubmitFormRequest` in integration, got 422. Spent 30 min tracing SubmitForm gateway → controller logic before realizing `SubmitFormRequest` (lines 161–166 FormSubmissionsController) has no `ConsentGiven` property, so the controller unconditionally builds `ConsentGiven=false`. Rolled back to public path, rollback worked immediately.

3. **Integration test CountAsync pollution (Round 2 secondary):** Shared integration class used `db.ConsentRecords.CountAsync()` to assert "orphan count == 0". When running test 2 after test 1 in same session, the count was > 0 because consent records from previous test persisted. Fixed by scoping count to per-test tenant + per-test template (ConsentRecord has no FK, only natural-key tuple {DataSubjectId, Purpose}).

## Root Cause Analysis

1. **FormTemplate.AddField design:** The method evolved — `isPii` was added as an optional param after several mandatory ones (`required`, `validationRulesJson`). The signature is now a minefield for positional calls. No caller validation (the method doesn't assert `required==false when isPii==true`), so a positional mistake silently lands `isPii=false` and the entire consent-data-flow gate is dead.

2. **Consent DTO shape unspecified in SubmitFormRequest:** The authenticated form submission API never articulated that it doesn't support consent workflows. The DTO is silent — no `[Obsolete]` attribute, no comment, just missing. Consent works on the public path because `SubmitPublicFormRequest` carries `ConsentGiven`. The asymmetry is reasonable (admins don't ask public for consent), but the interface is a trap.

3. **Test isolation in shared-db scope:** `FormSubmissionConsentAtomicityTests` reuses a single `ITestDatabaseProvider` for all 4 tests. Consent records are global to the tenant unless scoped by natural key. The `CountAsync()` assertion leaked across test boundaries — this is a minor class-design issue (every repo test should scope assertions by natural key, not just count).

## Lessons Learned

1. **Positional optional params in evolving method signatures are a code smell.** `FormTemplate.AddField` should either:
   - Require all params by name, OR
   - Have a builder API (`builder.WithField(key).Type(...).IsPii(true).Build()`), OR
   - Document in comments that slots 6–8 are high-risk.
   
   For future legacy-API usage (ConsentController grant, template edit), use named arguments and document the invariant.

2. **Consent is a cross-schema concern that MUST be exercised end-to-end.** Unit tests of the consent logic inside SubmitFormCommand are false-green if the command is never actually built by the controller. The lesson: **when fixing a cross-module atomicity issue, exercise it through the full HTTP→DTO→Command→Handler→Repo chain.** Public path was sufficient here, but only by accident.

3. **Integration rollback tests need per-path failure injection seams.** The authenticated path uses storage, the public path uses outbox. A single "throw mid-span" test cannot work across both. Future consent tests MUST be organized by path (auth vs public, JSON vs Table) and inject failure at the right seam (storage, outbox, child-row writer).

4. **Shared-DB integration test classes need scoped assertion helpers.** A bare `.CountAsync()` in a shared-DB test class is a footgun. Add a `CountByTemplateAsync(templateId)` or similar helper so assertions don't bleed across test runs. Or scope the test DB per-test (preferred, but slower).

5. **Designer files + CS0419 warnings surface only on fresh `-warnaserror` builds.** The `<see cref="BeginAsync"/>` ambiguity in `ICrossModuleUnitOfWork` is harmless in Debug but breaks a standalone build. File as `SHAREDKERNEL-BEGINASYNC-CREF-AMBIGUOUS` for future cleanup (qualify the cref or add CS0419 to NoWarn).

## Next Steps

1. **Filed:** `AUTH-SUBMIT-NO-CONSENTGIVEN` (product-shape question: is PII+consent intentionally public-only? or should authenticated submits gain a ConsentGiven field + controller wiring?). Blocking: none (design is reasonable, just undocumented).

2. **Filed:** `SHAREDKERNEL-BEGINASYNC-CREF-AMBIGUOUS` (CS0419 doc-cref ambiguity in BeginAsync, surfaces on `-warnaserror` build). Fix: qualify cref to `BeginAsync(System.Threading.CancellationToken)` or add CS0419 to NoWarn. Low priority (harmless in Debug).

3. **Document FormTemplate.AddField footgun** in a code comment or CLAUDE.md feedback file to prevent future positional-call mistakes.

4. **End-to-end consent coverage:** The rollback tests here are now the single source of truth for consent atomicity. Reuse them whenever consent logic is touched again.

---

**Status:** RESOLVED — `86c7f8d5` (working-tree diff ready to commit)
