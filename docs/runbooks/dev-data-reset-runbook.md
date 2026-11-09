# Dev Data Reset Runbook

> Reset DB dev sạch (nuke → migrate → seed) lặp lại được, 1 lệnh. Công cụ **dev-only**.
> Script: [`scripts/reset-all.ps1`](../../scripts/reset-all.ps1).

## TL;DR

```powershell
# Runner: pwsh (PowerShell 7) HOẶC powershell (Windows PowerShell 5.1) — script parse-clean cả 2.
# Nếu máy KHÔNG cài pwsh 7 (vd dev mặc định Win), dùng powershell.exe:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/reset-all.ps1          # default: DROP DATABASE + migrate + seed + verify
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/reset-all.ps1 -Hard    # docker compose down -v (xoá volume) + warm-up + seed
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/reset-all.ps1 -Force   # không hỏi xác nhận (CI/non-interactive)
```

> Có `pwsh` 7 thì `pwsh scripts/reset-all.ps1 [args]` cũng chạy y hệt. Script tự coerce `ASPNETCORE_ENVIRONMENT`
> rỗng → `Development`; chặn cứng mọi env ≠ Development.

Hoặc qua skill: `/aqt-runapp --reseed` (chạy reset trước rồi start stack).

## Khi nào dùng

- Sau khi sửa seeder / migration muốn xem kết quả seed sạch từ đầu.
- DB dev nhiễm bẩn (test rác, instance/run thừa) → muốn về trạng thái definitions-only.
- Lần đầu set up máy dev (kèm `-Hard` để dựng volume mới).

## 2 mode nuke

| Mode | Lệnh | Tác động | Khi dùng |
|------|------|----------|----------|
| **default** | `reset-all.ps1` | `DROP DATABASE AqtCoreFW` (giữ container + volume + Vault) | Reset nhanh thường ngày |
| **hard** | `reset-all.ps1 -Hard` | `docker compose down -v` → xoá **toàn bộ volume** (sqlserver + Vault in-memory) → up lại | Khi schema/volume hỏng, hoặc muốn sạch tuyệt đối |

> ⚠️ `-Hard` xoá Vault in-memory → script **bắt buộc re-seed Vault** (không kết hợp `-SkipVault` với `-Hard`).

## Luồng thực thi (thứ tự phụ thuộc)

```
0. ENV GUARD       → chặn cứng mọi ASPNETCORE_ENVIRONMENT ≠ Development (rỗng → coerce Development);
                     DB đích cố định = AqtCoreFW; hỏi xác nhận tên DB (trừ -Force).
1. STOP            → kill Auth/API/FE (giải phóng connection trước khi DROP).
2. NUKE            → default: DROP DATABASE (SET SINGLE_USER ROLLBACK IMMEDIATE trước).
                     hard: down -v + up sqlserver/redis/rabbitmq/vault + chờ port + SELECT 1 (engine ready).
3. VAULT           → probe 5 critical path; thiếu → docker compose run --rm vault-init (idempotent).
4. BUILD           → dotnet build 1 lần (tránh race CS2012 khi 2 host build song song Infrastructure.dll).
4b. MIGRATE-ONLY   → API `--migrate-only` migrate ĐẦY ĐỦ 20 ctx rồi EXIT (BẮT BUỘC, tạo DB trước host
                     runtime). Bỏ bước này → cold-DB race: Hangfire autodetect lúc host-build connect
                     DB rỗng → poison connection-pool → PII upgrader crash 4060. Xem §Cold-DB bootstrap.
5. AUTH            → start nền (migrate Identity+Messaging no-op + OpenIddict seed); chờ /health 200.
6. API             → start nền (migrate 20 ctx no-op + IHostedService seeders: BTC + MasterData).
7. POLL            → chờ 4 marker "complete" (xem dưới); xử lý 2-restart convergence; fail-fast nếu host exit.
8. VERIFY          → đếm Tenant / Users / OrgUnits; in bảng tiếng Việt.
```

> **Readiness (không dùng log "Now listening"):** AuthServer dùng Serilog → KHÔNG in "Now listening on" ra
> stdout. Script chờ **`GET /health` 200** (probe HTTP, fail-fast nếu host process thoát). API readiness suy ra
> từ seeder marker (bước 7), không cần health riêng (API không expose `/health` cùng path).

