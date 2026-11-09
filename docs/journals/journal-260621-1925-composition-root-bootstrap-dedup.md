# Journal — Composition-root bootstrap dedup (Vault + PII encryption upgraders)

**Date:** 2026-06-21 19:25 ICT
**Commit:** `9c46f62b` (code + ADR) — chưa push tại thời điểm viết
**Context:** Phase 4 của remediation kiến trúc/DI (plan local, gitignored). Vào qua `/cook P4`. Scope = FULL extraction (user chọn dù probe khuyến nghị minimal-safe).

## Điều đã làm

Đóng #5 (DRY 2 composition root), #6 (AuthServer `ValidateOnBuild`), #7 (AuthServer migrate-list).

- **#5a — Vault config:** thêm overload `AddVaultConfiguration(this IConfigurationBuilder, IConfiguration)` ở `src/shared/AqtCoreFW.Infrastructure/Configuration/VaultConfigurationProvider.cs` (gom bind section + 6 env-override). 2 host từ lambda inline trùng → cùng gọi `builder.Configuration.AddVaultConfiguration(builder.Configuration)`.
- **#5b — encryption upgraders:** tạo `StartupEncryptionUpgradeRunner.RunAsync(IServiceProvider, connStr, plaintextColumns[], legacyColumns[], ILogger)` ở `AqtCoreFW.Identity.Infrastructure/Startup/`. Gom chuỗi 3 bước (Step1 plaintext→encrypted, Step2 EmailHashBackfill, Step3 legacy-cipher) lặp inline ở cả 2 host. **Order + catch per-step + column list per-host giữ byte-faithful.**
- **#6 + #7 — ADR-006** (`docs/adr/006-authserver-composition-root-divergences.md`): ghi `ValidateOnBuild=false` (intentional: Identity handler cần infra chỉ-có-Api) + migrate-subset (AuthServer migrate Identity+Messaging; Audit/SystemParams do Api/Helm own, giữ login least-privilege). Kèm invariant "AuthServer migrate ⊆ Api migrate" + arch-guard là FU.

## Quyết định / phát hiện cốt lõi

🔴 **INVARIANT 260522-A — probe lật premise chính của plan (#5b):** plan nói "6 bước upgrader lặp → gom". Probe (Explore so 2 Program.cs) cho thấy **chỉ structure giống, column DIVERGENT hợp lệ**: Api 5 plaintext-col (+`audit/RtbfRequests/SubjectEmail|CitizenNationalId` PkColumn=InternalId) + legacy `files/FileRecords/UploaderDisplayName`; AuthServer 3 col + legacy `identity/AspNetUsers/TotpSecret`. Divergence đúng (Api own Audit/Files schema; AuthServer không đăng ký). Vault thì TRÙNG THẬT (AuthServer comment "mirrors Api exactly" = sync tay = drift-risk).

🔴 **Design chốt: orchestration shared, data (column) per-host.** Runner giữ thứ tự bắt buộc + fail-soft 1 nơi; mỗi host truyền column list riêng. Đây là DRY chính đáng (không phải forced abstraction) — phần khác nhau (column) ở lại host, phần giống nhau (ordering discipline + KeyNotFound handling) gom.

🔴 **Layering:** runner đặt ở `Identity.Infrastructure` (ref shared Infra cho upgrader + IKeyResolver; own IdentityDbContext/EmailHashService/EmailHashBackfillService). Cả 2 host đã ref Identity.Infrastructure → không tạo edge mới/vòng.

🔴 **Fidelity là rủi ro lớn nhất (security-sensitive):** giữ **3 try block độc lập** (Step1 catch KeyNotFound, Step2 catch Exception broad, Step3 catch KeyNotFound) — gộp thành 1 try sẽ đổi semantics. Giữ guard bao quanh mỗi host nguyên vẹn (Api trong block runtime-tasks always-run; AuthServer top-level sau ExitAfterMigrations early-return → không chạy migrate-only). Comment-only divergence (`NormalizedEmailHashGlobal` vs `NormalizedEmailHash`) không phải behavioral (cùng gọi `EmailHashBackfillService.BackfillAsync`).

🔴 **#6/#7 — review-audit-self-decision:** không đảo quyết định có-tài-liệu (ValidateOnBuild=false có lý do code-comment) khi 0 bằng chứng mới → ADR xác nhận, không bật.

## Verify

Build slnx **0E** (108 warning pre-existing, không tăng) · AuthServer.Tests **27/27** · Identity.Infrastructure.Tests **326/326** · **Identity.Integration.Tests 297/297 (login/token canary — sẽ ĐỎ nếu sai thứ tự upgrade vì login chết tới khi NormalizedEmailHash backfilled)** · Startup.SmokeTests **18/18** (Api boot). Net **-177 dòng** trùng lặp. Review code-reviewer **APPROVE** (0 HIGH/MED/LOW; 1 NIT IKeyResolver resolve 2 lần — đúng, cần cho catch boundary).

## Carry / unresolved

- Plan P5 (webhook/apikey ADR+guard RLS-rejected), P6 (backfill unit test MasterData/Org/SystemParams) còn pending.
- FU (ADR-006 ghi): arch-test guard cho invariant "AuthServer startup-migrate ⊆ Api startup-migrate" (deferred — text-scan 2 Program.cs hơi brittle, để khi cần).
