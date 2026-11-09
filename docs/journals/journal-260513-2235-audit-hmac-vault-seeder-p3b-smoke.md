# Journal — 2026-05-13 22:35 ICT

## AUDIT-HMAC-VAULT-SEEDER-SWEEP — P3b live smoke executed (ops-only)

### What shipped

- **Commit:** `cce13b1f` on `origin/main` (`docs(audit): P3b live smoke evidence + P3b-FU1/FU2 carry-over`).
- **Files:** 1 — `backlogs/backlog.md` only (plan.md + phase-03 doc edits are inside the gitignored `plans/` tree, kept local).
- **Source code:** 0 changed. P3b is operational verification, not implementation.

### What the smoke actually proved

The earlier P1+P2+P3a ship (`07b5487a..7736b12f` on `origin/main`) introduced `IAuditHmacKeyResolver`/`VaultAuditHmacKeyResolver`, seeded the Vault path `aqt/audit/hmac-v1`, and documented the replay procedure. P3b was deferred — it required a running dev stack to verify the chain end-to-end.

Execution:

1. **Vault re-seed.** Dev Vault container had been up ~2h but `curl -H "X-Vault-Token: dev-only-token" .../aqt/audit/hmac-v1` returned 404. In-memory storage loses the seed on every container restart and the seed step is not wired to the container lifecycle. Re-ran `infra/docker/vault-init.sh` via PowerShell `Invoke-RestMethod` (Git Bash CRLF + missing `curl` inside the official `hashicorp/vault` image both blocked running the shell script directly inside the container).
2. **API restart with `VAULT_TOKEN` env.** First boot attempt got `permission denied` on `aqt/audit/hmac-v1` despite Vault now being seeded. Root cause: `VaultKeyResolver.cs:131` falls back to `TokenAuthMethodInfo(opts.Token ?? "root")` — the literal string `"root"`, not the env value of `VAULT_DEV_ROOT_TOKEN_ID` (which is `dev-only-token` for this container). Restarted API with `VAULT_TOKEN=dev-only-token` in env; resolver succeeded on first call.
3. **Single-job replay.** Picked lowest Failed `HmacChainService.ComputeAsync` job (Id=91, MessageId `019e05d2-0eb0-7a7e-9130-a47cfc3f8e72`). Hangfire dashboard accepted `POST /hangfire/jobs/failed/requeue` after extracting the antiforgery cookie + token (`<meta name="csrf-token">`) from a single GET on `/hangfire/jobs/failed` — each GET issues a fresh pair, so the cookie + token must come from the same response. State transitioned Failed → Succeeded in <2s. `VaultAuditHmacKeyResolver` logged `Loaded audit HMAC key v1 from Vault`. Target entry HmacSignature length went 0 → 64 chars (HMAC-SHA256 hex). All success criteria from `phase-03-hangfire-backlog-replay.md` met.
4. **Bulk replay.** Requeued the remaining 496 failed jobs in one POST. Worker drained the queue in ~60s. Final state: 0 Failed / 497 Succeeded / 8 residual empty-sig audit entries.

The 8 residuals all have `CreatedAt` between `2026-05-11 14:52` and `15:39` ICT — 2 days before the P1 ship (`07b5487a` at 21:25 ICT on 2026-05-13). These predate the resolver entirely; their Hangfire jobs were purged long before the smoke. Per `HmacChainService.cs:84-89`, `VerifyAsync` treats empty signatures as **skip-not-tamper**, so the gap is not a correctness regression. Plan-C (accept gap on dev DB) was the documented acceptance path and was effectively in force from the start.

### Two surprising findings worth carrying

**1. `VaultKeyResolver` defaults to a literal `"root"` token.**

```csharp
// src/shared/AqtCoreFW.Infrastructure/Configuration/VaultKeyResolver.cs:131
IAuthMethodInfo authMethod = !string.IsNullOrEmpty(opts.RoleId)
    ? new AppRoleAuthMethodInfo(opts.RoleId, opts.SecretId!)
    : new TokenAuthMethodInfo(opts.Token ?? "root");
```

If `Vault:Token` is unset and `VAULT_TOKEN` env var is missing, the client authenticates with the literal string `"root"`. In a Vault dev container, the actual token is whatever `VAULT_DEV_ROOT_TOKEN_ID` was set to at container start — `dev-only-token` here — and `"root"` simply doesn't exist. The failure mode is silent: a `KeyNotFoundException` wrapped around `permission denied`, indistinguishable from a missing secret path. Worth knowing during prod incident triage when someone "forgot the token" — the symptom won't be "no auth method," it'll be "permission denied on a path that's clearly there."

**2. Hangfire dashboard CSRF enforces even when authz is off in Dev.**

`HangfireAuthorizationFilter.cs:14-24` allows unauthenticated access when `IHostEnvironment.IsDevelopment()`. But CSRF stays on regardless. Scripting the dashboard requires the `.AspNetCore.Antiforgery.fYyEpaEthMo` cookie + `X-XSRF-TOKEN` header pair, extracted from a single GET on a dashboard page (each GET issues a fresh pair; mixing across requests is a 400 Bad Request). Documented in the P3a runbook section, but the actual extraction recipe is now in [phase-03-hangfire-backlog-replay.md](../../plans/260513-1805-audit-hmac-vault-seeder-sweep/phase-03-hangfire-backlog-replay.md) for the next ops who needs it.

### Follow-ups filed

- **P3b-FU1 (P3, ~30 min):** Wire `VAULT_TOKEN=dev-only-token` into the `aqt-runapp` skill / dev launch profile / `docker-compose.override.yml` so raw `dotnet run` doesn't trap the next dev-stack session. Production unaffected — real deployments wire AppRole or explicit Helm-managed tokens, both of which short-circuit the literal `"root"` fallback.
- **P3b-FU2 (P4, ~15 min):** Dev Vault container uses in-memory storage → seed gone on every restart. Either add a healthcheck-dependent init sidecar that re-runs `vault-init.sh` automatically, or document a one-line re-seed snippet in `docs/deployment-guide.md` §5 next to the dev compose docs.

Plan status now: P1+P2+P3 (P3a docs + P3b smoke) + P4 + P5 shipped; only P6 finalize (changelog + closing memory index entries) remains, and most of that is already done inline this session.

### Process notes

- `/ck:cook P3b --auto` was the right shape — the plan was already written, the spec was already locked, the work was operational. Subagents would have been overhead. Inline edits + a single commit kept the trail short.
- Advisor caught a load-bearing assertion mid-session — the 8 residuals claim that read "pre-P1 writes" was not yet evidence. One SQL query closed it. Worth the prompt even on a session that felt almost done.
- Memory + backlog updates went first; the commit message references both. Future "what's next" surveys will see the FUs without needing to read this journal.
