---
title: Signature Enrollment Pivot — Over-engineer v1 → YAGNI v2 (2.5d)
date: 2026-04-22 09:07-10:00
author: ThienND
tags: [session, brainstorm, red-team, planning, scope-pivot, digital-signature, enrollment]
type: session
status: completed
---

# Signature Enrollment Pivot — Over-engineer v1 → YAGNI v2

**Date**: 2026-04-22 09:07-10:00  
**Severity**: Medium (scope reduction, not blocker)  
**Component**: Identity module, digital signature enrollment  
**Status**: Completed — plan v2 active, phase 1 ready to implement

---

## Tóm tắt

Session brainstorm → red-team v1 phát hiện 5 CRITICAL (phantom enum, 3 security breach, 12d effort) → user YAGNI pivot từ ceremony → enrollment-only → cắt scope 8d → 2.5d. Plan v1 cancel, plan v2 go-with-fixes. Red-team 6-8× ROI validated.

---

## Timeline & Key Decisions

### 09:07-09:43 — Brainstorm Hybrid H (over-engineered)
- User scout `/signatures` → module Signature độc lập (0 consumer).
- Phát hiện: ký số phải đi với nghiệp vụ, không standalone.
- 3 phương án A/B/H trình bày; user chọn H (hybrid flow).
- **7 decisions**: SignatureRequest aggregate bare-bone + drop SignatureResult/ValidationLog + embed evidence vào FileVersion + polymorphic ContextType/ContextId + SignerGroup Seq+Par + Outbox events + drop clean schema cũ + admin-only /signatures.
- **Flow 6 bước**: create case → attach → chuyên viên ký → lead phân công 2 người → 2 người ký parallel → lead approve.
- **Artifact**: `plans/reports/brainstorm-260422-0943-cases-signature-integration-hybrid.md`

### 09:43-09:50 — Planner v1 (6 phase, 8d) + 2 researcher parallel
- Mode: `--deep`. Spawn 2 researcher:
  - `researcher-260422-0943-vn-pki-cert-handling.md` — Remote signing pattern B, iText 8 + BouncyCastle, MISA/VNPT
  - `researcher-260422-0943-ocsp-ltv-ceremony-ddd.md` — OCSP direct + CRL fallback, PAdES-T phase 1, DDD aggregate
- **6 phase**: Signature slim → Files evidence → Cases attachment → Workflow Sign task → FE tab+inbox+admin → Migration+tests.
- Plan directory: `plans/260422-0943-cases-signature-integration-hybrid/` (soft-block với 260415-1737-cases-workflow-refactor).

### 09:50-09:55 — Red-team v1 NO-GO (5 CRITICAL + 7 HIGH + 4 MED + 2 LOW)
**Report**: `plans/reports/red-team-260422-0943-cases-signature-integration.md`

**CRITICAL findings**:
- **F01**: `WorkflowTaskType` enum KHÔNG tồn tại → toàn bộ phase P4 xây trên phantom. Nguyên nhân: planner tin brainstorm report không scout lại.
- **F02-F04**: Remote signing 3 tầng lỏng — cert không bind JWT (impersonation), skip RSA.Verify (ký mù), OCSP response từ body blindly trusted (forged revocation). Govt security breach nếu ship.
- **F05**: Effort 8d → realistic 12-14d (OCSP validator stub, 0 iText/BouncyCastle implementation, 27 file ref dropped entities).

**Plus HIGH/MED/LOW**: tenant filter, unique constraint, EF migration snapshot, admin cross-tenant, mid-migration rollback.

### 09:55-09:58 — User YAGNI Pivot
User nhận ra over-engineer:
> "Mục tiêu nghiệp vụ hiện tại chỉ cần người dùng **đăng ký chữ ký số** (USB Token hoặc Remote signing). Còn sau này khi vào nghiệp vụ cần ký thì sẽ implement ký thực tế."

**Scope reset**:
- **Drop**: ceremony, OCSP validation, iText/BouncyCastle, LTV, remote ceremony.
- **Keep**: User Certificate Registry (enrollment-only).
- **Module**: Identity (drop Signature module).
- **USB flow**: browser plugin/extension (MISA/VNPT/Viettel/FPT provided).
- **Remote flow**: store CAProvider + RemoteAccountRef only.
- **No OCSP** at enrollment (just parse cert + store validity window).

