# ClamAV Scan Gate for Signature Images — Ship SIGN-PROFILE-IMAGE-CLAMAV-SCAN

**Date**: 2026-07-02 08:35
**Severity**: High
**Component**: Identity signature image storage (UserSignatureImageStoreAdapter)
**Status**: Resolved

## What Happened

Shipped Phase-4 signature image upload with magic-byte narrowing (PNG/JPEG) but **bypassed ClamAV virus scan**. User-supplied images persist directly to MinIO → embedded into signed PDFs others download. Unscanned malware carrier.

## The Brutal Truth

Consciously deferred the scan behind a "blocker" that was actually a **conceptual confusion**. Row sat blocked 6 days. The real sting: the underlying port existed all along; we just mistook the async JOB pipeline for a fundamental incompatibility.

## Technical Details

- **Problem location**: `UserSignatureImageStoreAdapter.ValidateAndStoreAsync()` — post-magic-byte check, pre-MinIO upload, no scan call.
- **False blocker premise**: "ClamAV pipeline is async/quarantine state machine, incompatible with sync profile set." Partially true.
- **Actual port**: `IVirusScanner.ScanAsync(Stream)` is **sync-callable**, not async-job-only. The JOB pipeline (ClamAvScanJob + quarantine states) is *orchestration layered on top* of the underlying port.

## Root Cause

Conflated a pipeline's async job orchestration with its synchronously-callable dependency port. Never verified whether the blocking assumption was *architectural* vs *implementation-chosen*.

## Solution

Inject `IVirusScanner + IFeatureFlagService + ILogger` into adapter. Scan ≤500KB in-memory post-magic-byte, pre-upload, gated on `feature:files.virus-scan`:
- **OFF**: skip + CRITICAL log (dev/demo).
- **Clean**: proceed.
- **Infected**: 422 ValidationError.
- **Error/throws**: fail-closed 503 ServiceUnavailableError.

Mirrors ClamAvScanJob semantics. Nothing persisted on any failure path. Defensive try/catch hardens fail-closed.

## Lessons

1. **Distinguish pipeline orchestration from underlying port** before declaring something blocked. Ask: "Is this an architectural constraint or an orchestration choice?"
2. Phase-4 code-review M1 was conscious-skip, not oversight. Document *why* deferred features carry risk (unscanned uploads → downstream exposure).

## Next Steps

- ✓ Shipped: `src/host/AqtCoreFW.Api/CrossModule/UserSignatureImageStoreAdapter.cs` + tests 12/12 green.
- ✓ Gates: Arch 149/149, Integration 6/6, build clean, DI confirmed.
- ✓ No migration, no DTO, no FE change, no public-contract break.
- Plan: `plans/260702-0814-signature-image-clamav-scan-gate/plan.md` (PUSHED).

**Status: RESOLVED** — Row shipped, tests green, gates pass, risk eliminated.
