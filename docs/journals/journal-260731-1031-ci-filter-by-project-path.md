# CI Filter-by-Project-Path Verification & Latent Blocker Discovery

**Date**: 2026-07-31 10:31
**Severity**: High
**Component**: CI/CD, test-scoping, project-file structure
**Status**: Resolved (slice shipped, sub-blockers #5/#6 documented)

## What Happened

Shipped CI unit/integration test split via filter-by-project-path mechanism (commit `a52754754`): replaced fragile FQN-substring filtering (`--filter "FullyQualifiedName!~Integration"`) with explicit subset `.slnx` solution files (`ci/unit.slnx` 41 projects, `ci/integration.slnx` 23). Verify-first rewrite exposed TWO pre-existing latent blockers that shipped silently after CI went dark 2026-04-13 and never ran through the real ci.yml flag sequence.

## The Brutal Truth

This is exactly the verification pattern that should have caught those blockers **months ago**. The fact that we shipped code with latent errors (CS8425 async-iterator registration omissions, NU1902 transitive vulnerability warn-as-error upgrade) because CI has been dark since April is massively frustrating. A single honest verify-first build with the EXACT ci.yml flags would have walled this off immediately — instead we drifted blind for 3 months. The slice itself is solid, but it illuminated how dark the dark period was.

## Technical Details

### Blocker #4 Resolution (the planned work)
- **Original filter**: `--filter "FullyQualifiedName!~Integration"` leaked 3 projects with no "Integration" token in FQN (`Concurrency.Tests`, `Security.Tests`, `TenantIsolation.Tests`) into the unit (no-DB) job.
- **Solution chosen**: filter-by-project-path via explicit `.slnx` subset files (native MSBuild format, direct `dotnet build` support, single invocation preserves coverage pipeline).
- **Risk eliminated**: `.slnf` (solution-filter) rejected immediately (`MSB5025: Json in solution filter file … incorrectly formatted` on SDK 10.0.302); would have forced pivot mid-slice.
- **Membership audit**: code-review caught silent omission of `tests/contracts` (47 no-DB tests that old `!~Integration` DID run) → added to `ci/unit.slnx`. Plan enumerated `tests/{unit,architecture}` but never listed `tests/{contracts,regression}`.

### Blocker #5 (CS8425 — Pre-Existing, Shipped 2026-07-29)
- **Trigger**: Building `ci/unit.slnx` with `-p:TreatWarningsAsErrors=true` initially showed "clean" (DECEPTION).
- **Real crime**: Initial verify used MSBuild **property** (`-p:TreatWarningsAsErrors=true`), NOT the ci.yml **flag** (`/warnaserror`). The property does NOT elevate NuGet NU-warnings; the flag does. Only when exact ci.yml sequence reproduced (`/warnaserror` without property) did this surface.
- **Technical debt**: 7 files contain fake `QueryUnbufferedAsync` iterators (added by `801e21e67` "stream submissions CSV export" 2026-07-29, AFTER CI dark). Missing `[EnumeratorCancellation]` on cancellation token parameter. `tests/Directory.Build.props` TreatWarningsAsErrors at Release → would have failed immediate first re-enable.
- **Fixed in-slice** (mechanical `[EnumeratorCancellation]` registration), behavior-neutral, code-review confirmed matches canonical `DapperReadDbConnection.cs:163`.
- **A/B-convicted pre-existing**: touched projects already in slnx at time of slice; standalone build of untouched source = same errors. Shipped dark; verify-first only caught it.

### Blocker #6 (NU1902 — Pre-Existing, Shipped 2026-07-29)
- **Trigger**: `/warnaserror` flag elevates NuGet NU-warnings (property `/p:TreatWarningsAsErrors=true` does not).
- **Reality check**: Original untouched `dotnet build src/AqtCoreFW.slnx /warnaserror` = **32–64 NU1902 errors** (AngleSharp 0.17.1, known moderate CVE, transitive via Forms/Files). Pre-existing, independent of slice.
- **User decision**: Commit slice, defer NU1902 as separate dependency-decision sub-blocker (#6). Options: bump AngleSharp, suppress warning, accept risk — each needs test/audit.
- **Why it matters**: First `gh workflow enable` will FAIL with 32+ errors if NU1902 remains elevated by `/warnaserror`.

### The False-Green Trap (lesson earned hard)
Verify path #1 (using MSBuild property): "0 errors" → proceeded to next task.
Verify path #2 (exact ci.yml reproduction, `/warnaserror` flag): "32 NU1902 errors, 7 CS8425 errors" → **stopped**, A/B-convicted both pre-existing.

**The trap**: properties and flags are NOT interchangeable. A single MSBuild property does NOT substitute for the ci.yml flag stack. This masking cost verification-round turnaround.

## What We Tried

1. **`.slnf` solution-filter approach**: REJECTED by SDK 10.0.302 (malformed JSON error) — forced pivot to `.slnx`.
2. **Verify with `-p:TreatWarningsAsErrors=true`**: Showed false-green (0 errors) → misled towards "ready to enable" → blocker #6 invisible.
3. **Exact ci.yml flag reproduction (`/warnaserror` naked)**: EXPOSED both NU1902 and CS8425 immediately.
4. **Manual project enumeration for `ci/*.slnx`**: First draft dropped `tests/contracts` silently → code-review re-enumerated all test dirs.

## Root Cause Analysis

**Blocker #4 (FQN-filter leak)**: The original filter relied on a naming convention (projects with "Integration" in FQN) that three projects violated. Human-enumerated `.slnx` is more stable because membership is explicit, not convention-based.

**Blockers #5 & #6 (CS8425 + NU1902)**: Code shipped 2026-07-29 with new async-iterator usage and a transitive CVE-carrying package. CI has been dark (`disabled_manually` since 2026-04-13 per actions state) for **3.5 months**. No build step ran the real warning-as-error pipeline. A single honest `dotnet build /warnaserror` that month would have caught both. Verify-first only worked because we finally ran the exact sequence.

**Membership silent-drop in slice**: Plan text enumerated obvious test dirs but not the full sibling set. This is the SAME silent-membership-drop class this slice targets (FQN leakage), inverted — the fix and the risk live in the same problem space.

## Lessons Learned

1. **Verify-first-before-enable is load-bearing, not cosmetic.** Flipping a test mechanism forces a real build that exposes latent blockers. This slice's verify step caught TWO months-old regressions that would have broken the first real CI run. Without verify-first, `gh workflow enable` would have failed immediately.

2. **Property ≠ flag for MSBuild warning elevation.** `-p:TreatWarningsAsErrors=true` (property) does NOT elevate NuGet NU-warnings; `/warnaserror` (flag) does. A verify that uses the property gives a false-green for NU-warnings. Always reproduce the EXACT ci.yml flag sequence when verifying, not "equivalent" shortcuts.

3. **A membership-scoped slice must enumerate ALL sibling members, not the obvious ones.** Plan missed `tests/contracts` and `tests/regression` — only enumerated `tests/{unit,architecture}`. Code-review caught this by re-enumerating the full test directory tree. The slice targets silent-membership-leak; the plan itself leaked membership silently. Invert every enumeration slice: "what did I assume was NOT there?"

4. **Latent blockers shipped dark surface only under honest builds.** Both CS8425 (async-iterator registration) and NU1902 (NuGet warn-as-error) shipped on 2026-07-29. Neither violated any design or requirement — they're pure build-gate violations that only surface when the gate runs. A dark CI period is not just a delayed-feedback problem; it's an error-masking vacuum. The moment CI went dark, latent blockers became invisible.

5. **Git-Bash on Windows has a pathconv quirk.** `/warnaserror` → `C:/Program Files/Git/warnaserror` (local shell artifact). Use `MSYS_NO_PATHCONV=1` for local verification. CI runs ubuntu, so real runs are clean; this is local-only noise.

## Next Steps

1. **Sub-blocker #5 (CS8425)**: FIXED in this slice (mechanical registration). Code-review clean. Merged.
2. **Sub-blocker #6 (NU1902)**: DEFERRED per user decision. Separate dependency-audit task: bump AngleSharp vs suppress vs accept risk. MUST resolve before `gh workflow enable`.
3. **Architecture test for `ci/*.slnx` membership drift** (code-review MEDIUM): Add schema-validation that enumerates all `tests/**` dirs and asserts `ci/unit.slnx` + `ci/integration.slnx` covers the full set. Prevents silent-drops in future slices.
4. **Re-enable CI**: Blocked on sub-blockers #2 (contention flakes), #3 (SQL/Hangfire timing), #6 (NU1902 NuGet audit). `gh workflow enable` remains gated.

---

**Verify-limit statement**: CI remains disabled since 2026-04-13. All verification = local `dotnet build` + `--list-tests` + static yml audit. No real GitHub Actions run possible. Blockers #5/#6 are PRE-EXISTING and pre-date this slice's merge; they are orthogonal closure gates for CI re-enable, not slice-internal regressions.