## Seeder completion markers (poll dừng đúng)

Script poll log API tới khi thấy **cả 4** marker (chuỗi literal verify từ source — KHÔNG paraphrase):

| Marker | Nguồn |
|--------|-------|
| `[OrgTreeSeeder] All tenants complete` | `OrgTreeSeeder.cs` (end of StartAsync) |
| `[BtcIdentitySeeder] complete in` | `BtcIdentitySeeder.cs:63` |
| `[BtcAssignmentSeeder] complete:` | `BtcAssignmentSeeder.cs:179` |
| `MasterData seeding and cache loading complete` | `MasterDataSeeder.cs:46` |

> Nếu thêm/đổi tên seeder → **cập nhật danh sách marker** trong `reset-all.ps1` (`$MK_*`) lẫn bảng này.

## Gotcha: 2-restart convergence — ĐÃ FIX 2026-07-22 (giữ lại làm safety net)

**Trước 2026-07-22:** `TenantUserSeeder` (Identity) đăng ký DI **trước** `OrgTreeSeeder` (Organization) → boot 1 skip user-seed vì chưa có OrgUnits, cần restart lần 2. Từ slice `P1-STABLE-SEED-POSTURE`, API host đăng ký cụm seeder theo thứ tự dependency tường minh trong `Program.cs` (`TenantSeeder → TenantBackfillSeeder → TenantRoleSeeder → OrgTreeSeeder → TenantUserSeeder → TenantAssignmentSeeder`, pin bởi `SeederStartupOrderGuardTests`) → **boot 1 converge đủ**: org tree → users → assignments trong cùng một lần khởi động.

Nhánh restart trong `reset-all.ps1` (`$MK_ABORT_NOUSERS` = `[TenantAssignmentSeeder] incomplete`) **giữ nguyên làm safety net** — bình thường không còn kích hoạt; nếu thấy nó fire lại nghĩa là ordering regression (arch-test phải đỏ trước).

## Cold-DB bootstrap (vì sao có bước migrate-only)

Sau nuke, DB `AqtCoreFW` **không tồn tại**. KHÔNG được để host runtime (Auth/API) tự migrate từ DB rỗng:

- Lúc **host-build**, Hangfire `UseSqlServerStorage` chạy `TryAutoDetectSchemaDependentOptions` → connect tới DB
  chưa tồn tại → `SqlException 4060`. SqlClient **poison connection-pool** của connstr "Default" ~5s (blocking period).
- Trong cửa sổ đó, `PlaintextToEncryptedUpgrader` (AuthServer) mở **raw SqlConnection** → nhận lại cached 4060 →
  **crash UNHANDLED** dù EF MigrateAsync (chạy sau, tạo DB) đã thành công.

→ Script chạy **`dotnet run --project Api -- --migrate-only` TRƯỚC** (migrate đủ 20 ctx rồi exit — Api/Program.cs
return trước Hangfire runtime-wiring + PII upgrader, nên process migrate-only KHÔNG hit path crash). Khi Auth/API
runtime boot sau đó, DB đã tồn tại → Hangfire init OK → upgrader OK. Đây là bootstrap chính thức (≡ Helm `db-migrate`
Job chạy trên API image trước khi host lên), KHÔNG phải workaround.

> **`-Hard` cold-SQL:** sau `down -v`, container SQL fresh mở **port trước khi engine sẵn sàng** → connection bị
> drop (Win32 10054) → migrate-only crash transient. Script chờ **`SELECT 1` thành công** (`Wait-SqlReady`), không
> chỉ port-open + sleep cố định.

## MasterData dùng EnsureCreatedAsync (lệch các ctx khác)

`MasterDataSeeder` tạo schema qua `EnsureCreatedAsync` (KHÔNG `MigrateAsync` như 15 ctx còn lại). Sau nuke vẫn tạo schema riêng — **không cần migration cho MasterData**. Đây là hành vi đã có, không phải bug.

## Demo-seeder (Phase 2-7) — Enabled default false

