# User Signature Profile + Visible PDF Stamp — End-to-End Manual Smoke Runbook

> **Scope:** Live round-trip for self-service signing-cert **enrollment** (Remote Viettel Mysign +
> USB VGCA) → **admin approval gate** → **remote single-signer PAdES sign with a visible stamp** on a
> user-placed position. Proves what stubbed-provider unit/integration tests cannot: that a REAL
> possession-proof (Mysign OTP on the signer's phone / VGCA USB PIN) enrolls, that only an
> admin-**Approved** cert can sign, and that the rendered visible stamp embeds into a PDF whose
> signature still verifies (`VerifyAsync`) over its ByteRange.
>
> Closes Phase 7 of plan `260623-1950-user-signature-profiles-remote-visible-stamp`.
> Backlog row: `SIGNATURE-PROFILE-VISIBLE-STAMP`.

---

## Why manual

Automated coverage (P1–P8) verifies against **stubbed** Mysign/USB providers — it cannot exercise a real
OTP/USB private-key possession-proof, nor prove that QuestPDF raster + the PAdES appearance splice
survive a live provider signature end-to-end on the deploy runtime. The **Alpine musl render** is
verified separately (see `SIGN-PADES-STAMP-RASTER-MUSL-CI` + Phase-7 container check); this runbook is
the human-in-the-loop OTP/USB catch.

If any **valid** cell fails, capture the exact failure (enroll challenge nonce, provider response, or the
`VerifyAsync` verdict on the signed PDF) and re-open the owning phase with that evidence — do not patch
blind.

---

## Preconditions

1. **Stack up:** `/aqt-runapp --env dev` (Auth Server + API + Frontend). Vault seeded (Step 1.5).
2. **Trust store seeded:** root/intermediate CAs for Viettel UAT + VGCA present (mirrors the Forms
   digsig runbook preconditions).
3. **`TenantSignatureConfig` enabled** for the test tenant (`IsEnabled=true`, provider + OCSP mode;
   `OcspMode.Soft` acceptable for dev).
4. **CCCD established first:** the signer's `SigningNationalId` must be set BEFORE enrollment (enroll
   re-asserts CCCD == cert subject; it does not self-set from the same request — red-team C1). Set via
   self-service `PUT /api/v1/me/signing-national-id` (12-digit, locked once an active enrollment exists)
   or an admin/trusted path.
5. **Viettel UAT:** creds in user-secrets; test signer CCCD `017091004878`; a phone that receives the
   Mysign OTP.
6. **VGCA:** `VGCASignService` running locally; USB soft-token plugged + PIN available.

## Endpoints in play

| Purpose | Endpoint |
|---------|----------|
| Set CCCD (self-service) | `PUT /api/v1/me/signing-national-id` |
| Enroll — issue challenge nonce | `POST /api/v1/me/signature-cert/challenge` |
| Enroll — submit signed challenge | `POST /api/v1/me/signature-cert/enroll` (body: `Kind` = Remote/USB) |
| Signature overview (profiles + enrollments + badges) | `GET /api/v1/me/signature-overview` |
| Profile CRUD | `GET/POST /api/v1/me/signature-profiles` · `PUT /{id}` · `POST /{id}/default` · `DELETE /{id}` · `POST /{id}/images` |
| Admin — list pending | `GET /api/v1/admin/signer-cert-enrollments/pending` |
| Admin — approve / reject | `POST /api/v1/admin/signer-cert-enrollments/{publicId}/approve` · `.../reject` (reason) |
| Remote PAdES sign | `POST /api/v1/signing/request` (carries `Placement` for the visible stamp) |
| Poll async completion / confirm | `POST /api/v1/digital-signature/confirm` |

**Admin routes read tenant STRICTLY from the JWT claim** (ignore `X-Tenant-Id` header — red-team H4).

## Evidence to inspect

Enrollment lifecycle + approval:

```sql
SELECT TOP 10 PublicId, UserId, Kind, ApprovalStatus, IsActive,
       ApprovedBy, ApprovedAt, RejectionReason, CertSerial, CertIssuer
FROM   identity.SignerCertEnrollments
ORDER  BY Id DESC;
```

A **self-service enroll** ⇒ `ApprovalStatus=Pending`, `IsActive=1`. **Admin approve** ⇒ `Approved` +
`ApprovedBy/At`. **Reject** ⇒ `Rejected` + `RejectionReason`. Only `IsActive=1 && Approved` grants the
enrolled signing tier.

Signed PAdES transaction (deterministic key — Phase 8):

```sql
SELECT TOP 5 TransactionId, TenantId, UserId, Status, PadesSignedStorageKey
FROM   identity.SigningPendingTransactions
WHERE  PadesPlaceholderStorageKey IS NOT NULL
ORDER  BY InitiatedAt DESC;
```

A completed remote PAdES sign ⇒ `Status=Completed`, `PadesSignedStorageKey = {TenantId}/{TransactionId}.signed.pdf`
(deterministic — two poll replicas collapse to one blob). Download the signed PDF and confirm the
visible stamp renders at the placed position AND the signature validates in Adobe Reader.

