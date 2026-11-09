# QĐ 742 Cấp 4 Plan — Red-Team Findings (Round 1)

**Reviewer:** Adversarial agent (brainstormer)
**Date:** 2026-04-28
**Verdict:** APPROVE WITH MANDATORY AMENDMENTS (Severity HIGH)
**Status:** All findings addressed — see [`decisions-log.md`](./decisions-log.md) for resolutions.

---

## P0 CRITICAL (3 items — all FIXED)

### P0-1: Mục 5.1 Chữ ký số không có phase cover ✅ FIXED
**Issue:** Initial plan dismiss 5.1 thành "TLS OUT-OF-CODE". Source of truth nói rõ "sử dụng chữ ký số" — auditor literal interpretation thì TLS không thỏa.
**Impact:** Audit fail mục 5.1, ~1 tháng remediation post-audit, reputational risk customer government.
**Resolution:** Phase 5b NEW created — VN CA provider integration (Remote Signing + USB Token Web SDK), 8-10d effort. Per stakeholder decision: integrate vendors VN (MiSA/VNPT/Viettel/VGCA), KHÔNG self-built PKI.

### P0-2: Phase 01 SQL trigger vs migration backfill order bug ✅ FIXED
**Issue:** Single migration adding immutability trigger BEFORE backfilling AuditCategory + Severity columns → trigger blocks UPDATE → backfill crashes.
**Resolution:** Migration split into 2 files — M1 (`AddAuditCategoryAndSeverity_Backfill`) sets columns nullable + backfills data; M2 (`EnforceAuditImmutability_Trigger`) sets NOT NULL + adds trigger. Sequential deploy với smoke test giữa M1 và M2.

### P0-3: Phase 04 ↔ Phase 06 dependency mislabeled "parallel" ✅ FIXED
**Issue:** Phase 04 PII encryption uses `EncryptedStringConverter` requiring production key. If Phase 04 ships before Phase 06 with dev fallback key → mass re-encrypt sau Phase 06 = data corruption + audit fail 4.4.a (hardcoded credential).
**Resolution:** Phase 04 frontmatter `depends_on: phase-06`. Phase 06 promoted P0 with `blocks: phase-04`. Sequential gate strict — no parallel execution.

---

## P1 HIGH (5 items — all FIXED)

### P1-1: MFA SystemAdmin self-lockout (no break-glass) ✅ FIXED
**Resolution (Phase 03):**
- Mandatory ≥2 SystemAdmin enrollment guard before enabling mandatory MFA policy
- 16 BCrypt-hashed recovery codes (industry standard, was 10)
- 2-admin emergency disable endpoint với peer review approval
- Documented break-glass procedure: physical OS-level admin access + DB direct UPDATE (logged + alerted)

### P1-2: EmailHash unsalted SHA-256 (rainbow table risk) ✅ FIXED
**Resolution (Phase 04):** Replace with HMAC-SHA-256 + per-tenant pepper from Vault. Pepper rotation yearly + audit trail. Deterministic for search but rainbow table infeasible.

### P1-3: AuditCategory inference fragile (naming convention) ✅ FIXED
**Resolution (Phase 01):** Replace auto-mapping với explicit `[AuditCategory(AuditCategory.AccountManagement)]` attribute on command class. Default `AppAccess` for unattributed (safe + warning log). Migration scans all `IBaseCommand` classes.

### P1-4: First-login mass-lockout production risk ✅ FIXED
**Resolution (Phase 02):**
- Grandfather migration: existing active users get 30-day grace period
- Email warnings T-7, T-1 days before deadline
- Feature flag `Auth:RequireFirstLoginChange` (default OFF prod, ON staging first)
- New users: immediate force-change on first login

### P1-5: Phase 04 effort underestimate ✅ FIXED
**Resolution:** Updated 5-7d (was 3-4d). Justification: PII migration script + decrypt-on-read backward compat + per-tenant pepper Vault integration.

---

## Compliance Coverage Audit Result

- **40/41 ReqID có phase cover** initially → **41/41 after Phase 5b added**
- **All 4 cấp-4-specific items** (1.6.a/b, 6.2.a/b) covered ✅
- **Mục 5.1** previously not covered → now Phase 5b ✅

---

## Effort Reality Check

| Component | Initial | Red-team adjusted |
|-----------|---------|-------------------|
| Phase 04 | 3-4d | 5-7d |
| Phase 06 | (P1) | (P0 promoted, 4-6d) |
| Phase 5b | (missing) | 8-10d (NEW) |
| Phase 07 | 2-3d | 3-5d |
| **Total** | 22-34d | **31-47d** (+40% buffer) |

Initial plan was optimistic. Realistic 7-10 weeks (1 dev) reflects compliance prep + code review + audit iteration overhead.

---

## Top 5 Audit Fail Scenarios

| # | Auditor Test | Pre-amendment | Post-amendment |
|---|--------------|---------------|----------------|
| 1 | "Show chữ ký số transit" | ❌ FAIL (TLS only) | ✅ PASS (Phase 5b) |
| 2 | Sample 10 audit log rows for category match | ❌ FAIL (mis-categorized) | ✅ PASS (explicit attribute) |
| 3 | Demo IP whitelist UI | ✅ PASS | ✅ PASS |
| 4 | Request 10 MFA recovery codes | ⚠️ PARTIAL | ✅ PASS (16 codes) |
| 5 | Encryption key source | ❌ FAIL if Phase 06 not done | ✅ PASS (gated) |

---

## Lessons Learned

1. **Read primary legal text first** — web summaries misinterpreted 4 items as cấp 4 when source-of-truth shows them as cấp 5 only. Saved ~5-7 days of unnecessary work.
2. **Red-team rounds are high-ROI** — 3 P0 + 5 P1 caught before implementation, prevented audit fail + production migration disasters.
3. **Sequential gates beat parallel** when one phase produces input for another — Phase 06→04 dependency gate prevented re-encrypt cycle.
4. **Vendor-specific decisions defer** — Phase 5b vendor choices (MiSA/VNPT/Viettel/VGCA) deferred to ~3 weeks before execute, allows fresh vendor info + pricing.
