# Journal — 2026-06-19 21:18 — PAdES signed-PDF download endpoint: MVC integration coverage

**Slice:** `SIGN-P5-PADES-SIGNED-PDF-DOWNLOAD-INTEGRATION` (T2) · `/aqt-next` → `/cook` · commit `62f34b5a` → [PR #119](https://github.com/thiennd212/AqtCoreFW/pull/119) (→ main).

## Context

`/aqt-next` surfaced this as the smallest contained follow-up to the Phase 5 PAdES arc (16 ships that day, 21:18 — fatigue heuristic argued for a small slice over flagship scope). The endpoint `GET /api/v1/signing/request/{publicId}/signed-pdf` had 4 orchestrator unit tests but zero HTTP-boundary coverage.

## What shipped

`PadesSignedPdfDownloadHttpTests` (Identity.Integration.Tests, real-SQL Testcontainers) — 4 tests through the full MVC pipeline (auth + `[RequiresMfa]` + tenant-from-JWT):

1. Completed remote PAdES → `200 application/pdf`, body starts `%PDF-`
2. Cross-tenant publicId → `404`
3. Initiated PendingTransaction → `400 SIGNING_PADES_PDF_NOT_READY`
4. DocFlow-linked request → `400 SIGNING_PADES_NO_SIGNED_PDF`

Test-only; 0 production change; public contracts untouched. Build 0E, 4/4 green (~34s).

## Decisions / lessons

- **Audit-at-slice-start (INVARIANT 260522-A) paid off cheap:** empirical probe (endpoint exists, exactly 4 unit tests, no integration test touching the route) confirmed real-ready in minutes before opening scope.
- **Split the row's "3 cases" into 4** so each typed 400 is pinned by its error **code** in the body, not just the status. A regression collapsing both error paths into one generic 400 would otherwise pass green — the recurring false-green class from this signing arc (pin the reason, not the status).
- **Stub the port, not the transitive dependency:** the orchestrator reads via `ISigningPlaceholderPdfReader` (Identity.Application, directly referenced). Stubbed that instead of `IFileStorageService` (only visible transitively through Files.Domain). Trade-off: skips the 6-line host adapter (a stream→bytes copy, not the SUT) to keep the test inside this project's reference set — cleaner dependency boundary.
- **Fixture gotcha:** `WebAppFixture`'s default storage stub returns an empty stream on any key, which would silently defeat the `%PDF-` assertion. The happy-path must override the read boundary to return real `%PDF-` bytes.

## Follow-ups

None new. Sibling row `SIGN-P5-PADES-COMPLETION-INTEGRATION` (T2, concurrent-sweep persistence invariants) remains open as the next verify-hardening slice.
