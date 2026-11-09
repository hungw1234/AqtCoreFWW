---
ReqID: "4.1"
title: "Kiểm tra tính hợp lệ input trước khi xử lý"
muc: 4
phase: "01"
status: MET
generated: 2026-05-10
---

# 4.1 — Kiểm tra tính hợp lệ input trước khi xử lý

**Phase:** 01 | **Mục:** 4 — An toàn ứng dụng và mã nguồn

## Implementation Reference
- Code: [`src/shared/AqtCoreFW.SharedKernel/Application/Behaviors/ValidationBehavior.cs`](../../../../src/shared/AqtCoreFW.SharedKernel/Application/Behaviors/ValidationBehavior.cs) — MediatR pipeline behavior; runs FluentValidation validators before any handler
- Code: [`src/modules/identity/AqtCoreFW.Identity.Application/Commands/`](../../../../src/modules/identity/AqtCoreFW.Identity.Application/Commands/) — all commands have paired `*Validator.cs`
- Code: [`src/host/AqtCoreFW.Api/Program.cs`](../../../../src/host/AqtCoreFW.Api/Program.cs) — `services.AddValidatorsFromAssembly()` per module

## Evidence Artifacts
- **Test:** `Qd742Phase01ComplianceTests.Compliance_4_1_FluentValidation_PipelineBehaviorRegistered` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase01ComplianceTests.cs)
- **Test:** `IpRulesApiTests.Create_InvalidCidr_Returns422` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/IpRulesApiTests.cs)
- **Test:** `IpRulesApiTests.Create_EmptyCidr_Returns422` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/IpRulesApiTests.cs)
- **Audit log category:** `Error`
- **Screenshot path:** `screenshots/4-1-input-validation.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`ValidationBehavior<TRequest, TResponse>` is registered as a MediatR pipeline behavior globally for all modules via `AddValidatorsFromAssembly`. It executes all FluentValidation validators for a command/query before the handler runs; invalid inputs throw a `ValidationException` which the exception middleware converts to HTTP 422 with a structured error body. No handler processes unvalidated input.

## Verification Steps
1. Run `Qd742Phase01ComplianceTests.Compliance_4_1_FluentValidation_PipelineBehaviorRegistered` — verify passes.
2. POST an IP rule with CIDR `"not-a-cidr"` — verify 422 with field-level error detail.
3. POST a valid rule — verify 201; confirm handler executed.
