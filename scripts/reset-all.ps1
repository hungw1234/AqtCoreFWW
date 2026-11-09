#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Reset DB dev sạch (nuke → migrate → seed) rồi verify hội tụ — 1 lệnh.

.DESCRIPTION
  Reproducible dev-data reset cho AqtCoreFW. Xoá DB AqtCoreFW, để Auth/API
  migrate + chạy seeder, poll log tới khi seeder báo "complete", rồi in bảng
  verify (Tenant / Users / OrgUnits) bằng tiếng Việt.

  2 mode nuke:
    - default        : DROP DATABASE AqtCoreFW (giữ container + volume — nhanh).
    - -Hard          : docker compose down -v (xoá volume) → up lại → warm-up.

  DEV-ONLY: chặn cứng nếu ASPNETCORE_ENVIRONMENT=Production. DB đích cố định
  = AqtCoreFW (dev). Không bao giờ trỏ vào DB khác.

.PARAMETER Hard
  Nuke bằng `docker compose down -v` (xoá luôn sqlserver volume + Vault in-memory).
  Mặc định (không cờ): chỉ DROP DATABASE, giữ container chạy.

.PARAMETER SkipVault
  Bỏ qua check + re-seed Vault. Chỉ dùng khi chắc Vault đã seed (KHÔNG dùng với -Hard
  vì down -v xoá Vault in-memory → Auth/API sẽ crash VaultApiException).

.PARAMETER SkipBuild
  Bỏ `dotnet build` (dùng khi binaries đã build mới). Auth/API chạy `--no-build`.

.PARAMETER Force
  Bỏ xác nhận tương tác (cho CI / non-interactive). Vẫn enforce env-guard prod.

.PARAMETER TimeoutSec
  Thời gian tối đa chờ seeder hội tụ (mặc định 900s — BTC seed 10k user mất ~vài phút).

.EXAMPLE
  pwsh scripts/reset-all.ps1                 # DROP DB + migrate + seed + verify
  pwsh scripts/reset-all.ps1 -Hard           # down -v + warm-up + seed
  pwsh scripts/reset-all.ps1 -Force          # không hỏi xác nhận

.NOTES
  Runbook chi tiết: docs/runbooks/dev-data-reset-runbook.md
  Demo-seeder (BNV/Workflow/Form/BP/Niche — Phase 2-7) mặc định Enabled=false ở
  Development; script này set env override để bật khi reset. Xem $DemoSeederEnvOverrides.
#>

param(
    [switch]$Hard,
    [switch]$SkipVault,
    [switch]$SkipBuild,
    [switch]$Force,
    [int]$TimeoutSec = 900
)

$ErrorActionPreference = "Continue"

# === Paths ===
$ROOT      = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$API_DIR   = Join-Path $ROOT "src\host\AqtCoreFW.Api"
$AUTH_DIR  = Join-Path $ROOT "src\host\AqtCoreFW.AuthServer"
$DOCKER_DIR = Join-Path $ROOT "infra\docker"
$LOG_DIR   = Join-Path $ROOT ".reset-logs"
$SLN = Get-ChildItem (Join-Path $ROOT "src") -Filter "*.slnx" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $SLN) { $SLN = Get-ChildItem (Join-Path $ROOT "src") -Filter "*.sln" -ErrorAction SilentlyContinue | Select-Object -First 1 }

# === DB / container constants (dev-only, mirror docker-compose.yml + appsettings.Development.json) ===
$DB_NAME    = "AqtCoreFW"
$SQL_CONT   = "sqlserver"
$SA_PASS    = "YourStrong!Passw0rd"            # dev-only, committed in docker-compose.yml
$SQLCMD     = "/opt/mssql-tools18/bin/sqlcmd"  # path inside mssql/server:2022 image
$AUTH_PORT  = 5002
$API_PORT   = 5001
$FE_PORT    = 5173
$SQL_PORT   = 1433

# === Vault (mirror 1_run-local-dev.ps1) ===
$VAULT_ADDR  = "http://localhost:8200"
$VAULT_TOKEN = "dev-only-token"
$VAULT_CRITICAL_PATHS = @(
    "aqt/encryption/v1",
    "aqt/audit/hmac-v1",
    "aqt/encryption/email-pepper-00000000000000000000000000000000",
    "aqt/encryption/email-pepper-00000000000000000000000000000001",
    "aqt/encryption/email-pepper-a1b2c3d400000000000000000000b7c1"
)

