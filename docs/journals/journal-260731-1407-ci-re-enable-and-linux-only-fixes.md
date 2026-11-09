# CI Re-Enable Arc: Workflow Gate + Linux-Only Test Failures

**Date**: 2026-07-31 14:07  
**Severity**: High  
**Component**: CI/CD pipeline, SPA client validation, test harness (Windows-specific assumptions)  
**Status**: Resolved  

## What Happened

Afternoon session re-enabled the CI workflow after five months dark (since 2026-04-13). Filter-by-path + CS8425 + NU1902 blockers had been cleared in the morning; the re-enable itself surfaced two pre-existing Linux-only test failures and one critical platform-specific validation hole that Windows-local development had masked.

Arc:
1. **Run 1** — Build passed, Test timeout-killed at 10min (cold NuGet cache + full suite on 2-core runner). Raised to 25min (`bd9c49dae`).
2. **Run 2** (`30606206217`, 9m55s) — Exposed 2 real failures never before seen on CI:
   - **Failure A (product bug):** `SpaClientRedirectUris.Resolve` accepted `file:///callback` on Linux as "absolute" — validation hole invisible on Windows.
   - **Failure B (test assumption):** `DrillTempFileCleanupTests` Windows-only FileShare lock trigger; Linux POSIX-unlink bypassed it entirely.
3. **Run 3** (`30611141000`, `70308079c`) — Overall SUCCESS. First fully-green CI since April.

## The Brutal Truth

Five months in the dark is a disaster for platform-specific bugs. Every validation assumption, OS-conditional code path, and test trigger encoded in Windows-only development became invisible. The re-enable didn't find a "small bug" — it exposed that we shipped a **genuine redirect URI validation hole** to production that would silently pass `file://` URIs on the Linux deploy target. That's a security-relevant flaw. If we had landed that in staging or prod without CI, a malicious or misconfigured client could inject local-file URIs into the redirect flow.

The frustration: the fix was trivial (add scheme check), but the fact we couldn't see it for five months is a systemic failure. Windows-local dev is insufficient oracle.

## Technical Details

### Enabling CI: Two Gates Required

`gh workflow enable ci.yml` flipped the workflow to `active` but runs still queued with 0 jobs — the **repo-level** `actions/permissions` was `{enabled:false}`. 

**Fix:** 
```bash
gh api --method PUT repos/aqt/aqtcorefw/actions/permissions \
  -F enabled=true
```

**Critical detail:** `-f enabled=true` sends string `"true"` → HTTP 422 (not a boolean). Use `-F` for typed fields.

Also added `workflow_dispatch:` to `ci.yml` to allow manual run triggers.

### Failure A: File URI Validation Hole

**Code:** `src/Aqt.Domain.Identity/OpenIddict/SpaClientRedirectUris.cs:110`

```csharp
// BEFORE (incorrect)
if (!uri.IsAbsoluteUri)
    throw new ValidationException("Must be absolute URI");

// AFTER (fixed)
if (!uri.IsAbsoluteUri || (uri.Scheme != "http" && uri.Scheme != "https"))
    throw new ValidationException("Must be an http/https absolute URI");
```

**Root cause:** On Linux, `/callback` parses as absolute `file:///callback` (POSIX root path). Windows file system doesn't resolve `/callback` the same way, masking the bug. A relative redirect URI would silently degrade to a `file://` URI on Linux deploy.

**Test added:** `NonHttpScheme_Throws` — pins `file:///callback` and asserts rejection OS-independently.

### Failure B: Windows-Only Test Trigger

**Code:** `tests/Aqt.Infrastructure.FileSecurity.Tests/DrillTempFileCleanupTests.cs`

