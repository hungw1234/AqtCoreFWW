# Forms: `FormDataPreprocessor` JsonElement normalization DRY consolidation

**Date**: 2026-08-04 16:29
**Severity**: Medium
**Component**: Forms / FormDataPreprocessor
**Status**: Resolved

## Gì đã xảy ra

Đóng backlog row `FORMS-SUBMIT-PREPROCESSOR-JSONELEMENT-NCALC-GAP`. Bug tiềm ẩn (T4 post-v1.0): `FormDataPreprocessor` Formula branch truyền `rawData` thô vào `IFormulaEvaluationService.EvaluateAsync` mà không normalize `JsonElement` → NCalc 6.1.1 throw trên boxed JsonElement operand → formula trả về null im lặng. Mục ý của backlog row: "mirror Phase-4A P1's normalization". Commit `b0befc743`, branch `fix/forms-preprocessor-jsonelement-ncalc-gap`, chưa push. Forms.Application 375/375 tests; build 0 warn/0 err.

## Sự thật tàn khốc

Scout lộ rằng **backlog row "mirror P1" huy động một giả thuyết sai**: P1 không phải canonical, mà là **2-in-3 duplicate**. Cần DRY consolidate 3 JsonElement→CLR impls:
1. `JsonValueNormalizer` (canonical, recursive, long→decimal→double)
2. `FormulaBatchEvaluator.NormalizeJsonElementScalars` (private, scalar-only, shipped P1 cùng ngày 2026-08-03)
3. `FormDataPreprocessor.JsonElementToObject` (private, scalar-only)

Chốt decision: DRY tất cả qua canonical. Fix 2 private copy, cập nhật call-site. **Nhưng quyết định này kéo **BYPASS-AXIS** change** (change-impact-symmetry rule): tất cả 3 đều flow vào raw Dapper `parameters.Add(name, value)` mà Dapper phụ thuộc runtime-type, không kiểu schema. Regression-guard tồn tại sẵn = `FormSubmissionTableModeAtomicityTests` (Evaluate authed/public + insert Dapper). Code-review APPROVE-WITH-NITS 8.5/10; có 2 backward deltas lộ:

- **(a) Silent-loss → loud-reject, |x|>7.9e28**: old `GetDecimal()` throw→swallowed→silent-drop rows; new `canonical` return double→SQL overflow→ROLLBACK. Scenario: invalid government-data. **Quyết định: không guard, loud-reject ĐÚNG**, ghi documentation.
- **(b) Nested table-cell Array/Object**: old `GetRawText()` → JSON-string silent-accept; new `canonical` → List/Dict → Dapper NotSupportedException loud-reject. Scenario: adversarial input. **Quyết định: không guard, YAGNI**, FE table widget không bao giờ gửi nested cells (scalar only).

Cả hai backward delta = data-loss→error-throw, không-chạy-cũ → signal-CORRECT cho gov-data. Không cần guard; vô ích thêm trong v1.0 launch.

## Chi tiết kỹ thuật

**Backlog-row audit (INVARIANT 260522-A):** row nói "mirror P1 normalization" → implied ONE missing normalizer. Scout empirical grep:
- `JsonValueNormalizer` commit `a1c8e5a9` (2026-08-02 12:34, canonical)
- `FormulaBatchEvaluator.NormalizeJsonElementScalars` commit `40984df9f` (2026-08-03 14:51, P1 Phase-4A, scalar-only private)
- `FormDataPreprocessor.JsonElementToObject` line 79-90 (scalar-only private, hiệu quả là duplicate của P1, không canonical)

P1 **cũng là duplicate**, không là canonical. Backlog row "missing" = missing consolidation, không missing canonical.

