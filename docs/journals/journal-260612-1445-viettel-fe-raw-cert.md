# Journal — VIETTEL-FE-RAW-CERT: FE raw-cert wiring for Viettel Mysign verify

**Date:** 2026-06-12 · **Slice:** `/cook tiếp VIETTEL-FE-RAW-CERT` · **Commit:** `795612c6` — 8 ahead, not pushed (CI parked) · **Plan:** `signing-dev-enablement-viettel-vgca` (P2)

## Goal
Đóng follow-up `VIETTEL-FE-RAW-CERT` filed từ VGCA-smoke code-review CONCERN-1: FE verify path đã Raw-aware (commit `054be40a`) nhưng chỉ VGCA adapter populate `signerCertBase64`; Viettel Mysign sign qua demo page sẽ đụng lại lỗi CMS y hệt. Mục tiêu = unblock P2 Viettel sign-smoke (sync mode) qua demo page.

## Headline — scout phát hiện gap LỚN HƠN backlog mô tả: 2 bug, không phải 1
Backlog row chỉ ghi "remoteSign drops rawData". Scout BE/FE empirical lộ thêm bug chặn-cứng đứng TRƯỚC:
1. **Registry FE chưa hề register key `remote-viettel-mysign`** — D19 contract (`TenantSignatureConfig.cs:16`) chỉ còn 2 key hợp lệ `usb-vgca` + `remote-viettel-mysign`, VNPT SmartCA deprecated. Nhưng `remote-signing-adapter.ts` chỉ register `remote-vnpt-smartca` (deprecated) + generic `remote`. Khi tenant config `providerKey='remote-viettel-mysign'` → `resolveAdapter` (`registry.ts:96-99`) **throw "No adapter registered"** → sign fail trước cả lỗi CMS. Registry FE stale so với D19 BE contract.
2. **`remoteSign` drop `signingCertificate.rawData`** (bug gốc filed).

🔴 **LESSON: 1 grep trên backlog claim ≠ ground truth.** Backlog row mô tả gap FE-only "map rawData"; scout đúng cả 2 phía (BE provider key + capabilities + FE registry) mới thấy bug registization chặn-cứng đứng trước. INVARIANT 260522-A (audit-at-slice-start) pay-off lần nữa.

## Verified facts (scout-first, trước khi sửa)
- `ViettelMysignProvider.cs:69` key `remote-viettel-mysign`; `:81-82` Capabilities `ServerSideSigning | RawSign` (raw RSA PKCS#1 v1.5, no CMS).
- `:768-775` build `CertificateInfo(..., RawData: certDer)` — **cert ĐƯỢC populate** (mấu chốt: nếu rỗng thì cả fix vô nghĩa). `SignPayloadCommand.cs:128` trả nó trong DTO → FE `SignResponse.signingCertificate.rawData` (base64 DER).
- BE VerifyRequest default `hashAlg="SHA256"`/`encode="RSA"`; Viettel signHash dùng SHA256 OID.

## Open-Q (filed cùng row) ANSWERED
"Viettel verify qua demo `handleVerify` hay async callback riêng?" → UAT chạy **sync mode (async=0, plan P2)** → `signHash` trả signature + cert đồng bộ → qua shared `/verify` của demo `handleVerify`. Async callback (isPending) defer (plan P-async-callback, out-of-scope). → gap là THẬT.

## Fix design — parameterize theo format, KHÔNG map cert mù quáng
Demo page (`signature-demo-page.tsx:112`) discriminate `isRaw = !!signResult.signerCertBase64`. Nếu map `rawData → signerCertBase64` vô điều kiện cho MỌI remote provider → VNPT SmartCA (Pkcs7, CMS embed cert) sẽ bị flip sai sang Raw → **regression verify path của nó**. Giải: thêm param `isRawProvider = false` vào `remoteSign`; chỉ set cert + `hashAlg='SHA256'` + `encode='RSA'` khi `isRawProvider && signingCertificate?.rawData`. Register `remote-viettel-mysign` (isRaw=true); `remote-vnpt-smartca` + `remote` giữ Pkcs7 (no cert map). Trade-off: 1 param thay vì map vô điều kiện — đáng, 3 key chia 2 nhóm format.

## Test — TC-DSIG-REMOTE-006/007 (4 test mới)
- 006: `hasAdapter('remote-viettel-mysign')` true + map cert/alg đúng (`toEqual` đủ 7 field) + pending path không có cert.
- 007 (adversarial, đúng guard review yêu cầu): VNPT SmartCA **dù BE trả cert vẫn KHÔNG** emit `signerCertBase64` → verify ở Pkcs7 branch (no regression). TC-002 cũ (`toEqual` không có signerCertBase64) giữ green.

## Verification
tsc 0 · adapter 13/13 (9 cũ + 4 mới) · digital-signature suite 61/61 (57 + 4, 0 regress) · code-review **APPROVE** (gating đúng, pending an toàn, 0 contract drift). No BE change.

## Carry-over (2 follow-up filed từ review)
- **`VIETTEL-SIGN-ALG-ECHO`** (T4): `hashAlg/encode` hardcode SHA256/RSA = silent FE/BE coupling. Safe cho Viettel D19 hiện tại, nhưng nếu Viettel cấp cert ECDSA/sign SHA384 → mismatch verify. Fix: BE echo alg trong `SignResponse` HOẶC per-adapter config. Blocked-on: xác nhận Viettel UAT population có key ECDSA không.
- **`VNPT-SMARTCA-ADAPTER-DEPRECATE`** (T4): gỡ registration `remote-vnpt-smartca` khi probe xác nhận 0 tenant tham chiếu. Blocked-on: prod-data probe (template repo no prod → downstream-ops).
- **P2 live Viettel OTP sign-smoke** vẫn user-gated (mobile OTP) — chưa chạy.
- 8 commit unpushed; CI parked từ tháng 4 → "ahead" ≠ CI-verified.
