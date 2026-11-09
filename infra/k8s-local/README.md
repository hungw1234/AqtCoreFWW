# Local Kubernetes rehearsal environment (Docker Desktop kind)

Đường tập dượt cho **prod deploy target = Kubernetes** (user decision 2026-07-23) — plan
`plans/260723-1258-k8s-local-stable-monolith-deploy/`. Cluster: Docker Desktop built-in
kind provisioner, 1 node, containerd image store.

## Facts đã probe (đừng tin lại claim cũ)

| Claim | Verdict (2026-07-23) |
|-------|---------------------|
| "containerd image store → `docker build` image tự thấy trong cluster" | **SAI** — probe `kubectl run --image-pull-policy=Never` trả `ErrImageNeverPull`. Node kind có containerd namespace riêng. |
| Cách load image đúng | `docker save <img> \| docker exec -i desktop-control-plane ctr -n k8s.io images import -` — node container **ẩn** khỏi `docker ps` (checkbox "Show system containers" tắt) nhưng `docker exec` theo tên vẫn hoạt động. |
| Docker Desktop LB → localhost | ĐÚNG — ingress-nginx `LoadBalancer` nhận traffic tại `localhost:80/443`. |
| `*.localtest.me` | Resolve 127.0.0.1 sẵn, không cần sửa hosts file. Offline → thêm hosts entries `127.0.0.1 api.aqt.localtest.me auth.aqt.localtest.me app.aqt.localtest.me`. |

## ⚠️ Trước khi bắt đầu

1. **Port conflict**: compose dev stack (`infra/docker/docker-compose.yml`) chiếm 1433/8200/5000/5001/6379 trên host. Statefuls k8s dùng ClusterIP (không đụng), nhưng ingress bind 80/443 và `kubectl port-forward` debug sẽ đụng — **stop compose trước khi làm việc trong k8s env** (`docker compose -f infra/docker/docker-compose.yml down`).
2. **SQL Server + Vault dùng PVC (persistent)**: DB và Vault secrets **sống qua pod restart / máy sleep-resume**. Redis / RabbitMQ / MinIO / ClamAV vẫn ephemeral (emptyDir) — chúng không giữ state cần thiết để boot.
   - Vault chạy **file storage**, nên boot lên là **SEALED**; sidecar `unseal` trong `vault.yaml` tự init lần đầu và tự unseal sau mỗi restart. Không cần thao tác tay.
   - Đổi node-count/version trong Docker Desktop = XÓA cluster → mất cả PVC.
   - `scripts/k8s-local-reseed.sh` **từ chối chạy** khi PVC còn tồn tại (nó mint key mã hoá MỚI ở bước 5b — chạy đè lên DB cũ sẽ làm dữ liệu cũ không giải mã được). Muốn wipe thật sự: `RESET_DATA=1 bash scripts/k8s-local-reseed.sh` — xoá data + key CÙNG lúc.
   - Unseal key nằm ngay trên PVC của vault (`/vault/file/unseal-key`), nên key và data không bao giờ lệch nhau. **Local-only** — prod cần unseal ceremony thật với split key custody.
3. `.env.k8s-local`: copy từ `.env.k8s-local.example`, điền password MỚI (không tái dùng password dev đã commit).

## Thứ tự lệnh (lần đầu / sau reset)

