---
title: macOS (Apple Silicon M5) dual-machine dev setup
status: in-progress
created: 2026-09-23
branch: chore/macos-dual-machine-dev
---

# macOS dual-machine dev setup

## Brainstorm contract

- **Outcome:** Dev AqtCoreFW được trên MacBook M5 (Docker Desktop) song song máy Windows; chuyển máy không làm bẩn git, không vỡ build/test bên kia.
- **Constraints:** tại 1 thời điểm chỉ 1 máy làm (user 2026-09-23); không đổi code nghiệp vụ, API, schema, CI; hành vi `1_run-local-dev.ps1` trên Windows giữ y nguyên; `.bat` giữ nguyên.
- **Non-goals:** CI macOS runner; chạy k8s-local trên Mac; đồng bộ DB/Vault data giữa 2 máy; profile AI (qdrant/ollama).
- **Acceptance:**
  1. Windows: `git status` sạch, `dotnet build src/AqtCoreFW.slnx /warnaserror` xanh, `.\scripts\1_run-local-dev.ps1` (Windows PowerShell 5.1 và pwsh 7) chạy như cũ: infra + Vault seed + Auth/API/FE + login.
  2. Mac: `pwsh scripts/1_run-local-dev.ps1` dựng SQL (amd64/Rosetta) + Redis + RabbitMQ + Vault (seed qua vault-init) + Auth :5002 + API :5001 + FE :5173; login OIDC OK.
  3. Mac: `dotnet test ci/unit.slnx` xanh; bộ Testcontainers Messaging integration xanh; 1 lần export PDF (QuestPDF/Skia osx-arm64) OK.
  4. Round-trip: commit trên Mac -> pull trên Windows -> `git status` sạch (không diff eol/mode); `git ls-files -s '*.sh' | grep -c 100644` = 0.
  5. Claude trên Mac: rules/skills/memory lấy từ `claude-home` (Bitbucket org private); memory MỚI ghi trên Mac nằm trong repo claude-home (không cạnh link); hooks trên Mac fire (privacy-block + dev-rules-reminder).

## Scout evidence (2026-09-23, re-verified in validation + red-team)

- Index 100% LF (`git ls-files --eol`: 6233 lf, 0 crlf); `.gitattributes` đã ép `*.sh eol=lf`, Dockerfile/compose LF.
- Không có API chỉ-Windows trong `src/` (grep `OperatingSystem.IsWindows|IsOSPlatform|X509Store|LocalDB|System.Drawing|Process.Start` = 0 hit liên quan; `.exe` ở `FileSecurityService.cs:17` chỉ là blocklist). PDF font embed Noto Sans (`QuestPdfExporter.cs:18,29`).
- `mcr.microsoft.com/mssql/server:2022-latest` không có arm64: `infra/docker/docker-compose.yml:7`, Testcontainers `MessagingFixture.cs:24-25`, `OutboxStoreFixture.cs:28`, `DatabaseFixture.cs:15`. Linux mssql thường không có full-text; code tự degrade (`SqlServerFtsSearchService.cs:80-88`).
- `.dotnet/` bị track (20 file, có `C:\NuGetPackages` + sentinel `IsDockerContainer`), không bị ignore; không file nào đọc nó; nguồn ghi chưa xác định.
- 19/20 `*.sh` mode 100644; Windows `core.filemode=false`.
- `appsettings.Development.json` (Api + AuthServer) được track, conn `localhost,1433` + sa dev password -> Mac dùng nguyên trạng. File local duy nhất: `web/.env.local` (có `.env.example`).
- `scripts/1_run-local-dev.ps1` (631 dòng): phần chỉ-Windows = `Get-NetTCPConnection` (dòng 121, 148), `Start-Process powershell` (353, 373, 390), ~10 path backslash (75-76, 182, 209, 259, 299...). Có bước Vault start + `vault-init` (225-304).
- Skill `~/.claude/skills/aqt-runapp` đã OS-aware (`kill_port` lsof/netstat, SKILL.md:50-59,179).
- Vault dev mode in-memory (`docker-compose.yml:77,86`): mọi restart mất secret -> phải chạy lại vault-init.
- `plans/**` gitignored (`.gitignore:100`) -> phiên Mac không thấy plan; checklist phải ở docs (tracked).
- `~/.claude`: không phải git repo; có `.env`, `.credentials.json`, `daemon/pipe.key`, `sessions/*.key`; 2 skill là symlink (`agentation`, `agentation-self-driving`, `aqt-interview-techlead` - cần kiểm lại); `settings.json` hard-code `C:/...` path; memory 489 file, 105 file nhắc synodus/bitbucket (thông tin nội bộ org).
- Untracked: `docs/guides/onboarding-1-thang-*.{md,docx}`.

