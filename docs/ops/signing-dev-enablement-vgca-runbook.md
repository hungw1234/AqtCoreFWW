# Runbook — Bật ký số VGCA USB-token / soft-token (Ban Cơ Yếu) trên DEV

Phase P3 của plan `signing-dev-enablement-viettel-vgca`. Chuỗi ký USB token Ban Cơ Yếu chạy **QUA desktop tool VGCASignService** (FE flow không đổi). Phần CODE = **0 thay đổi**: cơ chế đóng chain 3-tầng (leaf→intermediate→root, FE chỉ trả leaf) đã do candidate-pool `VnCaIntermediateStore` (P1) lo; provider `UsbTokenSignatureProvider.VerifyRawAsync` truyền `intermediates:null` vẫn đóng chain nhờ store candidates. P3 = trích cert + config + ops.

> ⚠️ **Bảo mật:** password p12 (`pass_*.txt`) + p12 (`*.p12`) + installer `tool/` đều gitignored. Root + intermediate VGCA là cert CA công khai → an toàn đặt trong trust dir (cũng gitignored để gom chung với leaf-PII của Viettel, re-trích qua §1).

---

## 0. Dữ kiện soft-token (nguồn: `other/vgca_usb_token/` — p12 + pass gitignored)

| Field | Giá trị |
|-------|---------|
| Soft-token p12 | `other/vgca_usb_token/RSA3072_785E3D6FFB9E6CE3.p12` |
| Password | *(trong `pass_RSA3072_785E3D6FFB9E6CE3.txt` — KHÔNG ghi ra ngoài)* |
| Desktop service | `VGCASignServiceSetup.msi` (đã cài trên máy dev) · WSS `wss://127.0.0.1:8987` |
| Plugin FE | `web/public/vendor/vgca/vgcaplugin.js` (đã vendored) |

**Chuỗi cert (đã verify qua openssl `-legacy` 260612):**
```
Leaf  Test RSA3072 (user_test@bcy.gov.vn)
  → intermediate  CA phục vụ các cơ quan Nhà nước G2
    → root  RootCA chuyên dùng Chính phủ G2 (self-signed, serial 3B65B0A03006F9AB, hạn 2048)
```
Root = **Ban Cơ Yếu** (PKI Chính phủ), **KHÁC** root NEAC của Viettel (P2) → trust store giữ cả 2 root + 2 intermediate.

---

## 1. Trích cert chain VGCA (tái lập — đã chạy sẵn ra `other/signing-dev-roots/`)

Cert đã trích sẵn: `vgca-root-ca-g2.pem` (root) + `vgca-intermediate-nhanuoc-g2.pem` (intermediate). Leaf (test signer) bỏ — nó đến từ FE lúc verify. Thư mục gitignored. Tái lập:

```bash
P12=other/vgca_usb_token/RSA3072_785E3D6FFB9E6CE3.p12
PASS=$(cat other/vgca_usb_token/pass_RSA3072_785E3D6FFB9E6CE3.txt)
# -legacy bắt buộc: p12 mã hoá RC2-40-CBC (OpenSSL 3 disable mặc định)
openssl pkcs12 -in "$P12" -passin "pass:$PASS" -nokeys -legacy 2>/dev/null \
  | awk '/BEGIN CERT/{c++} {print > ("cert_"c".pem")}'
# cert có subject==issuer = root (RootCertPaths); cert do root cấp = intermediate (IntermediateCertPaths)
# openssl x509 -in cert_N.pem -noout -subject -issuer   # để phân loại
```

---

## 2. Set cert paths qua user-secrets (cùng host với Viettel — KHÔNG ghi đè index)

Chạy trong `src/host/AqtCoreFW.Api/` (PowerShell). VGCA dùng **index 1** vì Viettel đã chiếm index 0 — cả 2 seed vào CÙNG array `Signing:TrustStore`:

```powershell
cd src/host/AqtCoreFW.Api
# Trust store: ĐƯỜNG DẪN TUYỆT ĐỐI (TrustRootLoader resolve theo ContentRoot Api host; BC parser nhận PEM)
dotnet user-secrets set "Signing:TrustStore:RootCertPaths:1"         "C:\AqtCoreFW\other\signing-dev-roots\vgca-root-ca-g2.pem"
dotnet user-secrets set "Signing:TrustStore:IntermediateCertPaths:1" "C:\AqtCoreFW\other\signing-dev-roots\vgca-intermediate-nhanuoc-g2.pem"
```

> Nếu CHỈ test VGCA (không Viettel) → dùng index `:0`. Nếu chạy cả 2 provider cùng lúc → Viettel `:0`, VGCA `:1` (array không trùng).

---

## 3. Cài + chạy VGCASignService + nạp soft-token

