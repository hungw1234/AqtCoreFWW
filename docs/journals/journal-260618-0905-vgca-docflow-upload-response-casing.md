# VGCA Upload Response camelCase → Desktop Tool Never Fires Callback → 120s Timeout

**Date**: 2026-06-18 09:05
**Severity**: High
**Component**: BE Api / VGCA DocFlow FileUploadHandler + FE sign-uploaded modal
**Status**: Resolved

## What Happened

8th layer of the USB-signing chain — the first clean-PDF happy-path attempt. The modal showed a contradiction: a green "Ký số thành công" panel AND a red "vgca_sign_issued timed out after 120s" alert at the same time. `/ck-debug`.

## The Brutal Truth

The signing genuinely SUCCEEDED server-side — BE log `DocFlow upload completed: recordId=10002` — yet the browser promise still timed out. Two independent completion signals disagreed: the durable signing-request status poll (truth) said Completed; the VGCA plugin callback never fired so `runVgcaPlugin` hit its 120s timeout. The BE log showed TWO download+upload cycles for one record, and the console showed two `vgcaplugin.js: Connection is closed...` (WSS teardowns) — the tool was retrying.

## Technical Details

The VGCA desktop tool POSTs the signed PDF to FileUploadHandler, reads the JSON response, and only then invokes its browser-side callback `cb(rv)`. The tool parses that response case-sensitively and expects the vendor SDK sample's PascalCase shape `{Status, Message, FileName, FileServer}` (sample uses raw `Response.Write` of literal PascalCase).

Our handler returned `Ok(new { Status = true, FileName, FileServer })`. The host JSON config (`AddControllers().AddJsonOptions`) only adds an enum converter — it never overrides `PropertyNamingPolicy`, so `JsonSerializerDefaults.Web` (camelCase) applies, and boxing into an anonymous object does NOT bypass the policy. Wire bytes were `{"status":true,"fileName":…,"fileServer":…}`. The tool could not read `Status`/`FileServer` → treated the upload as not-yet-successful → retried the whole ceremony → still camelCase → gave up WITHOUT firing `cb`. The browser promise then ran to its 120s timeout.

This is the same class as the Files-list casing bug: anonymous-object boxing does not escape the STJ camelCase naming policy.

## Root Cause Analysis

Primary: upload response serialized camelCase; the case-sensitive desktop tool needs PascalCase. Secondary (UI): `sign-uploaded-file-modal` rendered the hook's `error` guarded only by `!isFailed`, not `!isCompleted` — so a stale best-effort callback error showed on top of the success result whenever the status poll already reported Completed. Shared blast radius: FileUploadHandler serves both Files and Forms signing.

## The Fix

1. Replace the anonymous object with a `VgcaUploadResponse` record carrying `[JsonPropertyName("Status"/"Message"/"FileName"/"FileServer")]`. The attribute overrides the naming policy and pins the wire bytes to the contract the tool was built against (PascalCase + the sample's `Message` field).
2. Modal: change the error guard to `error && !isFailed && !isCompleted` — the durable signing record is the source of truth; a missing best-effort callback no longer renders a failure alert over success.

Bundled with two carry-over fixes from the prior session, finalized after this E2E: reject encrypted PDFs at signing ingest (clear Vietnamese message), and surface the backend reason in the modal instead of a generic toast.

## Lessons Learned

1. **Pin the serialization casing at the real boundary.** An integration test now POSTs a signed PDF and asserts the response body contains `"Status":true` / `"FileServer":` and NOT `"status"` / `"fileServer"` — runs through the real MVC pipeline + MinIO, fails on any camelCase/anonymous-object regression. This is also the first automated coverage of the upload half of the happy path (previously "tested manually").
2. **A black-box client's contract authority is its vendor sample, not your assumption.** The handler comment claimed it returned `{Status:true,...}`; the wire bytes said otherwise. Verified against the sample, not the comment.
3. **Empirical confirmation, not just plausibility.** Before the fix: 2 download/upload cycles (retry). After: exactly 1 cycle (`recordId=10003`) + user-confirmed clean success. The disappearance of the retry is the proof the tool now accepts the response.
4. **Durable record beats best-effort callback.** When two completion signals exist, the persistent one wins; suppress the transient one's error rather than show both.
5. **`clearAllMocks()` does not reset `mockReturnValue`.** A pre-existing FE test passed only by inheriting a prior test's Completed status under the looser guard; the tightened guard exposed it. Fixed by stating the precondition explicitly.

## Next Steps

- **Status Quo**: 3 commits, NOT pushed — `20f523ce` (encrypt-gate), `aeaf511d` (error-surface), `cd37403b` (PascalCase + modal guard).
- **Verification**: BE build 0W/0E; Files.Application 19/19; DocFlowHandlerTests 9/9 (incl. new upload PascalCase test); FE 17/17; tsc 0. User confirmed real-USB Ca B (clean PDF) signs end-to-end with a clean success and no timeout alert.
- **Follow-up**: the signed-file `FileServer` URL points at `/api/signing/docflow/download/{tenant}/{key}`, which has no GET route — latent (the tool echoes it, does not dereference it here); add a download endpoint or drop the field if a future tool build fetches it. Batch `vgca_sign_files` arity still unverified against hardware.