## Phases

| # | Phase | Files | Status |
|---|---|---|---|
| P0 | Xử lý 3 file untracked `docs/guides/onboarding-1-thang-*` (hỏi user commit hay bỏ) trước khi tách nhánh | docs/guides | pending |
| P1 | Repo hygiene: tìm nguồn ghi `.dotnet/` (Dockerfile/compose mount `HOME`); `git rm -r --cached .dotnet` + `.dotnet/` vào `.gitignore` cùng 1 commit; `git update-index --chmod=+x` 19 `*.sh` sau khi đọc từng script | `.gitignore`, index | pending |
| P2 | Compose: `platform: linux/amd64` cho `sqlserver`; bind port dev (1433, 8200, 5672, 6379) về `127.0.0.1`; pin `curlimages/curl:latest` (vault-init) về version tag | `infra/docker/docker-compose.yml` | pending |
| ~~P3~~ | ~~Config mẫu~~ - BỎ (appsettings.Development.json đã track) | - | dropped |
| P4 | Port `scripts/1_run-local-dev.ps1` cross-platform (pwsh 7 trên Mac, giữ PS 5.1 Windows): nhánh `$IsWindows` cho port lookup (`Get-NetTCPConnection` vs `lsof -ti`), launcher (`powershell` vs `pwsh` / Terminal), path dùng `Join-Path` từng segment; giữ nguyên bước Vault + vault-init. BỎ `dev.sh` | `scripts/1_run-local-dev.ps1` | pending |
| P5 | Docs `docs/guides/macos-dev-setup.md` + link từ `docs/deployment-guide.md`: cài (Docker Desktop + Rosetta ON, ≥6GB RAM, .NET 10.0.103, Node 22, pnpm 10.33, pwsh), `cp web/.env.example web/.env.local`, git config Mac (`autocrlf=input`), cảnh báo credential dev-only, quy trình đổi máy (kèm `down -v` + reseed khi lệch migration), Vault in-memory -> luôn reseed, search FTS rỗng trên Mac, clamav (upload Quarantined nếu tắt), bảng hook Windows->Mac, **checklist verify Mac** | docs | pending |
| P6 | Verify Windows (acceptance 1, cả PS 5.1 và pwsh) + code-review; commit + push nhánh. Phiên Claude Code trên Mac chạy checklist P5 (acceptance 2-4), hỏi user trước khi sửa Testcontainers (`WithImagePlatform`) | - | pending |
| P7 | `claude-home` trên **Bitbucket workspace org, private**: `.gitignore` deny-all (`*`) rồi un-ignore `rules/`, `skills/`, `CLAUDE.md`, `memory/`; chặn `.env*`, `*.key`, `.credentials*`, `settings*.json`, `.venv`; gitleaks pre-commit; xử lý symlink trong skills (`find skills -type l` -> inline hoặc exclude); link memory: junction (Win) / symlink (Mac) theo slug từng máy, script từ chối target ngoài `~/claude-home`; `MEMORY.md merge=union`; hook SessionStart pull / Stop push. User tạo repo trên Bitbucket | ngoài repo: `~/claude-home/` | pending |

## Quy trình đổi máy (1 máy tại 1 thời điểm)

Rời máy: commit (WIP được) -> `git push` (origin + bitbucket) -> `claude-home` push (hook tự động).
Vào máy: `git pull --rebase` -> `claude-home` pull (hook) -> chạy runner (tự seed Vault) -> nếu có migration mới: apply; nếu DB lệch: `docker compose down -v` + reseed.

## Blast Radius & Đối xứng