Seeder demo mới (BNV / Workflow / Form / BP / Niche) **mặc định `Enabled=false` ở Development** (boot thường nhanh + integration test không phình). Chúng chỉ bật khi reset:

- Convention: config section `"<Module>:<Seeder>"` với `Enabled` (default false), bind qua `IOptions`.
- `reset-all.ps1` bật qua **env override**: section `Workflow:DemoSeed` → env `Workflow__DemoSeed__Enabled=true`.
- Danh sách override nằm trong `$DemoSeederEnvOverrides` (hashtable đầu script). Mỗi phase khi land seeder mới **PHẢI thêm 1 dòng** vào hashtable này. Đã có: `Identity__TenantSeed__Enabled` (Phase 2 — `TenantSeeder` seed 2 row Tenant chuẩn Bộ Tài chính + Bộ Nội Vụ).

> BTC seeder hiện có (`Organization:OrgTreeSeed`, `BtcAssignmentSeed`, `Identity:BtcUserSeed`) **đã `Enabled=true`** trong `appsettings.Development.json` → tự seed khi boot Development, không cần override. **Note:** `Organization:OrgTreeSeed` requires `Identity:TenantSeed` to run first (tenant rows must exist); reset-all.ps1 enables both via env overrides.

> **`TenantSeeder` chỉ INSERT-nếu-thiếu (theo PublicId), KHÔNG reconcile row có sẵn.** Trên DB đã nuke (luồng reset chuẩn) nó tạo row chuẩn từ đầu. Nếu chạy trên DB CŨ không nuke mà `TenantBackfillSeeder` đã tạo row BTC với code xấu `TENANT-xxxx`, `TenantSeeder` thấy PublicId `…b7c1` tồn tại → skip → code/tên cũ giữ nguyên. Muốn code chuẩn → nuke trước (hoặc `-Hard`).

## Verify

Script đếm 3 bảng:

| Thực thể | Bảng |
|----------|------|
| Tenant | `[identity].[Tenants]` |
| Users | `[identity].[AspNetUsers]` |
| OrgUnits | `[organization].[OrgUnits]` |

Kỳ vọng sau reset (1 tenant BTC hiện tại): ~10k users · ~1139 OrgUnits · ≥1 Tenant. (Phase 2 thêm BNV → 2 Tenant.)

> Nếu đổi schema/tên bảng → cập nhật SQL ở §7 `reset-all.ps1` (`Get-Count`).

## Tham số

| Tham số | Mặc định | Ý nghĩa |
|---------|----------|---------|
| `-Hard` | off | `down -v` (xoá volume) thay vì DROP DATABASE |
| `-SkipVault` | off | Bỏ qua Vault check (KHÔNG dùng với `-Hard`) |
| `-SkipBuild` | off | Bỏ `dotnet build` (binaries đã mới) |
| `-Force` | off | Bỏ xác nhận tương tác (CI) |
| `-TimeoutSec` | 900 | Timeout chờ seeder hội tụ (BTC 10k user ~vài phút) |

## Troubleshooting

| Triệu chứng | Nguyên nhân | Xử lý |
|-------------|-------------|-------|
| `DROP DATABASE` treo/lỗi lock | Connection còn mở | Script đã `SET SINGLE_USER WITH ROLLBACK IMMEDIATE`; nếu vẫn lỗi → kill thủ công process trên :5001/:5002 |
| Poll timeout, thiếu marker | Seeder lỗi / Enabled=false | Xem `.reset-logs/api.log`; check `appsettings.Development.json` các flag `Enabled` |
| `VaultApiException` khi seed user | Vault chưa seed (sau `-Hard`) | Đừng dùng `-SkipVault` với `-Hard`; chạy lại không `-SkipVault` |
| Verify in `ERR` / `?` | Tên bảng/schema lệch | Cập nhật SQL `Get-Count` theo schema thực tế |
| `--no-build` lỗi thiếu binaries | Chưa build lần nào | Bỏ `-SkipBuild` để script build trước |

## Log

Auth/API redirect ra `.reset-logs/auth.log` + `.reset-logs/api.log` (+ `.err`). Thư mục này nên nằm trong `.gitignore`.
