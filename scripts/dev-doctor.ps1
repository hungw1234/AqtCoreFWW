#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Readiness check for an AqtCoreFW dev machine (Windows or macOS). Run after `git pull`
  on the machine you are switching to; everything should be [OK] before starting work.

.DESCRIPTION
  Checks toolchain versions, Docker (incl. Rosetta amd64 emulation on Apple Silicon),
  git line-ending config, frontend env, API user-secrets (signing), dev ports, Vault,
  Claude Code hook paths and the claude-home sync repo. Secret VALUES are never printed;
  only key names / counts / path existence.

  Exit code = number of [FAIL] checks (0 = ready). [WARN] does not fail the run.

.PARAMETER Deep
  Also run the slow checks: amd64 emulation probe container and `dotnet tool restore`.

.EXAMPLE
  pwsh scripts/dev-doctor.ps1
  pwsh scripts/dev-doctor.ps1 -Deep
#>
param([switch]$Deep)

$ErrorActionPreference = "Continue"
$IS_WIN = ($PSVersionTable.PSEdition -eq "Desktop") -or $IsWindows
$IS_MAC = (-not $IS_WIN) -and $IsMacOS
$ROOT = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$script:fails = 0
$script:warns = 0

function Ok   { param([string]$m) Write-Host "  [OK]   $m" -ForegroundColor Green }
function Warn { param([string]$m) Write-Host "  [WARN] $m" -ForegroundColor Yellow; $script:warns++ }
function Fail { param([string]$m) Write-Host "  [FAIL] $m" -ForegroundColor Red; $script:fails++ }
function Section { param([string]$m) Write-Host "`n== $m ==" -ForegroundColor Cyan }
function Has { param([string]$cmd) [bool](Get-Command $cmd -ErrorAction SilentlyContinue) }

function Test-TcpPort {
    param([int]$Port)
    $tcp = New-Object System.Net.Sockets.TcpClient
    try { $tcp.Connect("127.0.0.1", $Port); return $true } catch { return $false } finally { $tcp.Close() }
}

# ---------------------------------------------------------------- toolchain
Section "Toolchain"
$globalJson = Get-Content (Join-Path $ROOT "src/global.json") -Raw | ConvertFrom-Json
$wantSdk = [version]$globalJson.sdk.version
if (Has dotnet) {
    $sdk = (& dotnet --version 2>$null)
    if ($sdk -and ([version]($sdk -replace '-.*$', '')) -ge $wantSdk -and ([version]($sdk -replace '-.*$', '')).Major -eq $wantSdk.Major) {
        Ok ".NET SDK $sdk (global.json >= $wantSdk, rollForward $($globalJson.sdk.rollForward))"
    } else { Fail ".NET SDK '$sdk' does not satisfy src/global.json $wantSdk (install .NET $($wantSdk.Major) SDK)" }
} else { Fail "dotnet not found" }

$pkg = Get-Content (Join-Path $ROOT "web/package.json") -Raw | ConvertFrom-Json
$wantNode = (Get-Content (Join-Path $ROOT "web/.nvmrc") -Raw).Trim()
if (Has node) {
    $node = (& node --version).TrimStart('v')
    if ($node.Split('.')[0] -eq $wantNode.Split('.')[0]) { Ok "Node $node (.nvmrc $wantNode)" }
    else { Warn "Node $node, web/.nvmrc and CI use $wantNode (nvm install $wantNode)" }
} else { Fail "node not found" }

$wantPnpm = ($pkg.packageManager -replace '^pnpm@', '')
if (Has pnpm) {
    $pnpm = (& pnpm --version)
    if ($pnpm -eq $wantPnpm) { Ok "pnpm $pnpm" } else { Warn "pnpm $pnpm, package.json pins $wantPnpm (corepack enable && corepack prepare pnpm@$wantPnpm --activate)" }
} else { Fail "pnpm not found (corepack enable)" }

if (Test-Path (Join-Path $ROOT "web/node_modules")) { Ok "web/node_modules present" }
else { Warn "web/node_modules missing: cd web && pnpm install --frozen-lockfile" }

if ($Deep) {
    Push-Location $ROOT
    & dotnet tool restore *> $null
    if ($LASTEXITCODE -eq 0) { Ok "dotnet tool restore (dotnet-ef from .config/dotnet-tools.json)" } else { Fail "dotnet tool restore failed" }
    Pop-Location
}

# ---------------------------------------------------------------- docker
Section "Docker"
if (-not (Has docker)) { Fail "docker not found (install Docker Desktop)" }
else {
    $arch = (& docker info --format '{{.Architecture}}' 2>$null)
    if (-not $arch) { Fail "Docker daemon not running (start Docker Desktop)" }
    else {
        Ok "Docker daemon up (arch: $arch)"
        if ($arch -match 'aarch64|arm64') {
            if ($Deep) {
                $m = (& docker run --rm --platform linux/amd64 alpine:3.20 uname -m 2>$null)
                if ($m -eq 'x86_64') { Ok "amd64 emulation works (SQL Server image is amd64-only)" }
                else { Fail "amd64 emulation failed: enable Docker Desktop > Settings > General > 'Use Rosetta for x86_64/amd64 emulation'" }
            } else { Warn "arm64 host: run with -Deep once to verify Rosetta amd64 emulation for SQL Server" }
        }
    }
}