### 09:58-09:59 — Plan v1 Cancelled + Planner v2 Scout-First
- Plan v1 frontmatter: `status: cancelled`, `cancelReason`, `supersededBy: 260422-1000-user-digital-signature-enrollment`.
- Gỡ bidirectional block với 260415.
- **Planner v2 BẮT BUỘC scout trước viết** → zero phantom lần 2.
- **Scout findings** (D-01 to D-05):
  - D-01: Entity convention (Workflow pattern vì Identity siblings là POCO)
  - D-02: Tenant filter repo-layer guardrail
  - D-03: ResolveUserId() divergence
  - D-04: No OCSP enforcement
  - D-05: PFX rejection (binary parse issue)
  - P1: Orphan schema cleanup trong AuditDbContext

**3 phase 2.5d**:
- **P1 (0.5d)**: Drop legacy Signature module — 4 BE project + 3 test project + FE signatures feature + 6 file contrib
- **P2 (1.0d)**: UserDigitalSignature entity + X509CertificateParser + 4 endpoint
- **P3 (1.0d)**: FE profile tab + plugin-bridge abstraction 4 provider

### 09:59-10:00 — Red-team v2 GO-WITH-FIXES
**Report**: `plans/reports/red-team-260422-1000-enrollment-light.md`

**0 CRITICAL** (huge improvement từ v1).
- **3 HIGH**: F1 cert fields non-null + RegisterRemote no cert → compile error, F2 PFX magic-byte 30 82 cũng là DER X.509, F3 dep-map lines missed.
- **4 MED**: F4 Consent precedent sai, F5 AuditDbContext path wrong, F6 unique constraint handler missing, F7/F8 admin cross-tenant underspec.
- **2 LOW**: F9 phase-03 tight, F10 no mid-migration rollback.

**10 patches** applied vào 3 plan files. Verified bằng Grep markers.

---

## Key Lessons Learned

### 1. Red-team 6-8× ROI — empirically validated
Không red-team v1 → 12 ngày coding trên phantom foundation + ship 3 security CRITICAL. Red-team cost: ~8 phút. Saving: ~12 ngày. **Memory feedback đúng.**

### 2. Scout-first trong planner bắt buộc
- v1 planner tin brainstorm report → phantom `WorkflowTaskType` enum.
- v2 planner scout 8 path TRƯỚC viết → zero phantom.
- **Bài học**: brainstorm report = signal, không source-of-truth. Scout = verify facts (enum, entity, endpoint, DI).

### 3. YAGNI pivot qua red-team feedback
User nhận ra over-engineer ngay khi thấy 18 findings. Red-team **brutal honest** cho phép user quyết định đúng. Scope 8d → 2.5d (70% cut).

### 4. Bidirectional plan dependency tracking
- Plan v1 `blocks`/`blockedBy` cross-plan mechanics quan trọng.
- Plan v1 cancel → gỡ block từ 260415-1737 automatically.
- **Frontmatter**: `supersededBy` field for cancellation audit trail.

### 5. "/signatures" scope clarification
Tôi ban đầu hiểu là cross-module client interfaces (I*ModuleClient). User làm rõ: feature digital signature ở FE route `/signatures`. **Luôn verify scope qua URL/screenshot trước deep-dive.**

---

## Next Steps

1. **Implement phase 1** (0.5d): Drop legacy Signature module, orphan schema cleanup.
2. **Implement phase 2** (1.0d): UserDigitalSignature entity, X509 parser, 4 endpoint.
3. **Implement phase 3** (1.0d): FE profile tab, plugin-bridge 4 provider.
4. **Red-team v3 + ship** (mid-late week).
5. Update roadmap v2.128 + changelog after phase 1 ship.

---

## Artifacts

- Plan v1 (cancelled): `plans/260422-0943-cases-signature-integration-hybrid/`
- Plan v2 (active): `plans/260422-1000-user-digital-signature-enrollment/`
- Brainstorm report: `plans/reports/brainstorm-260422-0943-cases-signature-integration-hybrid.md`
- Red-team v1: `plans/reports/red-team-260422-0943-cases-signature-integration.md`
- Red-team v2: `plans/reports/red-team-260422-1000-enrollment-light.md`
- Researcher reports: vn-pki-cert-handling, ocsp-ltv-ceremony-ddd (vào reports/)

---

**Status:** DONE  
**Summary**: Brainstorm → red-team v1 5-CRITICAL pivot → YAGNI enrollment-only → plan v2 2.5d. Scout-first planner zero-phantom. Ready for phase 1 implementation.
