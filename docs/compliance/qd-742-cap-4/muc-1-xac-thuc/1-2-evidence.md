---
ReqID: "1.2"
title: "Lưu trữ có mã hóa thông tin xác thực hệ thống (SHA-256/512/3 hoặc tương đương)"
muc: 1
phase: "06"
status: MET
generated: 2026-05-10
---

# 1.2 — Lưu trữ có mã hóa thông tin xác thực hệ thống

**Phase:** 06 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/shared/AqtCoreFW.Infrastructure/Persistence/EncryptedStringConverter.cs`](../../../../src/shared/AqtCoreFW.Infrastructure/Persistence/EncryptedStringConverter.cs)
- Code: [`src/modules/identity/AqtCoreFW.Identity.Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs)
- Code: [`src/shared/AqtCoreFW.Infrastructure/Configuration/VaultKeyResolver.cs`](../../../../src/shared/AqtCoreFW.Infrastructure/Configuration/VaultKeyResolver.cs)
- Migration: `20260510010458_AddCipherVersionPrefix` (Identity)

## Evidence Artifacts
- **Test:** `Qd742Phase06ComplianceTests.Compliance_4_4_a_EncryptedStringConverter_SupportsVersionPrefix` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Qd742Phase06ComplianceTests.cs)
- **Test:** `Qd742Phase06ComplianceTests.Compliance_4_4_a_VaultKeyResolver_TypesExist` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Qd742Phase06ComplianceTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/1-2-credential-encryption.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
User passwords are hashed with ASP.NET Core Identity's default PBKDF2-SHA256 (100k iterations). PII fields (Email, PhoneNumber, TotpSecret) are encrypted at rest via AES-256-GCM using `EncryptedStringConverter` with versioned key resolution through `VaultKeyResolver`. Keys are sourced exclusively from HashiCorp Vault — never from appsettings.

## Verification Steps
1. Run `Qd742Phase06ComplianceTests` — all 9 tests pass.
2. Inspect `AspNetUsers` table — confirm `Email` column contains `v1:` prefixed ciphertext.
3. Confirm `appsettings.json` contains no plaintext `FieldKey` value.
