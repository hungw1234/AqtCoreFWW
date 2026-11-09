---
ReqID: "1.5"
title: "Mã hóa thông tin xác thực trước khi gửi qua mạng"
muc: 1
phase: "06"
status: MET
generated: 2026-05-10
---

# 1.5 — Mã hóa thông tin xác thực trước khi gửi qua mạng

**Phase:** 06 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/host/AqtCoreFW.AuthServer/appsettings.Production.json`](../../../../src/host/AqtCoreFW.AuthServer/appsettings.Production.json) — `Vault:Enabled=true`, TLS enforced at infra level
- Code: [`src/shared/AqtCoreFW.Infrastructure/Configuration/ProductionSecretValidator.cs`](../../../../src/shared/AqtCoreFW.Infrastructure/Configuration/ProductionSecretValidator.cs) — gates startup unless secrets are Vault-sourced
- Code: [`src/host/AqtCoreFW.Api/Program.cs`](../../../../src/host/AqtCoreFW.Api/Program.cs) — HTTPS-only middleware (`UseHttpsRedirection`, `UseHsts`)

## Evidence Artifacts
- **Test:** `Qd742Phase06ComplianceTests.Compliance_4_4_a_ProductionSettings_DeclareEmptyPlaceholdersForVault` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Qd742Phase06ComplianceTests.cs)
- **Test:** `Qd742Phase06ComplianceTests.Compliance_4_4_a_ApplicationSettings_NoPlaintextSecretsInBaseFile` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Qd742Phase06ComplianceTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/1-5-tls-credential-transit.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
Credentials are never transmitted in plaintext: the OIDC/OAuth2 login flow runs exclusively over HTTPS (TLS 1.2+) enforced by `UseHttpsRedirection` + `UseHsts` in `Program.cs`. In production, the AuthServer token endpoint is only reachable via HTTPS. Tokens themselves are signed JWTs; password hashes are never returned in API responses. The `ProductionSecretValidator` gates startup to ensure no DEV_ plaintext credentials are present.

## Verification Steps
1. Confirm `appsettings.Production.json` has `Vault:Enabled=true` and empty secret placeholders.
2. Run `Qd742Phase06ComplianceTests` — `NoPlaintextSecretsInBaseFile` passes.
3. In staging, verify `http://` login request is redirected to `https://` (301/307).
