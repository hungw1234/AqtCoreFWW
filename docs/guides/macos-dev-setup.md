# macOS dev setup and switching between two machines

Scope: developing AqtCoreFW on a MacBook (Apple Silicon) alongside the Windows machine,
**one machine active at a time**. Code travels through git; machine-local state (secrets,
Docker volumes, logins) never does and is handled by the steps below.

Scripts (all PowerShell 7, run the same on both OS):

| Script | When | What |
|---|---|---|
| `scripts/1_run-local-dev.ps1` (or `scripts/1_run-local-dev.sh` on Mac) | start work | infra + Vault seed + build + Auth :5002 / API :5001 / FE :5173 |
| `scripts/dev-doctor.ps1 [-Deep]` | after every `git pull` on the arriving machine | readiness check, exit code = failures |
| `scripts/leave-machine.ps1 [-Push]` | before walking away | blocks on anything that would not reach the other machine |
| `scripts/dev-secrets-handoff.ps1 export\|import` | once per new machine (or when secrets change) | encrypted bundle of signing user-secrets + gitignored signing files |
| `~/claude-home/scripts/claude-home.ps1 push\|pull` | with leave / arrive | Claude Code rules, skills, agents, hooks, memory |

## 1. What goes where

| Channel | Carries |
|---|---|
| **git (this repo)** | code, `appsettings.Development.json` (dev-only credentials), `src/global.json`, `.config/dotnet-tools.json`, `web/.env.example`, backlogs, journals, docs, in-flight plans re-included in `.gitignore` |
| **claude-home** (private repo on the org Bitbucket) | `~/.claude/{rules,skills,agents,hooks,output-styles,agent-memory,commands}`, `CLAUDE.md`, statusline scripts, project memory, repo `.claude/agent-memory`, `settings.<os>.json` |
| **encrypted bundle** (AirDrop/USB, delete after import) | API user-secrets `aqtcorefw-api-dev` (9 `Signing:*` keys), `other/signing-dev-roots/*.pem`, dev `.p12` + passphrase file, Viettel CA config |
| **per machine, re-created** | Docker volumes (DB, MinIO, ClamAV signatures), Vault dev data (in-memory, reseeded by the runner), `web/.env.local` (runner creates it), git/gh/Bitbucket credentials, SSH keys, MCP servers in `~/.claude.json`, Claude plugins, skills Python venv |
| **not synced on purpose** | `.claude/worktrees`, `.claude/settings.local.json`, `.dotnet/`, `.eftools/`, logs, `other/vgca_usb_token/tool` (Windows USB-token installers) |

Dev data is not synced: an empty DB is fully usable because Auth/API run `MigrateAsync` and the
seeders (tenants, users, OpenIddict clients, demo form/process/integration) at startup. Files
uploaded into MinIO and data typed in by hand stay on the machine that created them.

## 2. One-time Mac bootstrap

1. **Docker Desktop**: Settings > General > enable *Use Rosetta for x86_64/amd64 emulation on
   Apple Silicon*; Resources > memory at least 6 GB. SQL Server has no arm64 image and runs
   under Rosetta (`platform: linux/amd64` in `infra/docker/docker-compose.yml`).
2. Toolchain (Homebrew):
   ```bash
   brew install --cask powershell dotnet-sdk
   brew install nvm git gh gitleaks
   nvm install 22 && corepack enable && corepack prepare pnpm@10.33.0 --activate
   ```
   The .NET SDK must satisfy `src/global.json` (10.0.103 or newer 10.0.x).
3. Git identity and line endings:
   ```bash
   git config --global core.autocrlf input
   git config --global user.name "ThienND"
   git config --global user.email "<same email as on Windows>"
   gh auth login
   ```
   Bitbucket: create an app password (or SSH key) and use it as the credential for the
   `bitbucket` remote; macOS keychain stores it (`git config --global credential.helper osxkeychain`).
