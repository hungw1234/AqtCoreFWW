# QĐ 742 Cấp 4 Plan — Decisions Log

**Last updated:** 2026-05-11 23:59 ICT
**Status:** Plan APPROVED, Phase-05b SHIPPED 2026-05-11

---

## User Decisions (Locked)

### D1 — Digital Signature Architecture (Mục 5.1)
**User decision (verbatim):**
> "Chữ ký số chỉ làm tích hợp với các nhà cung cấp chữ ký số của Việt Nam (Remote Signing, USB Token) như Ban Cơ Yếu chính phủ, Viettel, MiSA..v.v. Không tự làm 1 hệ thống ký số độc lập."

**Implementation impact:**
- Phase 5b: VN CA provider integration only — no self-built PKI
- Architecture: `IDigitalSignatureProvider` abstract + `RemoteSigningProvider` + `UsbTokenSignatureProvider`
- BouncyCastle for cert chain validation (verify side only)
- Multi-vendor support via strategy pattern (per-tenant config)

### D2 — Plan Approval Path
User chose **Hướng A** (approve plan v2 với 6 NC defaults, defer implementation). Plan locked, ready-to-start when stakeholder confirms cấp độ deploy + budget.

---

## NEEDS_CONFIRM Defaults Accepted

All 6 items locked với defaults below. Re-verify trước khi start respective phase nếu cần.

### NC-1 — Severity Mapping (Phase 01)
| Severity | Triggers |
|----------|----------|
| **CRITICAL** | AccessDenied repeated; LOGIN_FAIL >5/window; RoleRevoked-active session; sensitive RTBF |
| **WARNING** | DELETE; ROLE_ASSIGN/REVOKE; MFA-disabled; PERMISSION_GRANTED to high-tier |
| **INFO** | CREATE; UPDATE; READ; LOGIN success; LOGOUT |
| Fallback | INFO + warning log for unknown actions |

**Re-verify trigger:** Compliance officer review trước Phase 01 implement.

### NC-2 — First-Login Production State (Phase 02)
**Default assumption:** Có users đang dùng password mặc định → grandfather migration with 30-day grace.
**Re-verify trigger:** Verify production data trước Phase 02; nếu KHÔNG có default-pwd users → simplify to immediate force-change.

### NC-3 — DB-Level Audit Immutability (Phase 01)
**Default:** BOTH defenses enabled
- Service-layer guard (existing in `IAuditLogRepository`)
- SQL trigger NO UPDATE/DELETE (defense-in-depth)

**Re-verify trigger:** DBA validation trong deployment dry-run rằng trigger không phá Hangfire purge job hoặc EF migrations.

### NC-4 — Lockout Escalation Threshold (Phase 02)
**Default:** 3 lockout cycles within 24h → auto-disable account, admin manual unlock.
**Re-verify trigger:** Business confirmation về threshold + escalation contact (ai unlock).

### NC-5 — MFA Grace Period (Phase 03)
**Default:** 7 days new user enrollment; 0 days SystemAdmin (must enrol pre-policy-enable).
**Re-verify trigger:** Auditor có thể yêu cầu shorter grace; adjust nếu compliance review feedback.

### NC-6 — Compliance Dossier Location (Phase 07)
**Default:** `docs/compliance/qd-742-cap-4/` (gitignore screenshots subdirectory chỉ, master files committed).
**Re-verify trigger:** Compliance officer confirm gitignore strategy hoặc require separate private repo.

---

## DEFERRED Decisions (Phase 5b Vendor Selection)

To decide ~3 weeks before Phase 5b execute (allows fresh vendor info):

| Decision | Options | Notes |
|----------|---------|-------|
| Remote Signing provider | MiSA eSign / VNPT SmartCA / Viettel-CA | Pick most accessible REST API + pricing |
| USB Token Web SDK | VGCA SignSerSDK (Ban Cơ Yếu) / Viettel WebPlugin | VGCA gov-friendly nếu deploy cho cơ quan nhà nước |
| OCSP behavior | soft-fail (continue if OCSP timeout) / hard-fail (block sign) | Trade-off availability vs security strictness |

---

## Decision Approval Trail

| ID | Decision | Approver | Date | Notes |
|----|----------|----------|------|-------|
| D1 | Digital signature architecture | thien (user) | 2026-04-28 | Anti-pattern self-built PKI rejected |
| D2 | Approve plan v2 + defer impl | thien (user) | 2026-04-28 | Hướng A chosen |
| NC-1..6 | Sane defaults accepted | thien (user) | 2026-04-28 | Re-verifiable per phase |
| D19 | Phase-05b Contract Refactor + VGCA 3-tier + Viettel 3-mode Ship | auto (cook --auto) | 2026-05-11 | 10 commits W0-W9 + FIX-A/B/C/D. VGCA USB token (Tier-1 raw-hash, Tier-2 XMLDSig, Tier-3 docflow PDF). Viettel Mysign 3-mode (async=0/2/3). Code-review 8.4/10 APPROVED. NEAC root seed + Viettel credentials + Vault integration deferred post-ship. See docs/signing/spec-amendments.md |

---

## Pending Decisions (Future)

1. **Cấp độ deploy confirmation** — chủ quản hệ thống phải confirm AqtCoreFW deploy cho hệ thống cấp 4 trước khi start implementation. Cấp 4 = "tổn hại đặc biệt nghiêm trọng tới quốc phòng/an ninh" theo NĐ 85/2016 — chỉ áp dụng cho hệ thống quan trọng quốc gia.

2. **Budget + timeline allocation** — 7-10 weeks (1 dev) hoặc 3.5-5 weeks (2 dev). Stakeholder sign-off needed.

3. **Audit organization selection** — đánh giá ATTT cấp 4 cần tổ chức được Bộ TTTT cấp phép. Engage early (Phase 0 / Phase 07).

4. **Compliance officer review** of Severity mapping table (NC-1) trước Phase 01.

5. **DBA review** of SQL trigger immutability (NC-3) trước Phase 01 deployment.
