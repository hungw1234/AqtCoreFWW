# Journal — Viettel cert-info missing required `certificates` field (HTTP 400/58100)

**Date:** 2026-06-12 · **Flow:** `/ck-debug` → fix (proactive probe) · **Status:** fixed, tested (6 unit + 15 integration), committed.

## Headline — caught the NEXT failure before the user's OTP round
After the cache-Size fix let login succeed in-app, I proactively curled the **CertificateInfo** call (it runs after login, before the OTP-gated signHash) to pre-empt the next break. It returned **HTTP 400 `{"error":"58100","error_description":"Yêu cầu có thông tin không hợp lệ hoặc sai định dạng"}`** — even with a request faithful to the app (verified the named HttpClient adds no extra headers, so my probe == the app's request).

## Root cause — a missing MANDATORY request field, found in the vendor spec PDF
Read the in-repo spec `Tai lieu tich hop dich vu Mysign API SDK v1.9.pdf` §1.2.2. The cert-info request body includes `"certificates": "chain"` — and empirically it is **required**: a probe matrix proved the ONLY field that flips 400→200 is `certificates: "chain"`. (`client_secret` is NOT needed — the Bearer token authenticates; a probe without it but with `certificates` still returned 200.) The app's `MysignCertInfoRequest` DTO sent only `client_id/profile_id/user_id` → omitted `certificates` → 400/58100. With it, the endpoint returns the expected JSON array `[{"cert":{"status":"valid","certificates":[...],...}}]`.

## Fix
Added `[JsonPropertyName("certificates")] string Certificates = "chain"` to `MysignCertInfoRequest` (defaulted, so the existing named-arg call site needs no change and auto-includes it). Unit test asserts the serialized request carries `"certificates":"chain"`.

## Reusable lessons
- 🔴 **Proactive-probe the next external call after fixing the current one.** Login→cert-info→signHash is a chain; fixing the login cache only surfaced the cert-info 400. Curling cert-info (non-OTP) before the user's retry saved a wasted round-trip.
- 🔴 **A faithful repro needs the app's real request — verify the HttpClient adds nothing.** Confirmed `AddHttpClient<CertInfoClient>()` only attaches Polly (no default headers/base-addr), so the raw curl == the app's request; the 400 was real, not a probe artifact.
- 🔴 **When a vendor returns a generic "wrong format" (58100), the spec body example is the authority** — diff the implemented request against the spec's example field-by-field. The dev's DTO dropped `certificates` (and wrongly assumed `client_secret`); the probe matrix isolated which field actually mattered.
- 🔴 **This is the 3rd spec-vs-impl drift in one vendor (expires_in number, status-field risk, now certificates)** — Viettel's Mysign impl warrants a field-by-field spec reconciliation pass, not piecemeal fixes.

## Verification
slnx build 0W/0E · empirical live probe: cert-info WITH `certificates:"chain"` → HTTP 200 + real 3-cert chain · unit 6/6 (parsing 3 + cache 2 + cert-info-serialization 1) · ViettelMysign integration 15/15 (stubs match on path, unaffected).

## Next in the chain
The OTP-gated **signHash** is now the only unexercised step. Watch for `VIETTEL-SIGNHASH-STATUS-NUMERIC-RISK` (filed) — `MysignSignHashResponse.Status` `string?` with JsonOpts lacking `NumberHandling`; if UAT returns a numeric status it throws like expires_in did. The user's next OTP-confirmed sign reveals it.
