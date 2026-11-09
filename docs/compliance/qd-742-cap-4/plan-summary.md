# QĐ 742 Cấp 4 ATTT — Implementation Plan Summary

**Status:** APPROVED (2026-04-28), implementation deferred
**Plan dir (working artifacts, gitignored):** `plans/260428-1740-cap-4-qd-742-compliance/`
**Source of truth:** [`official-requirements.md`](./official-requirements.md)

---

## Goal

Đưa AqtCoreFW pass audit QĐ 742/QĐ-BTTTT cấp 4. End-state: 41 yêu cầu cấp 4 → tất cả MET với evidence (test + screenshot) ready cho audit. Tại thời điểm approve plan: 76% solid (54% MET + 22% PARTIAL).

---

## Scope

**IN — 41 mục cấp 4 cộng dồn:**
- Mục 1 Xác thực: 17 items
- Mục 2 Kiểm soát truy cập: 9 items
- Mục 3 Nhật ký: 5 items
- Mục 4 An toàn ứng dụng: 5 items
- Mục 5 Bảo mật liên lạc: 1 item (chữ ký số)
- Mục 6 Sao lưu: 4 items

**OUT (chỉ cấp 5, không implement):** 2.5 (tách cổng), 2.6 (time-window), 3.3 (audit log RBAC nhóm), 6.3 (backup tập trung).

**5 Cấp-4-specific deltas (so với cấp 3):**
1. 1.6.a UI quản lý chính sách MFA
2. 1.6.b MFA enforcement attribute
3. 5.1 Chữ ký số (VN CA integration only)
4. 6.2.a UI quản lý classification
5. 6.2.b Apply encryption rộng theo classification

---

## Phase Summary

| # | Title | Effort | Priority | Depends |
|---|-------|--------|----------|---------|
| 01 | Audit Log Baseline (5-nhóm category, Severity, retention, immutability) | 5-7d | P0 | — |
| 02 | Auth Lifecycle (first-login, lockout admin UI, MFA enforcement) | 3-5d | P0 | 01 |
| 03 | MFA Policy UI (cấp-4 NEW) | 4-6d | P0 | 02 |
| 04 | Data Classification (cấp-4 NEW) | 5-7d | P0 | **06** |
| 05 | Supporting UIs (IP whitelist + audit policy + backup policy) | 4-6d | P1 | 01 |
| 05b | Digital Signature VN CA Integration (cấp-4 NEW) | 8-10d | P0 | — |
| 06 | Vault Phase 02 Finish (no hardcode credential) | 4-6d | P0 | — |
| 07 | Verification + Audit Prep (evidence dossier) | 3-5d | P0 | All |
| **TOTAL** | | **36-51d** | | |

**Effort:** 1 dev FT = 7-10 weeks; 2 dev parallel = 3.5-5 weeks.

---

## Architecture Highlights

**Phase 5b Digital Signature** (per stakeholder decision — VN CA integration only, no self-built PKI):
- Abstract interface `IDigitalSignatureProvider`
- Implementations: `RemoteSigningProvider` (REST API CA: MiSA/VNPT/Viettel) + `UsbTokenSignatureProvider` (BE verify side cho FE Web SDK: VGCA/Viettel)
- Per-tenant config via `TenantSignatureConfig`
- BouncyCastle for cert chain validation, OCSP/CRL lookup

> **2026-05-01 PIVOT:** In-process Signature module (`AqtCoreFW.Signature.*`) **REMOVED** — see [`plans/260501-1007-delete-signature-module/`](../../../plans/260501-1007-delete-signature-module/). Phase 5b execution now scopes EXTERNAL CA integration only (adapter in Files module or new thin Integration.Signing module). No in-process PKI/HSM/OCSP infrastructure to build. Effort estimate may shrink during execution. Files module ND68 stub (`StubDigitalSignatureService`) preserved as integration seam.

**Phase 04 Data Classification** key changes:
- Apply `EncryptedStringConverter` (AES-256-GCM) to ApplicationUser PII (Email, Phone, UserName, NationalId)
- Apply to RtbfRequest.CitizenNationalId (P0 high-risk plaintext)
- HMAC-SHA-256 với per-tenant pepper từ Vault for searchable hashes

**Phase 01 Audit Refactor:**
- Explicit `[AuditCategory]` attribute pattern (replace fragile naming convention)
- 5 categories: AppAccess, AdminLogin, Error, AccountManagement, ConfigChange
- Add `Severity` column (CRITICAL/WARNING/INFO)
- Two-step migration: M1 backfill → M2 immutability trigger

---

## Cross-Cutting Concerns

- **Testing:** Mỗi ReqID → ≥1 test as audit evidence
- **Rollout:** All phases backward-compat, encryption migration với decrypt-on-read fallback
- **Compliance evidence:** Mỗi phase output → `docs/compliance/qd-742-cap-4/phase-XX/` (test outputs, screenshots, DDL)
- **Backward compat:** No breaking API changes; old endpoints không touch

---

## Success Criteria

1. 41/41 ReqID cấp 4 → MET với measurable evidence
2. Audit dossier complete trong `docs/compliance/qd-742-cap-4/`
3. ≥1 automated test per ReqID
4. AuditCategory enum match đủ 5 nhóm QĐ 742
5. All admin endpoints có `[RequiresMfa]` policy
6. Phase 06 Vault production-ready trước Phase 04 ship
7. 1 Remote Signing provider + 1 USB Token provider working E2E

---

## Resume

When ready to start implementation:
```
/ck:cook plans/260428-1740-cap-4-qd-742-compliance/phase-01-audit-log-baseline.md --tdd
```

Phase 01 has no external dependency, can start immediately.
