# Sign Uploaded PDF from Files Page

**Date**: 2026-06-17 18:15
**Severity**: Medium
**Component**: Files module + Signing orchestration + FE Forms/Files
**Status**: Shipped with known caveats

## What Happened

Phase 2c-C resolved user complaint "no sign button after uploading a PDF." Shipped 3-part slice: BE stage-for-signing command (copy file to signing bucket + mint docId), unified signing request initiate, FE modal + flow to run VGCA plugin and poll status. Full flow: stage → createSigningRequest(UploadedPdf) → runVgcaPlugin(handoff) → poll useSigningRequestStatus → invalidate list.

## The Brutal Truth

This slice only exists because the original "Phase 2" plan row was half-shipped and half-stale. Audit at slice-start re-verified: most of Phase 2 landed in 2a/2b; the original Step 4 ("wire PlacementJson anchor") was DEAD — FormSubmissionPdfGenerator has zero signers downstream (the PDF is only read by an export query). Building an anchor with no renderers is ceremony masquerading as work. User pivoted to the real unblocked slice: unblock FILES users who upload a PDF and want to sign it immediately. That pivot saved us from shipping dead code.

The trickier truth: getting there required untangling a FE initiate-path duality. The legacy VGCA code invokes DocFlow directly; the unified orchestrator also invokes it. Naively reusing the old path = duplicate signing record → the user gets two pending transactions. Had to extract the plugin invocation half as a pure function and let the unified handoff drive the ceremony. Small fix, large consequence of getting it wrong.

## Technical Details

**BE (Files module):**
- `StageFileForSigning` command: validates PDF in `aqtcorefw-files` bucket (Available status, MIME check, no parse), copies bytes to `signing-original` at key `{tenantId}/{docId}.pdf`, mints UUID docId, returns `{docId, originalStorageKey}`.
- `POST /api/v1/files/{id}/stage-for-signing` endpoint: `[Authorize]` + `[RequiresMfa]`, tenant-scoped, rejects non-PDF + non-Available files.
- Bucket mismatch was the real work: uploaded file lives in `aqtcorefw-files`; DocFlow downloader reads from `signing-original`. Stage-for-signing COPY is not optional—it's the bridge.

**FE (Files + Forms pages):**
- Refactored `vgca-docflow-methods.ts`: extracted `runVgcaPlugin(signTypeToPluginFn, handoff, requestId)` as pure plugin-invocation; kept legacy methods' initiate path intact (regression tests verify 7 methods still initiate-then-invoke once, no dupes).
- New: `file-signing-api.ts` (stage endpoint client), `use-sign-uploaded-file.ts` hook (orchestrate flow), `sign-uploaded-file-modal.tsx`, `sign-uploaded-file-agent-alerts.tsx`.
- "Ký số" action gated on PDF MIME + Clean scan status.

**Security caveat shipped knowingly:**
- USB-signed uploads trusted without cert==signer validation until Phase 2c-D/Phase 6 (cert parsing = later). UI shows caveat during + on success modal; documented in handler comment.

## What We Tried

1. **Reuse old VGCA invoke path directly** — caught in code-review: would duplicate the DocFlow initiation (already done by createSigningRequest). Extracted plugin half instead.
2. **Skip bucket copy, assume format works** — scout found the key-format passes validation but the BUCKET is wrong. Copy is mandatory; added to BE validator.
3. **Parse PDF magic bytes in stage handler** — unnecessary coupling; PDF parsing deferred to Phase 6 (cert extraction). Kept stage-for-signing as byte-copy + MIME check only.

## Root Cause Analysis

The original Phase 2 plan was a victim of parallel shipping: 2a+2b landed earlier, but the plan row stayed "Pending" with stale description. Audit-at-slice-start (INVARIANT 260522-A protocol) caught that the plan's own Step 4 (anchor render) had zero consumers—a schema without readers. Consumer-first thinking revealed the REAL unblocked work: the user-facing pain point (Files page + sign button) was a plan artifact, not yet scoped.

The DocFlow duality (legacy direct invoke vs. unified orchestrator) was pre-existing and not surfaced in any commit message. New code would have silently created two signing records without a second look at the legacy path.

## Lessons Learned

1. **Verify the consumer before building the mechanism.** When a plan step names a schema (PlacementJson) or feature (anchor), don't assume it's needed. Trace the write-path AND the read-path. A feature is only real when both exist. Dead features masquerade as work.

2. **Scout the seam, not just the contract.** The key-format validator passed; the BUCKET didn't match. A pre-cook bucket/seam audit beats discovering it in integration.

3. **Extract the pure function, leave legacy paths alone.** Two callers of the same ceremony (plugin invocation) can cause duplication if one is already wrapped. Extract the atomic action; let the orchestrator choose which ceremony to drive.

4. **Security gaps are acceptable if surfaced.** Certificate binding lands in Phase 6. Interim trust model (USB signature without cert verification) is documented in UI + code comment, not silent. Risk acknowledged.

## Next Steps

1. **Clear requestId on plugin failure** — in-modal retry should not reuse a stale request ID. FU task: cancel-and-retry flow before Phase 2c-D.
2. **Phase 2c-D (cert binding)** — validate signer certificate matches filer identity; blocks upload signing until Phase 6 cert parsing.
3. **Integration environment fix** — dev API host build-lock prevented integration test run; environmental issue, not code. Smoke verified locally.
4. **Commit & push** — awaiting lead approval; status: ready to stage.

## Verification

- **BE tests**: 126/126 (StageFileForSigning + validator + endpoint + integration).
- **FE tests**: 1114/1114 (refactor regression + 7 legacy methods + new flow suites).
- **Linting**: tsc 0 errors, eslint 0 errors.
- **Code review**: APPROVE_WITH_NITS (3 fixes applied: comment clarity, scan-gate logic, success caveat).
- **Integration**: skipped (environmental lock); local smoke verified.
