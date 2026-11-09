# BP-Run Digital-Signature Debug Chain: 4 Contract-Drift Bugs + 3 Seam-Test Follow-ups

**Date**: 2026-06-13 19:45
**Severity**: High (Phase A: production contract drift on live endpoint; Phase B: test-coverage gaps)
**Component**: API/Forms/BusinessProcess — CORS, BP-run submissions card, file upload, digital-signature evidence projection
**Status**: Resolved (6 commits pushed, Phase B all merged)

## What Happened

Two back-to-back debug phases discovered and resolved **the same root-cause class**: contract drift between frontend and backend, undetected by server-side tests because the test layer structurally cannot catch these gaps (CORS preflight skipped by TestServer, Dapper mocks don't reflect real payload shapes, read-only card never exercised with signed/attached data).

### Phase A: `/ck-debug` + Manual E2E Exercise

Manually exercised the full BP-run digital-signature flow (start → form fill → upload → sign → submit). Four sequential bugs surfaced:

1. **CORS preflight block** — "Bắt đầu quy trình" POST failed in browser: `Access-Control-Allow-Headers` omitted `X-Request-Token` (anti-double-submit header added in earlier commit, read by `AntiDoubleSubmitMiddleware`, but never added to CORS allow-list). TestServer/WebApplicationFactory bypass CORS preflight entirely → green server-side tests masked the real failure. Fixed: added header to `InfrastructureRegistration.cs` allow-list. Commit `1655137b`.

2. **Read-only render type-blind** — BP-run detail card displayed a drawn signature as raw base64 text and showed file fields uselessly. Root: `GetBpRunSubmissionsQueryHandler` projection dropped the field `Type` entirely; FE `bp-run-submissions-card.tsx` formatValue branched only on JS runtime type (not form-schema type). Fixed: surface `Type` through projection + DTO cascade (`BpRunSubmissionFieldDto`, `FormSubmissionSummaryDto`); FE dispatches on `Type` — Signature → `<Image>`, File → reuse Forms `AttachmentPreview` (DRY), else text. Commit `4a3db628`.

3. **Digital-signature field shows blank** — DigitalSignature field rendered empty despite evidence in database (verified via docker-sqlserver-1: `SignatureVerdict=1`, signer `CN=ĐINH QUỐC VIỆT`, verified timestamp). Root: DigitalSignature stores NO per-field value; proof lives in FormSubmission evidence columns not projected to the card. Separately confirmed "Tệp đính kèm —" was correct (File field genuinely null — no upload). Fixed (user chose A+B): extend `FormSubmissionSummaryDto` + `GetLatestForRunAsync` SELECT with evidence columns; map to `BpRunSubmissionDto` (`IsSigned`, `SignerName`, `SignatureVerifiedAt`); `ExtractCommonName` sends CN only (full DN + national-id stay server-side — data minimization per security review); FE shows "Đã ký số" badge in form header AND field cell. Part of `4a3db628`.

4. **File upload widget shows nothing** — uploaded file left widget empty and form value undefined (submit wouldn't link file). Root: `/files` upload endpoint switched to async two-phase (202 + `{fileId, statusUrl, message}` for background ClamAV scan) but FE `useUploadFile`/`FileFieldWidget` still expected old sync response shape `{Id, OriginalFileName}` → read undefined. Fixed: read `fileId` from async response, use locally-known filename. Commit `ccc3ba53`. (file-list-page also uses useUploadFile but ignores the result — no regression.)

**Phase A verification**: build 0W/0E (`-p:TreatWarningsAsErrors=true`), BP.Application.Tests 109/109 (incl new type + evidence tests). FE: tsc clean, vitest 1035 (all green). All commits pushed origin+bitbucket.

### Phase B: `/ck:cook` Follow-ups — Test Seam Closure

User opted into all 3 follow-ups + chose polling over SignalR for virus-scan polling.

1. **CORS regression-lock** — Extracted `AddGovApiCors(IServiceCollection, IConfiguration)` from inline `AddInfrastructure` (was untestable without Redis/DB) + `GovApiCorsPolicyTests` asserting BUILT policy's headers against independently hard-coded expected set (NOT production array — avoids tautological test). Locks the header-drift class so dropped header fails build. 4/4 green. Commit `6f124741`.

2. **Poll virus-scan status (FE)** — `useFileScanStatus` (react-query `refetchInterval`) polls `GET /files/{id}/metadata` after upload until terminal `FileStatus` (Available/Rejected) or attempt cap; Rejected files drop from form. Decision: polling over SignalR (no existing file-scan SignalR wiring; KISS). Code-reviewer caught real H1: cap used `dataUpdateCount` (only advances on success) → persistent `/metadata` error would poll forever. Fixed by also counting `fetchFailureCount`. tsc + vitest 85/85. Commit `d32db808`.

3. **Integration test seam** — Testcontainers test seeds signed FormSubmission (+ template + version snapshot) and reads back through REAL HTTP submissions endpoint (tenant session-context/RLS behave as production), asserting `isSigned`/`signerName` (CN-only)/`verifiedAt`. Locks Dapper SELECT → DTO evidence mapping that mock-client unit tests cannot catch. 1/1 green. Commit `8d2bcefd`. Pushed origin+bitbucket.

## The Brutal Truth

This session is a master class in **test layer blindness**. Four separate bugs (CORS, type dispatch, evidence projection, async contract) all made it through server-side test suites green because the tests structurally couldn't exercise the failing paths:

- TestServer does not run CORS preflight logic.
- Mock Dapper clients return hard-coded shapes, not real `SELECT` results.
- The card component never ran with actual signed + attached data in any existing test (only mocked stub forms with zero fields).

The real eye-opener: **all four bugs were "—" (blank/missing) display failures**. Three of them were projection gaps (Type, evidence columns, file-id mapping) — layers above the signing logic that shipped green because the signing test suite only cares about crypto, not FE rendering.

Worse: the bugs are not rare edge cases. They're the happy-path flow: user fills form, uploads file, signs, submits. The fact that nobody caught them manually until today suggests either the signing work was demo'd on sandbox data without actual file+signature, or the demo was read-only (no form fill end-to-end).

The polling cap fix (H1) was particularly embarrassing: a persistent network error would cause an infinite loop. The fact that it slipped through code review on the initial FE-only branch suggests the reviewer didn't model failure scenarios — only happy-path.

## Technical Details

**4 Bug Commits (Phase A):**
- `1655137b`: Allow `X-Request-Token` in CORS policy
- `4a3db628`: Render fields by type + surface evidence; ExtractCommonName (CN-only signer display)
- `ccc3ba53`: Align widget with async `/files` 202 contract
- (Reused `4a3db628` for evidence projection scope)

**3 Follow-up Commits (Phase B):**
- `6f124741`: Extract + lock GovApiCors policy via unit test
- `d32db808`: Add `fetchFailureCount` to polling cap; useFileScanStatus
- `8d2bcefd`: Testcontainers integration test, FormSubmission evidence → BpRunSubmissionDto

**Test Coverage Added:**
- `GovApiCorsPolicyTests` 4/4 (independently hard-coded expected headers)
- `useFileScanStatus` test 6/6 (Rejected file drop, polling cap with error)
- Testcontainers integration 1/1 (signed submission dapper→DTO→HTTP)

**DTO Changes (no migration):**
- `BpRunSubmissionFieldDto.Type` (was dropped; restored)
- `BpRunSubmissionDto` + `IsSigned`, `SignerName`, `SignatureVerifiedAt` (evidence summary)
- `FormSubmissionSummaryDto` evidence columns (SELECT scope extended)

**FE Changes:**
- `bp-run-submissions-card.tsx`: dispatch on `field.type` + Signature → Image, File → AttachmentPreview
- `useUploadFile`: read `fileId` from `{fileId, statusUrl, message}`
- `useFileScanStatus`: refetch until terminal + error count cap

## What We Tried

1. **CORS**: Verified via browser DevTools Network tab (preflight headers). Fixed allow-list. Locked via unit test (not prod hardcode).

2. **Read-only render**: Traced from "raw base64 text" through fe component → projection → query. Added Type column, dispatched on type. Verified with actual signed data via SQL (docker-sqlserver-1).

3. **Evidence projection**: Confirmed via SELECT (AqtCoreFW DB, SignatureVerdict=1, CN=ĐINH QUỐC VIỆT visible in column). Extended DTO cascade + Testcontainers lock.

4. **File upload**: Checked `/files` endpoint contract (202 async); updated widget to read `fileId`. Verified polling cap with error count.

## Root Cause Analysis

**Class: Contract Drift** — A change on one side (header added, endpoint goes async, evidence columns added) is not synced to the other side. The TEST layer cannot catch these because:

- **CORS**: TestServer doesn't run middleware preflight checks.
- **Type dispatch**: Unit tests mock Dapper, which returns hard-coded shapes not real schema.
- **Evidence projection**: Card test never fed actual signed/attached FormSubmission (only stubs with empty fields).
- **Async contract**: Widget test stubbed the response shape; no test exercised real `/files` 202 response.

**Why today?** Manual E2E is the only layer that runs the *real* HTTP requests (CORS included), the *real* Dapper queries (evidence columns), and the *real* form-filling sequence (sign → submit → read card).

## Lessons Learned

1. **"—" is ambiguous.** Always verify empirically (SQL, DevTools, manual step) whether a blank display is correct-empty or a real gap. Here: File was correctly null, DigitalSignature was a real projection gap, CORS was a real failure.

2. **Contract drift class requires seam tests.** Add integration tests at the exact points where contract shapes change: CORS policy unit lock, Dapper SELECT → DTO lock, async endpoint response lock. Don't rely on component-unit tests alone.

3. **Preflight bypass in TestServer is a silent threat.** Any change to allowed headers or origins will silently pass tests and fail in browser. Lock via `GovApiCorsPolicyTests` or similar.

4. **FE polling caps must count errors, not just successes.** A persistent network error (503, timeout) will cause infinite retry. Test both success AND error paths.

5. **Data minimization: surface only CN, keep full DN+national-id server-side.** User can identify signer by name in UI; backend audit has full legal signature. Reduces data exposure on the wire.

6. **"—" in card is not always wrong.** Verify the data exists in the source (SQL for evidence, form schema for Type) before "fixing" a missing render.

## Next Steps

- Phase B tests are all green and merged. No unresolved blockers.
- Task 04f (dependency-map docs + smoke checklist) remains pending — user can defer or expand scope.
- BP-run signing is feature-complete and locked against contract drift via 3 new seam tests.
- Document the GovApiCors pattern as a template for future CORS regressions (reusable on other policy changes).

---

**Unresolved questions:** None. All bugs empirically verified and fixed.