# === Demo-seeder env overrides (Phase 2-7 forward-convention) ===
# Demo seeder mới (BNV / Workflow / Form / BP / Niche) PHẢI default Enabled=false ở
# Development (boot thường nhanh + integration test không phình). reset-all bật chúng
# qua env override. Convention: section "<Module>:<Seeder>" → env "<Module>__<Seeder>__Enabled".
# Hiện trống vì các seeder demo CHƯA tồn tại; mỗi Phase 2-7 khi land seeder sẽ THÊM 1 dòng
# vào đây (vd "Workflow__DemoSeed__Enabled" = "true"). Xem docs/runbooks/dev-data-reset-runbook.md.
$DemoSeederEnvOverrides = @{
    "Identity__TenantSeed__Enabled"            = "true"   # Phase 2 (canonical Tenant rows: Bo Tai chinh + Bo Noi vu)
    "Identity__TenantRoleSeed__Enabled"        = "true"   # Phase 2 (business roles BTC_/BNV_; needs TenantSeed first)
    "Organization__OrgTreeSeed__Enabled"       = "true"   # Phase 2 (org trees: BTC + BNV; needs TenantSeed first)
    "Identity__TenantUserSeed__Enabled"        = "true"   # Phase 2 (users + user-roles BTC + BNV; needs OrgTree + roles)
    "Organization__TenantAssignmentSeed__Enabled" = "true"   # Phase 2 (assignments + histories; needs TenantUserSeed)
    "Workflow__DemoWorkflowSeed__Enabled"  = "true"   # Phase 4 (generic 6-state defs BTC + BNV, Draft; ByRole by code)
    "Forms__DemoFormTemplateSeed__Enabled" = "true"   # Phase 5 (1 published intake template/bo, diverse fields incl Select)
    "BusinessProcess__DemoBusinessProcessSeed__Enabled" = "true"   # 1 Draft BP/bo: workflow + intake form maps (needs workflow + form seeders enabled above)
    "Integration__DemoIntegrationSeed__Enabled" = "true"   # 1 Active partner + 1 Draft contract/bo (self-contained, no MessageLog)
    "Rules__DemoRulesSeed__Enabled"        = "true"   # 1 Draft RuleSet + 2 rules + 1 decision table/bo (self-contained)
    "Reporting__DemoReportingSeed__Enabled" = "true"   # 1 report def + 1 widget + 1 dashboard/bo + 1 global query-catalog (self-contained)
}

# === Colors ===
function Write-Header { param([string]$m) Write-Host "`n=== $m ===" -ForegroundColor Cyan }
function Write-Ok    { param([string]$m) Write-Host "  [OK] $m" -ForegroundColor Green }
function Write-Warn  { param([string]$m) Write-Host "  [!] $m" -ForegroundColor Yellow }
function Write-Err   { param([string]$m) Write-Host "  [X] $m" -ForegroundColor Red }
function Write-Step  { param([string]$m) Write-Host "  -> $m" -ForegroundColor DarkGray }

# === Helpers (mirror 1_run-local-dev.ps1) ===
function Test-Port {
    param([int]$Port)
    $tcp = $null
    try { $tcp = New-Object System.Net.Sockets.TcpClient; $tcp.Connect("127.0.0.1", $Port); return $true }
    catch { return $false }
    finally { if ($tcp) { $tcp.Close() } }
}

function Stop-PortProcess {
    param([int]$Port)
    $procs = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue |
             Select-Object -ExpandProperty OwningProcess -Unique
    foreach ($procId in $procs) {
        if ($procId -and $procId -ne 0) {
            $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
            if ($proc) {
                Write-Step "Killing $($proc.ProcessName) (PID $procId) trên port $Port"
                Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 500
            }
        }
    }
}

