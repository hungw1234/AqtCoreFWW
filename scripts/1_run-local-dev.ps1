#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Build & Run AqtCoreFW local dev (Auth Server + API + Frontend)

.DESCRIPTION
  Manage 3 services (Auth :5002, API :5001, Frontend :5173) individually or together.

  Default (no args): start all services + full infra check + interactive monitor loop.

  Per-service control: `start|stop|restart` × `fe|api|auth|all`.
  Per-service actions skip SQL/Docker checks (assumed running) and exit immediately.

  Prerequisites:
    - SQL Server 2022 via Docker Desktop (container "sqlserver" from infra/docker/docker-compose.yml, port 1433)
    - Docker (SQL Server + Redis + RabbitMQ -- app services run natively via dotnet run)

.PARAMETER Action
  start | stop | restart  (default: start)

.PARAMETER Service
  fe | api | auth | all   (default: all)

.PARAMETER SkipBuild
  Skip dotnet build step (faster restart)

.PARAMETER EnvMode
  Environment mode: dev (default), test, prod

.PARAMETER SkipVault
  Skip Vault container + seed check. Use only when you are certain Vault is
  already running and seeded; otherwise Auth/API will crash with VaultApiException
  because dev-mode Vault has in-memory storage (loses state on container restart).

.PARAMETER FrontendOnly
  [Back-compat] Same as: -Action start -Service fe

.PARAMETER BackendOnly
  [Back-compat] Same as: -Action start -Service auth+api (skip frontend)

.EXAMPLE
  .\scripts\1_run-local-dev.ps1                       # start all + monitor loop
  .\scripts\1_run-local-dev.ps1 start fe              # start only frontend
  .\scripts\1_run-local-dev.ps1 stop api              # kill API on :5001
  .\scripts\1_run-local-dev.ps1 restart auth          # bounce Auth Server
  .\scripts\1_run-local-dev.ps1 stop all              # stop all 3 services
  .\scripts\1_run-local-dev.ps1 restart all -SkipBuild
  .\scripts\1_run-local-dev.ps1 -FrontendOnly         # back-compat
#>

param(
    [Parameter(Position = 0)]
    [ValidateSet("start", "stop", "restart")]
    [string]$Action = "start",

    [Parameter(Position = 1)]
    [ValidateSet("fe", "api", "auth", "all")]
    [string]$Service = "all",

    [switch]$SkipBuild,

    [ValidateSet("dev", "test", "prod")]
    [string]$EnvMode = "dev",

    [switch]$SkipVault,

    [switch]$FrontendOnly,
    [switch]$BackendOnly
)

$ErrorActionPreference = "Continue"

# === Host OS ===
# Cross-platform: Windows PowerShell 5.1 (Desktop edition has no $IsWindows) and pwsh 7 on
# Windows/macOS/Linux. Windows keeps the original behavior (new console window per service);
# macOS/Linux run services in the background and write logs to .logs/<service>.log.
$IS_WIN = ($PSVersionTable.PSEdition -eq "Desktop") -or $IsWindows

# === Paths ===
$ROOT = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$API_DIR = [System.IO.Path]::Combine($ROOT, "src", "host", "AqtCoreFW.Api")
$AUTH_DIR = [System.IO.Path]::Combine($ROOT, "src", "host", "AqtCoreFW.AuthServer")
$WEB_DIR = Join-Path $ROOT "web"
$DOCKER_DIR = [System.IO.Path]::Combine($ROOT, "infra", "docker")
$LOG_DIR = Join-Path $ROOT ".logs"
# Solution file is in src/ directory
$SLN = Get-ChildItem (Join-Path $ROOT "src") -Filter "*.slnx" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $SLN) { $SLN = Get-ChildItem (Join-Path $ROOT "src") -Filter "*.sln" -ErrorAction SilentlyContinue | Select-Object -First 1 }
if (-not $SLN) { $SLN = Get-ChildItem $ROOT -Filter "*.slnx" -Recurse -Depth 2 -ErrorAction SilentlyContinue | Select-Object -First 1 }

# === Ports ===
$AUTH_PORT = 5002
$API_PORT = 5001
$FE_PORT = 5173

