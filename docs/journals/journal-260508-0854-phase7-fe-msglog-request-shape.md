# Phase-07 FE MessageLog/Contract Request-Shape Sync — Technical Journal

**Date:** 2026-05-08 08:54 ICT
**Branch:** `fix/phase7-fe-message-log-request-shape` → PR #87
**Commit:** `2833b4f0`
**Backlog Closes:** PHASE7-FE-DTO-CONTRACT-READ-SIDE (P2)
**Status:** SHIPPED (PR OPEN + MERGEABLE)

---

## Executive Summary

Đóng backlog item P2 còn sót sau retier wave Phase-07. **Premise của backlog refuted on source verify** — BE đã 100% Guid-symmetrical từ wave PR-D2/D3 (2026-05-07). Real drift nằm ở handwritten FE adapter request-shape (`partnerId`/`contractId` vs BE-expected `partnerPublicId`/`contractPublicId`) khiến filter dropdowns của Message Log + Contract list silently broken, và POST Create sẽ 422.

Bonus phát hiện trong scope expansion: Contract feature có drift y hệt (`CreateContractBody.PartnerPublicId`), bundled trong cùng commit.

---

## Premise Correction (cốt lõi của bài học)

### Backlog claim (stale)

> **PHASE7-FE-DTO-CONTRACT-READ-SIDE (P2, ~1h):** `MessageLogDto.ContractId : long?` + `ListMessageLogsQuery.ContractId : long?` symmetry post-PR-2 write-side fix.

### Source reality (2026-05-08)

| Surface | Type | Verified |
|---------|------|----------|
| `MessageLogDto.{Id, PartnerId, ContractId}` | `Guid` / `Guid` / `Guid?` | ✅ symmetric |
| `ListMessageLogsQuery.{PartnerPublicId, ContractPublicId}` | `Guid?` cả hai | ✅ symmetric |
| `MessageLogsController` `[FromQuery]` | `partnerPublicId`, `contractPublicId` Guid? | ✅ symmetric |
| `CreateMessageLogBody.{PartnerPublicId, ContractPublicId}` | `Guid` / `Guid?` | ✅ symmetric |
| `ContractsController.List` + `CreateContractBody` | `PartnerPublicId` Guid | ✅ symmetric |

BE đã đóng implicitly trong wave retier Partner+FormTemplate (PR-C/D2/D3). Backlog item không bao giờ được explicit-close bằng commit reference.

### Real drift (root cause)

`web/src/features/integration/integration-api.ts` là **handwritten adapter** song song với Orval generated client (`web/src/api/generated/message-logs/message-logs.ts`). Orval gen đã đúng. Handwritten layer dùng tên cũ `partnerId`/`contractId`:

```ts
// BEFORE (stale)
export interface CreateMessageLogDto {
  partnerId: string;       // BE expects partnerPublicId
  contractId?: string;     // BE expects contractPublicId
  ...
}
export function useMessageLogs(
  params: PaginationParams & { partnerId?: string; contractId?: string } = {}
) { ... }
```

ASP.NET Core model binder **silently drops** unknown query/body fields → filter dropdowns gửi `?partnerId=...` không match `[FromQuery] Guid? partnerPublicId` → handler nhận `null` → list trả về full unfiltered. POST body `{ partnerId, contractId }` → handler bind `Guid.Empty` → FluentValidation `NotEmpty()` → 422 (chưa quan sát thực tế vì test thủ công chưa triggered, nhưng path là chắc chắn).

---

## What Changed

### Files Modified (5)

| Path | Change |
|------|--------|
| `web/src/features/integration/integration-api.ts` | `CreateMessageLogDto` + `CreateContractDto` field renames; `useMessageLogs` + `useContracts` query-param signatures; `messageLogList` + `contractList` query-key signatures |
| `web/src/features/integration/message-log-list-page.tsx` | LogMessage modal `Form.Item name="partnerId|contractId"` → `partnerPublicId|contractPublicId` |
| `web/src/features/integration/contract-list-page.tsx` | CreateContract modal `Form.Item name="partnerId"` + `mutateAsync({ partnerId })` → `partnerPublicId` |
| `backlogs/backlog.md` | Đánh dấu CLOSED 2026-05-08 với premise correction note |
| `backlogs/completed.md` | Thêm entry với chi tiết premise correction + bonus Contract scope |

### Read-side response shape — KHÔNG đổi

`MessageLogDto.partnerId` / `contractId` + `ContractDto.partnerId` (camelCase JSON) **match BE response** vì BE record property names cũng là `PartnerId` / `ContractId`. Drawer/Table dataIndex giữ nguyên.

---

## Test Results

| Check | Status |
|-------|--------|
| `tsc --noEmit` (web/) | ✅ exit 0 |
| Vitest integration feature | N/A — không có test file cho `integration-api.ts` |
| Manual smoke (UI POST + filter) | ⏳ deferred to user runtime |

---

## Lessons (emotional honesty)

1. **Backlog claims that name specific symbols become stale fast in active retier waves.** Wave PR-D2/D3 đụng nhiều entities; mỗi PR docs sync chỉ cập nhật mục được chạm trực tiếp, không re-audit từng item P2/P3 nằm xa scope. Đây là phiên bản FE-side của lesson `feedback-verify-tier-rubric-from-source`: source verify trước khi assume scope, không tin backlog wording.

2. **Closed-by-side-effect pattern.** Khi một wave retier đụng N entities, các item P2/P3 thường resolve implicitly nhưng backlog không được close explicitly. Cần một backlog audit pass định kỳ (ví dụ end-of-phase) để loại các stale lines tương tự `PHASE4D-* trio` mà chúng ta đã clean trên main commit `450e6c72` hôm qua.

3. **Handwritten FE adapters duplicate Orval generated client = silent drift hotspot.** Ở repo này có cả handwritten (`features/*/integration-api.ts`) và generated (`api/generated/...`). Generated được sync từ OpenAPI + tsc check; handwritten thì không có gate. Đề xuất follow-up: hoặc migrate features sang Orval generated, hoặc thêm contract-test dùng generated types làm canonical reference. (Không tạo backlog item ngay — chờ tín hiệu thứ 2.)

---

## Decisions

- **Bundle Contract feature trong cùng PR** thay vì tách. Cùng root cause, cùng adapter, cùng pattern; tách chỉ tăng cost review không giảm risk.
- **Không thêm vitest cho `integration-api.ts`** trong PR này. Pure rename refactor + tsc đủ; thêm test sẽ là scope creep YAGNI.
- **Backlog premise correction được ghi rõ trong completed.md** thay vì chỉ "CLOSED" — preserve audit trail cho lần backlog hygiene tiếp theo.

---

## Unresolved Questions

Không.