4. Clone and add remotes (same names as Windows):
   ```bash
   git clone https://github.com/thiennd212/AqtCoreFW.git ~/AqtCoreFW
   cd ~/AqtCoreFW
   git remote add bitbucket https://bitbucket.org/synodus/v26grdnb63-be-corebasev2.git
   cd web && pnpm install --frozen-lockfile && cd ..
   dotnet tool restore
   ```
5. Secrets: on Windows run `pwsh scripts/dev-secrets-handoff.ps1 export`, AirDrop the bundle,
   then on the Mac `pwsh scripts/dev-secrets-handoff.ps1 import -Bundle ~/Downloads/aqt-dev-secrets.bundle`
   and delete the bundle on both machines. Paths inside the secrets are rewritten to the Mac checkout.
6. Claude Code (install, then):
   ```bash
   git clone <org-bitbucket>/claude-home.git ~/claude-home
   pwsh ~/claude-home/scripts/claude-home.ps1 init        # enter ~/AqtCoreFW
   pwsh ~/claude-home/scripts/claude-home.ps1 pull
   ```
   The first pull renders `~/.claude/settings.json` from the Windows one: `node.exe` path ->
   `node`, `C:/Users/...` -> `$HOME`, Windows-only `.cmd` hooks (Orca, 1DevTool) dropped. Review it,
   then `claude-home.ps1 push` to save `settings.macos.json`. Also on the Mac:
   - AgentKit `ak` CLI: install the darwin-arm64 build (the Windows `~/bin/ak` is a PE binary).
   - Skills Python deps: recreate the venv (`~/.claude/skills/.venv/bin/python3`, run the kit's `install.sh`).
   - MCP servers: write `mcpServers` in `~/.claude.json` for the Mac. `chrome-devtools` (npx) works
     as-is; 1DevTool, 1AIVault and SonarQube entries point at Windows `.exe` files and must be
     replaced by Mac builds or removed.
   - Plugins: reinstall from their marketplaces (`installed_plugins.json` holds absolute paths).
7. Verify: `pwsh scripts/dev-doctor.ps1 -Deep` must end with `READY`.

## 3. Switching machines (every time)

**Leaving** (either machine):
```bash
pwsh scripts/leave-machine.ps1 -Push     # syncs claude-home, pushes to origin + bitbucket, re-checks
```
It must end with `SAFE TO SWITCH MACHINES`. It blocks on uncommitted/untracked files, stashes,
unpushed commits and dirty worktrees, and warns about edited gitignored plans. Commit WIP
rather than stashing: stashes never leave the machine.

**Arriving**:
```bash
git pull --rebase
pwsh ~/claude-home/scripts/claude-home.ps1 pull
pwsh scripts/dev-doctor.ps1
pwsh scripts/1_run-local-dev.ps1          # or ./scripts/1_run-local-dev.sh
```
If the other machine added EF migrations, the runner's startup applies them. If the local DB
is out of line with the branch (migration history ahead/behind after a branch switch):
`cd infra/docker && docker compose down -v` then start the runner again to reseed.

## 4. Known differences on macOS

- Services run in the background; logs: `tail -f .logs/api.log` (also `auth`, `fe`, `*.err.log`).
- SQL Server under Rosetta starts slower (first start 30-60 s). Linux SQL Server images have no
  full-text search; `SqlServerFtsSearchService` degrades to empty results.
- Testcontainers pulls the amd64 `mssql/server` image the same way (needs Rosetta enabled).
- Building the `api` Docker image (`--runtime linux-musl-x64`) runs emulated; native `dotnet run`
  (the runner) is the normal Mac path.
- ClamAV (`clamav` service) is optional; without it uploads stay in Quarantined state.
- Infra ports (SQL 1433, Redis, RabbitMQ, MinIO, Vault, ClamAV) are bound to `127.0.0.1` only:
  the credentials in `docker-compose.yml` and `appsettings.Development.json` are public dev values.

## 5. In-flight plans

`plans/**` is gitignored. To carry a plan that is still being executed, add a pair to
`.gitignore` next to the existing `macos-dual-machine-dev` example and remove it when the plan
is archived. `leave-machine.ps1` warns about gitignored plan files edited in the last 48 h.