1. Cài `other/vgca_usb_token/VGCASignServiceSetup.msi` (đã cài trên máy dev). Service expose `wss://127.0.0.1:8987`.
2. Nạp soft-token p12 vào tool (theo `doc/HDSD_VGCASignService.pdf`) — password ở `pass_*.txt`.
3. **Browser accept self-signed WSS một lần:** mở `https://127.0.0.1:8987` → trust cert → đóng. (Chrome chặn WSS self-signed tới khi accept thủ công.)

---

## 4. Enable TenantSignatureConfig cho 1 tenant dev

Provider verify đọc `TenantSignatureConfig` (`VerifySignatureCommandHandler:71-92`). POST qua admin API sau khi app chạy:

- `IsEnabled = true`
- `ProviderKey = "usb-vgca"`
- `OcspMode = Soft` (responder VGCA có thể unreachable → pass-warn; OCSP vẫn thực thi nhờ P1 issuer-from-chain)

> ⚠️ Row **ephemeral per dev-DB-reset** — chạy lại sau mỗi reset.

---

## 5. Chạy dev stack

```
/aqt-runapp --env dev          # Vault seed + Auth + Api + FE
```
Khi seed root thành công, log đổi `VnCaRootStore contains 0 trusted roots` (warn) → `has N trusted root(s)` (info). N = 2 nếu seed cả Viettel + VGCA root.

---

## 6. Sign-smoke Tier-1 (committable bar) — CẦN DESKTOP SERVICE + SOFT-TOKEN

1. FE → `signature-demo-page` → chọn provider **USB VGCA**.
2. FE probe `isVgcaAgentAvailable` → `true` (service chạy + WSS accepted).
3. Ký Tier-1 `vgca_sign_msg(sender, {HashAlg:SHA256, HashValue:<b64>, Encode:RSA}, cb)` → `{Status:0, Signature, CertBase64}`.
4. FE POST payload + Signature + CertBase64(→SignerCertDer) tới `/verify`.

---

## 7. Kết quả mong đợi

- Verify trả `IsValid=true`, chain 3-tầng đóng (leaf→CA Nhà nước G2→RootCA G2) nhờ candidate pool P1.
- `OcspChecked=true` (P1 validator source issuer từ chain build). Responder unreachable + OcspMode=Soft → `OcspSucceeded=false` nhưng `IsValid=true` (pass-warn).
- Audit log `SIGNATURE_VERIFIED` (providerKey=usb-vgca, certSerial, certSubject, ocspChecked…) per QĐ-742.

---

## 8. Tier-2 (R3 — best-effort, KHÔNG gate P3 done)

`signXml`/`signJson` (`vgca-usb-adapter.ts`) mang marker `SDK_VERIFICATION_PENDING` (8 sites) — params (`XmlDsigForm`/`IsPss`) đoán từ doc, chỉ token thật kiểm chứng. Đối chiếu `doc/HD-tichhop-VGCASignService.pdf` + sample `sample/test-bc.htm`/`DefaultECC.htm` TRƯỚC khi thử. Nếu BE verify fail do params → tinh chỉnh + gỡ marker + cập nhật `StructuredVerifyTests`. Nếu chưa xong → file follow-up `VGCA-TIER2-SDK-VERIFY`, KHÔNG block P3.

---

## Trạng thái P3

| Việc | Trạng thái |
|------|-----------|
| Cơ chế đóng chain 3-tầng (candidate pool) | ✅ P1 (`CertificateChainValidatorTests` test 8/9 + pin-integrity + OCSP-raw-path) — DRY, không thêm test |
| Provider code (`UsbTokenSignatureProvider`) | ✅ 0 thay đổi (`intermediates:null` dùng store candidates) |
| PEM-load capability | ✅ Verified (BC `X509CertificateParser.ReadCertificate` nhận PEM armor) |
| Trích cert chain VGCA | ✅ Done (root+intermediate saved gitignored, leaf bỏ) |
| Installer `tool/` gitignored | ✅ `.gitignore` (109MB binary không vào history) |
| user-secrets (cert paths index 1) | ⏳ User chạy §2 |
| Cài service + nạp token + WSS accept | ⏳ User chạy §3 (chỉ máy dev) |
| Enable TenantSignatureConfig usb-vgca | ⏳ User chạy §4 (cần app) |
| Sign-smoke Tier-1 | ⏳ User chạy §6 (desktop service + token) |
| Tier-2 (R3) | ⏳ best-effort, có thể file follow-up |

**Lưu ý:** không chạy headless → smoke thủ công, không CI gate (CI parked sẵn từ tháng 4). Bằng chứng đóng chain = log `has 2 trusted roots` + verify `IsValid=true` lúc smoke.
