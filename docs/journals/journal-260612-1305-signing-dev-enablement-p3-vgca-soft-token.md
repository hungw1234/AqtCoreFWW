# Journal — P3 VGCA Soft-Token Dev Enablement (Ban Cơ Yếu)

**Date:** 2026-06-12 · **Slice:** `/cook P3` + live smoke · **Commits:** `6920e3d1` (scaffolding) · `054be40a` (FE format fix) · `565e2b02` (BE thumbprint fix) — 6 ahead, not pushed (CI parked) · **Plan:** `signing-dev-enablement-viettel-vgca`

> **✅ LIVE SMOKE GREEN (260612):** VGCA soft-token Tier-1 ký thật qua desktop tool → BE log `VGCA USB Token Raw verify complete: IsValid=True`, chain 3-tầng đóng (intermediate store lazy-loaded). Hai bug verify-path lộ ra trong smoke + đã fix (xem cuối). P3 committable bar (Tier-1 raw verify IsValid=true) **ĐẠT**.

## Goal
Bật nhánh USB/soft-token VGCA trên dev (chạy QUA desktop tool VGCASignService). Cùng pattern P2: tự động hoá phần config/cert/docs; sign-smoke thật user-gated (desktop service + soft-token + browser WSS).

## Headline — P3 = config + ops + docs, **ZERO production code change** (verified empirical)
Cơ chế đóng chain 3-tầng mà VGCA cần đã do P1 lo. Verify từng claim trước khi tuyên bố "không cần code":
1. **`intermediates:null` path an toàn:** `UsbTokenSignatureProvider.VerifyRawAsync:381` truyền `intermediates:null`, nhưng `CertificateChainValidator` thêm `storeCandidates` vào `allCerts` ở **construction-time** (`:89,96`), không phải per-call → store candidate vẫn đóng chain. Không sửa provider.
2. **DI wired:** `VnCaIntermediateStore` singleton inject vào scoped validator qua optional ctor param (`InfrastructureRegistration.cs:179,200`).
3. **PEM-load risk (one-step-away) — checked & cleared:** trust dir là **PEM** (`-----BEGIN CERTIFICATE-----`) nhưng loader tên `LoadDer`. Nếu nó feed thẳng vào DER-only parser → PEM fail. Vì P2 smoke còn user-gated nên PEM-loading CHƯA từng runtime-verify. Grep store: cả 2 dùng BC `X509CertificateParser.ReadCertificate(bytes)` — auto-detect PEM armor → **OK, không bug**.

## Key decision — DRY: KHÔNG thêm test VGCA dù plan TDD step nominally đòi
Plan phase-03 TDD step 2 muốn "mở rộng `UsbTokenRawVerifyTests` với VGCA root thật". Probe empirical: P1 `CertificateChainValidatorTests` **test 8** (`RawPath_IntermediateInCandidatePool_ChainClosesWithoutIntermediatesArg`) đã prove leaf←intermediate (chỉ trong candidate store) + `intermediates:null` → chain đóng = **chính xác topology VGCA**; + test 9 pin-integrity + OCSP-on-raw-path. Thêm test real-cert VGCA = **trùng lặp (DRY) + giòn** (phụ thuộc cert expiry/OCSP network) cho **zero** mechanism coverage mới. Verified-decision-sticky: bỏ test mà plan đòi, vì empirical cho thấy redundant. (`UsbTokenRawVerifyTests` hiện mock chain validator → chỉ test sig-verify, không chạm chain-building — đúng tầng, không đổi.)

## Cert extraction (openssl `-legacy`)
`openssl pkcs12 -nokeys -legacy` (RC2-40-CBC, OpenSSL 3 disable mặc định) trên `RSA3072_*.p12` → 3-cert chain:
- Leaf `Test RSA3072` (user_test@bcy.gov.vn) — **bỏ** (signer, đến từ FE lúc verify)
- Intermediate `CA phục vụ các cơ quan Nhà nước G2` → `vgca-intermediate-nhanuoc-g2.pem`
- Root `RootCA chuyên dùng Chính phủ G2` (self-signed, serial `3B65B0A03006F9AB`, hạn 2018→2048) → `vgca-root-ca-g2.pem`

`openssl verify`: root self-signed OK + intermediate verifies against root OK. Lưu vào `other/signing-dev-roots/` (gitignored). Root VGCA = **Ban Cơ Yếu** (PKI Chính phủ), KHÁC root NEAC của Viettel (P2) → trust store dev giữ **2 root + 2 intermediate**, khẳng định thiết kế 2-PKI.

## Hygiene P0 — gitignore installer 109MB
`other/vgca_usb_token/tool/` (2 zip: VGCAv1_Installer 60M + bit4id_xpki 49M) untracked, **chưa** gitignore → `git add other/` vô tình sẽ bloat history vĩnh viễn. Thêm `other/vgca_usb_token/tool/` vào `.gitignore`. (`.msi` + `doc/` + `js/` + `sample/` đã tracked từ session trước — pre-existing, để nguyên.)