```bash
# 0. Prereqs: cluster Running (Docker Desktop → Kubernetes), helm trên PATH
# 1. Ingress controller (một lần cho đời cluster)
helm upgrade --install ingress-nginx ingress-nginx/ingress-nginx \
  -n ingress-nginx --create-namespace --wait

# 2. Build images — CẢ BA đều context repo-root (`.`): api/authserver Dockerfile COPY
#    `src/...`, web Dockerfile COPY `web/...` + `other/vgca_usb_token` (VGCA plugin).
#    (compose authserver từng ghi context `../../src` — stale so với Dockerfile.)
docker build -t aqtcorefw/api:local        -f src/host/AqtCoreFW.Api/Dockerfile .
docker build -t aqtcorefw/authserver:local -f src/host/AqtCoreFW.AuthServer/Dockerfile .
docker build -t aqtcorefw/web:local \
  --build-arg VITE_OIDC_AUTHORITY=http://auth.aqt.localtest.me -f web/Dockerfile .

# 3. Load images vào node (containerd store KHÔNG tự chia sẻ — xem Facts)
for img in aqtcorefw/api:local aqtcorefw/authserver:local aqtcorefw/web:local; do
  docker save "$img" | docker exec -i desktop-control-plane ctr -n k8s.io images import -
done

# 4. Toàn bộ phần còn lại (namespaces → secrets → statefuls → vault seed → helm → smoke)
bash scripts/k8s-local-reseed.sh
```

## Kiến trúc local

- `aqt-data`: sqlserver (PVC) / vault (file storage + PVC, unseal sidecar) / redis / rabbitmq / minio / clamav — plain
  Deployment+Service, không operator (YAGNI), reseed qua `vault-seed` Job (tái dùng
  `infra/docker/vault-init.sh` nguyên bản qua ConfigMap).
- `aqt-app`: 3 helm release (`aqtcorefw` api + `authserver` + `web`) với `values-local.yaml`
  từng chart. `api-alias-service.yaml` tạo Service tên `api` để `web/nginx.conf`
  (proxy_pass `http://api:8080`) chạy nguyên bản như compose — prod sẽ thay bằng ADR-008
  (ingress same-origin).
- db-migrate: pre-install hook của api chart (`db-migrate-job.yaml`); reseed script chờ
  sqlserver ready TRƯỚC helm install nên hook không đua cold-start.
- AuthServer không có migrate job riêng: contexts của nó ⊂ migrate set của Api, và mặc
  định tự migrate lúc boot (NOTE trong `infra/helm/authserver/values.yaml`).

## ⚠️ NetworkPolicy carve-out

`networkPolicy.enabled=false` trong values-local — kindnet (CNI mặc định của Docker
Desktop kind) **không enforce NetworkPolicy**; bật lên chỉ tạo object no-op = false-green.
Verify netpol CHỈ trên cluster Calico/Cilium (standalone kind `disableDefaultCNI` + Calico,
hoặc staging). Xem `docs/deployment-guide.md` §2.

## Monitoring (kube-prometheus-stack — P3)

```bash
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm upgrade --install kps prometheus-community/kube-prometheus-stack -n monitoring \
  -f infra/k8s-local/monitoring-values.yaml --timeout 10m
bash infra/k8s-local/apply-grafana-dashboards.sh   # unwrap + sidecar ConfigMap
```

**Facts đọc từ Prometheus CR sống (2026-07-23, release `kps`)** — đọc lại nếu đổi release name:
- `ruleSelector` VÀ `serviceMonitorSelector` = `matchLabels {release: kps}` — app objects ở
  `aqt-app` phải mang label này (values-local `metrics.*.labels`), sai label = object tồn tại
  nhưng **silently ignored** (negative control đã demo: đổi label → rule biến mất khỏi
  `/api/v1/rules` sau 15s, object vẫn đó).
- Cả 2 `*NamespaceSelector` = `{}` = ALL namespaces — cross-ns discovery hoạt động.
- ServiceMonitor scrape **2 endpoints** (`/metrics` prometheus-net + `/metrics-otel` OTel) —
  mất 1 = mất nửa metrics không lỗi gì.
- Grafana: port-forward `svc/kps-grafana :80`, login `admin` + password từ Secret
  `grafana-admin` (tạo bởi `k8s-local-create-secrets.sh`, key `K8S_GRAFANA_ADMIN_PASSWORD`
  trong `.env.k8s-local`). 2 dashboard AqtCoreFW import qua sidecar.
