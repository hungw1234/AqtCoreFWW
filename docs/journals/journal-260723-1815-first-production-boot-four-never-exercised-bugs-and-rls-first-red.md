# Lần Production boot đầu tiên: 4 bug ẩn, và cái RLS red đầu tiên có giá trị hơn xanh

**2026-07-23 18:15 · plan `260723-1258-k8s-local-stable-monolith-deploy` P2 · Docker Desktop kind**

## Chuyện gì

Sau P1 (helm install lần đầu), chiều nay là `ASPNETCORE_ENVIRONMENT=Production` lần đầu tiên
trong lịch sử project — vẫn trên kind local, nhưng mọi guard "chỉ chạy khi Production" giờ mới
thực sự chạy. Scout đầu slice lộ 2 hard-block (VNeID guard throw unconditional, OpenIddict
không có nhánh cert non-Dev) → user chốt option B: 3 escape hatch rehearsal-only
(`VneId:AllowStub`, `Auth:AllowHttp`, `ForceHttps=false`), default-false, chỉ nằm trong
values-local, cả hai nhánh unit-test (11 test). Không tắt guard, chỉ mở cửa hẹp để rehearsal
chạy được.

## Validator sống đúng nghĩa lần đầu

`ProductionSecretValidator` chạy cả hai nhánh lần đầu tiên: nhánh âm trước — Vault còn dev-marker
→ migrate Job CrashLoop với message `Production secret missing or contains DEV marker:
Encryption:FieldKey`, helm exit 1 (đúng như thiết kế). Rồi nhánh dương — random key thật → Job
Completed, pod Ready 38s (so với ~10 phút bên Dev), smoke tất cả xanh.

## 4 bug chưa từng exercise — cùng họ với 10 bug P1

- **VaultConfigurationProvider thiếu `mountPoint`** → VaultSharp default rơi về `kv-v2` → 404.
  Provider này CHƯA TỪNG chạy (`Vault:Enabled=false` ngoài Production). `VaultKeyResolver.cs:66`
  đã truyền mountPoint tường minh — chỉ config provider quên.
- **rabbit probe login bằng user `''`** — appsettings ship `Username: ""`, code `"" ?? "guest"`
  giữ nguyên chuỗi rỗng (không phải null) → MassTransit tình cờ xanh nhờ hai chuỗi rỗng khớp
  nhau, không phải vì đúng.
- **`sqlcmd :setvar` trong file ĐÈ `-v` truyền vào** — NonAudit login bị tạo âm thầm với đúng
  placeholder password literal. Lỗ bảo mật thật cho DBA prod, không phải lý thuyết.
- **`NonAuditConnectionString.Get` dùng `??`** — Vault key rỗng (không null) crash mọi DbContext.

Bài học: họ `"" vs null` cắn BA lần trong một buổi (rabbit, connection string, và `:setvar` cũng
cùng hình dạng — "default thắng override tưởng đã truyền vào").

## RabbitMQ resolve sống

Tắt broker → service Degraded nhưng HTTP vẫn 200 → bật lại → Healthy, `ProbeConnectionSource`
tự reconnect. 65 probe → ổn định 2 connection (1 bus + 1 probe cache) — lớp leak 8.6k/ngày coi
như chết hẳn.

## Cái red có giá trị nhất buổi

NonAudit app-activation = lần đầu tiên boot bằng login KHÔNG PHẢI db_owner → RLS FILTER lần đầu
sống thật → `NotificationTemplateSeeder` check-then-insert bằng `AnyAsync` mù hoàn toàn (FILTER
giấu row tenant-zero khỏi scope không có SESSION_CONTEXT) → đâm unique index (index không bị RLS
filter) → CrashLoop. Mọi test trước giờ chạy bằng `sa` nên xanh giả — đúng như memory
`feedback-sa-dbowner-false-greens-rls-assertions` đã cảnh báo trước. 12 seeder khác dùng chung
pattern. User chốt: file row `SEEDER-RLS-BLIND-CHECK-THEN-INSERT`, rollback, acceptance partial.
Cái red này CHÍNH LÀ giá trị của rehearsal, không phải thất bại của nó — nếu xanh giả như mọi
lần trước thì bug này chỉ lộ ở prod thật.

## Seed-posture bằng chứng (§4-Q2, chờ user quyết)

Dưới Production: 0 user, 0 tenant (không login được), 2 OIDC client (seeder chạy mọi env nhưng
RedirectUris hardcode localhost → row mới), 11 notification template + masterdata + systemparams
(seeder không gate chạy hết). TenantBackfillSeeder chạy, backfill 0 row.

## Review: DONE_WITH_CONCERNS, 0 blocker

M1 — claim của tôi "SetIssuer không đổi cho prod" SAI cho path helm-base: env này trước đây
chết âm thầm, giờ pin issuer thật (fix có chủ đích, đã ghi vào commit). M2 — fix `IsNullOrEmpty`
đổi loud-fail thành silent guest-fallback, follow-up đã note vào backlog.

## Kết quả

Status: DONE. Production boot rehearsal lần đầu chạy trọn: 4 bug root-fix, RabbitMQ resolve
sống, RLS-blind seeder bug phát hiện qua non-db_owner boot thật (không phải audit tĩnh) — filed
`SEEDER-RLS-BLIND-CHECK-THEN-INSERT` cho 12 seeder cùng họ, rollback + acceptance partial chờ
user quyết seed-posture.
