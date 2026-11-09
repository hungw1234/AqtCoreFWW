# Journal — FE DigitalSignature field type (QĐ-742 verify-at-ingest, FE half)

**Date:** 2026-06-13 15:10
**Scope:** `/cook 04e` — add a `DigitalSignature` form field type end-to-end on the FE, reusing the existing crypto-signing infra, threading a signature proof bundle into the BE verify-at-ingest shipped 2026-06-12 (BE 04a–04d).
**Outcome:** DONE for authenticated forms. Anonymous/public-form signing deferred (user decision). 0 commits yet — finalize gate.

## What shipped

- **BE (minimal, additive):** `GET /api/v1/digital-signature/provider-info` — `SigningProviderInfoController` (Identity.Presentation) + `GetSigningProviderInfoQuery`/handler + `SigningProviderInfoDto` (Identity.Application). `[Authorize]`-only, JWT-tenant-scoped, returns `{ isEnabled, providerKey, capabilities }`. 4 NSubstitute handler tests.
- **FE field type:** `DigitalSignature` added to `FormFieldType` union + `field-type-registry` + `form-field-renderer` case + new `digital-signature-widget.tsx`. Mode config (`CanonicalJson | ServerDocHash`) edited in the builder properties panel, persisted to `FormField.validationRulesJson` `{"signatureMode":...}`.
- **Canonicalizer (TOP RISK):** `form-data-canonicalizer.ts` — byte-identical to BE `FormDataCanonicalizer.CanonicalizeMode1`. Parity test pins the SAME fixed vectors as `FormDataCanonicalizerTests.cs` (hex-compared).
- **Threading:** widget stores a `SignaturePayloadRequest` in antd form state; `extractSignaturePayload` lifts it to the submit body's optional `signature` sibling; `cleanFormData` drops the field value from `data`.
- **Mode-2:** `fetchSignatureDocHash` (auth) hits `POST /forms/{id}/signature-doc-hash`.

## The headline lesson — scout overturned a load-bearing plan premise

Phase 04e assumed `useSignatureConfig(tenantId)` would yield `providerKey`/`capabilities` in the fill flow. Scout proved it FALSE: that hook calls `GET /admin/security/signature-config/{tenantId}` — **SystemAdmin-only** — and `DigitalSignatureController` is `[Authorize][RequiresMfa]`, so a normal form filler (let alone an anonymous visitor) has **no path** to discover which provider drives the tenant. `<SigningPrompt>` needs `providerKey` to pick the client adapter (USB-local vs remote-OTP), so without it the modal can't open. Even the existing `signature-demo-page` only fully works for SystemAdmin.

This was surfaced (HARD-GATE) rather than silently inventing scope. User chose: add a minimal non-admin `provider-info` endpoint, defer anonymous signing. Lesson reinforced: **a "FE just threads X" step in a BE-focused plan can hide a missing read path — verify the FE can actually obtain X before trusting the plan.**

## Byte-parity nuances locked

- FE canonicalizes over `JSON.parse(JSON.stringify(values))` so antd `dayjs` Date objects collapse to the same ISO string the BE deserializes — otherwise `dayjs.toString() !== its JSON form` → 100% false-reject. Documented as the canonicalizer's input contract.
- `String(n)` (FE) === `GetRawText()` (BE) for all finite numbers (same ECMAScript ToString that produced the wire token). Non-primitive fallback hardened to `JSON.stringify` (matches BE `GetRawText` for complex JsonElement) instead of `String()` → no silent `[object Object]` divergence on the (unreachable-for-scalars) edge.
- `JSON.stringify` ↔ `UnsafeRelaxedJsonEscaping`: both leave non-ASCII literal; tested with Vietnamese `Hà Nội` (no `\u`) + NFC decomposed/precomposed parity.

## Invalidate-on-edit

The widget snapshots the canonical bytes at sign time; a `useEffect` clears the captured signature when the current covered-data snapshot diverges — editing any covered field after signing forces a re-sign (friendlier than a silent 400 at submit).

## Verify

- Identity.Presentation build `-p:TreatWarningsAsErrors=true` 0W/0E · Identity.Application.Tests 4/4 (new handler).
- FE `tsc --noEmit` clean · FE vitest full suite **1035/1035** (26 new: 18 canonicalizer parity vectors + 5 threading + 3 widget behavior).
- code-review **APPROVE**, 0 Critical/High. M1 (non-primitive coercion) applied; L1 (i18n EN keys) applied; M2 (useMemo perf) skipped as YAGNI.
- Forms module untouched this slice (no rebuild needed; unchanged since BE work).

## Carry-forward

- **04f:** live OTP/USB end-to-end smoke (user-driven on DEV Viettel Mysign / VGCA) — both modes + tamper-reject. dependency-map + changelog already updated.
- **Anonymous/public-form signing:** deferred — needs a public provider-discovery path + resolving the `[RequiresMfa]` constraint on remote `/sign` (anonymous would be USB-local only).

## Open questions

None blocking. Mode-2 `ServerDocHash` live path remains unexercised end-to-end (covered by 04f smoke).
