# Journal — 2026-05-14 08:40 ICT — AUDIT-HMAC P3b-FU1+FU2 shipped (v2.137p15)

**Commit:** `7fe6ebf5` (pushed `origin/main`, 4 files +43/-3)
**Scope:** Dev-only ergonomics; production unaffected.

## Session goal
Close the 2 follow-ups filed in v2.137p14 after the P3b live smoke caught the silent
`VaultKeyResolver.cs:131` literal `"root"` fallback trap when `VAULT_TOKEN` env is unset.

## What shipped

| Item | File | Change |
|------|------|--------|
| P3b-FU1 | `infra/docker/docker-compose.yml` | `VAULT_ADDR` + `VAULT_TOKEN=dev-only-token` on `api` + `authserver` env blocks, with dev-only warning comment |
| P3b-FU2 | `docs/deployment-guide.md` §5 | New `#### Dev Vault re-seed after restart` subsection — one-liner `docker compose ... run --rm vault-init` |
| Changelog | `docs/project-changelog.md` | v2.137p15 entry prepended |
| Backlog | `backlogs/backlog.md` | Both rows `[ ]` → `[x]` with ship annotation |

## YAGNI rejections (logged in commit body)

- `docker-compose.override.yml` — not created. Main compose is the right place for shared dev defaults; override files are per-developer and don't ship.
- `aqt-runapp` skill edit — not needed. Launch profiles already pass `VAULT_TOKEN` via `dotnet run` since commit `e646fc3f`.
- `VaultKeyResolver.cs:131` literal `"root"` removal — out of FU scope. Backlogged as risk. Trigger to revisit: ≥2 dev incidents.
- Vault healthcheck-dependent auto-restart sidecar (FU2 option A) — not built. Manual one-liner re-seed is fine; pain is rare.

## Tests
None run. Compose YAML + Markdown only; zero application source change. Architecture 97/97 + Audit.Infrastructure suites stay green from v2.137p13.

## Decisions

- **Pre-flight scout caught existing coverage.** `launchSettings.json` already carried `VAULT_TOKEN` since `e646fc3f`; aqt-runapp skill inherits via launch profile. Real gap was only Docker-mode → narrowed scope to `docker-compose.yml` + docs without wasted edits.
- **Option B over Option A for FU2.** YAGNI: docs one-liner beats healthcheck-sidecar plumbing when pain is rare and recovery is fast.

## Audit-HMAC-Vault-Seeder follow-up family

| ID | Status |
|----|--------|
| P3b-FU1 | ✅ shipped v2.137p15 |
| P3b-FU2 | ✅ shipped v2.137p15 |
| P4-FU | ⏸ deferred (other startup loop-seeders, not Vault-pepper consumers) |
| P5-FU | ⏸ deferred (cosmetic L-1/L-2 from reviewer) |
| FU-HMAC-ROTATION | ⏸ deferred (trigger: NIST rotation cadence ask) |
| FU-AUDIT-HMACKEY-LEGACY-REMOVE | ⏸ deferred (trigger: 30-day metric `aqt_audit_hmac_legacy_fallback=0`) |
| FU-AUDIT-REPLAY-OPS | ⏸ deferred (trigger: ops requests dashboard) |

## Next session
No carry-over. Suggested when continuing:
- **R4** Playwright E2E flake re-baseline (2-4h, test-infra leverage)
- **R1** Docker CI #21 (3-6h, deploy-time preempt)
