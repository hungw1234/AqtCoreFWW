# VGCA DocFlow FileName Must Be an Absolute Download URL — 0x0023 UriFormatException

**Date**: 2026-06-17 22:40
**Severity**: High
**Component**: FE Digital Signing / VGCA DocFlow Integration
**Status**: Resolved

## What Happened

4th USB-signing bug in the day's chain (route-404 → probe-load → arity → this). After the arity fix let the plugin actually talk to the desktop SignService, the VGCA dialog opened but pressing "KÝ SỐ" failed with **"Tải tệp ký số không thành công! (Mã: 0x0023)"**. `/ck-debug` → `/fix`.

## The Brutal Truth

I almost concluded "external SignService connectivity issue" — no BE access log, curl reached the route. Then the user pasted the VGCASignService's OWN exception log and it was unambiguous: `System.UriFormatException: Invalid URI` at `System.Net.WebRequest.Create`. The desktop tool was building a malformed URI from a value WE supplied. Lesson burned in: do not conclude "external" before reading the external component's own log.

## Technical Details

VGCASignService log:
```
System.UriFormatException: Invalid URI: The format of the URI could not be determined.
   at System.Net.WebRequest.Create(String requestUriString)
   at VGCASignService.Crypto.FormSignIssued...
```

`runVgcaPlugin` set the plugin param `FileName` to `metadata.fileName` (a bare display name like `test.pdf`). The VGCA integration doc (`other/vgca_usb_token/doc/HD-tichhop-VGCASignService.pdf`) defines `FileName` as "đường dẫn văn bản ký số" — the document's download path/URL — and the tool passes it straight to `WebRequest.Create`. A bare filename is a relative URI → UriFormatException → surfaced as 0x0023. The SDK sample `Demo.htm` sets `FileName=""`; the only relative string in our params object was FileName.

## Root Cause Analysis

`FileName` is the URL the desktop tool DOWNLOADS the to-be-signed PDF from, not a display name. We sent a bare filename → relative URI → crash inside the tool.

## The Fix

Point `FileName` at the gated handler download URL the `[AllowAnonymous]` endpoint requires:
`${handoff.handlerUrl}?docId=${encodeURIComponent(docId)}&token=${encodeURIComponent(jwt)}`. Absolute URI the tool can `WebRequest.Create`. Upload still POSTs to the same `handlerUrl`; the JWT nonce allows two uses (download slot 1, upload slot 2).

## Lessons Learned

1. **Read the external component's own log before blaming "external".** The SignService exception was the decisive evidence; BE-side absence of an access log was consistent with the request never leaving the desktop tool.
2. **Vendor doc + sample are the contract authority.** `FileName` semantics came straight from the integration doc, not from guessing at the field name.
3. **Test at the real boundary.** Updated the params-capture test to assert FileName is the exact composed URL + matches `^https?://` (never a bare filename).

## Next Steps

- **Status Quo**: Commit `b9f6a203` (2 files), FE-only, NOT pushed.
- **Verification**: download GET now reaches the BE (proven by the next bug surfacing — see sub-claim journal). FE-only change.
- **Test Coverage**: docflow refactor test 16/16; full FE regression 277/277.
- **Follow-up**: handlerUrl absoluteness is a BE-side invariant (`InitiateDocFlowCommand` builds it from BaseUrl) — the test pins the composed FileName but not handlerUrl itself; low risk.
