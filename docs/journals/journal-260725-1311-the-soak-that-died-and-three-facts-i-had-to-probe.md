# Journal — 2026-07-25 13:11 · Con soak đã chết, và ba sự thật buộc phải probe

**Commit:** `d04d6b842` (pushed) · **Plan:** `260723-1258-k8s-local-stable-monolith-deploy` P3 → COMPLETE-PARTIAL

---

## Session goal

Mở session bằng `/aqt-next` để xem tiếp theo làm gì. Carry-over ghi "soak clock start
2026-07-23T12:59Z, cuối tuần chạy `soak-check.sh` → chốt bảng → close plan". Kỳ vọng: đọc số
liệu, viết report, đóng plan. Thực tế: soak đã chết từ hơn một ngày trước.

## What worked

- **Không tin carry-over, đi kiểm cluster trước.** `kubectl get pods` ngay lập tức cho thấy
  API + AuthServer `CrashLoopBackOff` **146 restarts**. Nếu tôi đọc baseline CSV (1 sample lúc
  13:11Z, mọi tiêu chí xanh) rồi viết report, tôi đã báo "soak PASS" từ một dòng dữ liệu.
- **Prometheus retention là oracle.** Range-query `kube_pod_container_status_restarts_total`
  dựng lại được timeline chính xác: `restarts=0` xuyên 07-23 14:50Z (≈2.5h hợp lệ), gap không
  series (máy sleep), `restarts=15` lúc 07-24 01:50Z, rồi +~11.5/h. Không phải suy đoán — có
  số. Đây là lần monitoring dựng hôm 07-23 trả lại giá trị đầu tiên.
- **Probe trước khi thiết kế.** Trước khi viết fix tôi probe 4 thứ: image có `/bin/vault` không,
  entrypoint quyết định dev-vs-server thế nào, `/vault/file` ghi được không, init/unseal có
  work không. Ba trong bốn probe đổi thiết kế (xem mục dưới). Nếu code trước, cả ba đều thành
  bug âm thầm.
- **Acceptance là restart thật, không phải "manifest apply OK".** Xoá pod vault+sqlserver →
  vault tự unseal from stored key, app pods **0 restarts**, **200 bảng sống** không cần
  re-migrate, smoke ALL GREEN. Đúng kịch bản đã giết soak, chứ không phải một proxy dễ hơn.

## What didn't / lessons

- **`||` vô hiệu `set -e` trong TOÀN BỘ thân callee.** `code-reviewer` bắt (C1, blocking) và
  chứng minh trong container. Tôi viết `initialize || log "retrying"` để reconcile loop sống
  qua lỗi — và comment cẩn thận về `set -e` **ở call site**. Nhưng chính construct đó tắt
  `set -e` bên trong `initialize`, nên `store_key` `return 1` bị bỏ qua và code chạy tiếp qua
  `operator unseal` / `token create`. Kết quả: vault **initialized** mà unseal key chỉ nằm
  trong biến shell = volume không mở lại được vĩnh viễn — đúng thứ header comment của chính hàm
  đó gọi là "the worst outcome in this script". Reason được nửa vấn đề còn tệ hơn không reason,
  vì cái comment tạo cảm giác đã xử lý xong. → `[[feedback-or-context-disables-set-e-inside-the-callee]]`
- **Cùng lỗi parse JSON, ba lần, một slice.** `-format=json` của vault là **pretty-printed**,
  nên `sed`/`grep` kiểu `"field":value` im lặng không match. Bit ở: (1) parse unseal key sau
  init — tệ nhất, vì `operator init` đã chạy không hoàn lại được; (2) `json_flag` trong
  reconcile loop; (3) gate `"sealed":false` trong reseed script. Sau khi fix (1) tôi tưởng xong,
  (3) vẫn timeout — cùng root cause ở file khác. Bài học: fix một class bug thì **grep cả repo
  cho pattern đó**, đừng fix từng chỗ khi nó nổ. → `[[feedback-cli-json-output-shape-breaks-line-based-parsers]]`
- **Đọc `ls` timestamp phút-granularity rồi kết luận sai.** Thấy `core/` tạo 02:04 và pod start
  02:04:19, tôi kết luận "vault được init 2 phút TRƯỚC khi container này start → pod cũ làm".
  Sai — `ls` chỉ hiện tới phút, 02:04 phủ cả hai. Tôi đã đi điều tra replicaset/pod-generation
  vô ích một lượt. Khi độ phân giải dữ liệu thô hơn khoảng thời gian đang suy luận, đó không
  phải bằng chứng.
- **`kubectl exec` không có `-c` khi pod thành multi-container.** Sidecar làm
  `generate-prod-secrets.sh` in `Defaulted container "vault" out of: vault, unseal`. Nó default
  đúng nên không vỡ — nhưng là false-green chờ sẵn nếu thứ tự container đổi. Fix hygiene.
