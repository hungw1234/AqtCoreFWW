# 12 tình nghi, 1 thủ phạm — lần đầu tiên tất cả healthy

**2026-07-23 19:30 · row `SEEDER-RLS-BLIND-CHECK-THEN-INSERT` · Docker Desktop kind**

## Chuyện gì

Buổi chiều (18:15) file row với con số "12 seeder dùng chung pattern check-then-insert mù RLS".
Tối nay quay lại đóng row đó — và con số 12 sụp xuống còn đúng 1. Scout kéo full RLS matrix
(`GenerateRlsPolicyStatements`, 24 table group) ra so từng bảng: MasterData/Tenants tables **không
có RLS**. 10 trong 12 seeder chỉ chạy Development dưới `sa` — không đụng FILTER bao giờ. Thủ phạm
thật chỉ có `SystemParamSeeder`, và ngay cả seeder này cũng đã được thiết kế an toàn từ trước —
`RlsMigrationHelper.NullableTenant.cs` viết thẳng trong comment: "so the SystemParamSeeder can
write global rows without db_owner". Có người đã lường trước đúng class bug này, chỉ là chưa nối
dây.

## Bài học đau nhất buổi

Con số "12" trong backlog row là **upper bound viết lúc hoảng**, không phải fact đã verify. 18:15
tôi grep pattern "check-then-insert bằng AnyAsync" thấy giống nhau ở 12 chỗ rồi kết luận cùng họ
bug — nhưng "giống code shape" không phải "cùng risk". Matrix RLS mới là oracle, không phải grep
pattern-match. Nếu tối nay không quay lại verify từng bảng, row này sẽ ngồi "pending, 12 seeder"
cả tuần — đúng kiểu blast-radius decay mà INVARIANT 260522-A đã cảnh báo.

## Fix — 3 dòng, không seam mới

Dùng lại plumbing có sẵn từ Slice-1D (AmbientTenantScope): push tenant-zero **trước**
`CreateScope` → nhánh factory 2 của `ITenantContext` thật bắt được → `ScopedTenantContext(Guid.Empty)`
→ interceptor pin `SESSION_CONTEXT` → FILTER khớp global row cho cả đọc lẫn block-sau-insert.
Loại bỏ 2 phương án khác: catch `2601` (nuốt luôn duplicate thật, sai) và elevated-conn đọc riêng
(leo quyền chỉ để đọc — thừa, vi phạm least-priv không cần thiết).

## Review bắt được một overclaim

Reviewer soi ra doc-comment test tôi viết "REAL registration path" trong khi factory là hand-mirror
— sửa lại, gắn `ConsumeScopeTenantContextTests` làm real-path drift guard. Một điểm kỹ thuật đáng
ghi: interceptor giữ **object** `ScopedTenantContext` (immutable), không re-read AsyncLocal lúc mở
connection — nên dù pop có race với open cũng không TOCTOU.

## Kết quả — lần đầu tiên trong lịch sử project

NonAudit re-activate → `/health/ready` overall **Healthy**, `audit-read-isolation: Healthy` — chưa
bao giờ xanh cả hai cùng lúc. Seeder log 0 dòng — im lặng chính là bằng chứng: `AnyAsync` đã nhìn
thấy 11 row qua FILTER, không insert trùng. Restart ×2 sạch, smoke toàn xanh.

Definition-of-Stable AUDIT-ISO giờ **LOCAL-COMPLETE** — chỉ còn phần chạy trên prod DBA thật. P3
soak có thể chạy với isolation ON.

Status: DONE.
