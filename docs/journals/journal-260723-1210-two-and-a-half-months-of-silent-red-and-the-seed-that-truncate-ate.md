# 2,5 tháng đỏ âm thầm, và seed bị truncate nuốt mất

**2026-07-23 12:10 · slice `260723-1135-test-infra-mfa-header-truncate-seed` · branch `fix/test-infra-mfa-header-and-truncate-seed`**

## Chuyện gì

Full-suite đầu tiên sau nhiều tuần (leg-3 Definition-of-Stable) trả 182 red. Tester convict 155 là parallel-pollution nhưng dán nhãn 19 còn lại "real bug: permission chưa registered, seed thiếu, guard thiếu". Cả 3 hypothesis đều sai khi đối chứng:

1. **17 red MasterData+Organization** = controllers gained `[RequiresMfa]` ngày 02-05; hai suite test viết TRƯỚC đó không bao giờ được cập nhật gửi `X-Test-MfaAt`. Module không ai đụng → không ai chạy suite → đỏ âm thầm 2,5 tháng. Permission stub in-memory vô can — 403 đến từ requirement THỨ HAI trên cùng policy pipeline.
2. **2 red Audit** = `TruncateAllDataAsync` wipe seed nhúng migration trên warm DB; migration recorded không re-run → seed chết vĩnh viễn ở local-SQL mode. Cả seed lẫn guard 400 đều ĐÚNG trong source. Đây là false-red mode #3 — thứ mà "trust the isolated re-run" không bắt được, vì nó sống trong DB state chứ không trong parallelism.

## Ba bài học kỹ thuật

- **Fix lớp 1 lộ lớp 2** (instance mới của bài cũ): MFA fix mở đường cho 3 test Organization chạy sâu hơn và đâm vào stale Guid assert trên `orgUnitId` — DTO cố ý expose `long` FK từ tier-migration. Suite đỏ lâu ngày ủ NHIỀU lớp rot chồng nhau; đếm số fail giảm chưa phải là xong.
- **Error 208 compile-time**: batch SQL ad-hoc tham chiếu bảng thiếu fail ngay lúc compile batch, kể cả nằm trong IF unreachable — `IF OBJECT_ID(...) IS NOT NULL AND NOT EXISTS(...)` cùng statement KHÔNG cứu. Guard phải nằm ngoài batch (C#-side). Review H1 bắt đúng chỗ này: reseed gắn vào helper 4-caller-4-DB, 3 DB thiếu bảng → sẽ sập 3 suite mà acceptance của tôi hoàn toàn mù (chỉ chạy đúng path Main).
- **git-stash A/B với `--no-build` vô hiệu**: stash revert source, DLL vẫn giữ code mới. Tôi đã chạy một A/B như thế và suýt tin nó; bằng chứng pre-existing đúng hóa ra là run lịch sử (full-run 10:40 có TRƯỚC khi diff tồn tại).

## Kết quả

MasterData 15/15 · Organization 37/37 · Audit 84/84 · Messaging 12/12 · Identity.Infra 339/339 — isolated xanh hết. Còn 1 red pre-existing (`TENANTISO-LEASTPRIV-INSERT-TIMEOUT`, SQL timeout, row riêng). Leg 3: 0 production bug; suite giờ xanh cơ học trừ 1 row đã file.

## Cảm nhận

Cái đáng sợ nhất hôm nay không phải bug nào — là 2,5 tháng không ai biết 17 test đỏ. "Suite xanh" chỉ là mệnh đề về lần cuối cùng có người chạy nó.
