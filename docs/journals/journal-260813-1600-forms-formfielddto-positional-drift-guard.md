# Journal - FormFieldDto Positional-Drift Guard + Named-Args Migration

**Date:** 2026-08-13 (afternoon session, ~1h30m including context-compact recovery)
**Commit:** `f14042441` on `main`
**Row closed:** `FORMS-FORMFIELDDTO-POSITIONAL-CONTRACT-TEST-DRIFT` (T3 `[v1.1]` hygiene)

## Context

Row filed 2026-08-12 từ M1 Phase-07 round-2 review M-A: contract test `FormsApiContractTests.cs:17-19` gọi `new FormFieldDto(...)` với 11 positional args. Phase-07 M1 append `ParentFieldId` cuối record an toàn vì default=null, nhưng bất kỳ tương lai insert MID-record → silently shift positional args, mask drift — chính là trap `feedback-formtemplate-addfield-ispii-positional-trap` (13th late-optional PII flag pushed by name-omission → PII path went dead false-green).

Chiều 2026-08-13, `/aqt-next` sau khi đóng row `FORMS-CASCADE-INROW-GUARD-N-PLUS-1-DEDUP` xếp hạng row này là T3 hygiene tiếp theo trong scope B. User override fatigue recommendation "EOD wrap" bằng "tiếp scope B".

## What shipped

### Belt-and-braces close (2 remediation paths)

**Path A - Named-args migration** (`tests/contracts/AqtCoreFW.Tests.Contracts/Api/FormsApiContractTests.cs:23-34`):
- 11 positional args → 11 named args (`Id:`, `FieldKey:`, `Type:`, `LabelVi:`, `LabelEn:`, `DisplayOrder:`, `Required:`, `IsActive:`, `AddedInVersion:`, `ValidationRules:`, `Options:`).
- 4 tail-optional params (`VisibilityRulesJson`, `RequiredIfJson`, `PrefillSourceJson`, `ParentFieldId`) omitted vì all default null trong record definition.
- Comment inline reference `feedback-formtemplate-addfield-ispii-positional-trap` + arch guard cross-reference cho future editor.

**Path B - Arch guard test** (`tests/architecture/AqtCoreFW.Architecture.Tests/FormFieldDtoPositionalConstructionGuardTests.cs`, NEW 219 LOC):
- Text-scan-based (same idiom `MessageRetryConfigGuardTests.cs` — text scan chấp nhận được cho ranh giới project, Roslyn = over-engineering).
- Enumerate `.cs` files dưới `src/` + `tests/`, exclude `bin/`, `obj/`, `.claude/worktrees/`.
- Allowlist Application layer path `src/modules/forms/AqtCoreFW.Forms.Application/` (handlers own DTO, drift lộ ngay next to them).
- Self-exclude guard file (raw-string literals lines 111 + 127 chứa `new FormFieldDto(` → self-flag risk).
- Regex `\bnew\s+FormFieldDto\s*\(` + named-arg marker `^\s*[A-Za-z_][A-Za-z0-9_]*\s*:(?!:)` (loại `::` namespace-qual + `?:` ternary).
- 3 scanner-oracle unit tests:
  1. `Scanner_DetectsPositionalConstruction_AsViolation` - inline positional literal, assert scanner flag.
  2. `Scanner_AcceptsNamedArgumentConstruction` - inline named-args literal, assert scanner pass.
  3. `Scanner_IgnoresApplicationLayerPath` - path prefix win/nix separator, assert allowlist correct.

**Bonus fix-in-passing:** replace 2 em-dash (U+2014) trên lines 7-8 của contract test bằng hyphen (CLAUDE.md non-negotiable rule).

## Blast Radius & Đối xứng (change-impact-symmetry.md)

- **Backward:** `FormFieldDto` public signature UNCHANGED (positional record 15 params intact). Application-layer positional call-sites (`GetFormTemplateQueryHandler.MapField:71,92` + `GetPublicFormQueryHandler:91`) intentionally allowlisted — không đụng. No consumer breaking change.
- **Downstream:** không lan. `FormFieldDto` DTO shape identical, chỉ style of construction thay đổi ở test project. Zero cross-module client change.
- **Symmetry:** không thêm field/enum/route/DTO/migration. Arch guard là NEW test file, không phải shared schema.
- **Bypass:** text-based scanner có thể bị dodge qua Roslyn-generated code, `#pragma`, partial-naming (first-named-then-positional legal syntax). Realistic regression class (whole positional call-site) IS covered. Limitation documented in doc-comment.

