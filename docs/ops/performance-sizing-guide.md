# Performance Sizing Guide

> CCU tiers và infrastructure sizing tương ứng cho AqtCoreFW.
> Dựa trên k6 load test SLOs và .NET 10 / SQL Server 2022 benchmarks.

## CCU Tiers

| Tier | CCU | Use Case | Env |
|------|-----|----------|-----|
| **T1** | 500 | Hệ thống nội bộ GOV, < 5,000 CBCC | Staging / Small Prod |
| **T2** | 1,000 | Hệ thống tỉnh/thành phố, 5k–20k users | Medium Prod |
| **T3** | 2,000 | Cổng dịch vụ công, citizen-facing | Large Prod |

---

## SLOs theo Tier

| Operation | T1 (500 CCU) | T2 (1000 CCU) | T3 (2000 CCU) |
|-----------|-------------|--------------|--------------|
| Read p95  | < 200ms     | < 300ms      | < 500ms      |
| Write p95 | < 400ms     | < 600ms      | < 1000ms     |
| Error rate | < 1%       | < 1%         | < 2%         |

> SLOs relax tại T3 do DB contention tăng. Nếu muốn giữ T1 SLOs ở T3, cần read replica + Redis caching mở rộng.

---

## Infrastructure Sizing

### T1 — 500 CCU

```
API Pods:       2 × (2 vCPU, 4 GB RAM)
SQL Server:     4 vCPU, 16 GB RAM, SSD IOPS 3,000
Redis:          1 node, 2 vCPU, 4 GB RAM
RabbitMQ:       1 node, 2 vCPU, 2 GB RAM
MinIO:          1 node, 2 vCPU, 4 GB RAM
Hangfire:       1 worker pod (1 vCPU, 1 GB)
```

**Estimated cost (on-prem/VPS):** ~8 vCPU, 31 GB RAM total

---

### T2 — 1,000 CCU

```
API Pods:       4 × (2 vCPU, 4 GB RAM)          [HPA: min 3, max 6]
SQL Server:     8 vCPU, 32 GB RAM, SSD IOPS 6,000
  + Read Replica: 4 vCPU, 16 GB RAM              [Dapper reads → replica]
Redis:          1 node (or cluster), 4 vCPU, 8 GB RAM
RabbitMQ:       2 nodes, 2 vCPU, 2 GB RAM each
MinIO:          2 nodes (distributed), 2 vCPU, 4 GB each
Hangfire:       2 worker pods (1 vCPU, 1 GB each)
```

**Estimated:** ~26 vCPU, 82 GB RAM total

**Required changes vs T1:**
- Enable SQL Server read replica, route `IReadDbConnection` (Dapper) → replica
- Redis: increase `maxmemory` + `maxmemory-policy allkeys-lru`
- API HPA min replicas = 3

---

### T3 — 2,000 CCU

```
API Pods:       8 × (2 vCPU, 4 GB RAM)          [HPA: min 6, max 12]
SQL Server:     16 vCPU, 64 GB RAM, SSD IOPS 12,000
  + Read Replica: 8 vCPU, 32 GB RAM
  + Connection pooling: max 200 connections per pod (PgBouncer nếu migrate sang PG)
Redis Cluster:  3 nodes, 4 vCPU, 8 GB RAM each  [sentinel / cluster mode]
RabbitMQ:       3-node cluster, 2 vCPU, 4 GB each
MinIO:          4-node distributed, 4 vCPU, 8 GB each
Hangfire:       3 worker pods (2 vCPU, 2 GB each)
YARP Gateway:   2 pods (1 vCPU, 1 GB each)       [separate ingress tier]
```

**Estimated:** ~72 vCPU, 224 GB RAM total

**Required changes vs T2:**
- Redis cluster mode (3 shards)
- SQL Server: `max degree of parallelism`, query store tuning
- Enable response compression (already in InfrastructureRegistration)
- Rate limiter: giảm burst limit để protect downstream
- Circuit breaker: tune open threshold (currently InMemoryCircuitBreakerRegistry)

---

## Kubernetes HPA Config

```yaml
# Áp dụng cho cả 3 tiers — adjust minReplicas/maxReplicas theo tier
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: aqtcorefw-api-hpa
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: aqtcorefw-api
  minReplicas: 2      # T1: 2 | T2: 3 | T3: 6
  maxReplicas: 6      # T1: 4 | T2: 6 | T3: 12
  metrics:
    - type: Resource
      resource:
        name: cpu
        target:
          type: Utilization
          averageUtilization: 70
    - type: Resource
      resource:
        name: memory
        target:
          type: Utilization
          averageUtilization: 80
```

---

## Chạy Test theo Tier

```bash
# T1 — 500 CCU
k6 run tests/performance/scaled-load-test.js \
  --env BASE_URL=https://staging.internal \
  --env API_KEY=$K6_TEST_APIKEY \
  --env TIER=500

# T2 — 1000 CCU
k6 run tests/performance/scaled-load-test.js \
  --env BASE_URL=https://staging.internal \
  --env API_KEY=$K6_TEST_APIKEY \
  --env TIER=1000

# T3 — 2000 CCU
k6 run tests/performance/scaled-load-test.js \
  --env BASE_URL=https://staging.internal \
  --env API_KEY=$K6_TEST_APIKEY \
  --env TIER=2000
```

Results saved to `tests/performance/results/scaled-{tier}-result.json`.

---

## Bottleneck Analysis

| Component | Bottleneck tại CCU | Giải pháp |
|-----------|-------------------|-----------|
| SQL Server | ~800 CCU (write-heavy) | Read replica, index tuning |
| Redis | ~1500 CCU | Cluster mode, pipeline batching |
| API pods | ~400 CCU / pod | HPA scale-out |
| Connection pool | ~1200 CCU | Tăng `MaxPoolSize`, pgbouncer |
| MinIO | ~500 concurrent uploads | Distributed mode |

## Ghi chú GOV

- Hệ thống GOV Việt Nam thường không cần > T2 trừ cổng dịch vụ công quốc gia
- TTCP/Bộ ngành cấp tỉnh: T1 đủ dùng
- Đề xuất: triển khai T1, test định kỳ, scale lên T2 khi CCU thực tế > 300
