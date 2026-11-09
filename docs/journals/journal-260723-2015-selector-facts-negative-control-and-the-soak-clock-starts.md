# Selector facts, negative control, và đồng hồ soak bắt đầu chạy

**2026-07-23 20:15 · plan `260723-1258` P3 setup+verify · Docker Desktop kind**

## Chuyện gì

kube-prometheus-stack lên kind (1 replica, retention 72h, kind-noise targets tắt). Row kỷ luật
21/07 trả lại lãi hôm nay: hôm đó tự nhắc "đừng làm bước 1-5 kiểu đoán trước — mọi giá trị đều là
guess", vì CR thật chưa tồn tại để đọc. Tối nay CR đã tồn tại — bước 1-3 chạy trên CR thật,
không đoán nữa: `ruleSelector` VÀ `serviceMonitorSelector` đều là `{release: kps}`, namespace
selector rỗng `{}` nghĩa là ALL namespace. Đọc xong ghi thẳng vào README, không còn để trong đầu.

## Negative control — thấy tận mắt, không lý thuyết nữa

Đây là phần đáng giá nhất buổi. Gỡ label sai khỏi `AqtCoreFWWatchdog` PrometheusRule → 15 giây sau
rule **biến mất** khỏi `/api/v1/rules`, trong khi object Kubernetes vẫn nằm nguyên đó, `kubectl get`
vẫn thấy, không log lỗi nào. Đây chính xác là failure mode "silent-ignore" mà row selector đã cảnh
báo từ đầu — operator không reject, không warn, chỉ lặng lẽ không load. Gắn label đúng lại →
rule quay về. Có bằng chứng thực nghiệm rồi thì lần sau không cần nghi ngờ nữa: label sai = mất
rule, không tiếng động.

## Facts khác + 1 lần review cứu một CSV

Hai scrape endpoint (`/metrics` prometheus-net + `/metrics-otel` OTel) đều up — comment trong
template tự nhắc mất 1 trong 2 là mất nửa metric, không báo lỗi gì. promtool 3/3 xanh trên bản
render mới. 2 dashboard Grafana qua sidecar, cả 5 query đều có series.

Ba cú vấp nhỏ tự bắt được: Git Bash này không có `bc` (đổi sang awk); `count()` của Prometheus
trên selector rỗng trả về EMPTY chứ không phải 0 (per-metric empty-default, không phải "0 nghĩa
là healthy"); schema Hangfire thật là `[hangfire].[Job]`, không phải cái tên `messaging.HangfireJob`
đoán ban đầu. Reviewer bắt cú thứ 4 nguy hiểm hơn cả ba: `grep -c || echo na` in ra **"0\nna"** vì
`grep -c` in số đếm 0 rồi vẫn exit 1 — bash coi exit-code khác 0 nên chạy luôn nhánh `|| echo na`,
kết quả CSV có 2 dòng thay vì 1. Nếu không bắt trước lúc soak chạy nhiều ngày, đúng những cửa sổ
lỗi (khi count=0, tức là **failure window**) sẽ là cửa sổ CSV bị hỏng — vô hiệu hoá log đúng lúc
cần nó nhất.

## Soak bắt đầu

Pod curl in-cluster (sống qua host sleep) + `soak-check.sh` theo cadence ghi CSV. Baseline sạch:
ready=200, restarts=0, 5xx=0, targets_down=0, selfmon=0, rmq=2, hangfire 22s. `values-prod` chưa
đụng — prod chỉ đổi khi tự đọc selector của chính nó. Row DEADMAN: bước 1-3 xong ở local, 4-5 vẫn
prod-blocked, **chưa đóng**.

## Kết quả

P0-P2 đã ship, P3 setup xong, soak ≥24h chạy nền — báo cáo là việc kế tiếp. Definition-of-Stable
leg 5 (observability) giờ có bằng chứng local sống; leg 6 còn thiếu bảng soak + phần prod.

Status: DONE.
