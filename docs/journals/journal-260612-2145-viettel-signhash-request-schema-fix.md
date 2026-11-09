# Journal — Viettel signHash whole-body schema was wrong (HTTP 400/58100), then E2E sign succeeded

**Date:** 2026-06-12 · **Flow:** `/ck-debug` → fix → `/cook` finalize · **Trigger:** "SignHash failed — [58100] Yêu cầu có thông tin không hợp lệ hoặc sai định dạng" · **Status:** fixed, built 0W/0E, 40/40 unit, code-review clean, **E2E real OTP sign + verify succeeded**. Commits `ea2e657b` + `d9ab4715` (21 ahead, not pushed).

## Headline — the 5th vendor drift was the whole schema, not one field
The `hash:[array]` fix cleared the HTTP 500 and advanced signHash to **HTTP 400 error 58100** — the same "wrong format" code cert-info threw before `certificates:"chain"`. But this time it wasn't one missing field: the entire signHash request body used the login/cert-info **snake_case** shape, which Viettel rejects. The signHash endpoint (Mysign API SDK v1.9 §1.2.3/§2.2.3) is a **distinct schema**.

## Root cause — request shape mismatch, proven by curl
Empirical proof before touching code (the session's gold standard — the vendor ignores its own spec, so spec alone isn't proof):
- Old app body (`credential_id`/`hash_algorithm`/`sign_algorithm`, stray `profile_id`/`user_id`, **missing** `client_secret`/`numSignatures`/`documents`) → `400 {"error":"58100"}`.
- Spec-correct body (`credentialID`/`hashAlgo`/`signAlgo` + `client_secret` + `numSignatures` + `documents:[{document_id,document_name}]`) → `200 {"transactionId":...}`.

Used `async=2` for the probe so format validation returns immediately without blocking on OTP. `cert.status=valid`, `credential_id` resolved fine — the only fault was the request schema.

## Fix (`ea2e657b`, 6 files; public contract untouched)
- `MysignDtos.cs` — `MysignSignHashRequest` rewritten to the spec schema; new `MysignSignDocument`.
- `MysignDocumentName.cs` (new) — sanitizes `document_name` to Viettel's hard charset `[A-Za-z0-9 _-]`, Base64 < 100 chars, non-empty fallback. Defense-in-depth: a free-text/Vietnamese reason Base64'd verbatim reproduces the exact 58100 class. Display-only field, not bound to the hash → lossy sanitization is safe.
- `ViettelMysignSignHashClient.cs` — optional `documentName` param on both methods; builds the new body (`numSignatures:1`, one document, one hash).
- `ViettelMysignProvider.cs` — threads `request.Reason` → `documentName` at both call sites (sync + async).
- 2 test files: rewrote `MysignSignHashRequestTests` (assert spec keys present + legacy keys absent — the negative is what caused the prod bug), added `MysignDocumentNameTests`.

## Follow-up polish (`d9ab4715`) — Vietnamese "đ" fold
Code-review caught a real edge: NFD has **no decomposition for U+0111/U+0110 ("đ"/"Đ")**, so the d-stroke was dropped as disallowed, deleting every "đ" word ("Sửa đổi" → "Sua oi"). Since the signer reads this on their phone and "đ" is ubiquitous in VN names, fold đ→d / Đ→D before NFD. +1 test (40 total).

## Reusable lessons
- 🔴 **A vendor endpoint can have a different request schema from its siblings.** Login/cert-info take snake_case `user_id`/`profile_id`; signHash takes PascalCase `credentialID` + camelCase `hashAlgo`/`signAlgo` + a `documents` array. Don't assume one DTO style across a vendor's API — reconcile each endpoint against its own spec section.
- 🔴 **58100 = "diff your request against the spec example field-by-field."** Same code, different endpoint, different missing pieces. Cert-info was 1 field; signHash was the whole body.
- 🔴 **NFD does not fold every Vietnamese letter.** "đ"/"Đ" has no canonical decomposition — diacritic-strip via NFD silently deletes it. Pre-fold đ→d before normalizing when sanitizing VN text for an ASCII-restricted field.
- 🔴 **Environment can vanish mid-session.** A Visual Studio update resumed mid-flight and tore down the .NET 10 SDK/runtime (`dotnet --list-sdks` empty); the running net10 hosts survived only because they were already in memory. Don't fight it — wait for `msiexec`/`setup` to finish (it upgraded to SDK 10.0.204 / runtime 10.0.8), never interfere with an in-progress installer.
- 🔴 **The 5th spec-vs-impl drift in one vendor** (expires_in number · cert-info `certificates` · hash array · now whole signHash schema · + latent status-number risk). The Mysign DTOs were built from a misread of the SDK; a single field-by-field reconciliation pass would have front-loaded all five 400/500s instead of one OTP round each.

## Verification
SDK 10.0.204 · build 0W/0E `-warnaserror` · unit 40/40 Signing (3 DTO-shape + 9 sanitizer + cert-info/login/cache) · code-review DONE, 0 Critical/High/Medium (3 Low informational, `client_secret` never logged, contract intact, async path shares the corrected builder) · **E2E: user completed a real OTP-confirmed sign + chain verification against Viettel UAT — IsValid=true.** Stack restarted live (Auth 5002 · API 5001 · FE 5173 · Vault 8200, signing-health 2 trusted roots).

## Smoke status — Viettel Mysign remote signing is E2E operational on DEV
login → cert-info → signHash (sync, OTP) → signature → verify, all green with per-user CCCD + trust-store chain. The four request-shape bugs (expires_in / cache Size / certificates / hash-array) plus this schema fix complete the integration.

## Open / watch
- `VIETTEL-SIGNHASH-STATUS-NUMERIC-RISK` (filed) did NOT fire — the sync success path returns `signatures`, not `status`; the numeric-`status` risk only lives on the async-poll `/requests/status` response. Lower likelihood than feared, but unverified for async modes. Keep filed.
- 21 commits unpushed (CI parked since April; user controls push timing).
