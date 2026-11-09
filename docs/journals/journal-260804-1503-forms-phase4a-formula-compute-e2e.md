# Forms Phase-4A: Formula compute end-to-end shipped (BE + FE + rate-limit)

**Date**: 2026-08-04 15:03
**Severity**: Low
**Component**: Forms / Formula evaluation
**Status**: Resolved

## Gì đã xảy ra

Slice cuối cùng của `FORMS-HARDENING-PHASE3-4`: endpoint batch-evaluate formula (BE authed + public) + FE reactive formula field + rate-limit policy mới. Commits `db85944d4` + `40984df9f` trên `feat/forms-phase4a-formula-compute-endpoint`, chưa merge/push main. BE 15 tests (9 authed + 6 public), FE 15 tests, Forms.Application 372/372, FE slice 95/95; code-review APPROVE-WITH-NITS 9.4/10.

## Sự thật tàn khốc

Empirical FE field probe (component `form-field-renderer.tsx:135`) xác nhận **không bind `name` là đúng** — plan nói bind, nhưng `antd Form.Item name=` OVERRIDE child `value=` → computed value không render. Bind name = tạo feedback-loop, `cleanFormData` đã strip Formula → vô ích. Phần thuận: xác minh qua kiểm tra render-test trước khi theo plan mù quáng.

Slice này mở dưới user-override khuyến nghị dừng (10 ships/ngày fatigue) → audit-at-slice-start (INVARIANT 260522-A) xác định Phase-3-4 phần lớn đã ship/tách, **chỉ 4A Formula-compute là real-ready**. Thế nhưng FE-only phần binding decision có **lệch plan 1 trục: không liệt kê lý do không bind rõ ràng** → rủi ro future dev lạo thao "theo plan cũ".

## Chi tiết kỹ thuật

**3 bài học kỹ thuật cụ thể, mỗi cái: triệu chứng → gốc lỗi → fix**

1. **Integration test bắt được bug mà mock-unit false-green (NCalc JsonElement arithmetic null-degrade)**
   - Triệu chứng: Handler-unit test stub-service NÓI OK, nhưng real integration test lộ silent null computed.
   - Gốc: ASP.NET bind `Dictionary<string,object?>` numeric scalar thành `JsonElement` (type mismatch). NCalc 6.1.1 throw trên boxed JsonElement arithmetic → sandbox `EvaluateAsync` catch-all nuốt exception → null computed trả về.
   - Fix: normalize `JsonElement → CLR` trong `FormulaBatchEvaluator` TRƯỚC gọi NCalc; verified bằng NCalc repro độc lập (không đoán).
   - **Bài học**: handler-unit / stub-service bypass NCalc hoàn toàn → integration-boot oracle PHẢI bật (liên hệ `[[feedback-tests-pass-feature-fails]]`).

2. **FE binding decision: không bind `name` = đúng (antd Form.Item override gotcha)**
   - Triệu chứng: plan nói bind `name`, nhưng render-test lộ giá trị computed không xuất hiện.
   - Gốc: antd `Form.Item name=` OVERRIDE child `value=` prop (binding gotcha); `cleanFormData` đã strip Formula khỏi submit → bind name vô ích, tạo feedback-loop surface.
   - Fix: không bind; giữ `value=computed` pure-prop → computed-only no-loop behavior.
   - **Bài học**: verify empirical (kiểm tra render) trước khi tuân theo plan cũ; phải ghi rõ *tại sao* deviation là đúng cho future maintainer.

3. **Rate-limit quota test: sequential ≠ concurrent (window-reset false-green)**
   - Triệu chứng: 30-concurrent serial → không fire 429; 60-concurrent `Task.WhenAll` → fire 429 (expected).
   - Gốc: tuần tự trải dài >60s (latency thật) → window reset trước khi quota đủ → false-green. Phải concurrent-30+ mới chứng minh 429 fire.
   - Fix: `Task.WhenAll` 60-concurrent permit check.
   - **Bài học**: timing assertion phụ thuộc wall-clock → phải concurrent-stress để uncover; linear test false-green (liên hệ `[[feedback-contention-flake-family]]`).

## Đã cố gắng cái gì

- Unit test (handler stub) → false-green do bypass NCalc.
- Sequential rate-limit test → false-green do latency trải dài.
- Plan binding = fact assumption → lộ lệch qua render-test.

## Root cause analysis

1. **Handler-unit / stub isolation**: handler-logic `if (formula == null) return null` không dí JsonElement boxing; integration-boot oracle phải trong acceptance suite.
2. **FE binding gotcha**: antd Form.Item `name=` là control-plane, `value=` child chỉ là display — collision không rõ ràng trong doc. Phải read antd implementation hoặc test-probe.
3. **Rate-limit test clock dependency**: quota test phải concurrent từ construction; sequential mask window-reset behavior.

## Bài học

- Integration-boot test (WebAppFixture) là **oracle không thể substitute bằng unit-stub** cho NCalc / JsonElement chaining.
- FE binding: khi `name=` OVERRIDE child `value=`, phải ghi rõ trong code comment *tại sao* không bind → future dev không lạo thao.
- Timing assertion phụ thuộc latency thực tế → concurrent-stress là oracle, sequential = false-green.
- Slice dài / fatigue → audit-at-slice-start INVARIANT 260522-A **phải chạy** trước execute; phát hiện Phase-3-4 phần lớn stale, chỉ 4A real-ready → tiết kiệm scope.

## Bước tiếp theo

1. **File T4 post-v1.0**: `FORMS-SUBMIT-PREPROCESSOR-JSONELEMENT-NCALC-GAP` — submit-time `FormDataPreprocessor:79-90` có **CÙNG** latent JsonElement/NCalc gap; fix = mirror normalize. Non-blocker (submit-time formula null-degrade an toàn).
2. **Plan deviation document**: cập nhật FE binding decision vào phase-4a doc, ghi *tại sao* không bind (antd Form.Item override gotcha).
3. **Merge main + push**: sau approve, merge `feat/forms-phase4a-formula-compute-endpoint` → main, push.

---

**Unresolved questions:** không có.