# Resolve tên container sqlserver THẬT. Compose default = "<project>-sqlserver-1" (vd
# "docker-sqlserver-1"), KHÔNG phải bare "sqlserver". `docker ps --filter name=` là substring
# match nên tìm được, nhưng `docker exec` cần tên/ID chính xác → phải resolve. Cache sau lần đầu.
$script:SqlContainerResolved = $null
function Resolve-SqlContainer {
    if ($script:SqlContainerResolved) { return $script:SqlContainerResolved }
    $n = docker ps --filter "name=$SQL_CONT" --format "{{.Names}}" 2>$null | Select-Object -First 1
    $script:SqlContainerResolved = if ($n) { $n.Trim() } else { $SQL_CONT }
    return $script:SqlContainerResolved
}

# Chạy sqlcmd trong container sqlserver. Trả stdout; $LASTEXITCODE = exit code.
function Invoke-Sqlcmd-InContainer {
    param([string]$Query, [string]$Database = "master")
    docker exec (Resolve-SqlContainer) $SQLCMD -C -U sa -P $SA_PASS -d $Database -h -1 -W -Q $Query 2>&1
}

# Chờ SQL Server THỰC SỰ nhận query (không chỉ port mở). Sau `down -v` container fresh: port forward
# mở TRƯỚC khi engine sẵn sàng → connection bị drop giữa chừng (Win32 10054 "forcibly closed") →
# MigrateAsync crash transient. Probe `SELECT 1` tới khi thành công. Port-open + Start-Sleep cố định
# KHÔNG đủ cho container vừa tạo. Trả $true nếu sẵn sàng trong timeout.
function Wait-SqlReady {
    param([int]$TimeoutSeconds = 120)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $out = Invoke-Sqlcmd-InContainer -Query "SELECT 1" -Database "master"
        if ($LASTEXITCODE -eq 0 -and ($out -join "`n") -match '(?m)^\s*1\s*$') { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Test-VaultSecretExists {
    param([string]$Path)
    try {
        $resp = Invoke-WebRequest -Uri "$VAULT_ADDR/v1/secret/data/$Path" `
            -Headers @{ "X-Vault-Token" = $VAULT_TOKEN } -UseBasicParsing -ErrorAction Stop -TimeoutSec 3
        return ($resp.StatusCode -eq 200)
    } catch { return $false }
}

function Ensure-VaultSeeded {
    Write-Header "Vault (dev mode — in-memory, re-seed nếu thiếu)"
    if (-not (docker ps --filter "name=vault" --format "{{.Names}}" 2>$null | Select-String -Quiet "vault")) {
        Write-Warn "vault container chưa chạy — khởi động..."
        Push-Location $DOCKER_DIR; docker compose up -d vault; Pop-Location
    }
    $deadline = (Get-Date).AddSeconds(20); $ready = $false
    while ((Get-Date) -lt $deadline) {
        try {
            $h = Invoke-WebRequest -Uri "$VAULT_ADDR/v1/sys/health" -UseBasicParsing -ErrorAction Stop -TimeoutSec 2
            if ($h.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { Write-Err "Vault không healthy trên :8200 sau 20s — 'docker logs vault'"; return $false }
    Write-Ok "Vault healthy (unsealed)"
    $missing = @(); foreach ($p in $VAULT_CRITICAL_PATHS) { if (-not (Test-VaultSecretExists -Path $p)) { $missing += $p } }
    if ($missing.Count -eq 0) { Write-Ok "Vault đủ $($VAULT_CRITICAL_PATHS.Count) secret — bỏ qua seed"; return $true }
    Write-Warn "Vault thiếu $($missing.Count) secret — seed qua vault-init..."
    Push-Location $DOCKER_DIR; docker compose run --rm vault-init; $rc = $LASTEXITCODE; Pop-Location
    if ($rc -ne 0) { Write-Err "vault-init thất bại (exit $rc) — Auth/API sẽ lỗi VaultApiException"; return $false }
    Write-Ok "Vault đã seed"
    return $true
}

# Spawn 1 host (Auth/API) chạy nền, redirect stdout+stderr ra log file. Trả Process object.
function Start-HostBackground {
    param([string]$Name, [string]$Dir, [int]$Port, [string]$LogFile)
    Stop-PortProcess -Port $Port
    # LOAD-BEARING cho 2-restart convergence: xoá log cũ để boot-2 KHÔNG thấy lại abort-marker
    # cũ (false-trigger restart). Đừng đổi sang append-mode — sẽ phá poll hội tụ.
    if (Test-Path $LogFile) { Remove-Item $LogFile -Force -ErrorAction SilentlyContinue }
    $errFile = "$LogFile.err"
    if (Test-Path $errFile) { Remove-Item $errFile -Force -ErrorAction SilentlyContinue }
    $buildArg = if ($SkipBuild) { "--no-build" } else { "" }
    $argList = @("run") + @($buildArg | Where-Object { $_ }) + @("--project", $Dir, "--urls", "http://localhost:$Port")
    # Env (ASPNETCORE_ENVIRONMENT + demo overrides) đã set ở scope script trước khi gọi → child kế thừa.
    $proc = Start-Process -FilePath "dotnet" -ArgumentList $argList -WorkingDirectory $Dir `
        -RedirectStandardOutput $LogFile -RedirectStandardError $errFile -PassThru -WindowStyle Hidden
    Write-Ok "$Name starting (PID $($proc.Id)) → log: $LogFile"
    return $proc
}

# Poll log file(s) tới khi thấy 1 trong các marker (regex literal). Trả marker khớp, hoặc $null nếu timeout.
function Wait-LogMarker {
    param([string[]]$LogFiles, [string[]]$Markers, [int]$TimeoutSeconds, [string[]]$AbortMarkers = @())
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($lf in $LogFiles) {
            if (-not (Test-Path $lf)) { continue }
            $content = Get-Content $lf -Raw -ErrorAction SilentlyContinue
            if (-not $content) { continue }
            foreach ($ab in $AbortMarkers) { if ($content -match [regex]::Escape($ab)) { return "ABORT:$ab" } }
            foreach ($mk in $Markers) { if ($content -match [regex]::Escape($mk)) { return $mk } }
        }
        Start-Sleep -Seconds 2
    }
    return $null
}

# Poll HTTP endpoint tới khi 200. Trả "OK" / "EXITED:<code>" (host chết) / $null (timeout).
# Dùng cho readiness thay log-marker: Auth (Serilog) KHÔNG in "Now listening on" ra stdout —
# chỉ health 200 là tín hiệu sẵn sàng đáng tin. HasExited → fail-fast, không chờ hết timeout.
function Wait-HttpOk {
    param([string]$Url, [int]$TimeoutSeconds, [System.Diagnostics.Process]$Proc)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($Proc -and $Proc.HasExited) { return "EXITED:$($Proc.ExitCode)" }
        try {
            $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
            if ($r.StatusCode -eq 200) { return "OK" }
        } catch { }
        Start-Sleep -Seconds 2
    }
    return $null
}

# =====================================================================
# 0. ENV GUARD (dev-only — chặn cứng prod)
# =====================================================================
Write-Host ""
Write-Host "  AqtCoreFW — RESET DEV DATA (nuke → migrate → seed)" -ForegroundColor Magenta
Write-Host "  Mode  : $(if ($Hard) { 'HARD (docker down -v)' } else { 'default (DROP DATABASE)' })" -ForegroundColor DarkGray
Write-Host "  DB    : $DB_NAME @ localhost,$SQL_PORT" -ForegroundColor DarkGray
Write-Host ""

$aspnetEnv = $env:ASPNETCORE_ENVIRONMENT
# Chặn mọi env ≠ Development (Production/Staging/Testing/typo). Unset = OK (coerce → Development bên dưới).
if ($aspnetEnv -and $aspnetEnv -ine "Development") {
    Write-Err "ASPNETCORE_ENVIRONMENT=$aspnetEnv (≠ Development) → TỪ CHỐI reset. Đây là công cụ dev-only."
    exit 1
}
if ($DB_NAME -ne "AqtCoreFW") {
    Write-Err "DB đích '$DB_NAME' ≠ 'AqtCoreFW' → từ chối (an toàn)."
    exit 1
}
if (-not $Force) {
    Write-Warn "Sắp XOÁ TOÀN BỘ dữ liệu DB '$DB_NAME'$(if ($Hard) { ' + sqlserver volume (down -v)' } else { '' })."
    $ans = Read-Host "  Gõ tên DB '$DB_NAME' để xác nhận (hoặc Enter để huỷ)"
    if ($ans -ne $DB_NAME) { Write-Warn "Huỷ — không khớp tên DB."; exit 0 }
}
New-Item -ItemType Directory -Force -Path $LOG_DIR | Out-Null

# Set env cho các host con kế thừa
$env:ASPNETCORE_ENVIRONMENT = "Development"
foreach ($k in $DemoSeederEnvOverrides.Keys) {
    Set-Item -Path "env:$k" -Value $DemoSeederEnvOverrides[$k]
    Write-Step "Demo-seeder env: $k=$($DemoSeederEnvOverrides[$k])"
}

# =====================================================================
# 1. STOP services (giải phóng connection DB trước khi DROP)
# =====================================================================
Write-Header "Dừng Auth/API/FE (giải phóng kết nối DB)"
foreach ($p in @($AUTH_PORT, $API_PORT, $FE_PORT)) { Stop-PortProcess -Port $p }
Write-Ok "Ports đã giải phóng"

# =====================================================================
# 2. NUKE
# =====================================================================
if ($Hard) {
    Write-Header "NUKE (hard): docker compose down -v"
    Push-Location $DOCKER_DIR
    docker compose down -v
    Write-Step "Khởi động lại sqlserver + redis + rabbitmq + vault..."
    docker compose up -d sqlserver redis rabbitmq vault
    Pop-Location
    Write-Step "Chờ sqlserver healthcheck (:$SQL_PORT)..."
    $deadline = (Get-Date).AddSeconds(60); $up = $false
    while ((Get-Date) -lt $deadline) { if (Test-Port $SQL_PORT) { $up = $true; break }; Start-Sleep -Seconds 2 }
    if (-not $up) { Write-Err "sqlserver không sẵn sàng sau 60s — 'docker logs sqlserver'"; exit 1 }
    Write-Step "Port mở — chờ SQL engine thực sự nhận query (SELECT 1)..."
    if (-not (Wait-SqlReady -TimeoutSeconds 150)) {
        Write-Err "sqlserver mở port nhưng không nhận query sau 150s (container fresh chưa init xong) — 'docker logs sqlserver'"; exit 1
    }
    Write-Ok "sqlserver sẵn sàng (volume mới, SELECT 1 OK)"
} else {
    Write-Header "NUKE (default): DROP DATABASE $DB_NAME"
    if (-not (docker ps --filter "name=$SQL_CONT" --format "{{.Names}}" 2>$null | Select-String -Quiet $SQL_CONT)) {
        Write-Warn "sqlserver container chưa chạy — khởi động..."
        Push-Location $DOCKER_DIR; docker compose up -d sqlserver; Pop-Location
        $deadline = (Get-Date).AddSeconds(60); $up = $false
        while ((Get-Date) -lt $deadline) { if (Test-Port $SQL_PORT) { $up = $true; break }; Start-Sleep -Seconds 2 }
        if (-not $up) { Write-Err "sqlserver không sẵn sàng sau 60s"; exit 1 }
        if (-not (Wait-SqlReady -TimeoutSeconds 150)) { Write-Err "sqlserver mở port nhưng không nhận query sau 150s"; exit 1 }
    }
    # SET SINGLE_USER ROLLBACK IMMEDIATE để cắt mọi connection còn mở trước khi DROP.
    $dropQuery = "IF DB_ID('$DB_NAME') IS NOT NULL BEGIN ALTER DATABASE [$DB_NAME] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DB_NAME]; END"
    $out = Invoke-Sqlcmd-InContainer -Query $dropQuery -Database "master"
    if ($LASTEXITCODE -ne 0) {
        Write-Err "DROP DATABASE thất bại:"; Write-Host ($out | Out-String) -ForegroundColor Red; exit 1
    }
    Write-Ok "DROP DATABASE $DB_NAME xong (EF MigrateAsync sẽ tạo lại khi API/Auth boot)"
}

# =====================================================================
# 3. VAULT re-seed (bắt buộc sau -Hard; nhẹ với default)
# =====================================================================
if (-not $SkipVault) {
    if (-not (Ensure-VaultSeeded)) { Write-Err "Vault chưa sẵn sàng — Auth/API sẽ crash. Dừng."; exit 1 }
} else {
    Write-Warn "Bỏ qua Vault (-SkipVault). Auth/API có thể lỗi nếu pepper/HMAC thiếu."
}

# =====================================================================
# 4. BUILD (tránh race CS2012 khi 2 host build song song Infrastructure.dll)
# =====================================================================
if (-not $SkipBuild) {
    Write-Header "Build backend (1 lần, trước khi spawn host)"
    if ($SLN) {
        $bo = dotnet build $SLN.FullName --configuration Debug --verbosity quiet 2>&1
        if ($LASTEXITCODE -ne 0) { Write-Err "Build thất bại:"; Write-Host ($bo | Out-String) -ForegroundColor Red; exit 1 }
        Write-Ok "Build OK"
        $SkipBuild = $true   # host con dùng --no-build (binaries đã mới)
    } else { Write-Warn "Không tìm thấy solution — host con sẽ tự build." }
}

# =====================================================================
# 4b. MIGRATE-ONLY (tạo + migrate ĐẦY ĐỦ DB trước khi start host runtime)
# =====================================================================
# Cold-DB bootstrap (BẮT BUỘC sau nuke). API host `--migrate-only` migrate cả 20 ctx rồi EXIT
# (Api/Program.cs return trước Hangfire runtime-wiring + PII upgrader) → tạo DB đầy đủ mà KHÔNG
# chạy path crash. Nếu để host runtime tự migrate trên DB RỖNG: Hangfire UseSqlServerStorage
# autodetect lúc host-build connect tới DB chưa tồn tại → SqlClient poison connection-pool (~5s
# blocking) → PlaintextToEncryptedUpgrader (AuthServer/Program.cs) mở raw connection trong cửa sổ
# đó → cached SqlException 4060 "Cannot open database" → crash UNHANDLED. Đây là bootstrap chính
# thức của app (tương đương Helm db-migrate Job chạy trên API image trước khi host lên).
Write-Header "Migrate schema (API --migrate-only — tạo DB đầy đủ trước host runtime)"
$migrateLog = Join-Path $LOG_DIR "migrate.log"
if (Test-Path $migrateLog) { Remove-Item $migrateLog -Force -ErrorAction SilentlyContinue }
$mBuildArg = if ($SkipBuild) { "--no-build" } else { "" }
$mArgs = @("run") + @($mBuildArg | Where-Object { $_ }) + @("--project", $API_DIR, "--", "--migrate-only")
Write-Step "dotnet $($mArgs -join ' ') — chờ migrate 20 ctx + exit..."
# Blocking call. Dùng call-operator + $LASTEXITCODE (ĐÁNG TIN cho native exe). KHÔNG dùng
# Start-Process -PassThru .ExitCode: khi redirect ra file nó trả $null → $null -ne 0 = true →
# false-failure dù migrate xong. migrate-only nhanh (~10-20s) + tự exit (Api/Program.cs return
# sau khi log "schema migrations complete") → không cần timeout, nhất quán dotnet build blocking trên.
Push-Location $API_DIR
& dotnet @mArgs *> $migrateLog
$rc = $LASTEXITCODE
Pop-Location
if ($rc -ne 0) {
    Write-Err "migrate-only thất bại (exit $rc). Xem $migrateLog"
    exit 1
}
Write-Ok "Schema migrate xong — DB đầy đủ tồn tại, host runtime không hit cold-DB race"

# =====================================================================
# 5. START Auth → migrate Identity+Messaging + OpenIddict seed
# =====================================================================
Write-Header "Khởi động Auth Server (migrate + OpenIddict seed)"
$authLog = Join-Path $LOG_DIR "auth.log"
$authProc = Start-HostBackground -Name "Auth" -Dir $AUTH_DIR -Port $AUTH_PORT -LogFile $authLog
Write-Step "Chờ Auth healthy (:$AUTH_PORT/health)..."
# Health 200 = readiness (Serilog không in "Now listening on" → log-marker timeout giả). Detect
# host-exit để fail-fast nếu Auth crash (vd cold-DB 4060 nếu migrate-first bị bỏ qua).
$authReady = Wait-HttpOk -Url "http://localhost:$AUTH_PORT/health" -TimeoutSeconds 180 -Proc $authProc
if ($authReady -ne "OK") {
    Write-Err "Auth không healthy sau 180s ($authReady). Xem $authLog / $authLog.err"
    Stop-Process -Id $authProc.Id -Force -ErrorAction SilentlyContinue; exit 1
}
Write-Ok "Auth healthy (:$AUTH_PORT/health 200)"
Start-Sleep -Seconds 3   # cho Auth ổn định trước khi API validate token

# =====================================================================
# 6. START API → migrate 15 ctx + IHostedService seeders (BTC + MasterData)
# =====================================================================
Write-Header "Khởi động API (migrate 15 ctx + seeders)"
$apiLog = Join-Path $LOG_DIR "api.log"
$apiProc = Start-HostBackground -Name "API" -Dir $API_DIR -Port $API_PORT -LogFile $apiLog

# Marker thật (verify từ source — KHÔNG paraphrase):
$MK_ORGUNIT    = "[OrgTreeSeeder] All tenants complete"                  # OrgTreeSeeder.cs StartAsync end-of-loop
$MK_IDENTITY   = "[TenantUserSeeder] All tenants complete"               # TenantUserSeeder.cs end-of-loop
$MK_ASSIGNMENT = "[TenantAssignmentSeeder] All tenants complete"         # TenantAssignmentSeeder.cs (only when all tenants had users)
$MK_MASTERDATA = "MasterData seeding and cache loading complete"         # MasterDataSeeder.cs:46
$MK_WORKFLOW   = "[DemoWorkflowSeeder] All tenants complete"             # DemoWorkflowSeeder.cs StartAsync end-of-loop
$MK_FORM       = "[DemoFormTemplateSeeder] All tenants complete"         # DemoFormTemplateSeeder.cs StartAsync end-of-loop
$MK_BP         = "[DemoBusinessProcessSeeder] All tenants complete"      # DemoBusinessProcessSeeder.cs StartAsync end-of-loop
$MK_INTEGRATION = "[DemoIntegrationSeeder] All tenants complete"         # DemoIntegrationSeeder.cs StartAsync end-of-loop
$MK_RULES      = "[DemoRulesSeeder] All tenants complete"                # DemoRulesSeeder.cs StartAsync end-of-loop
$MK_REPORTING  = "[DemoReportingSeeder] All tenants complete"            # DemoReportingSeeder.cs StartAsync end-of-loop
$MK_ABORT_NOUSERS = "[TenantAssignmentSeeder] incomplete"               # retry trigger: a tenant's users not seeded yet (cross-module boot order)

$apiLogs = @($apiLog, "$apiLog.err")

Write-Header "Poll seeder hội tụ (timeout ${TimeoutSec}s)"
$restartUsed = $false
$pending = [System.Collections.Generic.List[string]]@($MK_IDENTITY, $MK_ASSIGNMENT, $MK_MASTERDATA, $MK_ORGUNIT, $MK_WORKFLOW, $MK_FORM, $MK_BP, $MK_INTEGRATION, $MK_RULES, $MK_REPORTING)
$overallDeadline = (Get-Date).AddSeconds($TimeoutSec)

while ($pending.Count -gt 0 -and (Get-Date) -lt $overallDeadline) {
    # Fail-fast nếu API chết trước hội tụ (sau restart $apiProc đã trỏ process mới → không false-trigger).
    if ($apiProc.HasExited) {
        Write-Err "API thoát (exit $($apiProc.ExitCode)) trước khi seeder hội tụ. Xem $apiLog.err"
        break
    }
    $remain = [int]([Math]::Max(5, ($overallDeadline - (Get-Date)).TotalSeconds))
    $hit = Wait-LogMarker -LogFiles $apiLogs -Markers $pending -AbortMarkers @($MK_ABORT_NOUSERS) -TimeoutSeconds $remain

    if ($null -eq $hit) { break }   # timeout tổng

    if ($hit -like "ABORT:*") {
        # 2-restart convergence: TenantAssignmentSeeder (Organization module) boots before the identity
        # user seeder has finished → no users → "incomplete". Restart API once so boot-2 sees the users.
        if ($restartUsed) {
            # Đã restart 1 lần (cap). Nhánh này gần như không tới sau nuke sạch (boot-1 seed từ rỗng
            # → không wipe; boot-2 resume-skip → Identity xong nhanh → Assignment thấy user cùng boot).
            # Nếu boot-2 VẪN race (hiếm): vòng này spin mỗi 3s tới khi timeout tổng — bounded, không loop.
            Write-Warn "Vẫn '$MK_ABORT_NOUSERS' sau 1 restart — chờ assignment complete hoặc timeout."
            Start-Sleep -Seconds 3
            continue
        }
        Write-Warn "Phát hiện race '$MK_ABORT_NOUSERS' → restart API 1 lần (chờ Identity seed xong)."
        # Chờ Identity complete trước khi restart để lần 2 assignment thấy user. Clamp theo ngân sách còn lại.
        $idRemain = [int]([Math]::Max(5, ($overallDeadline - (Get-Date)).TotalSeconds))
        $idDone = Wait-LogMarker -LogFiles $apiLogs -Markers @($MK_IDENTITY) -TimeoutSeconds $idRemain
        if (-not $idDone) { Write-Err "Identity seed không complete — xem $apiLog"; break }
        $pending.Remove($MK_IDENTITY) | Out-Null
        Stop-Process -Id $apiProc.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 3
        $apiProc = Start-HostBackground -Name "API (restart)" -Dir $API_DIR -Port $API_PORT -LogFile $apiLog
        $restartUsed = $true
        continue
    }

    Write-Ok "Seeder: $hit"
    $pending.Remove($hit) | Out-Null
}

if ($pending.Count -gt 0) {
    Write-Err "Seeder CHƯA hội tụ trong ${TimeoutSec}s. Còn chờ: $($pending -join ' | ')"
    Write-Warn "Xem log: $apiLog"
} else {
    Write-Ok "Tất cả seeder đã 'complete' — hội tụ."
}

# =====================================================================
# 7. VERIFY (đếm rows — in bảng tiếng Việt)
# =====================================================================
Write-Header "Verify dữ liệu"
function Get-Count {
    param([string]$Sql)
    $r = Invoke-Sqlcmd-InContainer -Query $Sql -Database $DB_NAME
    if ($LASTEXITCODE -ne 0) { return "ERR" }
    $line = ($r | Where-Object { $_ -match '^\s*\d+\s*$' } | Select-Object -First 1)
    if ($line) { return $line.Trim() } else { return "?" }
}
# Bảng AspNetUsers (Identity) + OrgUnits + Tenants. Tên bảng theo schema thực tế.
$tenants  = Get-Count "SELECT COUNT(*) FROM [identity].[Tenants]"
$users    = Get-Count "SELECT COUNT(*) FROM [identity].[AspNetUsers]"
$orgunits = Get-Count "SELECT COUNT(*) FROM [organization].[OrgUnits]"

Write-Host ""
Write-Host "  +-------------------+-----------+" -ForegroundColor DarkGray
Write-Host ("  | {0,-17} | {1,9} |" -f "Thực thể", "Số lượng") -ForegroundColor White
Write-Host "  +-------------------+-----------+" -ForegroundColor DarkGray
Write-Host ("  | {0,-17} | {1,9} |" -f "Tenant", $tenants) -ForegroundColor White
Write-Host ("  | {0,-17} | {1,9} |" -f "Users", $users) -ForegroundColor White
Write-Host ("  | {0,-17} | {1,9} |" -f "OrgUnits", $orgunits) -ForegroundColor White
Write-Host "  +-------------------+-----------+" -ForegroundColor DarkGray
Write-Host ""
Write-Step "(Tên bảng verify có thể cần chỉnh nếu schema khác — xem runbook §Verify.)"

# =====================================================================
# 8. KẾT
# =====================================================================
Write-Header "Hoàn tất"
Write-Host "  Auth : http://localhost:$AUTH_PORT (PID $($authProc.Id))" -ForegroundColor Green
Write-Host "  API  : http://localhost:$API_PORT (PID $($apiProc.Id))" -ForegroundColor Green
Write-Host "  Log  : $LOG_DIR\auth.log | api.log" -ForegroundColor DarkGray
Write-Host ""
Write-Step "Auth/API vẫn chạy nền. Dừng: scripts\1_run-local-dev.ps1 stop all"
if ($pending.Count -gt 0) { exit 2 } else { exit 0 }
