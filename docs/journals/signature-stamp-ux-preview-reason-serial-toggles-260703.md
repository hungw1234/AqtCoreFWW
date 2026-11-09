# PAdES Visible Stamp Defects + Image Preview + Profile Toggles

**Date**: 2026-07-03 (session)
**Severity**: Medium
**Component**: Identity.Signing (PAdES visible stamp, signature profiles, FE form/preview)
**Status**: Resolved
**Branch**: feat/signature-stamp-ux
**Commits**: `353bc3ac` + `adc78d0b` (pushed to origin + bitbucket)

## What Happened

Fixed seven interrelated PAdES stamp defects and UX gaps surfaced by user screenshots + smoke-test regressions:

1. **Profile page hero caused duplication**: redesigned `/profile` with identity hero + tabs; duplicate account card broke smoke tests (getByText found name/email twice).
2. **Modal save button said "Luu mat khau"**: reused password i18n key `profile.save` for a generic signature-profile modal.
3. **Signature image never displayed**: image-upload modal showed only a "Da co" badge, no actual preview image.
4. **Stamp signer name wrong**: showed profile label instead of cert subject.
5. **Stamp timestamp off by 7 hours**: formatted UTC while reader shows CMS signingTime in local +07.
6. **"Ly do" field contained filename**: no real user-input Reason, just the Mysign DocumentName.
7. **Serial and issuer not toggleable**: always shown; users wanted optional display per profile.

## The Brutal Truth

This session was **tedious but manageable**—a dozen small paper cuts from a lack of end-to-end verification. None were critical, but together they exposed a pattern: **we assume the happy path exists and only find the gaps when the user complains**.

The profile redesign broke smoke tests in a way that only surfaced in the **FULL** FE suite run; targeted component tests stayed green. That's maddening because we do run tests, but not always at the scope the refactor touched. We lost 20 minutes to an EF migration tooling mismatch (dotnet-ef CLI version vs runtime)—a config issue, not a logic issue, but it blocked momentum.

The image preview defect stung the most: we had the store's GetAsync, we had the UI, we just never wired the HTTP endpoint. **No one validated end-to-end**. The Reason field wasn't a bug—it was scope that was silently missing. These are process failures, not engineer failures.

## Technical Details

### Fix 1: Profile Page Hero → Removed Duplicate Account Card

- **Problem**: Identity hero + account detail card both rendered name/email; smoke test `getByText("Thien")` matched twice.
- **Solution**: Remove the account-card duplicate; identity shown once in hero section.
- **Lesson**: A page redesign that repeats the same data in multiple places is both a UX anti-pattern AND a test-brittleness trap.

### Fix 2: Modal Button i18n Key

- **Problem**: `<ModalButton>Luu mat khau</ModalButton>` on add-signature-profile modal.
- **Root**: Reused `profile.save` ("Luu mat khau" = Save password).
- **Solution**: Changed to `common.save` (generic "Luu").
- **Lesson**: Domain i18n keys like `profile.save` are domain-specific; modals need `common.*` keys.

### Fix 3: Signature Image Preview Read-Path Missing

- **Problem**: Image-upload modal showed badge "Da co" (Has image) but never rendered the image.
- **Root**: DTO carried only `HasFullImage` / `HasInitialImage` booleans; no HTTP read endpoint.
- **Solution**: Added `GET /me/signature-profiles/{publicId}/images/{visualType}` controller action + FE blob-fetch → objectURL.
- **Technical**: VgcaApiClient streams the image bytes; FE wraps in URL.createObjectURL for lazy load.
- **Lesson**: Source-verify the read-path exists end-to-end (DTO + controller + route) before debugging the render. We had the store but zero HTTP wiring.

### Fix 4: Stamp Signer Name

- **Problem**: Stamp showed profile label ("Chu ky tay") instead of the actual signer's certificate name.
- **Root**: Composer hardcoded profile.Label; cert subject parsing was never invoked.
- **Solution**: Call `VgcaCertSubjectParser.Classify(bcCert)` (same parser enrollment uses) to extract signer name from X.509 subject.
- **Added tests**: VgcaCertSubjectParser had ZERO coverage despite enrollment depending on it → added 8 test cases (CN extraction, fallback, edge cases).

### Fix 5: Stamp Timestamp Off by 7 Hours

- **Problem**: Visible stamp showed `2026-07-03 07:12:34 UTC` while CMS signingTime showed `2026-07-03 14:12:34 +07:00`.
- **Root**: Stamp formatter used `DateTimeOffset.UtcNow` (UTC); reader interprets CMS signingTime in local +07.
- **Solution**: Changed to `DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(7))` (VN is UTC+07 year-round, no DST).
- **Lesson**: Visible stamp and CMS signingTime are two separate timestamps in two separate contexts (display tz vs PDF reader tz). Align the display tz to match the reader's expectation.

### Fix 6: "Ly do" Field Added

- **Problem**: Stamp "Ly do" (Reason) field showed the Mysign DocumentName (filename), not a user-input reason.
- **Root**: No real reason field; Mysign integration forced the document name into that slot.
- **Solution**: Added `PadesSignParameters.Reason` (string, nullable), distinct from DocumentName; composer skips if empty.
- **Threaded through**: CreateSignatureProfileCommand, UpdateSignatureProfileCommand, SignatureProfileDto, controller update, FE form (text input), i18n.

### Fix 7: Stamp Serial and Issuer Made Optional

