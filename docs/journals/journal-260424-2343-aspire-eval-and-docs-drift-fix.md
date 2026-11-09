# Session Journal — 2026-04-24 23:14–23:43 — Aspire Evaluation + Docs Drift Fix

**Branch:** main
**Version:** v2.131p4 (no bump — docs polish + dev-only PoC config only)
**Duration:** ~30 min
**Trigger:** User asked "Can .NET Aspire be integrated into AqtCoreFW?"

## What happened

1. **Evaluated .NET Aspire integration against real codebase state** (not just memory).
   - Scouted `README.md`, `tech-stack.md`, `system-architecture.md`, `codebase-summary.md`, `deployment-guide.md`, `Program.cs`, `OpenTelemetryConfiguration.cs`, `docker-compose.yml`, `.csproj` files.
   - Verified: 0 Aspire references in code; OTel 1.15, Serilog, Polly resilience, Vault config provider, YARP gateway, Health Checks all ALREADY wired via custom infra modules.

2. **Verdict: SKIP full Aspire integration.** Reasons:
   - AqtCoreFW = monolith 2-host (Api + AuthServer), not distributed — service discovery + AppHost graph solve problems not present.
   - Observability stack mature (Prometheus + Grafana + Seq + OTel OTLP) → Aspire Dashboard = dev-only duplicate.
   - Deployment = on-prem K8s Helm + Vault agent sidecar → Aspire manifest (azd) incompatible with Vault dynamic rotation.
   - 80+ projects × `AddServiceDefaults()` refactor = high cost, no ROI.
   - Template repo cloned to multiple GOV projects → maintenance tax multiplier.

3. **Fixed 4 architecture/stack docs drifts** (all Grep-verified post-edit):
   - `deployment-guide.md:8` — "YARP disabled in Phase 1" → "Nginx (ingress) + YARP internal active" (YARP wired at `Program.cs:147` + `Gateway/GatewayRegistration.cs`)
   - `tech-stack.md:228-229` — "GitLab 18.9 CI" → "GitHub Actions primary CI; GitLab = source mirror" (verified `.github/workflows/ci.yml` exists, no `.gitlab-ci.yml`)
   - `system-architecture.md:6` — "13 modules" → "21 modules" (Identity..Governance full list)
   - `system-architecture.md:17` — "Testcontainers transitional" → "local SQL default shipped v2.114, TC fallback"

4. **Added Aspire Dashboard PoC** (cheap-win alternative, per user's "startup time" pain-point mismatch with Aspire value):
   - `docker-compose.yml:143-156` — new `aspire-dashboard` service, profile `aspire` (opt-in)
   - Port 4317 host → 18889 container = zero app config change (app default `Otel:Endpoint=http://localhost:4317` unchanged)
   - Usage: `docker compose -f infra/docker/docker-compose.yml --profile aspire up aspire-dashboard -d` → open http://localhost:18888
   - Auth unsecured (local dev only, documented in inline comment)

5. **Delegated startup-time investigation to planner agent** (background, running).
   - Plan dir: `plans/260424-2343-startup-time-investigation/`
   - 7 phases: baseline → dotnet-trace profile → rank suspects → vault/db/masterdata optimizations → re-measure
   - Suspect list: Vault bootstrap, 16 DbContext registrations, MasterData seed, 21 module DI graph, OpenIddict certs, Hangfire schema check, MassTransit consumers, 65 Program.cs imports.

## Decisions

| Decision | Rationale |
|----------|-----------|
| Skip full Aspire (AppHost + ServiceDefaults + manifest) | Vault sidecar model conflicts with Aspire connection-string-from-AppHost; 80+ project refactor tax; on-prem K8s deployment doesn't use azd |
| Adopt Aspire Dashboard standalone container with `aspire` profile | Zero code change, 5-min setup, dev-only, rollback = stop container |
| Aspire does NOT solve "startup time" pain point | Dashboard renders OTel traces, doesn't speed up app cold start. AppHost adds orchestration latency, can make startup worse. Startup optimization needs `dotnet-trace` profiling + targeted DI/DbContext/seeder fixes |
| Separate startup investigation plan (delegated to planner) | Large effort, measurement-first, iterative — needs its own plan dir |

## Artifacts changed

| File | Change |
|------|--------|
| `docs/deployment-guide.md` | Line 8 — YARP active inside API clarified |
| `docs/tech-stack.md` | Lines 228-229 — GitHub Actions primary, GitLab = source mirror only |
| `docs/system-architecture.md` | Line 6 — 21 modules (was 13); Line 17 — local SQL default, TC fallback |
| `infra/docker/docker-compose.yml` | New `aspire-dashboard` service with profile `aspire` (lines 143-156) |
| `plans/260424-2343-startup-time-investigation/` | Planner agent creating (in progress) |
| `docs/journals/journal-260424-2343-aspire-eval-and-docs-drift-fix.md` | This file |

## Documentation drift root cause

`system-architecture.md:6` "13 modules" was stale from P1-P6 (v1.0) era. `codebase-summary.md` kept module count fresh via regular updates, but `system-architecture.md` wasn't touched when 17→21 modules happened. Pattern: "Living documents" without mechanism to force sync.

Long-term mitigation candidate (YAGNI for now, file under backlog if drift recurs): add `ArchitectureTests` assertion that counts `AqtCoreFW.*.Application` projects and compares with docs. Defer unless 3rd drift reported in same doc.

## Follow-ups

- [ ] Review planner output for startup investigation plan (background agent running)
- [ ] If user pilots Aspire Dashboard PoC → gather feedback after 1 week → decide adopt permanently or remove profile
- [ ] Run baseline startup measurement (Phase 01 of investigation plan) — should take <15 min
- [ ] Consider adding inline comment in `Program.cs` annotating each startup section with `// [startup-phase: vault-bootstrap]` etc. — makes future profiling / log correlation easier

## Unresolved questions

- Aspire Dashboard version pinned to `9.0` — should we track latest or stay on 9.0 LTS? (defer until PoC adopted)
- Does user want GitLab source mirror documented elsewhere (CI webhook sync config)? Current wording in tech-stack.md is neutral.

## Memory updates needed

None this session — no new enduring facts worth storing beyond what's in docs. The "Aspire evaluation" context is ephemeral (recorded in this journal + docs drift fixes visible in git history).
