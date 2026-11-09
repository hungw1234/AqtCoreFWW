---
ReqID: "4.4.a"
title: "Không hardcode credentials/secrets — phải qua giao diện cấu hình"
muc: 4
phase: "06"
status: MET
generated: 2026-05-10
---

# 4.4.a — Không hardcode credentials/secrets — phải qua giao diện cấu hình

**Phase:** 06 | **Mục:** 4 — An toàn ứng dụng và mã nguồn

## Implementation Reference
- Code: [`src/shared/AqtCoreFW.Infrastructure/Configuration/VaultKeyResolver.cs`](../../../../src/shared/AqtCoreFW.Infrastructure/Configuration/VaultKeyResolver.cs)
- Code: [`src/shared/AqtCoreFW.Infrastructure/Configuration/ProductionSecretValidator.cs`](../../../../src/shared/AqtCoreFW.Infrastructure/Configuration/ProductionSecretValidator.cs) — gates startup if any required secret is empty/DEV_-prefixed
- Code: [`src/shared/AqtCoreFW.Infrastructure/Logging/SecretMaskingDestructuringPolicy.cs`](../../../../src/shared/AqtCoreFW.Infrastructure/Logging/SecretMaskingDestructuringPolicy.cs) — prevents secrets leaking into logs
- Code: [`docs/ops/vault-key-rotation-runbook.md`](../../../../docs/ops/vault-key-rotation-runbook.md) — ops runbook (2-person approval, N=3 key versions)

## Evidence Artifacts
- **Test:** `Qd742Phase06ComplianceTests.Compliance_4_4_a_VaultKeyResolver_TypesExist` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Qd742Phase06ComplianceTests.cs)
- **Test:** `Qd742Phase06ComplianceTests.Compliance_4_4_a_ProductionSecretValidator_CoversRequiredKeys` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Qd742Phase06ComplianceTests.cs)
- **Test:** `Qd742Phase06ComplianceTests.Compliance_4_4_a_ApplicationSettings_NoPlaintextSecretsInBaseFile` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Qd742Phase06ComplianceTests.cs)
- **Test:** `Qd742Phase06ComplianceTests.Compliance_4_4_a_ProductionSettings_DeclareEmptyPlaceholdersForVault` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Qd742Phase06ComplianceTests.cs)
- **Test:** `Qd742Phase06ComplianceTests.Compliance_4_4_a_KeyRotationRunbook_Exists` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Qd742Phase06ComplianceTests.cs)
- **Test:** `Qd742Phase06ComplianceTests.Compliance_4_4_a_SecretMaskingDestructuringPolicy_IsRegistered` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Qd742Phase06ComplianceTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/4-4-a-vault-no-hardcode.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
All production secrets (`ConnectionStrings:Default`, `Encryption:FieldKey`, `Audit:HmacKey`, etc.) are sourced exclusively from HashiCorp Vault via `VaultKeyResolver`. `ProductionSecretValidator` gates Api and AuthServer startup with `InvalidOperationException` if any required key is empty, whitespace, `DEV_`-prefixed, or matches the all-zero dev marker. `SecretMaskingDestructuringPolicy` redacts secret-shaped log properties. Architecture tests perform a regex scan of `appsettings.json` files to assert zero plaintext secrets. Key rotation ops runbook (`docs/ops/vault-key-rotation-runbook.md`) documents 2-person approval and N=3 version retention.

## Verification Steps
1. Run full `Qd742Phase06ComplianceTests` suite (9 tests) — verify all pass.
2. Inspect `appsettings.json` and `appsettings.Production.json` — confirm no plaintext secret values.
3. Start API without Vault accessible in Production mode — verify startup throws with descriptive error.
