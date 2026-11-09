# Signature Profile Image Upload: Dual-Path Convergence (File + Canvas Draw)

**Date:** 2026-07-01 16:10
**Component:** Identity.Web / Digital Signature
**Severity:** Medium
**Status:** Resolved (not yet committed; commit = next step)

## What Happened

Shipped self-service signature-profile visual-image management (`SIGN-PROFILE-IMAGE-UPLOAD-FE` backlog row). Two upload paths — file picker + canvas draw — both converge at **the same multipart endpoint** (`POST /me/signature-profiles/{publicId}/images`). BE required zero new endpoints; FE canvas draw path exports canvas→PNG via `dataUrlToFile` (base64→Blob→File), so both paths feed identical multipart body. DTO extended with per-slot image badges (`HasFullImage`/`HasInitialImage` boolean flags). Unblocks end-to-end self-service visible signing (image is hard prerequisite of `PadesTransactionInitiator` / `SIGNING_PADES_PROFILE_IMAGE_MISSING`).

## The Brutal Truth

Scope expanded twice during planning: (1) badges for image state, (2) canvas draw alongside upload. Both felt like separate problems requiring separate endpoints. The elegant part (and why I'm documenting this) is recognizing they **converge at the same multipart** — canvas is just a different source of the PNG, not a different sink. Feels almost unfairly clean in hindsight. Zero BE change, zero new DTOs, zero schema touch. KISS win. But getting there required asking "what does draw actually do" + resisting premature endpoint proliferation.

## Technical Details

**Dual-path convergence:** Upload path: user picks file → multipart POST. Draw path: user sketches → `SignatureWidget` (react-signature-canvas, DPR-scaled, 500KB guard) → export canvas → `dataUrlToFile()` (base64→Blob→File constructor) → **same multipart POST.** BE receives file + slot (Full/Initial) enum, stores BLOB, tracks state in `HasFullImage`/`HasInitialImage` boolean projections on `SignatureProfileDto`.

**DRY via promotion:** Discovered existing proven `SignatureWidget` in `features/forms/builder/` (already has jsdom mock, handles DPR scaling, enforces 500KB guard). Reused it by promoting to `@/shared/components/SignatureDrawingWidget` (git rename, 2 importers repointed) rather than duplicating canvas logic. Avoids dual maintenance.

**Enum drift caught:** Backlog named slots "Full/Initial," but BE enum source is `FullSignature=1`/`InitialSignature=2`. Verified from source before wiring the multipart field name (sends enum **NAME**, not value). Prevented a silent enum mismatch.

**Overview DTO gap:** `SignatureProfileDto` exposed no image state → added `HasFullImage`/`HasInitialImage` to Application DTO + updated both `SignatureOverviewQuery` + `ListMySignatureProfilesQuery` projections. FE renders slot badges (locked if image present, unlocked if absent). Zero schema migration (Application-only flags, derived from BLOB existence).

**Test rendering trap:** antd Modal renders into a PORTAL → `container.querySelector` finds nothing. Switched to `document.querySelector` in modal test. Sibling fixture `sign-uploaded-file-pades-modal.test.tsx` was missing the 2 new DTO fields — tsc caught it before runtime.

**Docs-manager post-hoc errors:** Subagent hallucinated wrong file path ("digital-signature/images/…" vs actual "profile/signatures/…") + wrote "PUSHED" when not pushed. Corrected before commit.

## Lessons Learned

1. **Converge paths late, not early.** Don't create separate endpoints for different sources of the same data. Canvas draw is just a different file source — it belongs at the same multipart sink.
2. **Verify enum names from source before wiring.** Backlog row names vs. actual enum shape can drift. One `grep` for `enum .*Signature` saves a runtime bug.
3. **Promotion is cheaper than duplication.** When component logic is proven (tests, guards, scaling), promote it to shared rather than clone. `SignatureWidget` already had 500KB guard + DPR scaling + jsdom mock — reuse it.
4. **Portal rendering requires document-level queries.** antd Modal PORTAL children invisible to `container.querySelector` — must use `document.querySelector`. tsc catches missing DTO fields, but rendering assumptions don't fail until runtime without explicit test verification.
5. **Verify subagent docs against filesystem.** Hallucinated paths + false "PUSHED" claims slip past review if not spot-checked. One `ls` + `git status` call catches them.

## Gates Passed

- BE Identity.Application: 415/415 tests (no new, but all still pass)
- Architecture: 149/149 tests
- FE vitest scoped: 39/39 tests (6 files: image upload, canvas draw, profile tabs, badge state, query hooks, modal integration)
- tsc: clean (including sibling fixtures)
- eslint: 0 errors
- code-reviewer: **DONE** (0 blocking, all acceptance criteria met, 2 Low cosmetic notes, YAGNI-skipped)

## Next Steps

- Commit `docs/dependency-map.md` + `docs/journals/` + code (not pushed yet)
- Run full FE suite + API E2E before push (vitest 1250 + Playwright smoke)
- Monitor: user-set signature images in staging (edge cases: revoked cert + new image, slot swap)
