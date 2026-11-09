# Forms DigitalSignature — End-to-End Manual Smoke Runbook

> **Scope:** Live verify-at-ingest round-trip for the Forms `DigitalSignature` field type (QĐ-742).
> Proves what stubbed-`IIdentitySigningClient` unit/integration tests cannot: that a REAL provider
> signature (Viettel Mysign OTP / VGCA USB) survives FE↔BE canonicalization + server verify, and that
> a tampered payload is hard-blocked (400, no persist).
> **This is the only check that catches a live canonicalization / document-render determinism drift.**
>
> Closes Phase 4 of plan `260612-1023-signing-dev-enablement-viettel-vgca` (sub-phase 04f).
> Backlog row: `FORMS-DIGSIG-FE-REMAINING`.

---

## Why manual

Automated coverage (04a–04e) verifies against a **stubbed** signing client — it cannot exercise a real
OTP/USB private-key signature, nor prove the FE-computed canonical bytes are byte-identical to what the
BE re-derives on a live submission. A 1-byte canonicalization drift → 100% false-reject, invisible to
the fixed-vector tests if both sides drift the same way. This runbook is the catch.

If any cell fails on a **valid** signature, capture the exact mismatching bytes (FE canonical payload vs
BE re-derived payload) and re-open `04c` (Mode-1 canonicalizer) / `04d` (Mode-2 doc-hash) / `04e` (FE
canonicalizer) with that evidence — do not patch blind.

---

## Preconditions

1. **Stack up:** `/aqt-runapp --env dev` (Auth Server + API + Frontend). Vault seeded (Step 1.5).
2. **Trust store seeded** (plan Phase 1): root/intermediate CAs for Viettel UAT + VGCA in the trust store.
3. **`TenantSignatureConfig` enabled** for the test tenant (plan runbook H3 — manual `POST` per dev-DB;
   set `IsEnabled=true` and the provider's OCSP mode; `OcspMode.Soft` is acceptable for dev = pass-warn
   if the OCSP responder is unreachable).
4. **Viettel UAT:** creds in user-secrets; test signer CCCD `017091004878`.
5. **VGCA:** `VGCASignService` running locally; USB soft-token plugged + PIN available.

## Endpoints in play

| Purpose | Endpoint |
|---------|----------|
| Authenticated submit | `POST /api/v1/forms/{formPublicId}/submissions` |
| Public submit | `POST /api/v1/public/forms/{code}/submit` |
| Mode-2 doc-hash fetch | `POST /api/v1/forms/{formTemplatePublicId}/signature-doc-hash` (auth) · `POST /api/v1/public/forms/{code}/signature-doc-hash` (anon) |

The submit body carries an **optional** `signature` object (additive, backward-compatible):
`{ signatureBase64, signerCertBase64?, format, hashAlg, encode, providerKey }`.

## Evidence to inspect (after each valid submit)

Query the new evidence columns:

```sql
SELECT TOP 5 Id, SignatureVerdict, SignerSubject, SignerSerial,
       SignatureVerifiedAt, SignerCertDer IS NOT NULL AS HasCertDer,
       LEN(SignatureChainEvidence) AS ChainEvidenceLen
FROM   forms.FormSubmissions
ORDER  BY Id DESC;
```

A **valid** signed submission ⇒ `SignatureVerdict=1`, `SignerSubject`/`SignerSerial` populated,
`HasCertDer=1`, `ChainEvidenceLen > 0`. A tampered submission ⇒ **no new row at all** (hard-block 400
before persist). A plain (unsigned) submission ⇒ row present, all 6 evidence columns NULL.

---

## Matrix — run every cell, record the outcome

For **each provider ∈ {`remote-viettel-mysign`, `usb-vgca`}**:

- [ ] **Mode 1 (CanonicalJson) — valid**
  Build a form with a required `DigitalSignature` field, mode = CanonicalJson. Fill data → sign (Viettel
  OTP / VGCA USB PIN) → submit.
  **Expect:** `200`; new `forms.FormSubmissions` row; `SignatureVerdict=1`, signer fields + chain evidence
  + cert DER populated.

- [ ] **Mode 1 — tamper**
  Intercept the submit body, flip one byte of a **covered** field value AFTER signing → submit.
  **Expect:** `400 ValidationError` ("xác minh chữ ký thất bại"); **NO new row** in `forms.FormSubmissions`.

- [ ] **Mode 2 (ServerDocHash) — valid**
  Form field mode = ServerDocHash. FE fetches the doc-hash via `…/signature-doc-hash`, sign the hash,
  submit.
  **Expect:** `200`; evidence persisted as in Mode-1 valid.

- [ ] **Mode 2 — tamper**
  Edit a covered field AFTER signing (so the signed hash is now stale) → submit.
  **Expect:** `400`; **no row**.

Then once (provider-agnostic):

- [ ] **No-signature regression**
  Submit a plain form with NO `DigitalSignature` field.
  **Expect:** `200`, behaves exactly as before; all 6 evidence columns NULL.

### Results log

> **2026-06-14 — executed by user; feature accepted.** All 8 matrix cells + no-sig regression run on DEV with real Viettel OTP + VGCA USB; no canonicalization/render determinism drift surfaced. Per-cell HTTP/evidence values not transcribed into the table below (operator-run, not logged here).

| Provider | Mode | Case | OTP/PIN outcome | HTTP | Row present? | Evidence snapshot | Notes |
|----------|------|------|-----------------|------|--------------|-------------------|-------|
| viettel | 1 | valid | | | | | |
| viettel | 1 | tamper | | | | | |
| viettel | 2 | valid | | | | | |
| viettel | 2 | tamper | | | | | |
| vgca | 1 | valid | | | | | |
| vgca | 1 | tamper | | | | | |
| vgca | 2 | valid | | | | | |
| vgca | 2 | tamper | | | | | |
| — | — | no-sig regression | n/a | | | | |

---

## Known flake / fallbacks

- **VGCASignService down / OCSP responder unreachable:** runbook precondition #3 — keep `OcspMode.Soft`
  for dev (pass-warn); a hard OCSP failure on Soft mode should NOT fail an otherwise-valid signature.
- **Viettel UAT OTP flakiness:** retry; this runbook targets sync sign mode (async-callback is
  out-of-scope/deferred per plan).

## Success criteria (Phase 4 closure)

- [ ] `docs/dependency-map.md` reflects the new `IIdentitySigningClient` edge + the FormSubmission
      evidence columns + the `signature-doc-hash` endpoint — **DONE 2026-06-13** (docs portion of 04f).
- [x] All 8 matrix cells + the regression pass on DEV (valid → persist + evidence; tamper → 400, no
      persist) — **DONE 2026-06-14, executed by user** (per-cell values not transcribed; feature accepted).
- [x] Live OTP/USB round-trip proves canonicalization + render determinism end-to-end — **DONE 2026-06-14** (user-reported, no drift).
