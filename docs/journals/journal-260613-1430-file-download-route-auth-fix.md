# File Download Route Auth Fix

**Date**: 2026-06-13 14:30  
**Severity**: High  
**Component**: Forms/File Management (FE download transport)  
**Status**: Resolved  

## What Happened

Smoke test of upload-UX slices (progress bar + lifecycle stepper) hit file download failure. GET `/api/v1/files/{id}/download` returned 404. User could upload but couldn't download. Ran `/ck-debug` to trace.

## The Brutal Truth

Two independent pre-existing bugs converged into one broken flow. The upload work is fine — this is legacy cruft that lived in the codebase because nobody tested the download path end-to-end with auth. Spending cycles on dead code nobody is running is exactly how bugs escape.

## Technical Details

**Layer 1 — Route mismatch (404)**

FE hand-written helpers (`downloadFile`, `getFileDownloadUrl`, `form-attachment-preview` href) appended `/download` suffix. BE `FilesController` has:
```csharp
[HttpGet("{id:guid}")]  // GET /api/v1/files/{id}
public async Task<IActionResult> Download(Guid id)
```

The endpoint is at `/files/{id}` NOT `/files/{id}/download`. Verified empirically:
- curl `/files/{id}/download` → 404
- curl `/files/{id}` → 401
- OpenAPI-generated client (`api/generated/files/files.ts:207`) maps to `/files/{id}` (correct).

The `/download` suffix was copy-pasted from the **reports** module which genuinely has it. Cross-module copy-paste without verification.

**Layer 2 — Auth transport (401)**

File route is `[Authorize]`. Auth = OIDC Bearer attached by `apiClient` request interceptor.

A raw `<a href>` or `<img src>` does NOT go through `apiClient`:
- Anchors/images only send cookies, never Authorization header.
- Since auth is header-based, 401 even with the correct URL.
- Visible symptom was 404 (layer 1), hiding the 401 (layer 2) beneath it.

**Layer 3 — Latent metadata endpoint bug**

`form-attachment-preview.tsx` fetched metadata from `GET /files/{id}` (the byte-stream route) expecting JSON `FileRecordDto`. Wrong endpoint. Should be `GET /files/{id}/metadata` returning `FileMetadataDto` (camelCase). Didn't surface because the 401 wall blocked it.

## What We Tried

1. Grep'd BE routes → found `/files/{id}` in generated client, confirmed 404 was wrong path.
2. Tested with curl + Bearer token → `/files/{id}` returned bytes (confirm auth works).
3. Traced FE anchor href → realized raw `<a>` never attaches Authorization header.
4. Built `fetchFileBlob(id)` using apiClient + responseType:'blob' → blob → createObjectURL → transient anchor.

## Root Cause Analysis

1. **No e2e test of download flow** — smoke tests skipped the auth transport layer. Upload was tested, download was forgotten.
2. **Hand-written URL helpers drift from generated client** — the OpenAPI-generated client is the source of truth and was correct. Helpers copy-pasted from reports and diverged.
3. **Raw `<a href>` is not an API call** — fundamental misunderstanding that plain HTML anchors/images can't carry custom headers. This is a whole class of bug.

## Lessons Learned

- **Raw `<a href>` to Bearer-authenticated API ALWAYS fails 401** — if a `[Authorize]` endpoint uses header-based auth, anchors/images can't reach it. Must fetch as blob through the http client.
- **OpenAPI-generated client = route source of truth** — when hand-written paths 404, diff against generated before guessing.
- **Module-local URL conventions don't generalize** — `/download` is reports-specific. Don't copy across modules without validation.
- **Blob responses survive apiClient envelope-unwrap safely** — guard is `'success' in data`; Blob has no such key, so it's never mangled. responseType:'blob' is safe.
- **Trace all layers before patching symptom** — visible 404 was hiding 401 above it above latent metadata-endpoint bug. Same lesson as BP-run 3-layer signature gap: one symptom, three failures.

## Next Steps

**Done (commit b60cc7ed, pushed origin + bitbucket, ahead/behind 0):**
- `file-api.ts` — `fetchFileBlob(id)` async; blob → createObjectURL → transient anchor click → revokeObjectURL. `useFileBlob(id, enabled)` hook for thumbnails. Removed `getFileDownloadUrl`.
- `form-field-renderer.tsx` — link onClick → `downloadFile(id)` instead of href.
- `form-attachment-preview.tsx` — metadata endpoint fix; image thumbnail from authenticated blob; PDF/generic download on click.
- Test coverage — 3 new unit tests in `file-api-download.test.ts`.

**Gate results:** tsc 0E · eslint 0E (1 pre-existing react-refresh warning unrelated) · full FE suite 1045/1045 · code-reviewer APPROVE_WITH_NITS (0-blocking; applied nit: console.error in catch).

**Open non-blocking follow-ups from review:**
- a11y keyboard-activation on Typography.Link downloads (no href).
- useFileBlob shows Spin forever on blob fetch error (cosmetic; click-download works).
- No unit test for useFileBlob unmount-revoke cleanup (low-risk; effect has no side effects on cleanup failure).