# ---------------------------------------------------------------- git
Section "Git"
Push-Location $ROOT
$autocrlf = (& git config --get core.autocrlf)
$wantCrlf = if ($IS_WIN) { "true" } else { "input" }
if ($autocrlf -eq $wantCrlf) { Ok "core.autocrlf=$autocrlf" } else { Fail "core.autocrlf='$autocrlf', expected '$wantCrlf' (git config --global core.autocrlf $wantCrlf)" }
$nonExec = @(& git ls-files -s '*.sh' | Where-Object { $_ -match '^100644' })
if ($nonExec.Count -eq 0) { Ok "all tracked *.sh are executable in the index" } else { Warn "$($nonExec.Count) *.sh without +x in index" }
$dirty = @(& git status --porcelain)
if ($dirty.Count -eq 0) { Ok "working tree clean" } else { Warn "$($dirty.Count) uncommitted change(s)" }
$branch = (& git rev-parse --abbrev-ref HEAD)
$counts = (& git rev-list --left-right --count "HEAD...@{u}" 2>$null)
if ($counts) {
    $ahead, $behind = $counts -split '\s+'
    if ([int]$behind -gt 0) { Fail "$branch is $behind commit(s) behind upstream: git pull --rebase" }
    else { Ok "$branch up to date with upstream (ahead $ahead)" }
} else { Warn "$branch has no upstream" }
Pop-Location

# ---------------------------------------------------------------- app config
Section "App config"
$envLocal = Join-Path $ROOT "web/.env.local"
if (Test-Path $envLocal) {
    $authority = (Select-String -Path $envLocal -Pattern '^VITE_OIDC_AUTHORITY=(.*)$').Matches.Groups[1].Value
    if ($authority -match ':5002') { Ok "web/.env.local authority -> :5002" }
    else { Warn "web/.env.local authority is '$authority' (local dev runner uses :5002)" }
} else { Warn "web/.env.local missing (runner creates it, or: cp web/.env.example web/.env.local)" }

# API user-secrets (digital signing). Values stay local; only names + path existence are checked.
$secretsDir = if ($IS_WIN) { Join-Path $env:APPDATA "Microsoft/UserSecrets" } else { Join-Path $HOME ".microsoft/usersecrets" }
$apiSecrets = Join-Path $secretsDir "aqtcorefw-api-dev/secrets.json"
if (Test-Path $apiSecrets) {
    $s = Get-Content $apiSecrets -Raw | ConvertFrom-Json
    $names = @($s.PSObject.Properties.Name)
    $signing = @($names | Where-Object { $_ -like 'Signing:*' })
    if ($signing.Count -ge 9) { Ok "API user-secrets: $($signing.Count) Signing:* keys" }
    else { Warn "API user-secrets: $($signing.Count) Signing:* keys (expected 9); import via scripts/dev-secrets-handoff.ps1" }
    foreach ($n in ($names | Where-Object { $_ -like 'Signing:TrustStore:*' })) {
        if (-not (Test-Path $s.$n)) { Fail "user-secret $n points to a missing file (re-run dev-secrets-handoff.ps1 import)" }
    }
} else { Warn "No API user-secrets: digital-signing flows disabled; import via scripts/dev-secrets-handoff.ps1" }

# ---------------------------------------------------------------- ports + vault
Section "Services"
foreach ($p in 5001, 5002, 5173) {
    if (Test-TcpPort $p) { Warn "port $p in use (runner will kill the owner on start)" } else { Ok "port $p free" }
}
try {
    $h = Invoke-WebRequest -Uri "http://localhost:8200/v1/sys/health" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
    if ($h.StatusCode -eq 200) { Ok "Vault up on :8200 (runner reseeds missing secrets automatically)" }
} catch { Ok "Vault not running yet (runner starts + seeds it)" }

# ---------------------------------------------------------------- claude code
Section "Claude Code"
$claudeDir = Join-Path $HOME ".claude"
$settings = Join-Path $claudeDir "settings.json"
if (Test-Path $settings) {
    $raw = Get-Content $settings -Raw
    $missing = @()
    foreach ($m in [regex]::Matches($raw, '"command"\s*:\s*"((?:[^"\\]|\\.)*)"')) {
        $cmd = $m.Groups[1].Value -replace '\\"', '"'
        # First token = executable (quoted or bare); only absolute paths are checked.
        $exe = if ($cmd -match '^"([^"]+)"') { $Matches[1] } else { ($cmd -split '\s+')[0] }
        if (($exe -match '^[A-Za-z]:[\\/]' -or $exe.StartsWith('/')) -and -not (Test-Path $exe)) { $missing += $exe }
    }
    $missing = @($missing | Select-Object -Unique)
    if ($missing.Count -eq 0) { Ok "all absolute hook executables in ~/.claude/settings.json exist" }
    else { foreach ($x in $missing) { Fail "hook executable missing: $x (render settings: claude-home.ps1 pull)" } }
} else { Fail "~/.claude/settings.json missing (claude-home.ps1 pull)" }

$ch = Join-Path $HOME "claude-home"
if (Test-Path (Join-Path $ch ".git")) {
    Push-Location $ch
    $chDirty = @(& git status --porcelain)
    $chCounts = (& git rev-list --left-right --count "HEAD...@{u}" 2>$null)
    Pop-Location
    if ($chDirty.Count -gt 0) { Warn "claude-home has $($chDirty.Count) uncommitted change(s)" }
    if ($chCounts) {
        $a, $b = $chCounts -split '\s+'
        if ([int]$b -gt 0) { Fail "claude-home is $b commit(s) behind: pwsh ~/claude-home/scripts/claude-home.ps1 pull" }
        else { Ok "claude-home up to date (ahead $a)" }
    } else { Warn "claude-home has no upstream (push it to the org Bitbucket repo)" }
} else { Warn "~/claude-home not found: rules/skills/memory are not synced to this machine" }

# ---------------------------------------------------------------- summary
Write-Host ""
if ($script:fails -eq 0) { Write-Host "READY ($script:warns warning(s))" -ForegroundColor Green }
else { Write-Host "NOT READY: $script:fails failure(s), $script:warns warning(s)" -ForegroundColor Red }
exit $script:fails
