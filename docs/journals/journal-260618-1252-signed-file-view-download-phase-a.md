# Signed File View & Download — Linking Signing Records to Uploaded/Form PDFs (Phase A)

**Date**: 2026-06-18 12:52
**Severity**: Medium
**Component**: BE Identity.Contracts + Identity.App + Files.App + Forms.Application + FE Files/Forms signing surfaces
**Status**: Resolved (unmerged, 2 commits ahead)

## What Happened

After USB DocFlow signing completes, users can now view (embedded react-pdf) and download each step's signed PDF. The feature bridges the gap between the signing orchestration (already shipping DocFlow records with per-step `SignedStorageKey`) and the file-management UI (Files list, Forms submission view). Ground-truth storage (4 commits prior) had the signed blobs; the gaps were: (1) no UI-facing GET endpoint to fetch them, (2) no persistent link from file/submission back to signing history, (3) no shared viewer on both surfaces.

## The Brutal Truth

We were exposing dormant cryptographic work — signing records existed but the UI couldn't reach them. A user who signed a PDF on the Files page got no way to view or re-download it; signing history was invisible. Forms submission signing was equally broken. The code had been written to persist per-sequence signed blobs (sequence 1, 2, 3... for multi-step signing) but the UI contract to retrieve them did NOT exist. A legitimate signing operation was logically orphaned from user visibility.

## Technical Details

**Storage state before this work**: SigningDocFlowRecord tracked `DocId` (signing request), `SignedStorageKey` (this step), `PreviousSignedStorageKey` (prior step), `SignerSequenceNumber` (1, 2, ... N). No link back to the originating file. StageFileForSigning created a transient `docId` and dropped it; the FileRecord never knew which signing request it came from.

**Design decisions**:

1. **Link**: Added `SigningRequest.SourceFileId (Guid?)` (Identity module). Set at request-creation when `ArtifactKind = UploadedPdf` or via Forms.Contracts when `IFormSubmissionSigningAnchor` creates the request. Migration `20260618034503_SigningRequestSourceFile`. Files module stayed signing-agnostic (no reverse link on FileRecord). Symmetric with Forms' already-shipped `FormSubmission.SigningRequestId`.

2. **Contract boundary**: 5 new Contracts-owned read methods:
   - `GetStepsForFile(tenantId, fileId) → List<SignedFileStep>` (grabs all signing requests by sourceFileId, hydrates signed blobs).
   - `GetStepsForRequestPublicId(publicId)` (polling endpoint variant).
   - `ResolveSignedStepForFile(tenantId, fileId, sequenceNumber)` and `ForRequest(...)` (single-blob fetch, server-side key resolution).
   - `GetSignedFileIds(tenantId, fileIds[]) → Dict<Guid, List<Guid>>` (batch endpoint for UI badges).

3. **Cross-module contract**: Forms→Files blob streaming via host-resident `ISignedDocumentDownloader` port + adapter to `IFileStorageService`. Keeps Forms domain signing-agnostic of Files storage. No circular dependency; host injects at composition root.

