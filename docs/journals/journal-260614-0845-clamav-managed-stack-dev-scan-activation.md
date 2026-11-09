# ClamAV Managed Stack & Dev Scan Activation

**Date**: 2026-06-14 08:45  
**Severity**: High  
**Component**: Files.Infrastructure.Scanning, docker-compose, SystemParamSeeder  
**Status**: Resolved

## What Happened

ClamAV antivirus scanning code was complete (`ClamAvVirusScanner`, `ClamAvScanJob` Hangfire handler, `ClamAvStartupHealthCheck`) but clamd daemon was absent from docker-compose. Additionally, dev environment had TWO redundant bypasses active: feature flag `feature:files.virus-scan` unseeded (defaulted false) and `ClamAV.BypassWhenUnavailable:true` in appsettings — result was uploads went straight to Available state without ever contacting the scanner. Essentially dormant code behind a gate that was nailed shut.

## The Brutal Truth

This is the second unmanaged backing-service outage in 48 hours (MinIO Friday + ClamAV Saturday). We keep shipping code paths with hard dependencies on infrastructure that lives outside the managed stack, then act surprised when they don't activate. The pattern is: pick ONE implementation of IFileStorageService or IVirusScanner, make it the only thing in production code, then forget to include it in compose/aqt-runapp/scripts. Silent dormancy is worse than loud failure.

The real jaw-drop moment came during code review when we traced through the error handling. The original ClamAvScanJob only branched on Clean vs "everything else" — if clamd was unreachable (Error), the job would reject and **delete the quarantined file permanently with zero retry**. Flipping `BypassWhenUnavailable:false` without fixing that path would have turned a transient clamd hiccup into **unrecoverable data loss**. Found this by audit during planning, not after losing customer uploads.

## Technical Details

**Root causes:**
1. clamd TCP 3310 service missing from docker-compose entirely
2. Dev feature flag never seeded to true, default-missing=false
3. ClamAV.BypassWhenUnavailable hardcoded true in Development.json
4. ClamAvScanJob error path (clamd unreachable) would delete files instead of retry

**Error evidence:** File upload → IVirusScanner.ScanAsync via Hangfire → clamd not listening on 3310 (no service) → job exception unhandled → job outcome ambiguous (Clean? Infected? Retry?).

## What We Tried

Option A (plan): plumb clamd into compose but leave bypass=true, flag unseeded. Risk: unmanaged deps still outside lifecycle. Rejected b/c same class as MinIO.

Option B1: same as A. Rejected for same reason.

Option B2 (chosen): add clamd to managed stack + flip both gates in dev + audit the error path. Commits `158dc980`+`efe4f030`.

## Root Cause Analysis

**Two structural failures:**

1. **Hard dependency without managed lifecycle.** Code assumes exactly one IVirusScanner impl exists and works. We don't verify the impl is in compose/aqt-runapp/scripts before shipping. MinIO had identical pattern (one IFileStorageService, no compose entry until Friday). Solution: audit EVERY hard-dep interface at code-review time — "is the single prod impl in docker-compose?"

2. **Incomplete error handling on activation.** ClamAV code was written with bypass-on-unavailable as the happy path. When we flipped that gate (B2), we didn't re-verify the failure scenario. Result: transient unavailable → permanent file deletion. Lesson: "fail-closed" doesn't mean "fail-loudly," it means "retry-safely." Audit what actually happens to user data when the control becomes active.

## Lessons Learned

- **Verify every hard-dep is in the stack.** If a single interface impl is the only thing prod uses, it must appear in docker-compose + aqt-runapp + lifecycle scripts before merge.

- **Incomplete fixes hide in error paths.** Activating a dormant control (flipping bypass=false) requires tracing the error case, not just the happy path. Reviewer caught this on first pass, but catching in planning == zero rework.

- **Dev gating prevents test pollution.** Using `IsDevelopment()` in SystemParamSeeder meant prod was untouched and test factories (UseEnvironment("Testing")) naturally got flag=false (VirusScanner stubbed anyway). This is right.

- **Cold-boot coupling is a trade-off.** Original PR had `depends_on: clamav: service_healthy` (180s start_period). After adding Error→Retry logic, service_started is sufficient (failures retry safely). Reviewer's challenge brought new reasoning that mooted the original caution. Accept it.

## Changes Made

1. **docker-compose.yml**: Added `clamav` service (clamav/clamav:1.4, port 3310, healthcheck, `clamav_data` volume) + API `depends_on: clamav: service_started` + env `ClamAV__Host: clamav`.

2. **appsettings.Development.json**: `ClamAV.BypassWhenUnavailable: false`.

3. **SystemParamSeeder.cs**: Inject `IHostEnvironment`, seed `feature:files.virus-scan=true` only when `IsDevelopment()==true`. Prod flag policy deferred (CLAMAV-PROD-FLAG-POLICY, T3).

4. **ClamAvScanJob.cs**: Split error path — Clean→Available, Infected→Reject+Delete, **Error (clamd unreachable)→throw to Hangfire→Retry, file stays Quarantined**.

5. **ClamAvScanJobTests.cs**: +4 tests (ErrorThrows, retries, infected rejects).

6. **aqt-runapp SKILL.md**: Step 1.7 (docker compose up clamav).

## Next Steps

**Filed follow-ups:**
- CLAMAV-PROD-FLAG-POLICY (T3): Prod scan default + clamd provisioning strategy
- CLAMAV-FIRSTBOOT-FLAG-RACE (T4): Seeder/refresh ordering window in dev startup

**Related to:** [[saturday-260614-minio-storage-down-download-hardening]] (same class infra-dep bug) + [[saturday-260613-file-download-bearer-anchor-route-fix]] (file service dependency chain).

**Gate status:** SystemParams.Infra 0W/0E · Files.Infrastructure 0W/0E · Files.Infrastructure.Tests 35/35 (4 new) · Files.Application 114/114 · docker compose config valid · code-review APPROVE 0 Critical/High.