```csharp
// BEFORE (Windows assumption implicit)
using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
{
    Assert.ThrowsAsync<UnauthorizedAccessException>(
        () => fileSecurity.DeleteAsync(filePath));
}
// On Linux, POSIX unlink succeeds on open file — test never triggers failure path.

// AFTER (OS-conditional)
if (OperatingSystem.IsWindows())
{
    using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => fileSecurity.DeleteAsync(filePath));
    }
}
else if (OperatingSystem.IsLinux())
{
    File.SetUnixFileMode(Path.GetDirectoryName(filePath)!, UnixFileMode.OwnerRead);
    try
    {
        Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => fileSecurity.DeleteAsync(filePath));
    }
    finally
    {
        File.SetUnixFileMode(Path.GetDirectoryName(filePath)!, UnixFileMode.OwnerReadWriteExecute);
    }
}
// Both branches assert the same invariant: delete failure is logged, not thrown.
```

**Why it matters:** Product code swallows `UnauthorizedAccessException` and logs it. The test must assert that behavior on the actual deploy OS. Windows lock + Linux read-only parent both trigger the exception; both verify the swallow-and-log path.

**CA1416 fix:** `[SupportedOSPlatform("windows")]` on the lock branch + `!OperatingSystem.IsWindows()` guard on Linux branch.

## What We Tried

1. **Re-enable via `gh workflow enable`** — failed silently; runs queued 0-job forever.
2. **Checked workflow YAML** — `active`, looked correct.
3. **Discovered repo-level gate via `gh api repos/...`** — that was the real gate.
4. **Run 1 with 10min timeout** — exposed timeout as a budget issue, not a code issue (build step green).
5. **Raised timeout to 25min** — Run 2 exposed the real failures; Run 3 clean.

## Root Cause Analysis

### Why the validation hole shipped

File URI acceptance was never tested on Linux before CI went dark. Windows absolute path parsing differs from Linux POSIX; the code assumed Windows semantics. A code review looking at `IsAbsoluteUri` alone would not catch this — the invariant is OS-dependent.

### Why the test assumption went unnoticed

`FileShare.None` is a Windows construct; on Linux it's a no-op. The test worked on Windows (lock enforced), so the invariant (swallow-and-log) appeared covered. But the test's **trigger** was Windows-only — Linux execution never hit the failure path, leaving coverage incomplete.

### Why CI darkness masks these

- **No Linux execution** = no platform-specific path exercised.
- **Windows-local dev is a single point of failure** = assumptions baked into file I/O, URI parsing, and OS APIs all normalize to one target.
- **Five months without feedback** = a latent hole becomes a shipped hole.

## Lessons Learned

1. **Workflow-level `active` ≠ repo-level `enabled:true`.** GitHub Actions requires BOTH gates. If runs queue with 0 jobs, check `actions/permissions` first. Use `-F` (typed boolean) not `-f` (string) when enabling via `gh api`.

2. **A dark CI is an error-masking vacuum.** Every platform-specific validation, OS-conditional code path, and test trigger that shipped during the outage became invisible until the first real run. The re-enable itself was the highest-value bug-finder of the day.

3. **File URI validation is OS-dependent.** URI parsing normalizes differently across platforms; `IsAbsoluteUri` alone is insufficient on mixed targets. Add explicit scheme checks for security-relevant URIs (redirect flows).

4. **A Windows-authored test can encode platform assumptions invisibly.** `FileShare.None` "locks" only on Windows; the test appeared to work but didn't exercise the failure path on Linux. **Fix:** OS-conditional trigger (Windows lock / Linux permission-deny) that keeps the **invariant** asserted on the actual deploy OS, not a skip.

5. **First-run timeouts are budget bugs.** Read where the step was killed; the build passing proved all real fixes correct. Timeout kills are not code failures — they're resource-budget failures.

## Next Steps

1. ✅ Commit fixes (`70308079c`): validation scheme check + OS-conditional test trigger + CA1416 guard.
2. ✅ Re-enable CI repo-level gate + workflow dispatch.
3. ✅ Raise test timeout to 25min to absorb cold NuGet cache + full suite on 2-core runner.
4. ✅ Verify Run 3 full-green (unit + quality gates + frontend + smoke, all ubuntu).
5. **Future:** Add Linux-specific integration tests for URI/file I/O paths before re-entering dark periods.
6. **Future:** Document platform-specific assumption in code (e.g., `// POSIX: unlink succeeds on open file, unlike Windows FileShare`).

---

**First fully-green CI since 2026-04-13. Linux is now the standing oracle again.**
