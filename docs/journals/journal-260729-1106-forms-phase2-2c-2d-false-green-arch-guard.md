# Journal — Forms Phase-2 slice 2C+2D: test-only launch-safety + a false-green arch-guard caught in review

**Ngày:** 2026-07-29 11:06 ICT · **Nhánh:** main · **Loại:** cook (Phase-2 partial) · **Plan:** `260728-1703-dynamic-forms-flagship-hardening`

## Session goal

Mở Phase-2 (launch-safety). 4 sub-task khác nhau nhiều → chọn **2D+2C trước** (test-only, low-risk) để đóng 2 box launch-safety chắc chắn trước khi đụng 2A (Formula sandbox, cần concurrency load-test) và 2B (export streaming, đổi contract).

## What worked (verified)

**Audit ground-truth cứu khỏi viết trùng (INVARIANT 260522-A):** scout phát hiện 2 test đã tồn tại — 2D bypass-clear = `PublicFormsLeastPrivilegeTests.PublicFormsBypassFlag_DoesNotLeak_AfterRead` (verified bằng mắt), 2C `OcspChecked=false` = `CertificateChainValidatorTests`. Slice co từ "4 test mới" xuống "2 test mới + 2 note".

**2D rate-limit 429 (`PublicFormSubmitRateLimitTests`, NEW):** insight — rate-limit `public-form-submit` chặn ở MIDDLEWARE trước MediatR → test không cần seed template hợp lệ, chỉ 10× anonymous POST cùng IP → assert có 429. PASS (1/1, 1m43s). Review xác nhận right-reason: 429 từ đúng policy quota=5 (env Testing → non-dev), không phải global anon limiter=20; `Gateway:Mode=Disabled` không tắt app-level limiter; `RateLimiting:Enabled` không bị set false trong `WebAppFixture`.

**2C security-note descope + arch-guard (`OcspClientNotRegisteredArchTests`, NEW):** `IOcspClient` 0-DI → `CertificateChainValidator` chạy `ocspClient=null` → `OcspChecked=false`. Arch-guard chặn ai đó silent-wire OCSP không timeout. 2 security-note → `docs/system-architecture.md`.

## What didn't / lessons

**CRITICAL false-green trong arch-guard — code-review bắt, tôi verify nông đã miss.** Guard 2C ban đầu PASS nhưng RỖNG: `AddIdentityInfrastructure` throw ở OpenIddict `AddValidation` (thiếu `Auth:AuthorityUrl` trong stub config) TRƯỚC signing block (line 207+), `TryInvokeRegistration` nuốt exception → `IOcspClient` registration site không bao giờ chạy → guard green-light chính regression nó quảng cáo chặn. Reviewer chứng minh empirically (`hasICertificateChainValidator=False`).

- **Tôi đã verify "method được invoke" nhưng KHÔNG verify "method chạy tới đoạn signing".** Đúng bài học `feedback-test-new-wiring-through-real-registration` + `feedback-verify-subagent-reports`: verify phải tới đúng đoạn cần test, không dừng ở "có gọi".
- **Fix**: (1) thêm `Auth:AuthorityUrl` vào stub config để method chạy qua line 112; (2) **sentinel assertion `ICertificateChainValidator` PHẢI present** ĐỨNG TRƯỚC ocsp-absent assertion — nếu tương lai early-throw thì sentinel fail → test green→red (self-guarding). Re-run PASS + sentinel-present = bằng chứng signing block đã chạy = ocsp-absent assertion có nghĩa thật.
- **Đây là lý do code-review mandatory kể cả cho test-only slice.** Một arch-guard pass-rỗng còn tệ hơn không có — nó cho cảm giác an toàn giả.

**2 nit doc:** cite public entry point (`QueryFirstOrDefaultWithPublicFormsBypassAsync`) thay helper private; OCSP-note claim "guard fails if wired" giờ đúng sau fix.

## Decisions made

- Thứ tự Phase-2: 2D+2C (test-only) trước, 2A+2B (code + oracle nặng) sau — mỗi cái slice riêng.
- 2C giữ descope OCSP (user chốt 07-28 v1.0 no OCSP) — không mở lại.
- Arch-guard tự-bảo-vệ bằng sentinel, không tin "invoke = executed".

## Test status

- `OcspClientNotRegisteredArchTests`: PASS + verified có răng (sentinel).
- `PublicFormSubmitRateLimitTests`: PASS 1/1 (testcontainer 1m43s).
- Full `AqtCoreFW.Architecture.Tests`: **204 passed, 0 fail** (arch-guard mới không phá blast-radius).
- Test-only slice — 0 production code changed, 0 business-logic regression khả dĩ.

## Next session

1. **2A Formula sandbox** (H2) — enumerate+block NCalc built-in loop, bound eval (step-limit/semaphore), depth-guard thật; oracle = concurrency load-test ≥50 (cần app-up). Slice khó nhất.
2. **2B export streaming** (M2) — Phase-1 verdict CẦN; mở rộng `IReadDbConnection` + impl streaming method (enumerator own connection), refactor handler byte[]→stream, controller `File(stream)`.
3. **HARD GATE** sau Phase-2 đủ: Forms vs 5 `[blocks-v1.0]`.