- **`wait --for=delete` + `|| true` che PVC finalizer.** PVC có
  `kubernetes.io/pvc-protection`, không xoá được khi pod còn mount → nằm Terminating → `apply`
  sau đó **re-bind đúng volume cũ**. Lần reset đầu tôi tưởng đã wipe (guard "verified gone" pass)
  nhưng PVC uid/timestamp chứng minh là volume cũ. Phải: xoá workload → **wait pod gone** →
  xoá PVC → **verify bằng get, không bằng wait**.
- **Git Bash path mangling ×2.** `/bin/vault` → `C:/Program Files/Git/usr/bin/vault`;
  `/tmp/x.yaml` kubectl không thấy. Cần `MSYS_NO_PATHCONV=1` + scratchpad path Windows.
- **PowerShell here-string trong bash.** Commit đầu lọt ký tự `@` vào subject vì tôi dùng
  `@'...'@` (cú pháp PowerShell) trong Bash tool. Amend lại bằng heredoc.

## Decisions made

- **Option B (user chốt): soak = PARTIAL, tiêu chí ≥24h dời sang staging gate — KHÔNG waive.**
  Lý do: (a) 2.5h clean là dữ liệu thật, ghi đúng giá trị của nó; (b) soak 24h trên laptop có
  sleep không đáng tin dù chạy lại bao lần — window 07-23→25 cho thấy máy suspend ít nhất 2 lần;
  (c) chính failure này là evidence prod cần về Vault seal-posture, giá trị hơn một bảng xanh.
  Đánh đổi đã nói rõ: Definition-of-Stable leg 6 giữ tiêu chí ở gate staging.
- **PVC cho CẢ Vault và SQL Server** (không chỉ Vault). SQL cũng `emptyDir` nên schema bay cùng
  lúc — fix nửa vời thì app vẫn crash vì mất schema. Việc SQL cũng ephemeral đồng thời xoá bỏ
  lo ngại key-rotation: không có data nào để mất.
- **Unseal key nằm trên PVC của vault, không trong k8s Secret.** Ép bởi probe (image không có
  curl) nhưng hoá ra thiết kế tốt hơn: key và data bị xoá cùng nhau, không bao giờ lệch. Trạng
  thái "PVC initialized + key mất" là không thể phục hồi, nên loại bỏ khả năng đó có giá trị hơn
  là RBAC + Secret.
- **Guard from-zero trong reseed script.** Persistence tạo ra hazard mới: step 5b mint key random
  MỚI, chạy đè DB sống sót sẽ orphan key của rows cũ. Đây là instance của
  "fixing a bug arms a dormant hole" — invariant "mọi reseed đều là from-zero" đã bị chính fix
  của tôi phá. Guard + `RESET_DATA=1` xoá data và key cùng lúc.

## Test status

Không có unit/integration test mới — thay đổi là k8s manifest + shell script. Verification là
empirical trên cluster sống:

| Kiểm tra | Kết quả |
|---|---|
| `bash -n` / `sh -n` toàn bộ script sửa | pass |
| `kubectl apply --dry-run=client` (7 objects) | pass |
| Unit-test 2 parser (4 case: pretty-printed status ×2, init array, base64 `=` padding, `hvs.` prefix) | 4/4 |
| C1 regression (store_key fail → initialize abort, loop sống) | pass |
| Reseed from-zero → 3 charts → smoke | ALL GREEN |
| Restart data-tier → vault auto-unseal | `Initialized true`, `Sealed false` |
| App pods sau restart | **0 restarts**, 0 lần "DEV marker" |
| DB persistence | **200 tables** không cần re-migrate |
| Smoke sau restart | ALL GREEN |
| Secret scan trên outgoing diff | sạch (3 hit = tên biến, 0 base64/hex, 0 `hvs.`, 0 `YourStrong`) |

`audit-read-isolation` = **Degraded** sau from-zero reset — NonAudit SQL login là manual DBA step
ngoài reseed script. Không phải regression của slice này; row `P1-STABLE-AUDIT-ISO-OPS-ACTIVATE`
đã track.

## Next session

1. **`OIDC-SPA-CLIENT-REDIRECT-URIS-HARDCODED`** (T3, effort S) — row duy nhất fix được bằng
   code ngay, chặn browser-login thật trên kind. `OpenIddictDataSeeder` hardcode localhost.
2. **User-owned decisions** (không code được, cần bạn): §4-Q2 seed-posture (0 users/0 tenants
   dưới Production — bootstrap admin từ đâu) · **prod Vault seal-posture** (nay có evidence thật:
   một restart mất hết secrets nếu không HA + unseal ceremony split-key) · VNeID v1.0 có/không ·
   prod cert custody.
3. **`SOAK-24H-REQUIRES-STAGING-NOT-LAPTOP`** — blocked-on staging tồn tại. Cùng class với
   `PROMETHEUS-DEADMAN-SWITCH-MISSING` steps 4-5.
4. Cân nhắc: `audit-read-isolation` từ Degraded → Healthy trên kind cần đưa NonAudit login vào
   reseed script (hiện là manual step) — nếu muốn từ-zero reseed cho ra stack fully-healthy.
