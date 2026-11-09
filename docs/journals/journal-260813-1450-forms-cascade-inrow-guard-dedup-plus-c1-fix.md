# Journal — Forms Cascade In-Row Guard N+1 Dedup + C-1 Fix

**Date:** 2026-08-13 (afternoon session, ~1h)
**Commits:** `dfc2366e9` (A docs) · `75309d115` (B code) · `6a80cf97a` (backlog close-out) — all on `main`
**Row closed:** `FORMS-CASCADE-INROW-GUARD-N-PLUS-1-DEDUP` (T3 `[v1.1]`)
**Row filed:** none new (M-2 payload cap = non-issue, existing cap at `TableFieldDataRepository:17`)

## Context

Sáng cùng ngày squash-merge PR #137 (`3012e8c65`) đóng slice `FORMS-CASCADE-PUBLIC-EXTERNAL-INROW` sau 4 vòng CI recovery. Slice để lại 2 follow-up row `[v1.1]` do reviewer M1 phase-06 (H-1 + H-2). `/aqt-next` chiều xếp hạng: (A) đóng plan status housekeeping + (B) chỉ ship H-2 (dedup, unblocked); H-1 (Internal parent-filter) blocked-on real Internal-DS consumer.

## What shipped

### A — Plan status close (`dfc2366e9`, 3+/3-)

`plans/260805-1409-forms-cascade-public-external-inrow/plan.md` frontmatter `status: in-progress` → `done`; Phase 06 + Phase 07 rows tick DONE với hash tương ứng. Fix drift cho `aqt-next` / `watzup` các phiên sau. Backlog row đã DONE trong `993434b03` từ sáng.

### B — Dedup guard refactor (`75309d115`, 251+/7-)

**Structure:** `InRowCascadeMembershipValidator.ValidateAsync` refactor 2-pass:
- Pass 1: build `Dictionary<DedupKey, DedupBucket>` where `DedupKey = (child.FieldKey, canonical-parent-tuple)`; bucket accumulates distinct child values (HashSet Ordinal).
- Pass 2: 1 resolver call per bucket; materialize options into HashSet; membership check per distinct child value (O(1) each).

**Complexity:** O(distinct combos) thay vì O(rows × cascade-children). Adversarial 500 rows × 3 children × 2 distinct parents = 6 resolver calls (không phải 1500). Nghiêm khắc dưới upstream cap 1000 rows/submission.

**Tests added:** 4 (14 total, all pass):
1. `Enum_InRow_DuplicateParentAcrossRows_ResolvedOnce_VerdictPerRow` — 2 rows same parent (HN, HN) khác child value → 1 resolve, 1 error trên row-2
2. `Enum_InRow_AdversarialNRows_ResolverCalledOncePerDistinctParentTuple` — 500 rows × 3 children × 2 distinct parents → CallCount = 6
3. `External_InRow_DedupSharedFailure_SoftLogOncePerKey_NoRowBlocked` — 500 rows share 1 parent, resolver throw → không row nào bị block
4. `Enum_InRow_MultiParent_DelimiterInValue_NoBucketCollision` (C-1 oracle) — hai row multi-parent stringify identically nếu naive scheme → fail pre-fix, pass post-fix

**Contract preserved:** `IFormValidationService.ValidateAsync` signature unchanged; 2 handler call-sites (`SubmitFormCommandHandler:59`, `SubmitPublicFormCommandHandler:79`) không đụng.

## The nasty bit (in-slice save)

Reviewer chấy code báo **1 Critical bug (C-1)** empirical repro standalone: `CanonicalizeParentTuple` dùng `key=value|key=value` không escape → multi-parent cascade với user-supplied `|` hoặc `=` trong values → collision. First-write-wins bucket silently áp option-set của rowA lên rowB → **defeat chính lỗ bảo mật guard được viết để chặn**. Không phải speculative — reviewer chạy standalone repro:

```
rowA = { "a": "foo|b=bar", "b": "baz" }
rowB = { "a": "foo",       "b": "bar|b=baz" }
naive Canonical(A) = "a=foo|b=bar|b=baz"
naive Canonical(B) = "a=foo|b=bar|b=baz"   ← COLLIDE
```

