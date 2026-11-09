# MessagePack DoS Vulnerability — NU1903 Transitive Pin (GHSA-hv8m-jj95-wg3x)

**Date:** 2026-06-13 11:40  
**Severity:** High (transitive runtime DoS advisory)  
**Component:** Backend dependencies (`src/Directory.Packages.props`, `tests/Directory.Packages.props`)  
**Status:** Resolved (commit `ac4c6693`, pushed origin+bitbucket ahead=0)

## What Happened

`dotnet build src/AqtCoreFW.slnx -c Release /warnaserror` was failing 26 times with **NU1903** — NuGet advisory GHSA-hv8m-jj95-wg3x flagging `MessagePack 2.5.187` (LZ4-decompression DoS via decompression bomb). This dependency enters the graph transitively via `Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.0`, which has `MessagePack >= 2.5.187` unbounded.

Probe-first (INVARIANT 260522-A) confirmed: 1-day-old backlog row's claim was real. 26× NU1903 still failing as stated. This prevented shipping the build.

Fix applied (config-only): pinned `MessagePack` to `2.5.302` (patched floor per advisory; v2 head) via CPM in **both** `src/Directory.Packages.props` and `tests/Directory.Packages.props`. Build result: 0 Error.

## The Brutal Truth

Silent advisory drift during CI darkness. A `/warnaserror` build that was green on 2026-06-10 (verified 0W/0E in prior session) broke with **zero code changes** when the NuGet advisory DB published GHSA-hv8m-jj95-wg3x. Because CI has been parked (disabled since April per `wednesday-260610-feature-dev-phase-pivot-park-ci.md`), no automated gate caught the drift. It only surfaced on the next manual local build.

Real lesson: while CI stays parked, dependency-advisory pollution accumulates invisibly. A manual `/warnaserror` build is the only canary.

## Technical Details

**Advisory:** GHSA-hv8m-jj95-wg3x (MessagePack LZ4 decompression DoS)  
**Vulnerable range:** v2 < 2.5.301 | v3 < 3.1.7  
**Transitive path:** `Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.0` → `MessagePack >= 2.5.187` (unbounded)

**Configuration change:**
```xml
<!-- src/Directory.Packages.props AND tests/Directory.Packages.props -->
<PackageVersion Include="MessagePack" Version="2.5.302" />
```

**Why v2.5.302 over v3.1.7:**
- SignalR 10.0.0 `Protocols.MessagePack` depends on `MessagePack >= 2.5.187` — version range accepts both v2 and v3, but is untested against v3
- v2.5.302 (patched head of v2 line) satisfies SignalR's declared dependency cleanly (NU1605/NU1608 downgrade rules not violated)
- v3 represents a major ABI jump (serialization changes, API surface changes) — YAGNI escalation without evidence SignalR handles it
- Chosen minimal-blast approach: patch-floor safe, no surprises

**CPM requirement (both trees):** Transitive pinning is per-properties-tree. `tests/Directory.Packages.props` resolves SignalR independently (via test project references), so a src-only pin would leave the advisory tripping in test projects' restore. Mirrors existing `Newtonsoft.Json 13.0.4` transitive-pin precedent.

**Code surface:** Zero `using MessagePack` / `MessagePackSerializer` references anywhere in codebase. Serialization handled entirely by SignalR's internal use. No public API contract exposed. No test-suite run needed beyond `/warnaserror` build gate.

## What We Tried

N/A — single-shot config-only fix. No alternatives considered; advisory floor is advisory floor.

## Root Cause Analysis

NuGet advisories publish asynchronously to local restore caches. With CI parked, no automated gate runs daily to alert on advisory growth. Previous build was green; next manual build hit the advisory without warning. This is **not a code bug** — it's a **visibility debt** of test-infra downtime.

The broader lesson: `/warnaserror` is a **manual canary** when CI is dark. Dependency advisories drift independent of commit cadence.

## Lessons Learned

1. **Advisory drift is invisible without CI:** When CI is disabled, dependency-advisory pollution accumulates silently. A manual `/warnaserror` build is the only early warning system. Schedule periodic manual builds (weekly?) if CI stays parked.

2. **Transitive pins live in BOTH props trees:** CPM is per-tree. A unilateral src-only pin leaves test restore exposed. Always verify both `src/` and `tests/Directory.Packages.props` when pinning transitives.

3. **Version escalation requires justification:** Jumping v2→v3 on a transitive because "newer exists" is YAGNI. Patch-floor safe approach: pin to latest stable of the SAME major version already declared by the primary package.

4. **Probe-first pays off in small configs:** Even config-only fixes benefit from "did the problem actually exist?" verification. Saved me from misdiagnosing or applying a phantom fix.

## Next Steps

- Unblock the `/warnaserror` build gate (now passing 0E)
- Re-enable CI when phase-focus allows (not this sprint; parked per `wednesday-260610-feature-dev-phase-pivot-park-ci.md`)
- While CI stays parked: add a manual `/warnaserror` build to weekly checklist to catch advisory drift early
- Close the 1-day-old backlog row `BE-NUGET-MESSAGEPACK-NU1903` with this commit

**Build verification:**
- `dotnet build src/AqtCoreFW.slnx -c Release /warnaserror` → **0 Error, 0 Warning** ✓
- Code-review: APPROVE (config-only, no logic change)