## Verify

- Arch build (`AqtCoreFW.Architecture.Tests.csproj`): 0W/0E `/warnaserror`, 11m42s
- Contracts build (`AqtCoreFW.Tests.Contracts.csproj`): 0W/0E `/warnaserror`, 4m27s
- Arch test filter `FormFieldDtoPositionalConstructionGuardTests`: 4/4 pass, 9s
- Grep verify: positional `new FormFieldDto(` outside Application layer = 0 sites (down from 1 pre-change)
- Em-dash grep 2 file touched: 0 hit

## Code review

**Verdict: DONE_WITH_CONCERNS - 0 crit/high/med, 1 low (pre-existing em-dash, fixed in-slice).**

Reviewer probed 8 edge cases (guard false-negative, guard false-positive, allowlist correctness, repo-root walk, migration completeness, `#if` preprocessor, multi-line comment between `(` and first arg, `.claude/worktrees/` exclusion). All correctly handled or documented as accepted limitation.

Positive observation từ reviewer: "Scanner unit tests are the right defense... Without them, a future regex refactor could silently degrade the main guard to always-pass. These three tests are the oracle for the oracle."

## Lessons

1. **Belt-and-braces close beats either-or.** Row filing gợi ý "migrate OR add arch guard". Ship cả 2: migration = fix TODAY's drift; arch guard = prevent TOMORROW's drift on new call-site. Cost thêm 219 LOC nhưng zero risk vì self-tested.

2. **Scanner-oracle tests là defense đúng cho arch guard.** Guard bản thân là test → cần test cho test. 3 scanner-oracle unit tests (detect / accept / allowlist) prove regex catch what it should, ignore what it shouldn't. Ngăn regression class "regex refactor silently degrades to always-pass" mà không detectable qua CI output (test vẫn "pass" nhưng vì scanner never fires).

3. **Doc-comment cite lesson theo tên (`feedback-formtemplate-addfield-ispii-positional-trap`).** Future dev gặp arch test fail sẽ grep lesson slug và land ngay vào trap definition với case study PR-137 M1 M-A. Compare positively vs "silent guard, mystery failure" antipattern.

4. **Application-layer allowlist là product decision, không phải hack.** Handlers OWN DTO — any param-shape change land next to them, drift obvious. Test/cross-module/presentation call-sites ở arm's length → phải fail loud. Doc-comment giải thích rõ để phân biệt "positional legal" khỏi "positional allowlisted".

5. **Text-scan chấp nhận được, Roslyn over-engineering.** Same idiom `MessageRetryConfigGuardTests.cs` — realistic regression IS caught, adversarial evasion (Roslyn-generated) không ai làm accidentally. Effort-cost saving rất lớn (Roslyn setup = weeks) cho zero incremental risk.

## Metrics

- **Time:** ~1h30m (~30min plan+write, ~15min compact recovery + re-orient, ~15min build wait, ~5min run test, ~5min code-reviewer delegate + wait, ~10min fix L1 + commit + journal)
- **LoC:** +239/-5 (arch guard +219, contract migration +12/-3, backlog +1/-1, em-dash fix +2/-2)
- **Tests:** 4 new arch tests (1 main guard + 3 scanner-oracle), 4/4 pass; positional call-sites outside Application layer: 1 → 0
- **Build:** 0W/0E `/warnaserror` cả 2 project
- **Commits:** 1 (`f14042441`)
- **Push:** TODO (user chọn timing)

## Unresolved

None. Reviewer flagged 3 low-priority observations:
- L1 em-dash: fixed in-slice
- L2 hypothetical false-positive on comment/string literal containing `new FormFieldDto(`: not present in repo today, non-blocking
- L3 path-prefix allowlist edge case (future `AqtCoreFW.Forms.Application.Something/`): verified prefix ends in separator, cannot silently allowlist siblings — non-issue
