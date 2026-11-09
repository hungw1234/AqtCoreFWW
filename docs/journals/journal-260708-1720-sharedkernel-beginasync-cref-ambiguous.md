# Journal — SHAREDKERNEL-BEGINASYNC-CREF-AMBIGUOUS (build-hygiene)

**Date:** 2026-07-08 (late) · **Type:** build-hygiene / doc-comment · **Row:** SHAREDKERNEL-BEGINASYNC-CREF-AMBIGUOUS (T4)

## What
Four XML-doc `<see cref="BeginAsync"/>` references in SharedKernel were CS0419-ambiguous ever since `ICrossModuleUnitOfWork.BeginAsync` gained a second `(Guid, CancellationToken)` overload (the public-form tenant-override path, Sóng-0 Phase-1). A normal Debug build only warned, but a fresh `dotnet build -warnaserror` of SharedKernel failed on them — first surfaced 2026-07-08 while isolate-building SharedKernel during the FORMS-CONSENT-PRE-SPAN-ORPHAN cook.

## Fix
Qualified each cref to the canonical `BeginAsync(CancellationToken)` overload:
- `ICrossModuleUnitOfWork.cs` (3×: lifecycle summary, ADDITIVE note, `IsActive` doc) → `BeginAsync(CancellationToken)`, mirroring the pre-existing correct usage at `:47`.
- `IOutboxWriter.cs` (1×, cross-type exception doc) → `ICrossModuleUnitOfWork.BeginAsync(System.Threading.CancellationToken)` (fully qualified for the cross-type reference).

Doc-comment only — no code, signature, behavior, or public-contract change (XML doc comments don't enter compiled metadata).

## Verify
SharedKernel builds **0 warnings / 0 errors** under BOTH a normal build and `-warnaserror` — the compiler is the authoritative oracle for cref resolution, so 0 warnings proves every cref now resolves to a real, unambiguous member. code-review APPROVE (repo-wide grep: all 5 `BeginAsync` crefs now carry a param list; zero unqualified stragglers).

## Note
Closes the FORMS-CONSENT-PRE-SPAN-ORPHAN follow-up pair (the other, AUTH-SUBMIT-NO-CONSENTGIVEN, shipped earlier the same day as `4839c102`). No tester run — a doc-only change has no runtime behavior; the `-warnaserror` build is the verification.