# === Colors ===
function Write-Header { param([string]$msg) Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-Ok { param([string]$msg) Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Warn { param([string]$msg) Write-Host "  [!] $msg" -ForegroundColor Yellow }
function Write-Err { param([string]$msg) Write-Host "  [X] $msg" -ForegroundColor Red }

# === Back-compat shim: legacy -FrontendOnly / -BackendOnly map to new Action/Service ===
# Only applies when user did NOT supply Action/Service explicitly (still defaults).
$useLegacy = ($PSBoundParameters.ContainsKey("FrontendOnly") -or $PSBoundParameters.ContainsKey("BackendOnly")) `
             -and -not $PSBoundParameters.ContainsKey("Action") `
             -and -not $PSBoundParameters.ContainsKey("Service")
if ($useLegacy) {
    $Action = "start"
    if ($FrontendOnly) { $Service = "fe" }
    elseif ($BackendOnly) { $Service = "backend" }  # synthetic -- handled below
}

# === Resolve service set (which targets to act on) ===
function Resolve-Targets {
    param([string]$svc)
    switch ($svc) {
        "fe"      { return @("fe") }
        "api"     { return @("api") }
        "auth"    { return @("auth") }
        "backend" { return @("auth", "api") }   # legacy -BackendOnly
        "all"     { return @("auth", "api", "fe") }
    }
}
$targets = Resolve-Targets -svc $Service

# === Port check & kill ===
# Returns PIDs listening on a TCP port (Get-NetTCPConnection on Windows, lsof elsewhere).
function Get-PortPids {
    param([int]$Port)
    if ($IS_WIN) {
        return @(Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue |
                 Select-Object -ExpandProperty OwningProcess -Unique)
    }
    $out = & lsof -nP -t "-iTCP:$Port" -sTCP:LISTEN 2>$null
    return @($out | Where-Object { $_ } | ForEach-Object { [int]$_ } | Select-Object -Unique)
}

function Stop-PortProcess {
    param([int]$Port)
    $procs = Get-PortPids -Port $Port
    foreach ($procId in $procs) {
        if ($procId -and $procId -ne 0) {
            $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
            if ($proc) {
                Write-Warn "Killing $($proc.ProcessName) (PID $procId) on port $Port"
                Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 500
            }
        }
    }
}

# Stop a named service by its port (idempotent -- silent if nothing listening)
function Stop-ServiceByName {
    param([string]$name)
    $port = switch ($name) {
        "auth" { $AUTH_PORT }
        "api"  { $API_PORT }
        "fe"   { $FE_PORT }
    }
    $label = switch ($name) {
        "auth" { "Auth Server" }
        "api"  { "API" }
        "fe"   { "Frontend" }
    }
    $listening = Get-PortPids -Port $port
    if ($listening.Count -eq 0) {
        Write-Warn "$label not running on :$port (nothing to stop)"
        return
    }
    Stop-PortProcess -Port $port
    Write-Ok "$label stopped (port $port freed)"
}

# === Infra checks (only run for full start-all) ===
function Test-SqlServerDocker {
    Write-Header "Checking SQL Server 2022 (Docker Desktop container)"

    # Port 1433 conflict detection: native MSSQLSERVER22 would block Docker bind
    $nativeSvc = $null
    if ($IS_WIN) { $nativeSvc = Get-Service -Name "MSSQL`$MSSQLSERVER22" -ErrorAction SilentlyContinue }
    if ($nativeSvc -and $nativeSvc.Status -eq "Running") {
        Write-Warn "Native SQL Server service MSSQLSERVER22 is Running -- will conflict with Docker on :1433"
        $answer = Read-Host "  Stop native service now? [y/N]"
        if ($answer -match '^[Yy]') {
            Stop-Service -Name "MSSQL`$MSSQLSERVER22" -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
            Write-Ok "Native service stopped (startup type untouched -- re-enable manual if needed)"
        } else {
            Write-Err "Cannot start Docker SQL while native service listens on :1433. Aborting."
            return
        }
    }

    $running = docker ps --filter "name=sqlserver" --format "{{.Names}}" 2>$null
    if ($running -match "sqlserver") {
        Write-Ok "sqlserver container running"
        return
    }
    Write-Warn "sqlserver container NOT running -- starting via docker compose..."
    Push-Location $DOCKER_DIR
    docker compose up -d sqlserver
    Pop-Location
    Write-Host "  Waiting for SQL Server readiness..." -ForegroundColor DarkGray
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        if (Test-Port 1433) { Write-Ok "sqlserver ready on :1433"; return }
        Start-Sleep -Seconds 2
    }
    Write-Warn "sqlserver did not respond on :1433 within 30s -- check 'docker logs sqlserver'"
}

function Test-DockerInfra {
    Write-Header "Checking Docker infrastructure (Redis + RabbitMQ)"
    $containers = @("redis", "rabbitmq")
    $missing = @()
    foreach ($c in $containers) {
        $running = docker ps --filter "name=$c" --format "{{.Names}}" 2>$null
        if ($running -match $c) {
            Write-Ok "$c running"
        } else {
            $missing += $c
            Write-Warn "$c NOT running"
        }
    }
    if ($missing.Count -gt 0) {
        Write-Warn "Starting Redis + RabbitMQ (targeted -- skipping nginx/app containers; sqlserver handled separately)..."
        Push-Location $DOCKER_DIR
        docker compose up -d redis rabbitmq
        Pop-Location
        Start-Sleep -Seconds 5
        Write-Ok "Redis + RabbitMQ started"
    }
}

# === Vault: container + seed check ===
# Why mandatory: Vault dev mode is in-memory backend. Any container restart wipes
# secrets. Auth/API call VaultPepperResolver + VaultAuditHmacKeyResolver during dev
# user seed -- they fail silently (VaultApiException, empty errors array) if pepper
# paths are missing. Unlike SQL Server (named volume = persistent), Vault dev = ephemeral.
$VAULT_ADDR_LOCAL = "http://localhost:8200"
$VAULT_TOKEN_DEV = "dev-only-token"

# Critical secret paths -- MUST match infra/docker/vault-init.sh DEV_TENANTS list.
$VAULT_CRITICAL_PATHS = @(
    "aqt/encryption/v1",
    "aqt/audit/hmac-v1",
    "aqt/encryption/email-pepper-00000000000000000000000000000000",
    "aqt/encryption/email-pepper-00000000000000000000000000000001",
    "aqt/encryption/email-pepper-a1b2c3d400000000000000000000b7c1"
)

function Test-VaultSecretExists {
    param([string]$Path)
    try {
        $resp = Invoke-WebRequest -Uri "$VAULT_ADDR_LOCAL/v1/secret/data/$Path" `
            -Headers @{ "X-Vault-Token" = $VAULT_TOKEN_DEV } `
            -UseBasicParsing -ErrorAction Stop -TimeoutSec 3
        return ($resp.StatusCode -eq 200)
    } catch {
        return $false
    }
}

function Test-VaultStack {
    Write-Header "Checking HashiCorp Vault (dev mode)"

    # 1. Ensure container running
    $running = docker ps --filter "name=vault" --format "{{.Names}}" 2>$null
    $vaultUp = $false
    if ($running) {
        foreach ($line in @($running)) {
            if ($line -eq "vault") { $vaultUp = $true; break }
        }
    }
    if (-not $vaultUp) {
        Write-Warn "vault container NOT running -- starting via docker compose..."
        Push-Location $DOCKER_DIR
        docker compose up -d vault
        Pop-Location
    } else {
        Write-Ok "vault container running"
    }

    # 2. Wait for unsealed health (200 = unsealed+active, 429 = standby, 503 = sealed)
    $deadline = (Get-Date).AddSeconds(20)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        try {
            $h = Invoke-WebRequest -Uri "$VAULT_ADDR_LOCAL/v1/sys/health" `
                -UseBasicParsing -ErrorAction Stop -TimeoutSec 2
            if ($h.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) {
        Write-Err "Vault not healthy on :8200 within 20s -- check 'docker logs vault'"
        return $false
    }
    Write-Ok "Vault healthy (unsealed) on :8200"

    # 3. Probe critical secret paths
    $missing = @()
    foreach ($p in $VAULT_CRITICAL_PATHS) {
        if (-not (Test-VaultSecretExists -Path $p)) { $missing += $p }
    }

    if ($missing.Count -eq 0) {
        Write-Ok "Vault has all $($VAULT_CRITICAL_PATHS.Count) critical secrets -- skipping seed"
        return $true
    }

    Write-Warn "Vault missing $($missing.Count) secret(s):"
    foreach ($p in $missing) { Write-Host "    - secret/data/$p" -ForegroundColor DarkYellow }

    # 4. Run vault-init (idempotent KV v2 POST -- safe to re-run)
    Write-Warn "Seeding Vault via vault-init container..."
    Push-Location $DOCKER_DIR
    docker compose run --rm vault-init
    $rc = $LASTEXITCODE
    Pop-Location
    if ($rc -ne 0) {
        Write-Err "vault-init failed (exit $rc). Auth/API will throw VaultApiException."
        return $false
    }
    Write-Ok "Vault seeded successfully"
    return $true
}

# === FE env setup ===
function Set-FrontendEnv {
    $envLocal = Join-Path $WEB_DIR ".env.local"
    if (-not (Test-Path $envLocal)) {
        Write-Warn ".env.local not found - creating from .env.example"
        $lines = @(
            "VITE_APP_NAME=AqtCoreFW",
            "VITE_APP_SUBTITLE=He thong quan tri - Khung CNTT Chinh phu",
            "VITE_APP_LOGO_URL=",
            "VITE_API_BASE_URL=http://localhost:$API_PORT",
            "VITE_OIDC_AUTHORITY=http://localhost:$AUTH_PORT",
            "VITE_AUTH_CLIENT_ID=aqtcorefw-spa-dev"
        )
        $lines -join "`n" | Set-Content -Path $envLocal -Encoding UTF8 -NoNewline
        Write-Ok "Created $envLocal (API=$API_PORT, Auth=$AUTH_PORT)"
    } else {
        Write-Ok ".env.local exists"
    }
}

# === Spawn helpers (one per service) ===
function Get-AspnetEnv {
    switch ($EnvMode) {
        "dev"  { "Development" }
        "test" { "Testing" }
        "prod" { "Production" }
    }
}

# Spawns one service script. Windows: new PowerShell console window (original behavior).
# macOS/Linux: background pwsh process running a generated .logs/<name>.run.ps1 file (a file
# avoids argument-quoting differences on Unix), stdout/stderr -> .logs/<name>.log / .err.log.
function Start-ServiceProcess {
    param([string]$Name, [string]$Command, [string]$WorkDir)
    if ($IS_WIN) {
        return Start-Process powershell -ArgumentList "-NoExit", "-Command", $Command `
            -WorkingDirectory $WorkDir -PassThru
    }
    New-Item -ItemType Directory -Force -Path $LOG_DIR | Out-Null
    $scriptFile = Join-Path $LOG_DIR "$Name.run.ps1"
    # No console to wait on in background mode: drop the "Press Enter to close" prompt.
    ($Command -replace '(?m)^Read-Host\s*$', '') | Set-Content -Path $scriptFile -Encoding UTF8
    $logFile = Join-Path $LOG_DIR "$Name.log"
    $errFile = Join-Path $LOG_DIR "$Name.err.log"
    $proc = Start-Process pwsh -ArgumentList "-NoProfile", "-File", $scriptFile `
        -WorkingDirectory $WorkDir -RedirectStandardOutput $logFile -RedirectStandardError $errFile -PassThru
    Write-Host "    logs: tail -f $logFile" -ForegroundColor DarkGray
    return $proc
}

function Start-AuthService {
    $aspnetEnv = Get-AspnetEnv
    $authCmd = @"
`$Host.UI.RawUI.WindowTitle = 'AqtCoreFW - Auth Server (:$AUTH_PORT)'
Write-Host '=== Auth Server starting ===' -ForegroundColor Cyan
Write-Host "Environment: $aspnetEnv" -ForegroundColor DarkGray
Write-Host ''
cd "$AUTH_DIR"
`$env:ASPNETCORE_ENVIRONMENT = '$aspnetEnv'
try { dotnet run --no-build } catch { Write-Host `$_.Exception.Message -ForegroundColor Red }
Write-Host '' ; Write-Host 'Auth Server stopped. Press Enter to close.' -ForegroundColor Yellow
Read-Host
"@
    $proc = Start-ServiceProcess -Name "auth" -Command $authCmd -WorkDir $AUTH_DIR
    Write-Ok "Auth Server starting -> http://localhost:$AUTH_PORT (PID $($proc.Id))"
    return $proc.Id
}

function Start-ApiService {
    $aspnetEnv = Get-AspnetEnv
    $apiCmd = @"
`$Host.UI.RawUI.WindowTitle = 'AqtCoreFW - API (:$API_PORT)'
Write-Host '=== API Server starting ===' -ForegroundColor Cyan
Write-Host "Environment: $aspnetEnv" -ForegroundColor DarkGray
Write-Host ''
cd "$API_DIR"
`$env:ASPNETCORE_ENVIRONMENT = '$aspnetEnv'
`$env:ASPNETCORE_URLS = 'http://+:$API_PORT'
try { dotnet run --no-build } catch { Write-Host `$_.Exception.Message -ForegroundColor Red }
Write-Host '' ; Write-Host 'API stopped. Press Enter to close.' -ForegroundColor Yellow
Read-Host
"@
    $proc = Start-ServiceProcess -Name "api" -Command $apiCmd -WorkDir $API_DIR
    Write-Ok "API starting -> http://localhost:$API_PORT (PID $($proc.Id))"
    return $proc.Id
}

function Start-FrontendService {
    Set-FrontendEnv
    $feCmd = @"
`$Host.UI.RawUI.WindowTitle = 'AqtCoreFW - Frontend (:$FE_PORT)'
Write-Host '=== Frontend starting ===' -ForegroundColor Cyan
Write-Host ''
cd "$WEB_DIR"
npm run dev
Write-Host '' ; Write-Host 'Frontend stopped. Press Enter to close.' -ForegroundColor Yellow
Read-Host
"@
    $proc = Start-ServiceProcess -Name "fe" -Command $feCmd -WorkDir $WEB_DIR
    Write-Ok "Frontend starting -> http://localhost:$FE_PORT (PID $($proc.Id))"
    return $proc.Id
}

# Generic dispatcher: spawn one service by name, returns PID
function Start-ServiceByName {
    param([string]$name)
    # Free port first to avoid bind conflicts
    $port = switch ($name) { "auth" { $AUTH_PORT } "api" { $API_PORT } "fe" { $FE_PORT } }
    Stop-PortProcess -Port $port
    switch ($name) {
        "auth" { return Start-AuthService }
        "api"  { return Start-ApiService }
        "fe"   { return Start-FrontendService }
    }
}

function Test-Port {
    param([int]$Port)
    $tcp = $null
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $tcp.Connect("127.0.0.1", $Port)
        return $true
    } catch { return $false }
    finally { if ($tcp) { $tcp.Close() } }
}

# === Banner ===
Write-Host ""
Write-Host "  AqtCoreFW Local Dev Runner -- $Action $Service" -ForegroundColor Magenta
Write-Host "  Auth: http://localhost:$AUTH_PORT" -ForegroundColor DarkGray
Write-Host "  API:  http://localhost:$API_PORT" -ForegroundColor DarkGray
Write-Host "  FE:   http://localhost:$FE_PORT" -ForegroundColor DarkGray
Write-Host ""

# === Detect "full bootstrap" mode (default invocation): start + all targets ===
$isFullStart = ($Action -eq "start") -and ($targets.Count -eq 3)

# =====================================================================
# DISPATCH: stop / restart actions exit fast (no infra check, no loop)
# =====================================================================
if ($Action -eq "stop") {
    Write-Header "Stopping services: $($targets -join ', ')"
    foreach ($t in $targets) { Stop-ServiceByName -name $t }
    Write-Host ""
    Write-Ok "Done."
    exit 0
}

if ($Action -eq "restart") {
    Write-Header "Restarting services: $($targets -join ', ')"
    foreach ($t in $targets) { Stop-ServiceByName -name $t }
    Start-Sleep -Seconds 2

    # Vault check before restarting Auth/API (skip if only FE restarting)
    $hasBackend = $targets -contains "auth" -or $targets -contains "api"
    if ($hasBackend -and -not $SkipVault) {
        $vaultOk = Test-VaultStack
        if (-not $vaultOk) {
            Write-Err "Vault check failed -- aborting restart. Use -SkipVault to override."
            exit 1
        }
    }

    # Optional rebuild before restart (only if backend services involved)
    if ($hasBackend -and -not $SkipBuild) {
        Write-Header "Building backend (dotnet build)"
        if ($SLN) {
            $buildResult = dotnet build $SLN.FullName --configuration Debug --verbosity quiet 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Err "Build failed!"
                Write-Host ($buildResult | Out-String) -ForegroundColor Red
                Write-Warn "Continuing anyway... services may fail to start."
            } else { Write-Ok "Build succeeded" }
        } else { Write-Err "Solution file not found! Skipping build." }
    }

    Write-Header "Starting services"
    foreach ($t in $targets) {
        Start-ServiceByName -name $t | Out-Null
        if ($t -eq "auth") { Start-Sleep -Seconds 4 }  # Auth must be up before API
    }
    Write-Host ""
    Write-Ok "Restart complete. Check spawned terminal windows for logs."
    exit 0
}

# =====================================================================
# DISPATCH: start (partial = quick spawn, exit; full = bootstrap + loop)
# =====================================================================

if (-not $isFullStart) {
    # Partial start: skip SQL/Docker check, no monitor loop
    # But still run Vault check if Auth or API is in target set.
    $hasBackend = $targets -contains "auth" -or $targets -contains "api"
    if ($hasBackend -and -not $SkipVault) {
        $vaultOk = Test-VaultStack
        if (-not $vaultOk) {
            Write-Err "Vault check failed -- aborting start. Use -SkipVault to override."
            exit 1
        }
    }

    Write-Header "Starting services: $($targets -join ', ')"
    foreach ($t in $targets) {
        Start-ServiceByName -name $t | Out-Null
        if ($t -eq "auth" -and ($targets -contains "api")) { Start-Sleep -Seconds 4 }
    }
    Write-Host ""
    Write-Ok "Started. Check spawned terminal windows for logs."
    exit 0
}

# === Full start (default): infra check + build + spawn all + interactive loop ===

# 1. SQL Server Docker container (pre-check, non-blocking)
Test-SqlServerDocker

# 2. Docker infra (Redis + RabbitMQ only)
Test-DockerInfra

# 2.5. Vault (container up + critical secrets seeded). Must run BEFORE Auth/API
#      because OpenIddictDataSeeder hits VaultPepperResolver during user seed.
if (-not $SkipVault) {
    $vaultOk = Test-VaultStack
    if (-not $vaultOk) {
        Write-Err "Vault check failed -- Auth/API would crash. Use -SkipVault to override."
        exit 1
    }
} else {
    Write-Warn "Skipping Vault check (-SkipVault flag). Auth/API may fail if peppers missing."
}

# 3. Kill conflicting ports
Write-Header "Checking ports"
foreach ($p in @($AUTH_PORT, $API_PORT, $FE_PORT)) {
    Stop-PortProcess -Port $p
    Write-Ok "Port $p free"
}

# 4. Build backend
if (-not $SkipBuild) {
    Write-Header "Building backend (dotnet build)"
    if (-not $SLN) {
        Write-Err "Solution file not found! Skipping build."
    } else {
        Write-Host "  Solution: $($SLN.FullName)" -ForegroundColor DarkGray
        $buildResult = dotnet build $SLN.FullName --configuration Debug --verbosity quiet 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Err "Build failed!"
            Write-Host ($buildResult | Out-String) -ForegroundColor Red
            Write-Warn "Continuing anyway... services may fail to start."
        } else {
            Write-Ok "Build succeeded"
        }
    }
}

# 5. Spawn all services in titled windows
Write-Header "Starting services"
$spawnedPids = @{}
$spawnedPids["Auth Server"] = Start-AuthService
Start-Sleep -Seconds 4
$spawnedPids["API"] = Start-ApiService
$spawnedPids["Frontend"] = Start-FrontendService

# 6. Health check
Write-Header "Health check (waiting for services to start...)"
Start-Sleep -Seconds 6

$allOk = $true
if (Test-Port $AUTH_PORT) { Write-Ok "Auth Server READY on :$AUTH_PORT" }
else { Write-Warn "Auth Server not yet responding on :$AUTH_PORT (may still be starting)"; $allOk = $false }
if (Test-Port $API_PORT) { Write-Ok "API READY on :$API_PORT" }
else { Write-Warn "API not yet responding on :$API_PORT (may still be starting)"; $allOk = $false }
if (Test-Port $FE_PORT) { Write-Ok "Frontend READY on :$FE_PORT" }
else { Write-Warn "Frontend not yet responding on :$FE_PORT (may still be starting)"; $allOk = $false }

# 7. Summary
Write-Host ""
Write-Header "Services Status"
Write-Host "  Auth Server : http://localhost:$AUTH_PORT" -ForegroundColor Green
Write-Host "  API         : http://localhost:$API_PORT" -ForegroundColor Green
Write-Host "  Frontend    : http://localhost:$FE_PORT" -ForegroundColor Green
Write-Host ""

if (-not $allOk) {
    Write-Warn "Some services may still be starting. Check each terminal window for details."
    Write-Host ""
}

# 8. Interactive monitor loop
Write-Host "  Spawned terminal windows:" -ForegroundColor DarkGray
foreach ($svc in $spawnedPids.Keys) {
    Write-Host "    $svc -> PID $($spawnedPids[$svc])" -ForegroundColor DarkGray
}
Write-Host ""
Write-Host "  [S] Stop all services   [R] Re-check health   [Q] Quit (services keep running)" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Tip: stop/start/restart individual services from another terminal:" -ForegroundColor DarkGray
if ($IS_WIN) {
    Write-Host "    .\scripts\1_run-local-dev.ps1 restart api" -ForegroundColor DarkGray
    Write-Host "    .\scripts\1_run-local-dev.ps1 stop fe" -ForegroundColor DarkGray
} else {
    # Background mode: [Q] leaves services running with no visible window.
    Write-Host "    ./scripts/1_run-local-dev.sh restart api" -ForegroundColor DarkGray
    Write-Host "    ./scripts/1_run-local-dev.sh stop all      (services keep running after [Q])" -ForegroundColor DarkGray
    Write-Host "    logs: tail -f .logs/{auth,api,fe}.log" -ForegroundColor DarkGray
}
Write-Host ""

while ($true) {
    $key = Read-Host "Command (S/R/Q)"
    switch ($key.ToUpper()) {
        "S" {
            Write-Header "Stopping all services"
            foreach ($svc in $spawnedPids.Keys) {
                $svcPid = $spawnedPids[$svc]
                if (Get-Process -Id $svcPid -ErrorAction SilentlyContinue) {
                    Stop-Process -Id $svcPid -Force -ErrorAction SilentlyContinue
                    Write-Ok "Stopped $svc (PID $svcPid)"
                } else {
                    Write-Warn "$svc already stopped"
                }
            }
            # Also kill port processes (catch detached children)
            Stop-PortProcess -Port $AUTH_PORT
            Stop-PortProcess -Port $API_PORT
            Stop-PortProcess -Port $FE_PORT
            Write-Ok "All services stopped"
            Read-Host "Press Enter to exit"
            exit 0
        }
        "R" {
            Write-Header "Re-checking health"
            if (Test-Port $AUTH_PORT) { Write-Ok "Auth :$AUTH_PORT OK" } else { Write-Err "Auth :$AUTH_PORT DOWN" }
            if (Test-Port $API_PORT) { Write-Ok "API :$API_PORT OK" } else { Write-Err "API :$API_PORT DOWN" }
            if (Test-Port $FE_PORT) { Write-Ok "FE :$FE_PORT OK" } else { Write-Err "FE :$FE_PORT DOWN" }
        }
        "Q" {
            Write-Host "  Services keep running in their terminal windows." -ForegroundColor DarkGray
            exit 0
        }
        default { Write-Warn "Unknown command. Use S, R, or Q." }
    }
}
