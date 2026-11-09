# Journal — Forms cascading data-source: advisory → --deep plan → red-team slice

**2026-08-04 17:28 ICT** · branch `feat/forms-cascading-datasource` · plan `260804-1701-forms-generic-cross-field-cascading-data-source-*` (gitignored scratch)

## Session goal
User hỏi: Forms động đã có tính năng "control này lọc data theo control khác" (tỉnh→xã) chưa. Từ 1 câu hỏi → advisory `/ak-ask` → `/ak-plan --deep --tdd`. KHÔNG cook (post-v1.0, launch gate còn mở).

## What happened
1. **`/ak-ask` advisory:** scout xác định (B) CHƯA hỗ trợ tổng quát — chỉ `AddressField` hardcode tỉnh→xã; visibility-engine + resolver + useWatch tồn tại nhưng KHÔNG nối vào data-source filtering. User chốt nhu cầu: tổng quát 0-1-N field, cùng/external DS.
2. **`/ak-plan --deep`:** 2 scout song song (nested TableField + security/external), scaffold 8 phase, viết plan, red-team.

## Decisions made (user)
- Reverse-inference (chọn con→suy cha) CẮT → v1.1.
- AddressField GIỮ song song (không migrate — tránh regression địa chỉ VN).
- External auth → BE proxy bắt buộc; credential Vault.
- Nested = cascade GIỮA CỘT trong 1 TableField row (table-in-table bị code cấm — hiểu sai ban đầu, scout sửa).
- Chuỗi không giới hạn cứng; cả authed + public (ban đầu).

## Red-team (source-verified) → SLICE v1
Red-team re-verify từ source, KHÔNG tin plan text — tìm 1 CRITICAL + 2 HIGH, tất cả verified:
- **C1 (CRITICAL):** `FormFieldRepository.GetByIdAsync:20` filter CHỈ `Id==id && !IsDeleted` — 0 tenant/template scope. Authed an toàn nhờ ambient EF RLS; anonymous public path (no ambient tenant) → attacker fieldId rò cross-tenant options + config. → CẮT anonymous public.
- **H1 (verified):** Internal resolver CHỈ có `ResolveInternalDataSourceDefaultHandler` (trả []), 0 handler thật → P3 Internal multi-param + arch-test = **vacuous green**. → CẮT Internal.
- **H2:** anonymous trigger external-Vault-auth fetch = đốt quota. → CẮT external-auth.
- **M1:** in-row = client-trust (BE shape-only validation). → CẮT in-row.

→ **v1 slice (~3-4d) = authed-only + top-level + external-STATIC + EnumRef.** 4 phần rủi ro cắt sang row `FORMS-CASCADE-PUBLIC-EXTERNAL-INROW`.

## Lessons
- **Red-team verify-from-source bắt được cả security leak (C1) LẪN vacuous-work (H1).** Interview của tôi bỏ sót cả 2: C1 vì không đọc `GetByIdAsync` khi hỏi public scope; H1 vì tin "Internal resolver tồn tại" mà không check có handler thật. Bài học: khi hỏi scope-expanding question (public? external?), phải scout luôn tầng thực thi của phần định mở, không chỉ phần đang có.
- **"Nested" = từ mơ hồ.** Tôi hiểu "nested TableField" = bảng lồng bảng (đắt, per-row graph); scout sửa: table-in-table bị cấm, "nested" thực = cascade giữa cột 1 row (rẻ hơn, widget internal state). Chốt định nghĩa qua scout trước khi ước lượng effort.
- **YAGNI thắng qua red-team:** slice cắt 7-9d → 3-4d bằng cách KHÔNG xây phần chưa có nền (Internal handler) + phần rủi ro cho post-v1.0 (public/external-auth).

## Test status
N/A — planning only, 0 code. Preprocessor fix (phiên trước cùng ngày) đã ship `main` `0807897ff` (unit 375/375 + integration 9/9).

## Next session
- `FORMS-CASCADING-DATASOURCE` v1 slice: cook khi launch gate (soak/staging) clear HOẶC user yêu cầu. Plan READY.
- Deferred 4 phần → `FORMS-CASCADE-PUBLIC-EXTERNAL-INROW` (gated per-piece real-need).
- Standing: launch blocker code-doable = 0; gate còn lại = operational (soak ≥24h staging, observability ops-activation, FU-A GOV auditor, prod Vault seal-posture).