1. **Backward:** `1_run-local-dev.ps1` là runner chính trên Windows - P4 sửa trực tiếp nó -> rủi ro cao nhất; mọi nhánh mới rẽ theo `$IsWindows`, đường Windows giữ y nguyên lệnh cũ; test hồi quy cả PS 5.1 và pwsh 7. P1 bỏ track `.dotnet/` (không ai đọc). P2 bind `127.0.0.1` - kiểm tra không container/host nào khác truy cập qua IP LAN; `platform` no-op trên x64/CI (compose job CI `ci.yml:571`).
2. **Downstream:** `.gitignore` ảnh hưởng mọi clone; 7 worktree `.claude/worktrees/*` vẫn track `.dotnet/` tới khi rebase. Skill `aqt-runapp` có logic riêng (OS-aware), không đổi.
3. **Symmetry:** 1 runner duy nhất cho 2 OS (không còn `dev.sh`). Junction Windows ↔ symlink Mac trong P7. Hook Windows ↔ Mac liệt kê trong P5.
4. **Bypass:** Testcontainers bỏ qua `platform:` compose -> phụ thuộc Docker Desktop auto-select amd64 + Rosetta. k8s-local scripts không đổi (Windows-only).

## Regression test (đường thực thi)

- Windows PS 5.1: `.\scripts\1_run-local-dev.ps1` -> sqlserver/redis/rabbitmq/vault up -> vault-init seed -> Auth/API/FE lên -> login -> tạo & submit 1 Form -> `stop all` giải phóng 3 port.
- Windows pwsh 7: lặp lại lệnh trên.
- Windows: `docker compose -f infra/docker/docker-compose.yml up -d sqlserver` sau P2 -> healthy; API connect `localhost,1433` OK.
- Windows: `git status` sạch sau `git pull` commit từ Mac.
- Windows sau P7: Claude session mới load `MEMORY.md`; ghi 1 memory thử -> file nằm trong `~/claude-home/memory/`.

## Validation Log

### Session 1 - 2026-09-23

Verification Results
- Claims checked: 18 | Verified: 16 | Failed: 2 | Unverified: 0 | Tier: Full
- Failed 1: "appsettings.Development.json gitignored" - sai, file được track. -> P3 bỏ.
- Failed 2: phiên Mac đọc plan qua git - sai, `plans/**` gitignored. -> checklist vào docs P5.

Decisions (user): P3 bỏ; P2 `platform` tường minh; claude-home = P7; verify Mac do phiên Claude trên Mac.

## Red Team Review

4 reviewer (Security, Assumption, Failure, Scope): 22 finding thô -> 12 sau dedupe. User: apply toàn bộ Accept; P7 host Bitbucket org.

| # | Finding | Sev | Disposition |
|---|---|---|---|
| 1 | P7 đẩy thông tin nội bộ org (105 memory file) lên GitHub cá nhân | Critical | Accept -> host Bitbucket org |
| 2 | Runner Mac thiếu bước Vault/vault-init -> login fail | Critical | Accept -> giữ nhờ port `.ps1` |
| 3 | `dev.sh` trùng lặp `.ps1` (phần Windows-only ~10 dòng) | High | Accept -> bỏ `dev.sh`, port `.ps1` |
| 4 | `.gitignore` P7 không chặn `.env`, `*.key`, `daemon/` | High | Accept -> deny-all + gitleaks |
| 5 | Symlink trong skills | High | Accept |
| 6 | Memory link: ghi mới có thể vỡ link; conflict `MEMORY.md` | High | Accept -> test ghi + `merge=union` + hook pull/push |
| 7 | Rosetta/Testcontainers chưa đo; FTS khác biệt | High | Accept -> prerequisite + gate Messaging + note |
| 8 | bash 3.2/BSD tools cho `dev.sh` | High | Moot (dev.sh bỏ) |
| 9 | Nguồn ghi `.dotnet/` chưa xác định | Medium | Accept |
| 10 | Credential dev + port 0.0.0.0 trên laptop | Medium | Accept -> bind 127.0.0.1 |
| 11 | Vault in-memory, lệch DB khi đổi máy | Medium | Accept -> docs |
| 12 | Pin `latest` image; PDF Skia arm64 smoke; hooks Mac; untracked onboarding docs | Medium | Accept |
| R1 | Dùng `azure-sql-edge` | - | Reject: Microsoft đã ngừng hỗ trợ |
| R2 | Ép `*.sh eol=lf` trong `.gitattributes` | - | Reject: đã có (`.gitattributes:3`) |