**Bypass-axis verify** (ParseTableFieldRows / DeserializeJsonRow → Dapper.SqlMapper.Add):
- `TableFieldDataRepository.InsertRowsAsync(parameters)` → `parameters.Add(name, value)` (Dapper type-detect)
- Table cell columns: SCALAR SQL (FormTableMigrator.MapToSqlType: decimal(18,4) / nvarchar / date / bit / uniqueidentifier)
- Test coverage: FormSubmissionTableModeAtomicityTests 9/9 (Evaluate authed/public + table-mode cell insert, existing test lộ Dapper path)

**Diff stats**: -47 lines (xóa 2 private), +23 lines (canonical + call-site) = -24 net.

**Regression guard** (existing, covers this change):
```
FormSubmissionTableModeAtomicityTests
  + Evaluate form submission table-mode
  + Assert table cells persisted → Dapper insert → SQL
  + Covers runtime-type path (Bypass-axis)
```

## Đã cố gắng cái gì

- Backlog row claim direct = mirror P1 → first scout found P1 cũng duplicate
- Manual grep: 3x JsonElement→CLR normalize variant
- Empirical backward-delta audit: |x|>7.9e28 + Array/Object table-cell scenarios
- Verify test coverage: existing FormSubmissionTableModeAtomicityTests 9/9 xanh

## Root cause analysis

1. **Backlog-row stale metadata**: row nói "mirror P1" huy động giả thuyết P1 canonical, nhưng empirical = P1 cũng là duplicate. Lý do: backlog row được tạo sau Phase-4A P1 ship cùng ngày, chưa kịp identify P1 là duplicate.
2. **3 duplicate scatters**: JsonElement normalize logic xuất hiện 3 chỗ bởi vì đều là scalar-only case, 2 dev không biết canonical tồn tại → dân DRY ngay tại current-layer riêng.
3. **Bypass-axis not obvious**: chỉnh sửa normalization logic không rõ ràng kéo theo Dapper type-detection thay đổi (vì raw `parameters.Add(value)` phụ thuộc runtime-type). Người review cần empirical Bypass-axis verification, không phải chỉ logic-diff.

## Bài học

1. **Audit backlog-row claim = không trust row's implied hypothesis**: "mirror P1" → xác nhận P1 canonical thực tế hay duplicate trước quyết định. Backlog row names where bug NOTICED, không where it FULLY ROOTED. Grep toàn module, không chỉ cite-site.
2. **DRY consolidation flowing into Dapper = Bypass-axis change**: chỉnh sửa runtime-type normalize kéo theo Dapper type-detect → regression-guard phải là execution-path test (submit→Dapper insert), không logic-only unit-test. Verify guard tồn tại TRƯỚC quyết định DRY.
3. **Silent-loss → loud-reject on adversarial input = IMPROVEMENT gov-data**: không code guard để preserve silent data loss. `|x|>7.9e28` invalid number → ROLLBACK rõ ràng > silent-drop row. Tài liệu decision, không patch code.
4. **Private-private duplicate breed**: 2 private methods copy nhau ⇒ toàn bộ 1 chỉ duplicated bản public canonical. Grep-span tests/ + src/tests/ + Presentation khi refactor, không chỉ src/.

## Bước tiếp theo

1. ✅ **Consolidate 3 variants** → tất cả → `JsonValueNormalizer` canonical (đã ship)
2. ✅ **Verify Bypass-axis** → FormSubmissionTableModeAtomicityTests 9/9 xanh (guard tồn tại)
3. ✅ **Document backward deltas** (loud-reject improvement, YAGNI guard)
4. 🔜 **Push branch** → `fix/forms-preprocessor-jsonelement-ncalc-gap` → main (PR/merge TBD)
5. 🔜 **Update Forms.Application design-note** (optional v1.1+): centralize JsonElement→CLR normalize trong FormDataPreprocessor ctor / Options, không per-branch logic

---

**Unresolved questions:**
- **Should we centralize JsonElement normalize at FormDataPreprocessor ctor level?** (Out of scope v1.0, deferred v1.1 architectural polish; current per-branch normalize is safe + tested.)