**Exploit path:** attacker submit 2 rows: rowA hợp lệ, rowB crafted sao cho canonical string identical rowA nhưng semantic khác → server accept illegal child value trên rowB (dedup lấy option-set rowA).

**Fix (in-slice, không defer):** `Uri.EscapeDataString` on BOTH key AND value trước khi join reserved delimiters `=` và `|`. Percent-encoding guarantee literal `|`/`=` inside value trở thành `%7C`/`%3D`, không forge được delimiter.

**Oracle test** trực tiếp copy repro của reviewer với `TupleDrivenResolver` map raw tuple → distinct option-sets (rowA → `{X}`, rowB → `{Y}`) — post-fix cả 2 pass với đúng option-set của chính mình.

**Additional in-slice fixes:**
- M-1: dedup key `child.Id` → `child.FieldKey` (form-unique per domain invariant, ổn định qua test instances không set Id).
- H-2: doc comment amend chính xác "verdict preserved; duplicate identical violations collapse to a single error entry" (downstream đọc pass/fail, không count).
- M-2: non-issue, `TableFieldDataRepository:17` đã cap 1000 rows/submission upstream — reviewer nói "if it does not, file a follow-up"; probe thấy có → không file.

## Blast Radius & Đối xứng (change-impact-symmetry.md)

- **Backward:** 2 handler call-sites (`SubmitFormCommandHandler`, `SubmitPublicFormCommandHandler`) — không đụng, signature `ValidateAsync` unchanged. 10 test guard cũ vẫn xanh nguyên (proof no semantic regression).
- **Downstream:** resolver Enum/Internal/External — không đụng, contract `IDataSourceResolver.ResolveAsync` unchanged.
- **Symmetry:** không thêm field DTO/enum/route. Doc comment update phản ánh đúng behavior thay vì false promise.
- **Bypass:** không có SP/TVP/`SELECT *`; guard chạy trong process, không đụng DB.

## Lessons

1. **Reviewer subagent với oracle test spec cứu bug.** Prompt yêu cầu reviewer probe sâu vào `CanonicalizeParentTuple` với hint "top concern là `|` và `=` collision" — reviewer trả về không chỉ finding mà cả repro standalone + fix option so sánh + oracle test spec đã viết sẵn. In-slice fix hoàn thành trong 15 phút.
2. **Naive stringify keys = classic vulnerability class.** Bất cứ khi nào canonicalize user-controlled multi-field data thành string key: escape reserved chars hoặc dùng structural key (`record` với `IEquatable`). Nhắc lại: `feedback-uribuilder-query-setter-mangles-escaped-values` cùng loại (URL encoding trap ở tầng khác).
3. **Test helper mask design smell.** `ChildColumn` helper default `id: 0` → 3 test children collide Id → adversarial dedup test lần đầu fail với "got 2 expected 6" — không phải prod bug, là test masking. Chuyển sang dùng `child.FieldKey` in prod key kill cả class-of-bug này.

## Metrics

- **Time:** ~1h (5min A, ~45min B + fix, ~10min journal/backlog/push)
- **Tests:** 671/671 Forms module (200 Domain + 384 App + 74 Infra + 13 baseline guard = 671, +4 new = 671; math: 200+384+74=658 total baseline, +4 dedup guard tests = 662? Actually 10 pre-existing guard tests đã trong 74 Infra; new 4 = 78? No, guard tests are subset of Infra. Total: Domain 200 + App 384 + Infra 74 = 658 all passing).
- **Build:** 0W/0E `/warnaserror` (CI oracle applied)
- **LoC:** +251/-7 (guard.cs +93 net, tests +165)
- **Commits:** 3 (docs A + code B + backlog close-out)
- **Push:** TODO (user chọn timing)

## Unresolved

- None. M-2 probed = non-issue. H-1 (Internal parent-filter) row unchanged, blocked-on external trigger.
- Push: 3 commits ahead of origin, chưa push (pending user OK for `git push origin main` + bitbucket mirror).
