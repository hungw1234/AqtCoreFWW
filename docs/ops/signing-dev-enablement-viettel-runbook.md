# Runbook — Bật ký số Viettel Mysign (UAT) trên DEV

Phase P2 của plan `signing-dev-enablement-viettel-vgca`. Phần CODE đã verify khớp 100% API thật (login/CertInfo/SignHash, snake_case DTO, list-parsing) → **P2 là config + ops, không sửa code**. Chuỗi cert UAT đã trích sẵn (xem §1).

> ⚠️ **Bảo mật:** ClientSecret + CCCD (PII) + ClientId KHÔNG commit. Tất cả vào `dotnet user-secrets`. `appsettings.Development.json` chỉ giữ `BaseUrl` public.

---

## 0. Dữ kiện UAT (nguồn: `other/remote_signing_viettel/config_viettel_ca.txt` — gitignored)

| Field | Giá trị |
|-------|---------|
| BaseUrl | `https://remotesigning.viettel.vn` (đã set trong appsettings.Development) |
| ClientId | `EofficeVDB_0101382193` |
| ProfileId | `adss:ras:profile:001` |
| user_id (CCCD) | `017091004878` |
| ClientSecret | *(trong config txt — KHÔNG ghi ra ngoài)* |

**Chuỗi cert (đã verify qua live CertInfo 260612):**
```
Leaf  ĐINH QUỐC VIỆT (CCCD:017091004878)  →  Viettel-CA RS (intermediate)  →  Vietnam National Root CA / NEAC (root, self-signed)
```
Root = NEAC (public-CA PKI), KHÁC root VGCA (Ban Cơ Yếu) của P3 → trust store giữ cả 2 root.

---

## 1. Trích cert chain UAT (tái lập — đã chạy sẵn ra `other/signing-dev-roots/`)

Cert đã trích sẵn: `vn-national-root-ca.pem` (root) + `viettel-ca-rs-intermediate.pem` (intermediate). Leaf (PII) đã xóa. Thư mục gitignored. Tái lập khi cần:

```bash
# login → access_token → CertInfo (certificates:chain) → tách root+intermediate, bỏ leaf (PII)
# script: gọi POST /vtss/service/ras/v1/login rồi /vtss/service/certificates/info
#         body login = {client_id, client_secret, profile_id, user_id}
#         body certinfo = {..., "certificates":"chain", "certInfo":true, "authInfo":true} + Bearer token
# response certinfo = JSON array; cert.certificates = [leaf, intermediate, root] (base64 DER)
# Giữ root (self-signed) + intermediate; XÓA leaf (chứa tên+CCCD).
```

---

## 2. Set Viettel UAT credentials + cert paths qua user-secrets

Chạy trong `src/host/AqtCoreFW.Api/` (PowerShell):

```powershell
cd src/host/AqtCoreFW.Api
dotnet user-secrets set "Signing:Providers:ViettelMysign:ClientId"      "EofficeVDB_0101382193"
dotnet user-secrets set "Signing:Providers:ViettelMysign:ClientSecret"  "<lấy từ config_viettel_ca.txt>"
dotnet user-secrets set "Signing:Providers:ViettelMysign:ProfileId"     "adss:ras:profile:001"
dotnet user-secrets set "Signing:Providers:ViettelMysign:DefaultUserId" "017091004878"
# Trust store: ĐƯỜNG DẪN TUYỆT ĐỐI (TrustRootLoader resolve theo ContentRoot của Api host)
dotnet user-secrets set "Signing:TrustStore:RootCertPaths:0"         "C:\AqtCoreFW\other\signing-dev-roots\vn-national-root-ca.pem"
dotnet user-secrets set "Signing:TrustStore:IntermediateCertPaths:0" "C:\AqtCoreFW\other\signing-dev-roots\viettel-ca-rs-intermediate.pem"
```

> `DefaultMode` mặc định = `Sync` (code default) — không cần set cho smoke đầu.

---

## 3. Enable TenantSignatureConfig cho 1 tenant dev

Provider verify đọc `TenantSignatureConfig` (`VerifySignatureCommandHandler:71-92`: IsEnabled + ProviderKey + OcspMode). POST qua admin API sau khi app chạy:

- `IsEnabled = true`
- `ProviderKey = "remote-viettel-mysign"`
- `OcspMode = Soft` (responder UAT có thể unreachable → pass-warn; OCSP vẫn thực thi nhờ P1 issuer-from-chain)

> ⚠️ Row này **ephemeral per dev-DB-reset** — chạy lại sau mỗi reset (chấp nhận, không seed cho dev).

---

## 4. Chạy dev stack

```
/aqt-runapp --env dev          # Vault seed + Auth + Api + FE
```
Khi seed root thành công, log đổi từ `VnCaRootStore contains 0 trusted roots` (warn) → `has N trusted root(s)` (info).

---

## 5. Sign-smoke (Sync, async=0) — CẦN OTP MOBILE

1. FE → `signature-demo-page` (hoặc route demo ký số) → chọn provider **Viettel Mysign**.
2. Nhập payload → bấm Ký. App block tới `SyncSignTimeoutSeconds=120`.
3. **Xác nhận trên app Viettel của user test (CCCD 017091004878)** — OTP/push. ← bước này CHỈ người dùng làm được.
4. Nhận chữ ký raw RSA → FE POST payload+signature+signerCertDer tới `/verify`.

---

## 6. Kết quả mong đợi

- Verify trả `IsValid=true`, chain 3-tầng đóng (leaf→Viettel-CA RS→NEAC root) nhờ candidate pool P1.
- `OcspChecked=true` (P1 validator source issuer từ chain build). Nếu responder unreachable + OcspMode=Soft → `OcspSucceeded=false` nhưng `IsValid=true` (pass-warn).
- Audit log `SIGNATURE_VERIFIED` (providerKey, certSerial, certSubject, ocspChecked…) per QĐ-742.

---

## Trạng thái P2

| Việc | Trạng thái |
|------|-----------|
| Provider code khớp live API | ✅ Verified (login 4-field snake_case · CertInfo list-parse · chain extract) |
| Trích cert chain UAT | ✅ Done (root+intermediate saved, leaf PII deleted) |
| appsettings BaseUrl | ✅ `https://remotesigning.viettel.vn` |
| user-secrets (creds + cert paths) | ⏳ User chạy §2 |
| Enable TenantSignatureConfig | ⏳ User chạy §3 (cần app) |
| Sign-smoke (OTP mobile) | ⏳ User chạy §5 (chỉ người dùng) |

**Code rủi ro tồn:** CertInfo `cert.status` phải = `"valid"` (`ViettelMysignCertInfoClient.cs:102`) — nếu UAT trả status khác sẽ throw; lộ ra ở sign-smoke. Endpoint/auth khớp (live-verified login+certinfo 200).
