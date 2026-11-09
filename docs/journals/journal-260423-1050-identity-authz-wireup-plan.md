---
date: 2026-04-23
time: 10:50
topic: Identity Authorization Wire-up Plan (DataScope + ABAC)
status: plan-only (no code execution)
related:
  - v2.128p3 (admin identity menu hygiene batch, same session)
  - Future: v2.131 (DataScope wire-up ship)
  - Future: v2.132 (ABAC wire-up ship)
---

# Identity Authorization Wire-up — Plan Document Session

## Bối cảnh

Session-3 follow-up sau v2.128p3 (soft-hide 3 admin identity menus). Anh request architecture consultation về `/admin/abac-rules` và `/admin/data-scopes`: mục đích, cách dùng, có chồng chéo không.

Kết luận consultation:
- **Không chồng chéo chức năng** — ABAC gate action layer, DataScope filter row layer. Orthogonal concerns.
- **Cả hai đều DORMANT runtime** — scout phát hiện: ABAC có duplicate handlers + zero route consumer; DataScope có `ResolvedDataScope` không ai inject + thiếu `TenantId` (P0 cross-tenant leak).
- **Business case BTC rõ hơn cho DataScope** (cây tổ chức Cục/Chi cục) so với ABAC (cần 1-2 scenario cụ thể: giờ hành chính? MFA level? department?).

Anh quyết: **Plan trước, không implement, không soft-hide menu** (giữ hiện trạng UI).

## Artifact tạo ra

**Directory:** `plans/260423-1044-identity-authz-wireup/`

| File | Lines | Purpose |
|------|-------|---------|
| `plan.md` | 52 | Overview + 2 phase list + 4 open questions escalated |
| `phase-01-datascope-wireup.md` | 193 | P1 3-5d ship v2.131 |
| `phase-02-abac-wireup.md` | 172 | P2 1-2d ship v2.132 blocked by P1 |

**Tổng:** 417 lines plan docs.

## Key architectural decisions

1. **DataScope filter pattern:** Extension method `ApplyDataScope<T>()` trên `IQueryable<T>` với marker interface `IDataScopeFilterable` — KHÔNG dùng EF global filter. Lý do: global filter không access `ICurrentUser` tại `OnModelCreating` vì DbContext pooled. Extension approach = DI clear, test isolated, KISS.

2. **TenantId backfill:** `DataScopeType` = global lookup (`TenantId=Guid.Empty` shared cross-tenant); `RoleDataScope` + `UserDataScopeOverride` = per-tenant. Query filter `TenantId == current || TenantId == Empty`. Migration tự động bind current tenant từ context.

3. **Override expiry:** Hangfire recurring job 1h + Redis pub/sub **reuse** infrastructure pub/sub của `AbacCacheSubscriber` (v2.120). Thêm `Processed` flag cho idempotent sweep.

4. **ABAC scenario chọn Option B:** department-based `cases.approve` route. Pair tự nhiên với Phase 01 Cases wire-up, scenario BTC rõ (phòng BNV/PBT duyệt, VHC không duyệt). Options A (business_hours) và C (MFA level) defer sang sau.

5. **Sequence cứng:** Phase 02 BLOCKED by Phase 01 ship + 24h green. Không parallel vì cùng `IdentityDbContext` migration infra + risk DI graph churn.

## Biggest risks (flagged in plan, user review)

- **P1 DataScope:** EF query filter có thể bloat SQL 1.5-2x trên 10k cases. Mitigation: benchmark + EXPLAIN + split query nếu OR clauses degenerate.
- **P1 DataScope:** `Case` entity có thể thiếu `CreatedBy`/`AssignedTo`/`OrgUnitId` fields. Phase 01 Step 4 MANDATORY scout trước khi design filter.
- **P2 ABAC:** `DepartmentClaimEnricher` middleware có thể chưa tồn tại — nếu không compute claim từ user's OrgUnit, tất cả requests 403. Phase 02 Step 05 bắt buộc grep trước wire route.
- **P2 ABAC:** `OrgUnit.Code` values ("BNV"/"PBT") có thể không khớp v2.128 seed → scout OrgUnit seeder trước step 06.
- **Security P0:** DataScope `TenantId` migration là ship-gate non-negotiable. Cross-tenant probe integration test phải có.

## Open questions chờ user answer

1. **TenantId backfill strategy:** Guid.Empty default + infer từ role JOIN OK, hay migrate qua `ITenantContext.CurrentTenantId` fallback?
2. **ABAC scenario final:** Confirm Option B (cases.approve) hay preference A (business_hours) / C (MFA)?
3. **Cache lag tolerance:** 1h5 worst-case drift trên override expiry OK cho v1?
4. **Benchmark baseline:** Có prod benchmark `ListCasesQuery` 10k cases chưa, hay cần tạo từ đầu?

## Bước tiếp theo

Anh review 3 plan files → answer 4 questions ở cuối plan.md → em execute Phase 01 khi anh bật đèn xanh. Hiện plan chưa assigned lịch cụ thể, để user quyết timing.

## Lessons

1. **Plan-only commit hợp lý cho multi-day work**: 5-7d commitment Phase C + Phase B nếu skip plan = rủi ro cao. Plan 30 phút giúp lộ risk sớm (TenantId P0, duplicate handlers, seed gap).
2. **Scout reuse from prior agent spawns**: 2 scout reports từ `/ask` turn trước được planner dùng trực tiếp — không cần re-scout. Token efficiency 2x.
3. **User override quyết định architecture advice của em**: Em recommend Phase 0 soft-hide trước, anh reject "cứ để menu đó" — legitimate business choice (menu dormant không broken, chỉ empty dropdown). Architect propose, user dispose.
4. **Sequential > Parallel cho same-module wire-up**: Em push-back parallel đúng. Cả Phase B+C cùng touch IdentityDbContext + controllers + seeder → parallel = merge conflict hell + code-reviewer context bloat.
