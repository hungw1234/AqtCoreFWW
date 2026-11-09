# Journal — 2026-07-23 10:00 — Gate 4 lớp cho 5 module deferred, và recurring job sống lâu hơn registration của nó

## Bối cảnh

P2 audit sáng nay chốt leg 1 Definition-of-Stable = PASS nhưng lộ gap: quyết định
07-16 "defer 5 module sang v1.1" chưa có cơ chế thực thi — đăng ký unconditional,
Rules/AI/Collab còn 0 permission code. User chọn option B (config-flag). Ship trong
ngày: `Features:{Name}:Enabled` default true, gate 4 lớp + FE.

## Bài học chính: một "module" không tắt ở một chỗ

Đếm cuối cùng, tắt sạch một module cần đúng NĂM chỗ, và chỉ 1 chỗ là hiển nhiên:

1. **DI registration** (hiển nhiên) — skip `Add*Module/Infrastructure`.
2. **Controller discovery** — DependencyContext auto-discovery kéo MỌI Presentation
   assembly được ProjectReference bất kể DI. Không remove ApplicationPart → endpoint
   500 (handler resolve fail) thay vì 404. Phát hiện lúc scout, tránh được ship bug.
3. **Migration set** — `StartupMigrationSets.Contexts` static; không lọc thì
   `GetRequiredService(contextType)` throw ngay boot (context không đăng ký).
4. **Endpoint mapping ngoài controller** — `MapHub<ChatHub>`.
5. **Static Hangfire facade** — cái này REVIEW mới bắt (C1): `AddReportingRecurringJobs()`
   gọi static ở Program.cs post-build, NGOÀI khối registration. Tệ hơn: schedule đã
   persist bởi deployment ON trước đó sống trong Hangfire storage — skip registration
   không xóa nó → job fire hàng đêm vào type không activate được, retry loop vĩnh viễn,
   đúng ở posture prod mà flag tồn tại để phục vụ. Fix = gate + `RemoveIfExists` ở
   nhánh OFF.

Plan tôi viết đã claim "tắt theo registration là đủ" cho cả Rules lẫn Reporting —
đúng cho Rules (IHostedService chết cùng khối), SAI cho Reporting. Một câu plan-claim
2 vế, 1 vế đúng che vế sai. Reviewer cũng chỉ ra fixture OFF **cấu trúc không thể
bắt** leak này (nó boot với `Hangfire:RegisterRecurringJobs=false`) → guard bổ sung
là text-parse Program.cs (tiền lệ SeederStartupOrderGuardTests), mutation-verified.

## Mutation testing trả công hai lần

Mutation part-removal-disable không chỉ xác nhận suite non-vacuous — nó **bắt được
3 probe yếu**: `/api/v1/ai`, `/reports`, `/rules` bare 404 cả khi module ON (không
có action GET không tham số) = pass vì lý do sai. Thay bằng route `[HttpGet]` thật
→ mutation từ 3 fail lên 6 fail. Một probe 404 chỉ có nghĩa khi nó chứng minh được
mình sẽ KHÔNG-404 ở nhánh kia.

## FE: cái nút nổi không thuộc route nào

H1 nửa sau đáng nhớ hơn nửa route-bị-sót: `CopilotSidebar`/`CopilotToggleButton`
render ở app-layout trên MỌI trang authenticated — không qua route, không qua menu.
Grep route + menu = đủ cho 12/13 bề mặt; bề mặt thứ 13 là global UI chrome. Khi
inventory bề mặt FE của một module, phải quét cả layouts/providers, không chỉ
router + menu.

## Kỹ thuật giữ lại

- Pre-strip capture trong fixture: chụp danh sách IHostedService TRƯỚC khi
  WebAppFixture strip → assert "0 hosted service từ module OFF" không vacuous.
- `UseSetting` (không phải `AddInMemoryCollection`) cho flags — Program.cs đọc
  chúng ở ConfigurationManager-phase lúc registration.
- FE fail-open (`data ?? ALL_MODULES_ENABLED`): BE 404 là chốt thật; glitch mạng
  không được phép khóa nhầm 5 tính năng.
- Menu filter theo route-prefix map, post-filter trên item tree đã build → 1 chỗ
  cover cả 2 nguồn menu (API-driven + fallback), không tag từng entry.

## Kết quả

Architecture 200/200 · Startup.SmokeTests 30/30 (9 OFF-branch mới, boot Program.cs
thật) · FE typecheck sạch, gate 7/7, layouts 30/30, smoke-pages 62/62 · build 0E ·
review DONE_WITH_CONCERNS → C1+H1 fixed in-slice · 3 vòng mutation đều ĐỎ đúng chỗ.
Row `LAUNCH-DEFERRED-MODULES-OFF-MECHANISM` RESOLVED — leg 1 Definition-of-Stable
giờ trọn vẹn.
