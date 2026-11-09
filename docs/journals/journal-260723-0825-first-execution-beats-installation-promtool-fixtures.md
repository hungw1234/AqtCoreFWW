# Journal — 2026-07-23 08:25 — Lần thực thi đầu tiên thắng việc cài đặt: promtool fixtures

## Bối cảnh

Row `ALERTING-PROMTOOL-FIXTURES-UNVERIFIED` (filed 2026-07-21): fixture promtool
`infra/prometheus/alert-rules-test.yml` được sửa cho nhất quán với label contract
`source="scheduled"` nhưng **chưa bao giờ chạy** — false green đúng shape
`[[feedback-no-helm-conftest-use-static-source-policy-test]]`. Bundle thêm
`ALERTING-SOFTDELETE-COMMENTS-CONTRADICT` (comment sai trong `AlertingDbContext.cs`).

## Điều đáng ghi: hướng fix của row đã cũ ngay khi mở

Row (2 ngày tuổi!) nói "install `promtool`... it ships in the Prometheus tarball".
Probe 5 phút lật ngược: **header của chính file fixture** — viết cùng ngày với row —
đã document đầy đủ quy trình Docker-based không cần cài gì (`alpine/helm` render →
extract → `prom/prometheus` promtool), và Docker đang chạy trên máy. Scope thật =
script hóa quy trình đã có + chạy lần đầu, không phải cài đặt.

Bài học lặp: một row mô tả DEFECT đúng nhưng FIX DIRECTION sai — lần này chỉ sau
2 ngày, và nguồn phủ định nằm ngay trong file mà row trỏ tới. Probe trước khi tin
hướng fix, kể cả row mới.

## Mutation thứ nhất chọn sai chỗ — arithmetic cứu

Mutation đầu (đổi `source="test"`→`"scheduled"` ở case burst-press *evaluation*)
vẫn XANH. Suýt kết luận vacuous. Làm arithmetic: series `0+1x12` cho
`increase(...[5m]) ≈ 5`, ngưỡng là `> 5` — im lặng là ĐÚNG. Case *notification*
(ngưỡng `> 0`) mới mang label contract. Mutation 2 (fixture-side) và 3
(template-side, bỏ selector khỏi expr) đều ĐỎ đúng kỳ vọng.

Đây là `[[feedback-accepted-not-engineered-means-do-the-arithmetic]]` ở chiều
ngược: arithmetic lần này *bào chữa* cho kết quả bất ngờ thay vì buộc tội nó.
Nguyên tắc chung: một mutation-check PASS bất ngờ cần được giải thích bằng số
trước khi kết luận vacuous — và một mutation-suite chỉ đáng tin khi mutation nằm
đúng cạnh nhạy của ngưỡng.

## Kỹ thuật nhỏ đáng nhớ

- awk thay python để extract `spec.groups` (không image nào ship python); guard
  `grep -q '^groups:'` fail-loud khi template đổi shape.
- `MSYS_NO_PATHCONV=1` bắt buộc cho docker mount path trên Git Bash/Windows.
- `.gitattributes` đã pin `*.sh eol=lf` từ trước (chính vì lỗi `set -eu\r` Alpine) —
  reviewer xác nhận file mới thuần LF.
- index.lock xuất hiện transient giữa chừng: check `tasklist` (0 git process) rồi
  nó tự biến mất — không reset hard, đúng `[[feedback-never-reset-hard-to-clear-index-lock]]`.

## Kết quả

- `run-promtool-tests.sh` mới; lần chạy ĐẦU TIÊN của fixture: SUCCESS; non-vacuous
  chứng minh 2 chiều; comment `AlertingDbContext.cs` sửa đúng sự thật (reviewer
  chain-verify tới `Entity.Delete()`).
- Code-review APPROVE_WITH_NITS; 2 nit Low (trap cleanup, self-dialogue comment)
  fixed in-slice. Build 0W/0E, Alerting unit 34/34.
- Backlog actionable còn lại sau slice này ≈ 0 code-able rows — mọi thứ chờ hạ
  tầng deploy đầu tiên hoặc quyết định của user.
