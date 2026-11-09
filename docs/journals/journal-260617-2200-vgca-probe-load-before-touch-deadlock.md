# VGCA Script Load Deadlock — Probe Before Plugin Loaded

**Date**: 2026-06-17 22:00
**Severity**: High
**Component**: Digital Signature (FE) — VGCA USB adapter + availability probe
**Status**: Resolved

## What Happened

User switched dev tenant to USB provider (`usb-vgca`), clicked "Ký số" on `/files` → modal reported "Không kết nối được VGCASignService / Tải phần mềm VGCA" (`not-installed` state). Same machine, same agent running, demo page `/digital-signature/demo` signed fine. Availability probe lied; sign button unreachable; script never loaded → deadlock loop.

## The Brutal Truth

This is the fourth false-green pattern in the signing arc. We shipped a "probe does not throw on missing plugin" test (`TC-DSIG-VGCA-004`) that PASSED FOR THE WRONG REASON — the probe short-circuited on `window.vgca_*` being undefined and never executed the script-load path the test claimed to cover. Prod exposed the real code path the test was pretending to exercise. Demo worked NOT because the probe succeeds, but because `signature-demo-page.tsx:140` skipped the probe entirely by passing `capabilities=''`, so the feature never tested the probe-gated path.

Maddening: we have 5+ files touching VGCA globals, only 1 loads the script, and the 2 that gate features don't know about that loader.

## Technical Details

**Root cause:**
- `/vendor/vgca/vgcaplugin.js` defines all `window.vgca_*` globals (injected via `<script>` tag)
- `ensurePluginLoaded()` in `vgca-usb-adapter.ts` lazily injects the script — but lived PRIVATE to the adapter
- **Probe** (`probeVgcaAgent` in vgca-availability-probe.ts) read `window.vgca_*` directly (no load)
- **DocFlow invoker** (`runVgcaPlugin` in vgca-docflow-methods.ts) same: direct read (no load)
- Files modal, form submission PDF signer, signing-prompt with RequiresLocalAgent → probe runs FIRST
- Global absent → 'not-installed' verdict → button gated → unreachable sign() → script never loads → TRAPPED

**Verification:**
```
// Demo path (works):
signature-demo-page.tsx:140 → capabilities='' → requiresLocalAgent=false 
→ skip probe → straight to sign() → ensurePluginLoaded() fires → SUCCESS

// Files modal path (was broken):
file-sign-modal → probeVgcaAgent() → reads window.vgca_* (undefined) 
→ 'not-installed' → button disabled → sign() unreachable → DEADLOCK
```

## What We Tried

Initial hypothesis: agent not running. Verified: `localhost:18080` responding, `/digital-signature/demo` signs flawlessly on same session. Probe reports wrong.

Traced probe code: reads global without loading script. Traced sign code: loads the script. **The two paths diverged at a capability flag check**, not at agent availability.

## Root Cause Analysis

Three interlocking failures:

1. **Private API leakage**: `ensurePluginLoaded()` was a private utility in the adapter — no contract for "you must load before reading globals". Probe and docflow were written as if globals pre-existed.

2. **Test assumption mismatch**: `TC-DSIG-VGCA-004` passed because undefined-global check exited early. Test INTENDED to cover "load fails gracefully" but load was unreachable in the test scenario. Test name claimed to cover the failure case; code never tested it.

3. **Divergent entry points**: Demo page shortcut-skipped the probe on empty capabilities. Every other path hit probe first. Feature legitimacy was conditional on which flag was set.

## Lessons Learned

**FE contract testing**: probe tests mocked `window.vgca_*` as pre-loaded, hiding the dependency on script injection. When test and impl diverge on "what's the precondition", the test passes wrong. New regression test (`DCP-VGCA-007`) pins the REAL boundary: (1) no script loaded, (2) trigger probe, (3) script injection happens, (4) globals present. Fails without the fix.

**False-green #4 root**: the test validated a statement (probe doesn't throw) not the capability (probe detects true state). Same error as POLISH-B/C (asserting at wrong layer). Unit test ≠ contract verification — contract lives at the real serialization boundary.

**Fake-timer trap**: inserting `await` before the `setTimeout` broke `vi.useFakeTimers()`. Fixed by switching to `vi.advanceTimersByTimeAsync()` — async calls need async timer advance.

## The Fix

**NEW leaf module** `vgca-script-loader.ts` (dependency-free, avoids cycles):
```ts
let pluginLoadPromise: Promise<void> | null = null;

export async function ensureVgcaPluginLoaded(): Promise<void> {
  if (pluginLoadPromise) return pluginLoadPromise;
  pluginLoadPromise = injectVgcaScript().catch(err => {
    pluginLoadPromise = null; // Reset on failure for retry affordance
    throw err;
  });
  return pluginLoadPromise;
}
```

**Callers (4 sites):**
- `vgca-usb-adapter.ts`: `sign()`, `getCertInfo()` (already did; now shared)
- `vgca-availability-probe.ts`: `probeVgcaAgent()` — `await` before global read
- `vgca-docflow-methods.ts`: `runVgcaPlugin()` — made async, `await` before global read

**Semantic gain:** 'not-installed' now means script genuinely unloadable (network error, 404, CORS); agent-not-running correctly classifies as 'unreachable' (probe succeeds, global present, initiate fails).

## Next Steps

None blocking. Commit `103a5bfd` (7 files), 5 commits ahead of origin/main, NOT pushed.

**Test coverage:** vitest 276/276 (digital-signature 7/7 new + files + forms + signing-prompt). tsc 0, eslint 0.

**Code review:** APPROVE_WITH_NITS — applied 2 doc comments (sentinel-global short-circuit assumption + querySelector early-resolve assumption in loader).

**Not adopted:** eager SDK preload at app bootstrap (rejected YAGNI — lazy load-before-touch sufficient and cheaper on TTI).
