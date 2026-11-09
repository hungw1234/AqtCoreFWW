# Lần helm install đầu tiên: 10 bug, và cái chart chưa từng chạy

**2026-07-23 15:50 · plan `260723-1258-k8s-local-stable-monolith-deploy` P0+P1 · Docker Desktop kind**

## Chuyện gì

Sáng nay user chốt prod = Kubernetes. Chiều nay full stack (api + authserver + web) chạy trên
kind cluster local, smoke xanh ×3 — lần đầu tiên hệ thống này tồn tại dưới dạng deployment.
Giữa hai mốc đó là 10 bug, không cái nào tầm thường, tất cả chung một nguồn: **những artifact
"intentions, never exercised"** (đúng chữ backlog đã dùng cho helm values từ 07-21).

## Phổ bug theo lớp

- **Chart chưa từng render** (5): probe path `/health` không tồn tại; thiếu hẳn
  `serviceaccount.yaml`; ConfigMap/Secret không phải hook trong khi migrate Job là hook
  (họ hook-ordering — pre-install pod chạy TRƯỚC manifests thường); configmap không render
  key mà chính values env của nó tham chiếu; migrate job không có đường lấy DB conn
  (secret hai tên `-secret`/`-secrets` mâu thuẫn nội bộ).
- **Build-infra chưa từng chạy** (3): web thiếu `.dockerignore` → node_modules Windows đè;
  phantom deep-import `rc-upload` pnpm-strict; **web image bất khả build từ khi VGCA
  USB-token land** (vite copy file ngoài context) — `docker-web:latest` compose là xác ướp;
  compose authserver context cũng stale (Dockerfile tự ghi "context: repo root").
- **k8s-ification** (2): thiếu startupProbe — liveness kill pod giữa lúc seeder nạp 14k user;
  readiness cần conn AuditAdmin (RLS-bypass) chưa được cấp.

## Reviewer cứu tôi khỏi 3 quả mìn prod

Hookify Secret/ConfigMap/SA của tôi fix local nhưng gài prod: `keep`+hook mâu thuẫn (nhánh
nào của Helm cũng hỏng), rollback tạo config/code skew (thiếu `pre-rollback`), và optional
env tôi đặt SAU commented elevated override — **last-wins sẽ vô hiệu hóa runbook
RLS-PRINCIPAL một cách im lặng** khi ai đó uncomment theo tài liệu. Fix: bỏ `keep` + contract
ownership rõ (chart-owned `-secret` vs external `-secrets`), thêm `pre-rollback` cả 3, đảo
thứ tự env. Đóng bằng bằng chứng: **upgrade lần 2 trên release sống** — thứ mà install-drill
from-zero không bao giờ exercise được (mỗi drill = lại lần-đầu).

## Ba bài học

1. **"First install" là một tập test khác với "reinstall from zero"** — hooks, release
   upgrade path, rollback chỉ lộ trên release SỐNG. Drill wipe-and-rebuild cho cảm giác đã
   phủ mà chưa.
2. **Probe claim trước khi xây trên nó**: "containerd store chia image cho cluster" nghe
   rất hợp lý và SAI (`ErrImageNeverPull`); node container ẩn khỏi `docker ps` nhưng
   `docker exec` theo tên vẫn ăn — hai fact chỉ có được bằng probe, giờ nằm trong README
   như facts, không phải assumptions.
3. **Pipe nuốt exit code cắn tôi HAI lần trong một buổi chiều** (`docker build | tail`,
   `bash drill | tail`) — dù memory đã có bài này từ trước. Bài cũ chưa thành phản xạ.

## Kết quả

P1 acceptance trọn: lint+render 3 chart · migrate job Completed (98s/121 migrations) ·
smoke ×3 · policy tests 7/7 sau mọi template change · reseed drill pass · secrets hygiene
sạch. Leg 6 Definition-of-Stable đi từ "chưa bắt đầu" → "local rehearsal sống". P2 (Production
env + RabbitMQ + NonAudit) đã có nền đứng.