- **Problem**: Serial number and issuer name always shown on stamp; users want to hide them per profile.
- **Solution**: Added `UserSignatureProfile.ShowSerial` and `ShowIssuer` (bool, default true for backward compat).
- **EF Migration**: 2 nullable-false bit columns with default value 1 (true).
- **Threaded through**: Domain entity, DTO Create/UpdateSignatureProfileCommand, controller, FE form (Switch components), composer skips fields if toggled off, i18n keys added.
- **Backward compat**: Existing rows default to true, no silent behavior change.

### Time Sink: dotnet-ef CLI Version Mismatch

- **Problem**: EF migration launch failed; dotnet-ef 10.0.5 in `.config/dotnet-tools.json` with rollForward:false wouldn't run on runtime 10.0.8.
- **Root**: Tool version pinning too strict; runtime patch bumps on the build machine but tool manifest didn't allow roll-forward.
- **First attempt**: Created a global fallback dotnet-ef 10.0.8 (workaround, not a fix).
- **Real fix**: Repinned `.config/dotnet-tools.json` to dotnet-ef 10.0.3 (matches EF Core package version in csproj) + set rollForward:true.
- **Lesson**: CLI tool pins should match the primary package version, not a later build; rollForward:true is necessary for dev environments where runtime patches without ceremony.

### Test Results

- **BE**: 0 build errors; 11/11 new unit tests (VgcaCertSubjectParser) + 22/22 signing sweep (migration, DTO, controller).
- **FE**: 3 initial smoke-test regressions (profile page hero duplication) → fixed → 1260/1260 full suite passing.
- **EF Migration**: Applied to dev DB, model snapshot updated.
- **TypeCheck**: 0 errors (FE strict mode).

## What We Tried

1. **Profile hero issue**: Targeted ProfilePage component tests passed; only full suite run caught the duplication breakage. → Lesson: always run full suite when restructuring a shared page.
2. **Dotnet-ef blocking migration**: Tried global 10.0.8 tool (slow, not declarative). → Switched to manifest fix + rollForward:true (correct, declarative).
3. **Image preview**: Initially thought it was a render bug in the modal; then realized the HTTP layer was missing. → Lesson: trace the full stack (DTO → controller → HTTP → FE) before assuming UI logic broke.

## Root Cause Analysis

### Why These Gaps Existed

1. **Scope creep without handoff**: Visible stamp features were incrementally added (signer name, time, reason, toggles) without a single end-to-end validation pass.
2. **Lack of end-to-end test coverage**: Image read-path was never tested; we had store logic but no HTTP surface. Tests covered the sync flow (ClamAV gate) but not the happy-path read.
3. **Reused domain i18n keys**: No lint rule preventing `profile.save` (password-specific) from being reused in a generic modal.
4. **Assumption-based feature delivery**: We delivered based on "obvious" assumptions (signer name comes from profile.Label, stamp time is UTC, serial is always shown) without verifying those were the user's intent.
5. **Smoke-test scope too narrow**: Targeted runs masked a page-level redesign regression that only surfaced in the full suite.

### Why the Tooling Broke

Build-machine runtime patch bumps (10.0.5 → 10.0.8) without updating the CLI manifest; manifest had rollForward:false as a safety guard. Correct behavior from the tool, but unclear guidance on matching versions.

## Lessons Learned

1. **End-to-end validation is not optional**: Image preview, API read-paths, controller wiring—trace all three before declaring a feature done. Test-coverage gaps here are silent failures.
2. **Page-level refactors need full suite runs**: Targeted tests are green, full suite catches cross-component regressions (duplication, shared selectors).
3. **Domain i18n keys are domain-specific**: `profile.save`, `profile.label`, etc. should not be reused by generic modals; lint or convention must enforce this.
4. **Visible artifacts (stamps, previews) require CMS/tz alignment**: Two independent systems (visible stamp, CMS signingTime) rendering the same logical data often use different coordinate systems; sync the display tz explicitly.
5. **Parser/helper libraries hidden from tests**: VgcaCertSubjectParser was a critical helper (enrollment + stamp both depend on it) but had zero unit test coverage. Expose and test these utilities early.
6. **CLI tool version pinning: match packages + allow roll-forward**: dotnet-ef should target the same major.minor as EF Core package; rollForward:true lets runtime patches not break the tool.
7. **Small gaps accumulate into paper cuts**: 7 separate defects felt like 7 separate issues, but they share root causes (lack of end-to-end thinking, assumption-driven scope, test-scope misalignment).

## Decisions

- **Page redesign**: Identity hero at the top; NO duplicate account card below. Identity persists across profile + preferences tabs.
- **Image preview**: Lazy-load via dedicated HTTP endpoint (not base64 in overview DTO). Reduces DTO size for list operations.
- **Timestamp display**: Fixed +07 offset (deterministic, cross-platform) over tz-db lookup (machine-dependent, can vary by region).
- **Stamp fields**: ShowSerial and ShowIssuer default true (existing rows unchanged, full backward compat).
- **Reason field**: Real text input (PadesSignParameters.Reason), distinct from Mysign DocumentName label.

## Next Steps

1. **Create PR** feat/signature-stamp-ux → main; will include all 7 fixes + tests.
2. **Remove unused i18n key** `profile.accountTitle` (was for the duplicate account card).
3. **Optionally clean up**: Remove global dotnet-ef 10.0.8 tool (properly pinned manifest should make it unnecessary).
4. **Defer**: Multi-signer visible stamp (multiple subject names on one stamp) still deferred; requires rework of composer's name-join logic.

---

**Session Summary**: Seven small defects, one big lesson: **end-to-end tracing beats assumption-driven design**. Tests are passing, branch is ready for review. No surprises left.
