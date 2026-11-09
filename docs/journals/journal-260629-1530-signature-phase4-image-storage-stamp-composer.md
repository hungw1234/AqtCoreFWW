# Signature Phase 4: Image Storage & Stamp Composition — H6 Boundary Resolved

**Date**: 2026-06-29 15:30  
**Severity**: High (cross-module boundary + validation gates)  
**Component**: Identity.Application + CrossModule adapter; image upload + stamp composition  
**Status**: DONE (2 commits staged, not yet pushed at journal time)

## What Happened

Phase 4 of plan 260623-1950 completed: interactive cook with re-verified scout (4-day-old plan decay), H6 cross-module decision gate, TDD impl, code-review M1/M2 findings. Delivered Part A (image upload pipeline, Identity→Files) + Part B (pure-logic stamp composer). All tests GREEN: 15 unit + 8 adapter + arch 149/149 + Identity.Application.Tests 385/385.

## The Brutal Truth

The plan left ONE critical design gap: where does the cross-module image store adapter live? We had to make that call fresh. The relief is that scout caught a ghost requirement (SignatureVisualType enum already shipped in P1); the sting is that grep missed it initially — trust-but-verify saved us from duplicating an enum. The bigger pivot: QuestPDF requires SkiaSharp native libs incompatible with Alpine musl + our "no-raster" constraint. Result: composer flipped from Infrastructure to Application, pure-logic instead of rich Domain. That's better for testability, but it's a reminder that architectural assumptions in 4-day-old plans can evaporate. M1 (ClamAV deep-scan bypass, S8) got accepted + a follow-up task; the deferral stings because validation is supposed to be airtight, but async routing in P5 is justified by cost/benefit.

## Technical Details

**Part A — image upload pipeline:**
- Port: `IUserSignatureImageStore` (Identity.Application) + host `UserSignatureImageStoreAdapter` (CrossModule, mirrors `SigningDocFlowBlobStoreAdapter` pattern)
- Validation: `IFileSecurityService.ValidateMimeTypeAsync` accepts unknown-magic (defers to ClamAV), but adapter enforces PNG/JPEG magic-byte narrowing (S8: renamed-SVG or PDF-as-PNG caught here, not by declared content-type)
- Storage: MinIO bucket `user-signatures`
- Endpoint: `POST /me/signature-profiles/{publicId}/images` multipart; caller-scoped anti-IDOR, targeted-slot write
- Overwrite: best-effort delete-prior-blob after save (swallows errors so upload never fails); reclaim job catches stragglers
- DI: Program.cs

**Part B — stamp composer:**
- Port: `ISignatureStampComposer` + DTOs `StampContent`/`ComposedStamp`
- Implementation: sanitize text (control/newline/ellipsize), pass-through image bytes (no raster)
- Location: Identity.Application (pure logic), NOT Infrastructure (QuestPDF/SkiaSharp deferred to P5)
- Tests: 15 unit (composer sanitization + composition logic), 8 adapter (upload validation + storage flow)
- DI: IdentityInfrastructureRegistration

## What We Tried

1. **H6 boundary (two paths):** (a) mirror host-adapter pattern (chosen, keeps ModuleIsolationTests green) vs (b) port magic-byte to SharedKernel (rejected: zero primitives + circular risk). User approved via AskUserQuestion.
2. **C2 no-raster consequence:** plan assumed Infrastructure impl. Pivoted to Application after discovering SkiaSharp Alpine incompatibility; PDF drawing + glyph tests deferred to P5.
3. **FileSecurityService tightening:** attempted PNG/JPEG magic-byte in ValidateMimeTypeAsync; realized it returns accepted-type for unknowns. Added explicit narrowing in adapter instead.

## Root Cause Analysis

**Scout stale on SignatureVisualType:** Plan 260623-1950 claimed enum missing; ship history (`def58fd0` P1) already delivered it. 4-day lag + grep miss = false requirement. Lesson: re-grep claimed "missing" enums before opening design.

**ClamAV M1 finding (deep-scan bypass):** FileSecurityService is weak upstream check (cheap); deep-scan is stateful/slow. Decision: accept async routing in P5 when broad sharing activates. Valid finding, justified deferral per cost/benefit.

**Orphan blob M2 (overwrite risk):** Initial impl deleted prior-blob before saving (rollback risk). Fixed: save first, then delete-prior best-effort. Safer, reclaim job cleans stragglers.

## Lessons Learned

1. **Plan decay > 4d is real.** Scout for "already shipped" primitives before claiming they're missing. One grep miss acceptable; silent duplication would be worse.
2. **Cross-module pattern:** port in consumer (Identity.Application), host adapter in host (CrossModule). Keeps isolation tests green, avoids circular deps. Do not centralize primitives in SharedKernel as a shortcut.
3. **Validation gate layering:** weak check upstream (FileSecurityService), strong check at boundary (magic-byte narrowing). Declared content-type never trusted (S8). Async deep-scan deferred when cost justified (bulk sharing later). This is sound.
4. **Constraints cascade early:** no-raster constraint meant SkiaSharp native was blocker; composer became pure-logic Application instead of rich Infrastructure model. Better for testing, clearer separation.
5. **YAGNI on ContentType:** dropped from StampContent before impl (dead input, we derive from bytes).

## Next Steps

1. **Commit 2 slices** (not yet done): image upload pipeline + composer logic + all tests. Commit cites backlog row from plan 260623-1950.
2. **File follow-up:** `SIGN-PROFILE-IMAGE-CLAMAV-SCAN` — route deep-scan when profile shared + blob reachable outside tenant (P5/P6 scope).
3. **Code-review approval:** M1 + follow-up documented in adapter; M2 verified via MinIO stub.

## Unresolved Questions

- **ClamAV async routing for broad sharing (P5+):** Re-scan on profile-share, or is upload scan sufficient? (Deferred; current upload safe for v1.)
- **Diacritic coverage on Alpine:** Will P5 XObject rendering handle Vietnamese diacritics with default Helvetica? (Test deferred to P5; plan assumed embedded font.)
- **Orphan blob SLA:** Is hourly reclaim sufficient, or prioritize same-tenant overwrite? (Backlog decision; current best-effort acceptable.)
