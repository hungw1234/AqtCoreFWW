# MinIO Storage Down + Silent 200 Download Hardening

**Date**: 2026-06-14 00:15
**Severity**: High
**Component**: Files.Infrastructure (MinIO), FileUploadHandler, DownloadFileQueryHandler
**Status**: Resolved

## What Happened

After shipping FE file-download fix (commit b60cc7ed), user reported downloads "rất chậm" (extremely slow). curl to backend returned TTFB 8.2s constant (cold + warm), HTTP 200, Content-Length 0, but valid xlsx Content-Disposition header. The response body was empty despite the success code. Diagnosis revealed two distinct faults:

1. **Ops failure**: MinIO object storage container was not running. It died silently, never restarted, never auto-recovered. The BE had NO visibility into this.
2. **Code fault**: DownloadAsync returned an empty RecyclableMemoryStream as a "success" when GetObject retrieved nothing. Fail-silent instead of fail-fast.

## The Brutal Truth

This is genuinely infuriating. MinIO is a critical dependency—the ONLY IFileStorageService implementation—yet it was completely unmanaged:
- Not in docker-compose.yml
- Not in the aqt-runapp skill init steps
- Not in any infra/scripts
- No container health checks
- No alerting

It "worked" until it didn't. The moment it died, every file download became a 0-byte corrupt file with HTTP 200, which is worse than a crash because the FE couldn't tell something was wrong. We shipped corrupted artifacts with a success code.

The 8.2s latency was the MinIO SDK v6.0.4's connect-retry budget exhausting against dead localhost:9000. Each attempt to GetObject hung for 8 seconds before failing silently inside the code. That should have been an exception, not an empty stream.

## Technical Details

**Symptoms:**
- curl: TTFB 8.2s constant, body 0 bytes, HTTP 200, valid Content-Disposition
- BE logs (correlation ID): only token-introspect (0.2ms), then 8.2s silent
- Zero MinIO errors in logs (MinIO wasn't instrumented; the client tried, timed out, moved on)

**Root diagnosis:**
```bash
# Probed MinIO localhost:9000
docker ps | grep minio  # EMPTY
grep -r "minio" infra/docker-compose.yml  # NO MATCH
tasklist | findstr minio  # NO MATCH
netstat -an | grep 9000  # NOTHING
```

MinIO was never there. The uploaded files that worked earlier? Either MinIO was briefly spun up manually and forgotten, or those uploads hit a different service before MinIO died.

**Code fault:** `MinioFileStorageService.DownloadAsync` called GetObject and returned RecyclableMemoryStream even when the object didn't exist. No pre-flight HEAD. No exception. Empty stream as "success."

## What We Tried

1. **Initial hypothesis**: FE file-download fix introduced the bug
   - Ruled out: curl hit BE directly, isolated to backend
2. **Network/latency hypothesis**: slow connection
   - Ruled out: time_connect 1ms, time_starttransfer 8.2s → TTFB is BE, not transport
3. **Backend slow query/lock**: database or crypto contention
   - Ruled out: logs show only token-introspect (0.2ms) then 8s silence with no log activity
4. **MinIO misconfiguration**: wrong endpoint
   - Diagnosis: no MinIO service running at all

## Root Cause Analysis

**Two separate issues, both preventable:**

1. **Unmanaged infra dependency**: MinIO was a hard dependency (the only IFileStorageService) yet had no lifecycle management. No Dockerfile service, no script auto-start, no health checks, no alerting. It was assumed-running instead of managed-running. When it died, the application silently degraded.

2. **Silent empty-success pattern**: `DownloadAsync` should fail loudly when the object is missing. Instead it returned a 0-byte stream as HTTP 200, producing corrupt files. This masked the real failure (storage down) under a false success signal.

3. **State commit ordering bug** (exposed by the fix): `FileUploadHandlerController` (signing docflow) committed nonce use + marked download as taken BEFORE fetching the PDF from storage. With the new fail-fast behavior, storage failures burned the nonce and stranded the record unrecoverably.

## Lessons Learned

- **Every hard infrastructure dependency must be in the stack's lifecycle management**. If code hard-depends on MinIO, it must be in docker-compose, aqt-runapp, or CI pipeline. Assumed-running is a guarantee of eventual catastrophe.

- **Silent degradation is worse than loud failure**. A storage layer must fail fast and loud (throw FileNotFoundException) so callers know something is wrong. Empty-as-success corrupts files, masks the real issue, and breaks downstream error handling.

- **Fail-fast changes ripple through caller state machines**. Making a layer throw exception instead of degrade forces you to grep ALL callers and reorder state mutations. Commit nonce use / mark-as-taken must come AFTER successful storage fetch, not before. Found via code-reviewer catch.

- **Constant-latency timeouts are a diagnostic tell**. 8.2s identical on cold + warm = fixed retry/timeout budget against a dead endpoint, not variable load. Separates storage-down from slow-storage instantly.

- **Diagnose with curl + timing breakdown (`curl -w`)**. Broke out BE TTFB (8.2s) from network connect (1ms) from FE in one command. Eliminated the just-shipped FE fix as root cause immediately.

## Next Steps

**Shipped (both committed + pushed origin + bitbucket, ahead/behind 0):**
- Commit ace693a8: Added minio service to docker-compose.yml (pinned image 2025-09-07, ports 9000+9001, dev creds, depends_on in api); added MinIO Step 1.6 to ~/.claude/skills/aqt-runapp/SKILL.md. No init container; bucket auto-creates.
- Commit 5fbe3066: `MinioFileStorageService.DownloadAsync` calls StatObject before stream (fail-fast on missing). `DownloadFileQueryHandler` catches FileNotFoundException → 404 (orphaned record). `FileUploadHandlerController` reordered: fetch PDF FIRST, commit nonce+state only on success (fail-safe, retryable on storage error). +integration test asserting nonce.UseCount=0 on storage failure.

**Gate**: Build warnaserror 0E; Files.Infrastructure+API.Host 0E; Files.Application.Tests 114/114; DocFlow integration 6/6 (new H1 test included); MinIO container health 200 in 3ms; docker-compose config valid; code-reviewer APPROVE_WITH_NITS (M1 depends_on applied, H1 folded in).

**Non-blocking follow-ups** (low priority, no correctness impact):
- L1: `DownloadFileResult.SizeBytes` hydrated from DB not StatObject (drift risk long-term)
- M2: GetObject-side ObjectNotFound not explicitly mapped (TOCTOU → 5xx not 404, benign fail-fast)

**Existing data**: File records uploaded while MinIO was down are orphaned (fresh minio_data volume). They now correctly 404 on download instead of silent 0-byte. New uploads work.
