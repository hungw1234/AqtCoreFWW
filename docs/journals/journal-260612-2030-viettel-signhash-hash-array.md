# Journal — Viettel signHash `hash` must be a JSON array (HTTP 500 Jackson ArrayList)

**Date:** 2026-06-12 · **Flow:** `/ck-debug` → fix · **Trigger:** "Ký số thất bại — SignHash failed: Viettel Mysign SignHash failed — HTTP 500" · **Status:** fixed, tested (30 unit + 15 integration), committed.

## Headline — reached signHash at last; `hash` shape was wrong
The cache-Size and cert-info-`certificates` fixes let the flow get past login + cert-info to **signHash**, which returned HTTP 500. The live API log was the user's own process (not capturable), so I reproduced the call by curl (login → cert-info → signHash, faithful to the app's fields) and captured Viettel's 500 body:
```
{"status_code":500,"message":"JSON parse error: Cannot construct instance of `java.util.ArrayList`
 ... no String-argument constructor ... from String value ('Mc2O5...=')"}
```

## Root cause
Viettel's signHash expects **`hash` as a JSON array** (it signs N hashes → returns N signatures; the response `signatures` is already `List<string>`). The DTO `MysignSignHashRequest.Hash` was a single `string`, so Viettel's Jackson deserializer 500'd trying to build an `ArrayList` from a String. Confirmed empirically: re-probing with `hash:[...]` removed the ArrayList-500 (advanced to a different, async-mode-only error). 

## Fix
- `MysignSignHashRequest.Hash`: `string` → `List<string>`.
- `ViettelMysignSignHashClient`: `Hash: Convert.ToBase64String(hashBytes)` → `Hash: [Convert.ToBase64String(hashBytes)]` (one-element array for one document).
- Response side already correct (`signatures` is a list; provider reads `[0]`). Unit test pins `"hash":[...]` serialization.

## Reusable lessons
- 🔴 **Reproduce-by-curl beats log-hunting when the live process isn't yours.** The user's API process held the only real log; a faithful curl (verified the app adds no extra HttpClient headers) reproduced the exact 500 + gave Viettel's raw Jackson message — the precise field + type.
- 🔴 **A vendor Jackson "Cannot construct ArrayList from String" names the field AND the expected shape.** The error literally said `hash` must be a list; the response being `List<string>` corroborated it (request/response symmetry).
- 🔴 **4th spec-vs-impl drift in this one vendor** (expires_in number, cert-info `certificates`, now `hash` array — plus the latent status-number risk). The Mysign DTOs were built from a misread of the SDK doc; each field that's a number/array on the wire but string/scalar in the DTO is a separate 400/500. A full field-by-field reconciliation against SDK v1.9 would have front-loaded all of these.

## Verification
slnx build 0W/0E · empirical: `hash:[array]` removed the 500 Jackson error · signing unit 30/30 (incl. hash-array serialization) · ViettelMysign integration 15/15 (path-match stubs unaffected).

## Next
async=0 (sync) + `hash:[array]` should now reach the **mobile OTP** confirmation — the only remaining unautomatable step. The user's next confirmed sign yields the raw RSA signature (or surfaces the still-latent `VIETTEL-SIGNHASH-STATUS-NUMERIC-RISK` if a numeric `status` comes back).