### Whole-Plan Consistency Sweep
- Mọi tham chiếu `dev.sh` đã gỡ (phases, acceptance, blast radius, regression); finding #8 đánh dấu moot.
- Acceptance 1 nay bao gồm Vault + PS 5.1/pwsh; acceptance 3 thêm PDF smoke; acceptance 5 thêm test ghi memory + hooks.
- P7 host đổi từ "GitHub private" -> Bitbucket org ở mọi chỗ.
- Không còn mâu thuẫn chưa giải quyết.

## Implementation Log (2026-09-23, Windows side)

Scope extended by user ("làm đầy đủ ... 100%") with the Kongming handoff gaps (P1b, P7b, P8-P11).

| Phase | Result |
|---|---|
| P0 | NOT touched: 3 untracked `docs/guides/onboarding-1-thang-*` files left for the user to decide |
| P1 | `.dotnet/` untracked + ignored (writer: something runs with CLI home at repo root, PATH contains `C:\AqtCoreFW\.dotnet\tools`); `.logs/` ignored; 20/20 `*.sh` mode 100755 |
| P1b | `web/.env.example` authority 5002; `CLAUDE.md` rules paths -> `~/.claude/rules` |
| P2 | sqlserver `platform: linux/amd64`; infra ports (1433, 6379, 5672, 15672, 9000, 9001, 3310, 8200) bound to 127.0.0.1; `curlimages/curl` pinned 8.10.1 (same as k8s-local) |
| P4 | `1_run-local-dev.ps1` cross-platform (`$IS_WIN`, lsof, background pwsh + `.logs/`); pre-existing `1_run-local-dev.sh` (no Vault step) turned into a pwsh wrapper |
| P5 | `docs/guides/macos-dev-setup.md` + link in `docs/deployment-guide.md` |
| P7/P7b | **Design change vs P7 row: copy-mirror sync instead of junction/symlink** (Claude Code writes via temp+rename, which can replace a link). `~/claude-home` (local git repo, no remote yet): copy-mirror sync `claude-home.ps1 init/status/push/pull`, deny-all `.gitignore`, secret scan, per-OS settings render, first-sync additive, stale-push refusal, trash instead of delete |
| P8 | `scripts/dev-doctor.ps1`, `scripts/leave-machine.ps1` |
| P9 | `scripts/dev-secrets-handoff.ps1` (PBKDF2-SHA256 200k, AES-256-CBC + HMAC-SHA256) |
| P10 | MCP: documented only (Windows `.exe` servers need Mac builds) |
| P11 | this plan re-included in `.gitignore` |

Verification (Windows): all 5 scripts parse (PS 5.1); runner `start fe` / `stop fe` / `stop all` OK; doctor runs (flags Node 24 vs `.nvmrc` 22 as WARN, Docker off as FAIL); leave-machine blocks on dirty tree; secrets export/import round-trip identical, wrong/short passphrase rejected, no temp leftovers; settings render drops 15 `.cmd` hooks, 0 `C:/` left; claude-home sandbox tests (first-sync additive, stale push refused, pull guard, trash). No C#/TS source changed, so build/tests unaffected (not re-run).

Code review (fresh context): C1/H1/H2/H3/M1/M2/M3/M4/M5/L1/L2 fixed; L3 (byte slicing speed) accepted.

Pending on the Mac (checklist in `docs/guides/macos-dev-setup.md` section 2): acceptance 2-5.

NOT verified on Windows: full `start all` (Docker Desktop was not running, so SQL/Vault/Auth/API boot and login were not exercised) and the pwsh 7 path (pwsh is not installed on this Windows machine). Both are covered by the Mac checklist; run `.\scripts\1_run-local-dev.ps1` once on Windows with Docker up before relying on it.
