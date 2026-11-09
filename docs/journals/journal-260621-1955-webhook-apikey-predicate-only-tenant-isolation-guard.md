# Journal — Webhook/ApiKey tenant isolation: ADR-009 + predicate-only guard

**Date:** 2026-06-21 19:55 ICT
**Commits:** ADR-009 + guard tests → NOT yet pushed
**Context:** Phase 5 của remediation kiến trúc/DI (plan local, gitignored). Vào qua `/cook P5`. Audit finding #9 = RLS parity risk trên shared-infra tenant-scoped tables.

## Điều đã làm

Đóng finding #9 (webhook + ApiKey tenant isolation). Zero production code change, zero migration.

- **ADR-009 (`docs/adr/009-webhook-apikey-tenant-isolation-predicate-only.md`):** ghi lý do tại sao 3 shared-infra table (`webhooks.Subscriptions`, `gateway.ApiKeys`, `gateway.ApiKeyScopes`) KHÔNG dùng RLS mà dùng predicate-only allowlist. Audit instinct "add RLS for parity vs 15 module DbContext" bị red-team (C1) reject vì RLS unfeasible + thực ra là landed mine khi least-privilege hardening hoàn thành.
- **Guard `SharedInfraTenantTableIsolationGuardTests` (4 tests):** reflection-scan mọi concrete DbContext ở `Infrastructure` assembly, tìm entity type nào dùng Guid TenantId, assert exact match allowlist (WebhookSubscription/ApiKeyRecord/ApiKeyScope). Force conscious decision (RLS-or-allowlist) cho table tenant-scoped shared-infra tương lai. Registry by ENTITY TYPE (không phải heuristic tên cột — tránh false-positive trên Alerting/Backup/Messaging system context không có TenantId).

## Quyết định / phát hiện cốt lõi

🔴 **Tại sao RLS không feasible.** RLS predicate (RlsMigrationHelper.cs:57-70) cần SESSION_CONTEXT('TenantId') set trước query. Nhưng 2 surface đọc WITHOUT ambient tenant:
  - `ApiKeyService.ValidateAsync` (ApiKeyService.cs:141-147): lookup key bằng Prefix TRỌc TRƯỚC biết tenant (lookup-before-tenant) → RLS reject mọi key → M2M auth dead.
  - `WebhookDeliveryJob.ExecuteAsync` (WebhookDeliveryJob.cs:48): read subscription bằng PK trong Hangfire background job, KHÔNG có tenant context → RLS zero-row → delivery job chết im lặng (log "not found", không exception/retry).

🔴 **Key insight: RLS là landed mine, chứ không phải bảo vệ.** Nếu dev/CI chạy sa/db_owner → RLS bypass via carveout (bề ngoài vô tư, thực ra rủi ro); khi db_owner→least-privilege hoàn thành → RLS trigger → webhook + M2M auth ĐỔ cùng lúc. Thêm RLS bây giờ tương đương setup trap timed chính xác khi security hardening hoàn (false-green class).

🔴 **Adulthood:** đóng audit finding = chứng minh nó non-issue + backstop thực risk tương lai, KHÔNG phải "thêm control giống nhất lẫn". ADR ghi asymmetry là ĐÚNG + guard giữ future table cẩn thận.

🔴 **Guard design:** registry by entity type, không tên cột → precision (không false-positive Alerting/Backup có khác structure). Discovery walk DbSet<T> only (owned/nav-only entities escape → deferred, ghi MED-2 trong ADR). Teeth test (tautology-catch) mới: tạo synthetic entity type → run real IsTenantScoped + FindUnaccounted predicate → future refactor neuters detection → test ĐỎ.

## Anti-false-green kỷ luật

Red-before-green PROVEN THực 2 lần:
1. Xoá 1 entry khỏi allowlist → main test ĐỎ.
2. Gỡ predicate `IsTenantScoped` check → teeth test + discovery test ĐỎ, cả hai restore → XANH.

Tautology-trap (xảy ra #2 iteration-1): teeth test assert tên-made-up không trong hardcoded dict = chứng minh không (không cầm đầu discovery). Rewr ired → synthetic entity → REAL predicate walk → trap xác thực.

## Verify

Arch 112→**116** · `SharedInfra.Tests` 4/4 (predicate-discovery + teeth + negative). Review code-reviewer **APPROVE_WITH_NITS**: MED-1 tautology trap cứu vào lần sửa. Carry: 2 coverage-gap ghi MED-2/3 trong ADR (DbSet walk, Guid-only TenantId).

## Carry / unresolved

- Plan P6 (backfill MasterData/Organization/SystemParams module unit test) còn pending.
- Pre-existing nit: `docs/adr/` có 2 file ADR-006 (composition-root-divergences + sqlserver-docker-dev). New file dùng ADR-009. FU: deduplicate số.