---

## Matrix — run every cell, record the outcome

For **each provider ∈ {`remote-viettel-mysign`, `usb-vgca`}**:

- [ ] **Enroll — valid possession-proof**
  `challenge` → sign the nonce (Remote = Mysign SignHash forcing sync-mode → OTP on the signer's phone;
  USB = `vgca_sign_msg` over the nonce) → `enroll` with the matching `Kind`.
  **Expect:** `200`; new `SignerCertEnrollment` row, `ApprovalStatus=Pending`, CCCD in cert subject ==
  `SigningNationalId`, serial/issuer captured from the challenge result.

- [ ] **Enroll — wrong nonce / cross-user nonce / mismatched CCCD**
  Submit a signed challenge for a foreign/expired nonce, or a cert whose subject CCCD ≠ the caller's
  `SigningNationalId`.
  **Expect:** `4xx`; **NO enrollment row** created.

- [ ] **Pending blocks sign**
  With the enrollment still `Pending` (not yet approved), attempt `POST /signing/request`.
  **Expect:** reject — binding-guard `NotBound` (Pending/Rejected certs cannot sign).

- [ ] **Admin approve → sign with visible stamp (ảnh ký thường)**
  Admin `approve` the pending enrollment. Then remote-sign a clean single-page PDF with a placement +
  the profile's **normal** signature image.
  **Expect:** sign succeeds; downloaded PDF shows the stamp (image + name + time + reason + serial + CA)
  at the placed position; `VerifyAsync` / Adobe = valid over ByteRange.

- [ ] **Sign with visible stamp (ảnh ký nháy)**
  Repeat using the profile's **initials** image.
  **Expect:** as above with the initials image.

- [ ] **Tamper after sign**
  Flip one byte of the signed PDF's covered range.
  **Expect:** `VerifyAsync` / Adobe = **invalid**.

- [ ] **Admin reject**
  Reject a fresh pending enrollment (reason required) → attempt sign with that cert.
  **Expect:** reject (`NotBound`); `RejectionReason` persisted.

- [ ] **Revoke-between-initiate-and-completion (C5, async only)**
  Initiate a remote async sign with an Approved cert, then admin-revoke/reject BEFORE the poll completes.
  **Expect:** completion **fails terminally, no signed blob** stored.

Then once (provider-agnostic):

- [ ] **Regression — invisible path unchanged**
  Remote-sign with NO placement (or malformed placement) → PDF signs **invisibly** (backward-compat);
  USB DocFlow sign-path unchanged; bootstrap signer (zero enrollment rows) still signs.

### Results log

> _Pending first execution — record per-cell HTTP / evidence values below when run on DEV with real
> Viettel OTP + VGCA USB._

| Provider | Cell | OTP/PIN outcome | HTTP | Row / state | Stamp visible + valid? | Notes |
|----------|------|-----------------|------|-------------|------------------------|-------|
| viettel | enroll valid | | | | | |
| viettel | enroll bad-nonce | | | | | |
| viettel | pending-blocks | | | | | |
| viettel | approve→sign normal | | | | | |
| viettel | sign initials | | | | | |
| viettel | tamper | | | | | |
| viettel | reject | | | | | |
| viettel | revoke-mid-complete | | | | | |
| vgca | enroll valid | | | | | |
| vgca | enroll bad-nonce | | | | | |
| vgca | pending-blocks | | | | | |
| vgca | approve→sign normal | | | | | |
| vgca | sign initials | | | | | |
| vgca | tamper | | | | | |
| vgca | reject | | | | | |
| — | invisible regression | n/a | | | | |

---

## Known flake / fallbacks

- **Mysign sync-mode OTP timeout (~120s):** the enroll challenge sign forces sync-mode to block for the
  phone OTP as possession-proof; retry on timeout.
- **VGCASignService down / OCSP unreachable:** keep `OcspMode.Soft` for dev (pass-warn).
- **QuestPDF raster on musl:** verified out-of-band in the Alpine container check
  (`SIGN-PADES-STAMP-RASTER-MUSL-CI`); a local (glibc) render passing does NOT prove musl.
- **Multi-signer is OUT of scope** (v1 single-signer only — `MULTI-SIGNER-VISIBLE-STAMP` deferred).

## Success criteria (Phase 7 closure)

- [ ] `docs/dependency-map.md` reflects the signature entities/enrollment columns/enums/nonce/endpoints/
      ports/authz/binding-guard.
- [ ] Alpine musl container renders the QuestPDF stamp + the rasterizer test passes on `linux-musl-x64`.
- [ ] All enroll + sign matrix cells + the invisible-regression pass on DEV with real OTP/USB
      (**operator-run — mark USER-CONFIRMED when done**).
- [ ] Live OTP/USB round-trip proves enroll possession-proof + visible-stamp render + PAdES verify
      end-to-end.