## Cross-runbook detail
Viettel + VGCA seed vào **CÙNG** array `Signing:TrustStore:RootCertPaths` → VGCA phải dùng **index 1** (Viettel index 0) để cùng tồn tại; nếu chỉ VGCA thì index 0. Ghi rõ trong runbook §2 để tránh ghi đè user-secrets.

## Deliverables (committable)
- `docs/ops/signing-dev-enablement-vgca-runbook.md` (mirror Viettel runbook: trích cert, user-secrets index-1, cài service + WSS accept, enable tenant `usb-vgca`, Tier-1 smoke, Tier-2 R3 best-effort)
- `.gitignore` (+`tool/`)
- Cert chain + plan/phase status (gitignored, không vào commit)

## User-gated remainder (per runbook)
Cài `VGCASignServiceSetup.msi` → nạp soft-token → accept WSS self-signed `127.0.0.1:8987` → set user-secrets cert paths index 1 → enable `TenantSignatureConfig` (`usb-vgca`, OcspMode Soft) → FE probe + Tier-1 `vgca_sign_msg` smoke → verify `IsValid=true`, chain 3-tầng đóng. Bằng chứng = log `has 2 trusted roots` + verify pass.

## Live smoke (260612) — 2 verify-path bugs lộ ra + fixed (`/ck-debug` ×2)

Stack chạy thật (`/aqt-runapp`: Vault re-seed dev-mode in-memory + Auth 5002 + API 5001 + FE 5173; VGCA service 8987). Startup log `SIGNING-HEALTH: has 1 trusted root(s)` — đúng warning gốc `contains 0 trusted roots` (mở ra cả arc) giờ resolved. Ký Tier-1 → 2 bug tuần tự:

### Bug 1 — FE Raw sig mis-routed → CMS parser (`054be40a`, 3 FE file)
`CMS signature could not be parsed: IOException`. VGCA Tier-1 trả Raw RSA + cert riêng, nhưng FE verify request **drop `format` + `signerCertBase64`** → BE default `Pkcs7` → CMS parser nuốt raw bytes. Mắt xích rớt: `signing-prompt onSuccess(sig, key)` vứt cert; `VerifyRequest` interface thiếu field. Fix: `onSuccess(result: SignResult)` + verify gửi `format:'Raw'`+cert khi có signerCert (Pkcs7 path giữ nguyên). FE 57/57 vitest · tsc 0.

### Bug 2 — BE trust root pin theo hash TEXT PEM, không phải DER (`565e2b02`, 2 BE file)
`Root certificate thumbprint '761A8D40…' is not in the VN CA trust store pin set`. 🔴 **Chính là latent PEM risk tôi flag lúc start P3** — phát tác ở tầng thumbprint: `VnCaRootStore:72 ComputeThumbprint(der)` hash **raw file bytes** = text PEM (`-----BEGIN…`+base64) → pin `32E9F3A2…`; pin-CHECK (`CertificateChainValidator:162`) hash `rootCert.GetEncoded()` = DER thật → `761A8D40…`. Cùng root, 2 hash → fail dù chain build đúng. Fix: hash `cert.GetEncoded()` (cert đã parse `:70`) → format-independent + khớp pin-check; 2 startup-log site cũng vậy. BE 23/23 signing unit. 🔴 **LESSON: "BC parser nhận PEM" CHỈ đúng cho parse — KHÔNG đúng cho thumbprint; phải hash DER của cert đã-parse (`GetEncoded()`), đừng bao giờ hash raw file bytes của trust anchor.** DER input không đổi (canonical → `GetEncoded()==der`).

### Code-review (DONE_WITH_CONCERNS) — bắt over-claim của tôi
Cả 2 fix PASS cho path đã smoke (VGCA). Reviewer bắt **CONCERN đúng**: câu "Fix 1 sửa luôn Viettel" của tôi SAI — BE provider-agnostic thật nhưng FE `remote-signing-adapter remoteSign()` **drop `signingCertificate.rawData`** → smoke Viettel (P2) sẽ đụng đúng lỗi CMS y hệt. Không phải regression (gap sẵn có). User chọn commit VGCA + file follow-up → `VIETTEL-FE-RAW-CERT` (T3). LESSON: "provider-agnostic ở BE" ≠ "wired ở FE" — verify từng adapter, đừng suy diễn từ 1 path.

## Open / carry-over
- **`VGCA-TIER2-SDK-VERIFY`** (R3): `signXml`/`signJson` params `SDK_VERIFICATION_PENDING` (8 sites) — đoán từ doc, chỉ token thật kiểm chứng; best-effort, KHÔNG gate P3 done.
- **P2 + P3** đều `🔄 in-progress` — chờ user smoke (OTP Viettel / desktop VGCA) trước khi mark completed.
- **3 commit unpushed** (`3c8f6508` P1 + `afc48c07` P2 + `6920e3d1` P3); CI parked từ tháng 4 → "ahead" ≠ CI-verified.
- **P4** (Forms verify-at-ingest) = feature cross-module thật, depends P2+P3 smoke pass.
