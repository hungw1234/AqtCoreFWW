# AqtCoreFW — SLO Definitions

**Version:** 1.0
**Effective:** 2026-03-24
**Review cadence:** quarterly

---

## Service Level Objectives

### SLO-01 Availability

| Metric | Target | Window |
|--------|--------|--------|
| HTTP success rate (2xx + 3xx / total) | ≥ 99.5% | rolling 30 days |
| Health endpoint `/health` reachable | ≥ 99.9% | rolling 30 days |

**Error budget:** 0.5% × 30d × 24h × 60m = **216 minutes/month**

Prometheus query:
```promql
sum(rate(http_requests_received_total{app="aqtcorefw",code!~"5.."}[30d]))
/ sum(rate(http_requests_received_total{app="aqtcorefw"}[30d]))
```

---

### SLO-02 API Latency

| Percentile | Target | Measured over |
|------------|--------|---------------|
| p50 | < 50 ms | 5-minute window |
| p95 | < 200 ms | 5-minute window |
| p99 | < 1 000 ms | 5-minute window |

Applies to all `POST /api/**` and `GET /api/**` routes excluding health checks and static assets.

Prometheus query (p95):
```promql
histogram_quantile(0.95,
  rate(http_request_duration_seconds_bucket{app="aqtcorefw"}[5m])
) * 1000
```

---

### SLO-03 Error Rate

| Metric | Target | Window |
|--------|--------|--------|
| 5xx error rate | < 1% | rolling 5 minutes |
| Unhandled exception rate | < 0.1% | rolling 5 minutes |

Prometheus query:
```promql
sum(rate(http_requests_received_total{app="aqtcorefw",code=~"5.."}[5m]))
/ sum(rate(http_requests_received_total{app="aqtcorefw"}[5m]))
```

---

### SLO-04 Background Job Reliability

| Metric | Target | Window |
|--------|--------|--------|
| Outbox processor success rate | ≥ 99% | rolling 1 hour |
| Max retry exhaustion queue depth | < 100 messages | instantaneous |

---

## Alerting Thresholds

| Alert | Condition | Severity | Page |
|-------|-----------|----------|------|
| AvailabilityBudgetBurn | Error budget burn > 5x in 1h | critical | yes |
| HighLatencyP95 | p95 > 500ms for 5m | warning | no |
| HighLatencyP95Critical | p95 > 1s for 5m | critical | yes |
| ErrorRateHigh | 5xx > 5% for 2m | critical | yes |
| ErrorRateWarning | 5xx > 1% for 5m | warning | no |

---

## SLI Instrumentation

All SLIs are derived from:
- `http_requests_received_total` — counter with labels `app`, `method`, `code`, `route`
- `http_request_duration_seconds` — histogram with labels `app`, `method`, `route`
- Emitted by ASP.NET Core OpenTelemetry metrics exporter → Prometheus

Pre-computed recording rules: `infra/prometheus/recording-rules.yml`
Grafana dashboard: `infra/grafana/slo-dashboard.json`

---

## Review Process

1. Monthly: review error budget consumption in Grafana SLO dashboard
2. Quarterly: adjust targets based on actual P99 baseline
3. On SLO breach: post-mortem within 5 business days, update runbook
