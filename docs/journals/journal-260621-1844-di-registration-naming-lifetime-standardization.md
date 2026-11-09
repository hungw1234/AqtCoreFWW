# Journal — DI registration naming + lifetime standardization

**Date:** 2026-06-21 18:44 ICT
**Commit:** `6eb571d7` (code+tests+docs) — chưa push tại thời điểm viết
**Context:** Phase 3 của remediation kiến trúc/DI (plan local, gitignored). Vào qua `/cook "Tiếp P3"`. Scope = FULL P3 (user chọn dù plan tự đánh dấu cuttable).

## Điều đã làm

Đóng #4 (chuẩn hoá tên đăng ký DI), #8 (lifetime stub AI), #11 (gộp `IFeatureFlagService`).

- **#4 — convention đồng nhất 15 module:** App method `Add{Module}Module`; Infra method `Add{Module}Infrastructure`; Infra file+class `{Module}InfrastructureRegistration`. Rename: 3 Infra method outlier (`AddSystemParams`/`AddOrganizationModule`/`AddMasterData`), 3 App method outlier (`AddAiApplication`/`AddFormsApplication`/`AddWorkflowApplication`), 10 Infra file+class (`git mv` + class decl + `typeof(Class).Assembly` self-ref). Call-site chỉ `Api/Program.cs` (6 method + 3 fully-qualified recurring-job class ref L431/434/437); AuthServer chỉ comment.
- **#8 — lifetime stub AI:** 3 stub (`IAiDocumentExtractor`/`IAiAgent`/`IAiAutoFlaggingService`) `TryAddSingleton`→`TryAddScoped`, khớp nhánh Ollama thật (`AddScoped`). Hướng an toàn (Singleton→Scoped), 0 captive-dependency.
- **#11 — `IFeatureFlagService`:** `SystemParams.Application.IFeatureFlagService` KẾ THỪA `SharedKernel.Contracts.IFeatureFlagService` (1 nguồn `IsEnabled`), giữ nguyên `Invalidate`/`ReloadAllAsync`; class Infra rút gọn `: IFeatureFlagService`. Registration vẫn map cả 2 interface về cùng singleton.
- **Guard H2 mới:** `ModuleMediatRRegistrationTests.AllModules_ExposeDiscoverableRegistrationMethod` — mọi module trong `AssemblyFixtures.Modules` phải có ≥1 registration-method discover được. Đóng gap L45 (`expectedHandlers.Count==0 continue` bỏ canh module 0-handler).

## Quyết định / phát hiện cốt lõi

🔴 **INVARIANT 260522-A — probe lật 2 điểm trong chính plan P3 (viết trước khi biết kết quả P2):**
1. **#4 root-cause ĐÃ bị P2 khử.** Lý do gốc của #4 (guard parse-theo-tên thủng) không còn vì P2 đã đổi `ModuleStartupWiringTests` sang directory-scan → rename còn lại thuần cosmetic/consistency. Đã nêu rõ trade-off cho user; user vẫn chọn full.
2. **#11 KHÔNG phải dup → fix theo plan ("delete one, forward") sẽ REGRESS.** 2 interface là ISP cố ý: `SharedKernel.Contracts` = read hẹp cross-module (`IsEnabled`); `SystemParams.Application` = quản-lý rộng nội-bộ (`IsEnabled`+`Invalidate`+`ReloadAllAsync`, dùng bởi `FeatureFlagSubscriber`/`RefreshService`). Xoá interface kia = mất 2 method consumer cần. → đảo sang **kế thừa** (xoá 0 method).

🔴 **Anti-false-green:** guard mới chứng minh red→green THẬT (tạm phá suffix class MasterData `MasterDataInfrastructureRegistration`→`MasterDataBootstrap` → guard ĐỎ liệt kê "MasterData" → revert → XANH).

🔴 **Test bắt coupling ẩn khi rename:** `Qd742Phase05bSigningComplianceTests` D6 allow-list hardcode tên file CŨ `InfrastructureRegistration.cs` để cho phép key `"usb-vgca"`. Rename Identity → allow-list không khớp → key bị flag → test ĐỎ đúng. Fix: cập nhật allow-list sang `IdentityInfrastructureRegistration.cs`. **Bài học: rename file có thể phá allow-list hardcode-tên ở nơi khác — build xanh không đủ, suite arch/compliance mới lộ.**

## Verify

Build slnx **0 error** (108 warning pre-existing) · Architecture **112/112** (+1 guard, red→green proven) · Startup.SmokeTests **18/18** (boot host thật qua `WebApplicationFactory`, exercise mọi call-site rename + ValidateOnBuild + controller discovery) · Ai.Infrastructure **16/16** (#8) · SystemParams **41/41** (#11). Review code-reviewer **APPROVE** (0 HIGH/0 MED; 1 NIT D6 allow-list filename-only, pre-existing, chỉ Identity-rooted nên không leak).

## Carry / unresolved

- Plan P4–P6 còn pending (P4 dedupe Api/AuthServer composition root, P5 webhook/apikey ADR+guard [RLS rejected], P6 backfill unit test 3 module).
- NIT D6: nếu helper `ScanDirectory` sau này dùng cho root rộng hơn Identity, cân nhắc match theo relative-path thay vì filename-only.
