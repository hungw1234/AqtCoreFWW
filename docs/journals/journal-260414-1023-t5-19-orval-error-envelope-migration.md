# T5-19 Orval Error Envelope Migration — Structural Root-Cause Fix

**Date**: 2026-04-14 10:23
**Severity**: Medium
**Component**: web — `api-error.ts`, `query-config.ts`, generated Orval schemas, `shared/types/api.ts`
**Status**: Resolved (runtime OpenAPI snapshot test deferred — BE offline)

## What Happened

Closed the structural FE↔BE `ApiError` contract drift that caused T4-04 bugs. FE had
hand-written `BackendApiError { field?: string }` — wrong field name, wrong shape,
never connected to the Orval-generated type from `aqtCoreFWApiV1.schemas.ts`. The
generated `ApiError` (with `property`) existed the whole time; nobody imported it.
Zero compile-time protection meant a BE rename would silently produce `undefined` in
all error notifications at runtime.

Discovery bonus during planning: ASP.NET Core 9 built-in OpenAPI already reflects
`ApiError` correctly — Swashbuckle attribution work scoped into plan was unnecessary.
Effort shrank from 1-1.5d → ~0.75d before a line was written.

## The Brutal Truth

We had a generated type sitting in the codebase, unused, while someone hand-wrote a
parallel type that was structurally wrong (`field` vs `property`). This is the
textbook "auto-generated code exists but nobody reads it" failure. The T4-04 bugs
that surfaced were symptoms; this was the disease. Satisfying to finally close the
root cause instead of whack-a-mole patching normalisation logic on top of broken
types.

The BE offline blocker is wearing thin. This is the third session in a row
(T4-04 Playwright, T4-04-B admin forms, now T5-19 OpenAPI snapshot) where the
runtime validation gate is blocked because BE is not reachable at :5001/:5002. The
compile-time guard is real protection, but we keep accumulating deferred runtime
gates. That debt will bite us the first time the compile-time guard misses a
structural change that doesn't affect TypeScript field names.

## Technical Details

- **Commit**: `ae448ac6` — `refactor(web): replace hand-written BackendApiError with generated ApiError type`
- **Files changed**: 6, +65/−54, pushed fast-forward `31dc2765..ae448ac6` → `origin/main`
- **Key file**: `web/src/core/api/api-error.ts`
  - Deleted: `BackendApiError { code, message, field?, detailVi? }` (wrong shape)
  - Deleted: named alias `BackendEnvelope<T>` (YAGNI — user Q2 choice: inline cast)
  - Added: `import { ApiError as GeneratedApiError } from '@/api/generated/aqtCoreFWApiV1.schemas'`
  - Renamed export: `ApiError` → `NormalizedApiError` (collision resolution — user Q1 choice)
  - Inline envelope cast: `(body as { errors?: GeneratedApiError[] | null })`
- **Contract guard**: new `web/src/test/api-error-contract.test.ts`
  - `extends keyof` type assertion — guards `code`, `message`, `detailVi`, `property`
  - BE rename/removal now breaks `tsc`, not silent runtime `undefined`
- **Bonus DRY cleanup**: `web/src/shared/types/api.ts`
  - Deleted dead `ApiError { field?: string }` + `ApiResponse<T>` — zero importers (grep confirmed)
  - Plan listed 3 files; 4th added on discovery. No scope creep — pure removal.
- **Backlog**: bumped to v2.86, T5-19 marked COMPLETE
- **Tests**: 11/11 green (10 normalization preserved + 1 new contract guard), `tsc` clean

## What We Tried

- Attempted runtime OpenAPI snapshot test (Phase-04 Step 1) — ECONNREFUSED, BE offline
- Compile-time `extends keyof` guard adopted as first-line defence in absence of runtime gate
- TypeScript typecheck as substitute quality gate — clean pass accepted

## Root Cause Analysis

The drift had two compounding causes:

1. **Orval adoption was incomplete**: Generated types were wired for request/response
   bodies but never adopted for the error envelope. Manual `BackendApiError` was
   written during initial FE scaffolding and never revisited when Orval was added.

2. **No enforcement mechanism**: Nothing prevented the hand-written type from drifting.
   No compile-time assertion, no schema snapshot test, no contract test. Silent runtime
   failure was the only feedback loop — and only when an actual error occurred in
   a test environment.

## Lessons Learned

- When Orval generates types, grep for any hand-written parallel types immediately.
  If the shape exists in generated code, the manual version is tech debt from day one.
- `extends keyof GeneratedType` assertions cost ~10 lines and catch field renames at
  build time. Write one per generated type that crosses the BE→FE boundary. Do it
  when the type is first imported, not after a bug report.
- Naming collisions between generated and FE types are a signal the domain model is
  unclear. `NormalizedApiError` vs `ApiError` is the correct split: generated = raw
  BE shape, normalized = FE consumption shape. Establish this convention project-wide.
- Inline envelope casts beat named aliases when the alias has exactly one use site.
  `BackendEnvelope<T>` existed for one cast in one function. YAGNI applies here.

## Next Steps

| Item | Owner | When |
|---|---|---|
| Runtime OpenAPI snapshot test (Phase-04 Step 1) | tester | When BE port 5001/5002 online |
| Evaluate CI integration: fail build on OpenAPI schema diff | arch/lead | Next BE-online session |
| Decide `traceId` surfacing in error notifications | lead/UX | Before next error UX pass |

## Open Questions

1. **BE offline recurring blocker**: Three sessions of deferred runtime gates. When
   is BE reliably reachable for CI/E2E runs? Should we wire OpenAPI snapshot diff into
   the build pipeline so drift fails loud without needing a live BE at test time?

2. **`traceId` field**: `NormalizedApiError` now carries `traceId` (was silently
   dropped by old `BackendApiError`). Should it surface in error notification UI for
   debugging, or stay log-only? Surfacing aids support — but clutters UX for end users.
   Needs a product call before implementing.
