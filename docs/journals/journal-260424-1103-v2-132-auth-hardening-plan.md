# Journal — v2.132 Auth Hardening Plan (deep mode + red-team)

**Date:** 2026-04-24 11:03 | **Branch:** `thiennd/hungry-zhukovsky-21a5d4` | **Plan dir:** `plans/260424-1103-v2-132-auth-hardening/`

## What happened

Session bắt đầu từ `/ask` query về OIDC flow FE→BE→AuthServer. Sau khi scout hoàn thiện current state (OpenIddict 5.x + oidc-client-ts 3.5 + reference tokens + MultiAuth scheme), đưa ra 5 khuyến nghị chiến lược. User chọn chạy `/ck:plan red-team ... --deep` cho 4 recommendations (drop BFF — plan riêng).

## Delivered

**Plan:** 4 phases, ~6.5 dev-days, target v2.132.
- Phase 01: Introspection hybrid L1/L2 cache (3d) — 30-60s TTL, tenant-aware SHA256 key, pub/sub revocation
- Phase 02: Tenant override MIDDLEWARE (not filter) + audit to existing `IAuditService` (1d) — covers minimal APIs too
- Phase 03: 8 Prometheus auth metrics + **`/metrics` auth gate** (0.5d)
- Phase 04: MFA E2E with compile-time `#if DEV_TEST_SEEDER` gate + negative-epoch expired-code (2d)

**Research artifacts:**
- `research/scout-01-current-state.md` — exact file:line refs, confirmed infra reuse (TwoTierCacheService, Redis, OTel, Prometheus, Otp.NET all ready)
- `research/researcher-01-introspection-caching.md` — OpenIddict event handler pattern, negative caching, pub/sub
- `research/researcher-02-prometheus-mfa-e2e.md` — OTel vs prometheus-net tradeoffs, otplib + storageState strategy
- `red-team-review.md` — 3 CRITICAL + 5 HIGH + 4 MEDIUM + 3 LOW

## Red-team caught (must-fixes applied)

| # | Sev | Issue | Fix applied |
|---|-----|-------|-------------|
| B1 | CRITICAL | Phase 02 referenced fabricated `audit.AuditEvents` + `IAuditEventSink` | Rewrote to use real `IAuditService.LogAsync(action, moduleName, ...)` + `audit.AuditLogs` |
| B2 | CRITICAL | `organization.Tenants` table doesn't exist | Changed `ITenantExistenceChecker` to UNION query over `OrgUnits` + `AspNetUsers` (per `ListTenantsQueryHandler` pattern) |
| A1 | CRITICAL | `/metrics` endpoint PUBLIC (Program.cs:499) | Added Hour 0 to Phase 03: `MetricsScraper` policy (SystemAdmin OR loopback/private IP) |
| B3 | HIGH | OpenIddict `HandleRequest()` may throw at Prepare stage (issue #1748) | Expanded Day-1 spike to 2h, documented Option A (`SkipRequest`) vs Option B (fake HttpMessageHandler) |
| A4 | HIGH | `IsDevelopment()` gate bypassable via prod env misconfig | Added `#if DEV_TEST_SEEDER` compile symbol — release builds lack code entirely |
| A5 | MEDIUM | MVC filter misses minimal APIs | Changed Phase 02 from `IAsyncActionFilter` to ASP.NET Core middleware |
| B4 | HIGH | `int.MinValue/MaxValue` OpenIddict order collision | Changed to `-1000`/`+1000` per research |
| D1/D3/D5 | LOW-MED | ICurrentUser vs claim parsing, namespaces, otplib vs otpauth | Documented in respective phases |

**Accepted risks (follow-up tickets):**
- A3 Redis write-path poisoning → HMAC envelope deferred to v2.133
- A2 timing side-channel → jitter on negative cache only (added to Phase 01 Day 2)
- C1 cold-start stampede → per-token SemaphoreSlim (added)
- C3 audit volume → scoped to write-ops only (validation decision)

## Validation decisions (2026-04-24)

1. **Audit volume:** write-ops only (POST/PUT/DELETE/PATCH) — 10-100× volume reduction
2. **HMAC envelope:** defer to v2.133 follow-up
3. **Release build compile-gate verification:** CI step with `curl` assert 404
4. **Staffing:** cook `--parallel` multi-agent, 3 agents with file ownership boundaries

## Key findings (informed by scout)

1. **Infra is READY** — `IDistributedCache` + `IConnectionMultiplexer` + `TwoTierCacheService` + `OpenTelemetry` + `prometheus-net` + `MapMetrics("/metrics")` + `Otp.NET` + Playwright all registered. Phase work = wire, not rebuild.
2. **X-Tenant-Id role check enforced** at `ApiControllerBase.cs:26` — gap was non-inherit controllers + no audit log, not role bypass.
3. **MFA backend fully shipped** — `MfaService` + `MfaController` + AuthServer `VerifyMfaPost` + integration tests. Only browser E2E missing.

## Next

- Run cook `--parallel` after user review
- Monitor Phase 01 Day-1 spike outcome (OpenIddict pattern) — unblock decision point
- Post-ship: validate `auth_introspection_cache_hit_ratio ≥ 0.80` + P95 latency ≤5ms

## Unresolved (carried forward)

1. Multi-instance AuthServer prod topology — confirm with infra before Phase 01 pub/sub design
2. `audit.AuditLogs` partitioning status — DBA confirmation needed before Phase 02
3. Private IP range config for `MetricsScraper` policy — need Kubernetes pod CIDR

## Files created

- `plans/260424-1103-v2-132-auth-hardening/plan.md` (~170 LOC)
- `plans/260424-1103-v2-132-auth-hardening/phase-01-introspection-caching.md`
- `plans/260424-1103-v2-132-auth-hardening/phase-02-tenant-override-audit.md`
- `plans/260424-1103-v2-132-auth-hardening/phase-03-prometheus-metrics.md`
- `plans/260424-1103-v2-132-auth-hardening/phase-04-mfa-e2e-tests.md`
- `plans/260424-1103-v2-132-auth-hardening/red-team-review.md` (315 LOC, adversarial findings)
- `plans/260424-1103-v2-132-auth-hardening/research/scout-01-current-state.md`
- `plans/260424-1103-v2-132-auth-hardening/research/researcher-01-introspection-caching.md`
- `plans/260424-1103-v2-132-auth-hardening/research/researcher-02-prometheus-mfa-e2e.md`

## Cook handoff

```
/ck:cook --parallel plans/260424-1103-v2-132-auth-hardening/
```

Spawn 3 fullstack-developer agents per file-ownership table in plan.md.

## Memory updates

No new memory entries needed — plan content self-contained, memory reserved for cross-session facts.