- Watchdog: `AqtCoreFWWatchdog` fire liên tục vào Alertmanager void — ĐÚNG kỳ vọng local;
  external heartbeat receiver là prod-only (deployment-guide §9 step 4).

## Soak 24-48h (P3 step 7)

```bash
kubectl apply -f infra/k8s-local/soak-load-pod.yaml    # load nhẹ in-cluster (sống qua host sleep)
bash infra/k8s-local/soak-check.sh                     # cadence 4-6h/lần → soak-log.csv (gitignored)
```
Pass criteria: 0 restart ngoài dự kiến · memory không trend tăng đơn điệu >20%/24h · 5xx≈0 ·
Hangfire đúng lịch · 2 self-monitor alerts im · targets up liên tục · rmq_conns ổn định.
**Tắt sleep máy trong thời gian soak.** Kết thúc: `kubectl delete -f soak-load-pod.yaml`.

## Park / khôi phục (giải phóng RAM mà KHÔNG mất data)

Cách nên dùng khi tạm nghỉ: scale về 0, PVC giữ nguyên. Vault tự unseal khi scale lại (sidecar),
DB còn nguyên schema — không cần reseed. Xuống ~0 pod Running, Docker VM còn ~0.8 GB idle.

```bash
# --- PARK (thứ tự: app → monitoring → ingress → data-tier) ---
kubectl -n aqt-app       scale deploy --all --replicas=0
kubectl -n aqt-app       delete pod soak-load --ignore-not-found     # pod trần, không thuộc deploy
kubectl -n monitoring    scale deploy --all --replicas=0
kubectl -n monitoring    scale statefulset --all --replicas=0        # Prometheus + Alertmanager
kubectl -n ingress-nginx scale deploy --all --replicas=0
kubectl -n aqt-data      scale deploy --all --replicas=0
# node-exporter là DaemonSet — scale deploy KHÔNG chạm tới nó:
kubectl -n monitoring patch daemonset kps-prometheus-node-exporter \
  -p '{"spec":{"template":{"spec":{"nodeSelector":{"scaled-down":"true"}}}}}'

# --- KHÔI PHỤC (data-tier TRƯỚC, app sau — app crash nếu DB/Vault chưa sẵn) ---
kubectl -n aqt-data      scale deploy --all --replicas=1
kubectl -n aqt-data      wait --for=condition=ready pod -l app=sqlserver --timeout=300s
kubectl -n aqt-data      wait --for=condition=ready pod -l app=vault --timeout=120s
kubectl -n ingress-nginx scale deploy --all --replicas=1
kubectl -n monitoring    scale deploy --all --replicas=1
kubectl -n monitoring    scale statefulset --all --replicas=1
kubectl -n monitoring patch daemonset kps-prometheus-node-exporter \
  -p '{"spec":{"template":{"spec":{"nodeSelector":null}}}}'
kubectl -n aqt-app       scale deploy --all --replicas=1
bash infra/k8s-local/smoke-k8s.sh
```

Vault boot lên là **sealed**; đợi sidecar unseal trước khi tin app đã sẵn sàng:

```bash
kubectl -n aqt-data exec deploy/vault -c vault -- \
  env VAULT_ADDR=http://127.0.0.1:8200 vault status -format=json | tr -d '\n\r\t ' | grep -q '"sealed":false'
```

## Teardown (XÓA data — không hoàn tác)

```bash
helm uninstall aqtcorefw authserver web -n aqt-app
kubectl delete ns aqt-app aqt-data                               # xóa luôn PVC sqlserver-data + vault-data
helm uninstall kps -n monitoring; kubectl delete ns monitoring   # mất cả Prometheus retention
# ingress-nginx giữ lại được (vô hại); xóa hẳn: kubectl delete ns ingress-nginx
```

Dựng lại sau teardown cần `RESET_DATA=1 bash scripts/k8s-local-reseed.sh` (~10 phút).
