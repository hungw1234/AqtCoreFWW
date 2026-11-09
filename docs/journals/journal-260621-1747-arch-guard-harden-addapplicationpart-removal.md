# Journal — Arch-guard hardening + AddApplicationPart removal

**Date:** 2026-06-21 17:47 ICT
**Commits:** `d9992e55` (code/test) + `72fc3844` (docs) → pushed origin/main (`51391ca8..72fc3844`, bundle gồm cả `2dae6c73` P1 + `c4142b27` MED-1)
**Context:** Phase 2 của remediation kiến trúc/DI (plan local, gitignored). Vào qua `/aqt-next` → `/cook` bước 1+2 → `/cook` bước 3.

## Điều đã làm

Đóng finding #2 (guard sót module) + #3 (AddApplicationPart thừa).

- **#2 — `ModuleStartupWiringTests`:** parser tìm DbContext đổi từ glob tên file `*InfrastructureRegistration.cs` → quét mọi `*.cs` trong thư mục project `*.Infrastructure`. Glob cũ sót đúng 5 module đặt tên file khác (Forms/Workflow/MasterData/Organization/SystemParams: `*Module.cs` / `*Registration.cs`) → guard "mọi DbContext có MigrateAsync" mù với chúng. Thêm guard `TextParser_Should_Discover_EveryModuleDbContext` lấy ground-truth bằng reflection từ `AssemblyFixtures.Modules[].Infrastructure` (base-type walk tới `Microsoft.EntityFrameworkCore.DbContext`, không cần ref EF).
- **#3a — presence guard:** `ApiControllerDiResolutionSmokeTests.Api_EveryModulePresentation_ContributesAtLeastOneController` assert 15/15 module Presentation đóng góp ≥1 controller vào `ControllerFeature` discovered. Vá false-green H1: test constructability cũ chỉ check controller *đã enumerate* resolve được, KHÔNG assert per-module presence → drop 1 module vẫn xanh.
- **#3b — gỡ wiring:** xoá cả 18 `AddApplicationPart` trong `Program.cs` (13 module + 5 host-admin) + comment auto-discovery. `AiPresentationModule.cs` sửa stale comment trỏ method không tồn tại.
- Docs: `module-creation-guide.md` / `system-architecture.md` / `code-standards.md` thay hướng dẫn AddApplicationPart → DependencyContext auto-discovery.

## Quyết định / phát hiện cốt lõi

🔴 **INVARIANT 260522-A cứu một false-action.** Plan claim "13 AddApplicationPart thừa". Một Explore agent trung gian lại kết luận ngược: "13 load-bearing, gỡ→404" + "Integration thiếu call = bug 404 latent". CẢ HAI đều **suy luận, không probe** (agent dựa vào comment doc của `HostDiArchGuard`).

Probe runtime dứt khoát: `ContractEndpointsTests` (`WebApplicationFactory<AqtCoreFW.Api.Program>` — boot Program.cs thật) PASS, GET `/api/v1/integration/contracts` → **200**, dù `Integration.Presentation` KHÔNG có trong list `AddApplicationPart`. ⇒ ASP.NET Core auto-discover module Presentation project-referenced qua DependencyContext (deps.json). Vậy:
- Plan ĐÚNG (vindicated), số chỉnh **18 không phải 13**.
- Explore agent SAI (không có Integration bug; không load-bearing).
- Dự án 0 trim/AOT/single-file → DependencyContext-discovery nguyên vẹn.

🔴 **Anti-false-green kỷ luật:** #2 chứng minh red-before-green thật (tạm revert parser về glob cũ → guard ĐỎ → re-apply → XANH). #3 chứng minh an toàn bằng safety net empiric: presence guard + 2 HTTP routing test (Integration 4/4, BP 2/2) đều GREEN *sau* khi gỡ — guard sẽ đỏ nếu drop module, nó xanh.

🔴 **LESSON:** sub-agent kết luận "load-bearing / sẽ vỡ" mà chỉ dựa comment/doc của code = cùng class với plan-claim chưa probe. Quyết định gỡ wiring phải pin bằng HTTP routing test thật (`WebApplicationFactory<Program>`), không phải framework-lore. ProjectReference (không `Private=false`/`ReferenceOutputAssembly=false`) đảm bảo có trong deps.json → auto-discovery, độc lập với việc code có `typeof(XController)` ref hay không.

## Verify

arch 110→**111** · `Startup.SmokeTests` 18/18 · Integration HTTP 4/4 · BP HTTP 2/2 · `ModuleStartupWiringTests` 3/3. Review code-reviewer **APPROVE_WITH_NITS**: MED (`GetTypes()` chống `ReflectionTypeLoadException`) FIXED; NIT stale AI comment FIXED; NIT 15-list duplication giữ (presence guard ⊥ AssemblyFixtures khác project, không ref chéo — defensible).

## Carry / unresolved

- Plan P3–P6 còn pending (P3 chuẩn hoá DI, P4 dedupe composition root, P5 webhook/apikey ADR+guard, P6 backfill unit test).
- NIT list-duplication: nếu sau này muốn DRY, cân nhắc đưa danh sách module Presentation về 1 nguồn dùng chung cross-project.
