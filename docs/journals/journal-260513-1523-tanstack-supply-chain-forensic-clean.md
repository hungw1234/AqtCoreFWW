# TanStack npm Supply-Chain Attack — Forensic Scan (CLEAN)

**Date**: 2026-05-13 15:23 ICT
**Severity**: External advisory; project verified CLEAN
**Component**: `web/` npm workspace, `.claude/`, `.vscode/`
**Status**: No action required — defensive lockfile pin recommended

## What Happened

External advisory: 2026-05-11 19:20–19:26 UTC, attacker published 84 malicious versions across 42 `@tanstack/*` packages on npm via poisoned GitHub Actions release runner (trusted-publishing OIDC abuse, not token theft).

Payload (`router_init.js` ~2.3 MB, SHA-256 `ab4fcadaec49c03278063dd269ea5eef82d24f2124a8e15d7b90f2fa8601266c`) delivered through optional dep `@tanstack/setup` pointing at GitHub commit `79ac49eedf774dd4b0cfa308722bc463cfe5885c`. Persists via `.claude/router_runtime.js`, `.claude/setup.mjs`, `.vscode/tasks.json`, `.vscode/setup.mjs` — opening Claude Code / VS Code in an infected project re-arms the payload.

Project AqtCoreFW uses `@tanstack/react-query`, `@tanstack/react-router`, `@tanstack/react-virtual` → in-scope. Ran full IOC sweep.

## Findings — All Negative

| IOC | Result | Evidence |
|---|---|---|
| `router_init.js` / `router_runtime.js` / `setup.mjs` (any path) | NOT PRESENT | `Glob **/router_init.js` etc → 0 |
| Package `@tanstack/setup` | NOT PRESENT | `Grep` over `package.json` + `package-lock.json` → 0 |
| Malicious commit `79ac49eedf...5885c` | NOT PRESENT | Repo-wide grep → 0 |
| `optionalDependencies` GitHub ref to TanStack | NOT PRESENT | Lockfile shows no such block |
| `.claude/router_runtime.js`, `.claude/setup.mjs` | NOT PRESENT | `.claude/` contains only `launch.json`, `settings.local.json`, `agent-memory/`, `session-state/`, `worktrees/` |
| `.vscode/tasks.json`, `.vscode/setup.mjs` | N/A | `.vscode/` directory does not exist |

**Timeline gate:** `web/package-lock.json` mtime = **2026-05-11 03:36 UTC** (10:36 ICT). Attack publish window started **2026-05-11 19:20 UTC** → install froze **~16h before malicious versions existed on npm**.

**Installed TanStack packages** (all `registry.npmjs.org/...tgz` with sha512 integrity, no GitHub deps):

- `@tanstack/react-query@5.90.21` + `query-core@5.90.20`
- `@tanstack/react-router@1.167.4` + `router-core@1.167.4` + `history@1.161.6`
- `@tanstack/react-virtual@3.13.23` + `virtual-core@3.13.23`
- `@tanstack/react-store@0.9.2` + `store@0.9.2`

## The Brutal Truth

Negative-result journal. Two minutes from threat headline to clean verdict because two unrelated safety nets — lockfile pin + 16h timing gap — happened to align. Neither was designed for this incident; we got lucky on the timing.

Real risk going forward: a teammate running `npm install` / `npm update` *without* reviewing the lockfile diff would silently pull malicious or unverified versions if registry hasn't fully cleaned the malicious tarballs. No automated guard exists in repo.

## Decisions

**Done now:**
- IOC scan documented (this journal).
- No code change. No credential rotation. No `node_modules` rebuild.

**Deferred — not justified by YAGNI today:**
- Pre-commit hook rejecting `@tanstack/setup` / commit hash in `package*.json` diff. Speculative; one-time incident; would add maintenance for zero recurrence signal. Re-evaluate if a 2nd supply-chain incident hits the npm side of the repo.
- Upgrade `@tanstack/*` past 1.167.4 / 5.90.21 / 3.13.23 — wait until TanStack publishes clean post-incident versions and Socket/StepSecurity advisories settle (est. 2–5 days). Track via WebFetch on TanStack postmortem.

## Lessons Learned

- **Lockfile mtime is forensic evidence.** A pinned `package-lock.json` older than the attack window is conclusive for npm supply-chain incidents — assuming no one ran `npm install` post-incident. Cheap to verify; should be the first check.
- **YAGNI applies to security hardening too.** Guardrails for zero-recurrence incidents drift into noise. Wait for the second occurrence before adding pre-commit checks; document the runbook instead.
- **Persistence vectors expand attack surface beyond `node_modules`.** Future supply-chain advisories should always trigger sweeps of `.claude/`, `.vscode/`, shell rc files — even if `node_modules` looks clean.

## Test Evidence

N/A — read-only forensic scan, no code change.

## Next Steps

| Item | Owner | When |
|---|---|---|
| Watch TanStack postmortem for full list of poisoned versions to confirm 1.167.4 / 5.90.21 / 3.13.23 are outside it (highly likely given 16h timing gap) | Self | 2–3 days |
| When advisories stabilize, plan `@tanstack/*` minor-version upgrade with lockfile review | Backlog | Next sprint |
| If a 2nd npm supply-chain incident hits this repo's deps, revisit pre-commit IOC guard | Backlog | On recurrence |

## Commit

No commit (read-only investigation). This journal documents negative result for audit trail.
