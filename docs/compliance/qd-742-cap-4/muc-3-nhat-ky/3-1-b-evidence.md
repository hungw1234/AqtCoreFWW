---
ReqID: "3.1.b"
title: "Phân loại ≥5 nhóm: (i) truy cập, (ii) đăng nhập quản trị, (iii) lỗi vận hành, (iv) quản lý tài khoản, (v) thay đổi cấu hình"
muc: 3
phase: "01"
status: MET
generated: 2026-05-10
---

# 3.1.b — Phân loại ≥5 nhóm nhật ký

**Phase:** 01 | **Mục:** 3 — Nhật ký hệ thống

## Implementation Reference
- Code: [`src/shared/AqtCoreFW.SharedKernel/Compliance/AuditCategoryAttribute.cs`](../../../../src/shared/AqtCoreFW.SharedKernel/Compliance/AuditCategoryAttribute.cs) — defines `enum AuditCategory { AppAccess, AdminLogin, Error, AccountManagement, ConfigChange }`
- Code: [`src/modules/audit/AqtCoreFW.Audit.Domain/Entities/AuditLogEntry.cs`](../../../../src/modules/audit/AqtCoreFW.Audit.Domain/Entities/AuditLogEntry.cs) — `Category` property of type `AuditCategory`

## Evidence Artifacts
- **Test:** `Qd742Phase01ComplianceTests.Compliance_3_1_b_AuditCategory_HasFiveValues` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase01ComplianceTests.cs)
- **Audit log category:** all 5
- **Screenshot path:** `screenshots/3-1-b-audit-categories.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`AuditCategory` enum defines exactly 5 values mapping to QĐ 742 §3.1.b mandatory groups:
- `AppAccess` → (i) truy cập
- `AdminLogin` → (ii) đăng nhập quản trị
- `Error` → (iii) lỗi vận hành
- `AccountManagement` → (iv) quản lý tài khoản
- `ConfigChange` → (v) thay đổi cấu hình

The architecture test `Compliance_3_1_b_AuditCategory_HasFiveValues` asserts the exact count and names via reflection, preventing accidental removal of any mandated category.

## Verification Steps
1. Run `Qd742Phase01ComplianceTests.Compliance_3_1_b_AuditCategory_HasFiveValues` — verify passes.
2. Inspect `audit.AuditLogEntries` table — confirm `Category` column populated from enum for various operations.
3. Filter audit log by each category in admin UI — verify distinct entries present.
