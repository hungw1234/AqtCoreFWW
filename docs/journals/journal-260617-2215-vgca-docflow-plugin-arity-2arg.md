# VGCA DocFlow Plugin Arity Mismatch — USB Signing Hung on Plugin Reply

**Date**: 2026-06-17 22:15
**Severity**: High
**Component**: FE Digital Signing / VGCA DocFlow Integration
**Status**: Resolved

## What Happened

3rd USB-signing bug today. After probe-load fix unblocked the path, clicking "Ký số" (USB) on `/files` invoked the VGCA plugin but hung forever on "Đang thực hiện ký số…" — USB PIN dialog never appeared. `/ck-debug` → `/fix` autonomous flow caught it.

## The Brutal Truth

This is maddening because the demo worked fine in isolation. Spent hours chasing "BE didn't stage file" → "BE didn't initiate request" → "agent probe failed" when the real culprit was **we called the plugin with the WRONG number of arguments**. Found it by reading live browser console via Chrome MCP and seeing the plugin crash INSIDE its own WebSocket reply handler. That's the kind of gut-punch that teaches you to skip the hypothesis ladder and read the actual error first.

## Technical Details

Live Chrome console via MCP showed deterministically:
```
TypeError: _0x9e9c9b is not a function at onmessage (vgcaplugin.js)
```

Inside the plugin's WebSocket message handler. The plugin WAS invoked (BE staging + request-init succeeded) but its reply dispatch failed. Proved the call reached the plugin; only the callback contract was broken.

## What We Tried

1. Checked BE stage-copy endpoint — returned 200, file in bucket ✓
2. Checked signing-request initiation — returned 200, record created ✓
3. Assumed probe-load was still broken (false after recent fix)
4. Read vgcaplugin.js exception location → immediately obvious: callback invocation failed

## Root Cause Analysis

`runVgcaPlugin` in `vgca-docflow-methods.ts` called DocFlow functions as **`vgcaFn(sender, params, cb)` (3-arg)** with a 2-arg callback. Official VGCA SDK sample (`other/vgca_usb_token/sample/Demo.htm:36`) proves DocFlow functions are **`(paramsJson, cb)` (2-arg, NO sender)**, and the callback is **`(rv) => JSON.parse(rv)` (1-arg)**.

The spurious leading `sender` shifted the `params` JSON string into the callback position. Plugin stored that string as the callback and tried to invoke it on WSS reply → `"is not a function"` → our real callback never fired → promise hung until 120s timeout.

The demo uses the **raw path** `vgca_sign_msg(sender, json_prms, cb)` which IS genuinely 3-arg. Two VGCA function families, different contracts. Exposed now because probe-load fix made the DocFlow path reachable E2E for the first time (never run with real USB; W2 functions carried `SDK_VERIFICATION_PENDING` flags).

## Lessons Learned

1. **False-green via mock arity**: Old refactor test mocked the 3-arg contract — mirroring the code's WRONG assumption, not the plugin's contract. Mock that echoes buggy code = false-green factory. New test pins 2-arg arity using vendor sample as source-of-truth; asserts `mock.calls[0].length === 2` and arg0 parses as JSON. Re-adding sender makes `JSON.parse(uuid)` throw.

2. **Live console is decisive**: Browser exception location (inside vgcaplugin.js onmessage) separated "BE hung" from "plugin crashed" in seconds. Beats hypothesis hunting.

3. **Demo divergence again**: demo + broken path split at plugin function family / arity. "Demo works ⇏ feature works."

## Next Steps

- **Status Quo**: Commit `7ed2f484` (3 files), 6 commits ahead origin/main, NOT pushed. FE-only, no API/schema change.
- **Verification**: User should retry in browser (HMR loaded fix; close modal + "Ký số" again) to confirm USB PIN dialog appears and ceremony completes.
- **Follow-up**: Pre-existing — batch sign calls `vgca_sign_files` whose arity the sample doesn't demonstrate. Verify before batch-sign ships.
- **Test Coverage**: Commit includes docflow refactor test 16/16 ✓; full suite tsc 0, eslint 0, vitest 276/276 ✓. Code-review: APPROVE_WITH_NITS.