4. **Endpoints** (all 4 new, session-authed, tenant-scoped, cross-tenant → 404):
   - `GET /api/v1/files/{fileId}/signed-steps` (list all steps for a file).
   - `GET /api/v1/files/{fileId}/signed-step/{sequenceNumber}` (fetch one step's PDF blob).
   - `GET /api/v1/forms/submissions/{publicId}/signed-steps` (Forms variant).
   - `GET /api/v1/forms/submissions/{publicId}/signed-step/{sequenceNumber}`.

5. **FE**: Shared embedded react-pdf viewer + signing-steps sidebar (DRY across Files & Forms). "Đã ký" badge on Files list (calls the batch endpoint). "Lịch sử ký" history drawer surfaces sequence, signer role, timestamp, status. Unconditional "chưa xác minh chữ ký điện tử" caveat retained per Phase A scope (verify deferred to Phase B).

## What We Tried

**Rejected alternatives**:

1. Column on FileRecord for SigningRequestId: larger blast radius (Files.Domain change), less clean separation (Files logically unaware of signing).
2. Orphan-discovery sweeper (query SigningDocFlowRecord, backfill FileRecord): post-hoc linkage; we chose to link at creation instead.
3. Per-module viewer (separate react-pdf copies in Files vs Forms): duplication; extracted shared `SignedDocumentViewer` component.

## Root Cause Analysis

The signing work had been split across phases: storage (Phase 0), orchestration (Phase 1), execution bridge (Phase 2a), multi-signer (2b), forms/file integration (2c). The UI was planned as Phase A (this). The storage and orchestration were shipped without the UI surface, leaving the feature incomplete at the user boundary. No root error; rather, a deliberate phased approach that left the user-facing half until now. The gap was NOT a bug, just unfinished work.

## Implementation Notes

**Orchestration choice**: Implemented BE first (01→02→03) in ONE sequential agent to keep the cross-module contract coherent, then FE in one agent against the live wire shapes. Deliberately NOT parallel BE‖FE — the codebase has a documented history of parallel agents drifting a contract field (notably `SigningRequestDto.ProviderKey` count). Synchronous delivery of the contract eliminated rework.

**Test gap-close**: The BE impl agent wrote Identity-integration + Files-unit tests; spawned a separate pass to add Files.Integration + Forms.Integration ENDPOINT tests (+16). Cross-tenant access → 404. Download assertions check BOTH Content-Type AND the `%PDF-` magic header (guards the "200 empty body" failure mode the codebase has shipped before). This closes the false-green class.

## Lessons Learned

1. **Contract authority is the vendor sample (or the wire test).** When parallel agents work cross-module, pin the wire shape FIRST in an endpoint test (not in prose). The 2c-A signing-request-DTO earlier had 3 agents negotiate over "4 fields"; the endpoint test at serialization boundary caught the drift immediately (RequestChanges review).

2. **Re-signing creates duplicates without deterministic ordering.** ResolveSignedStepForFile used FirstOrDefault on (tenantId, sourceFileId, sequenceNumber) without ORDER BY → engine-dependent behavior. Fixed with explicit ordering (prefer Completed, then latest Id) + per-sequence deduplication in the steps list (multi-signer sequences preserved). Regression test pinned it.

3. **Authorization inside a class-level [Authorize] is invisible.** The new Forms endpoints had only controller-level auth; a user without Forms.Submit.View could read another tenant's submission's signed PDF. Review caught it; added per-endpoint policies. The cross-tenant 404 was already enforced, so no breach, but per-endpoint policies are now explicit.

4. **Wire-level test is not mock-level test.** An earlier test pinned `sourceFileId` by mocking the Command handler; a later test mocked the apiClient (one layer higher). The mock test was 1 layer above serialization, so it didn't catch a wire-shape break. New test at the real apiClient.post boundary + integration HTTP pipeline.

5. **Phase A labels in code comments violate rule §5.** Removed all references to plan phase numbers from code (comments kept only the WHY — e.g., "verify deferred, unconditional caveat retained for now").

## Next Steps

- **Status quo**: 2 commits unmerged:
  - `3096795b` (docs: changelog + signing roadmap update).
  - `9f7ae6d3` (feat: signed file view/download, 57 files).

- **Verification**: BE 662/662 (Identity.Domain 85, Files.App 144, Identity.Integration 280, Files.Integration 15, Forms.Integration 29, Architecture 110 — module isolation intact). FE Phase A 31 tests green, tsc 0, eslint 0. RequestSignatureAsync signature unchanged; all additions are additive.

- **Deferred / carry-over**:
  1. **H1**: Negative-permission test (403 for user without Forms.Submit.View). Integration harness cannot seed restricted users without a full Role/Permission graph. The policy itself IS enforced; this is a test coverage gap, not a security gap.
  2. **Phase B (Verify)**: PdfPig/BouncyCastle signature parser + sandbox + async verify-at-step job persisting evidence. Reuses existing chain/OCSP verify engine.
  3. **FE dedup**: Two near-duplicate Forms history test files (consolidate).
  4. **Pre-existing 11 FE flakes**: workflow-migration / workflow-state-taxonomy; jsdom `getComputedStyle` + test timeouts. Unrelated to this feature.
  5. **Latent**: FileServer dead-URL in upload response (`/api/signing/docflow/download/{tenant}/{key}` has no GET route). Deferred.
  6. **E2E human-verify**: Real-USB device signing with the new view/download surface. Dev stack UP (5001/5002/5173); not yet human-confirmed.

- **Unresolved questions**: None — all design and authorization decisions verified with review feedback.
