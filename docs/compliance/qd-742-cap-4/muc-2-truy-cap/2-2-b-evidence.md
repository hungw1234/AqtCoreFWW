---
ReqID: "2.2.b"
title: "Thực thi chính sách giới hạn IP"
muc: 2
phase: "05"
status: MET
generated: 2026-05-10
---

# 2.2.b — Thực thi chính sách giới hạn IP

**Phase:** 05 | **Mục:** 2 — Kiểm soát truy cập

## Implementation Reference
- Code: [`src/shared/AqtCoreFW.Infrastructure/Middleware/IpFilterMiddleware.cs`](../../../../src/shared/AqtCoreFW.Infrastructure/Middleware/IpFilterMiddleware.cs) — evaluates `IpFilterRule` list on every request
- Code: [`src/host/AqtCoreFW.Api/Program.cs`](../../../../src/host/AqtCoreFW.Api/Program.cs) — `UseIpFiltering()` registered in middleware pipeline

## Evidence Artifacts
- **Test:** `Qd742Phase05ComplianceTests.Compliance_2_2_a_IpFilterRule_IsAuditableEntity` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase05ComplianceTests.cs)
- **Test:** `IpRulesApiTests.Create_ValidCidr_Returns201` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/IpRulesApiTests.cs)
- **Test:** `IpFilterMiddlewareCacheInvalidationTests` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/IpFilterMiddlewareCacheInvalidationTests.cs)
- **Audit log category:** `AppAccess`
- **Screenshot path:** `screenshots/2-2-b-ip-filter-enforcement.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`IpFilterMiddleware` runs early in the pipeline and evaluates the requester's IP against active `IpFilterRule` entries (CIDR matching). Blocked IPs receive 403 Forbidden before any controller logic executes. Rules are cached in-memory and invalidated automatically when the admin creates, updates, or deletes a rule via the API. Both allowlist (IsBlock=false) and blocklist (IsBlock=true) modes are supported per-tenant.

## Verification Steps
1. Add a block rule for `127.0.0.1/32` (loopback).
2. Call any API endpoint from localhost — verify 403.
3. Delete the rule — verify requests succeed again; confirm cache invalidated immediately.

## Observations / Audit Notes
- `IpFilterCacheService` citation removed Wave-2 fix-up (file not present in current codebase; cache invalidation logic is internal to `IpFilterMiddleware`).
