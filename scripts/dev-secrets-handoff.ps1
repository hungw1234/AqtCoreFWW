#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Move the machine-local dev secrets that git deliberately does not carry (API user-secrets
  for digital signing + gitignored signing material under other/) between dev machines as
  ONE passphrase-encrypted file.

.DESCRIPTION
  export: reads the API/AuthServer user-secrets and the gitignored files under other/
          (signing-dev-roots/*.pem, the dev .p12 + its passphrase file, Viettel CA config;
          the Windows-only USB token installers under other/vgca_usb_token/tool are skipped),
          rewrites absolute paths inside secrets that point into this repo to a {REPO}
          token, zips everything and encrypts it (PBKDF2-SHA256 200k -> AES-256-CBC +
          HMAC-SHA256, encrypt-then-MAC).
  import: verifies + decrypts, restores other/ files into this repo, and writes the
          user-secrets to this OS's location with {REPO} replaced by this checkout's path.

  Secret values are never printed. Move the bundle by AirDrop/USB, never via git, chat or
  email, and delete it on both machines after import.

.EXAMPLE
  pwsh scripts/dev-secrets-handoff.ps1 export                 # -> ~/aqt-dev-secrets.bundle
  pwsh scripts/dev-secrets-handoff.ps1 import -Bundle ~/Downloads/aqt-dev-secrets.bundle
#>
param(
    [Parameter(Mandatory, Position = 0)][ValidateSet("export", "import")][string]$Action,
    [string]$Bundle = (Join-Path $HOME "aqt-dev-secrets.bundle")
)

$ErrorActionPreference = "Stop"
$IS_WIN = ($PSVersionTable.PSEdition -eq "Desktop") -or $IsWindows
$ROOT = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$SECRET_IDS = @("aqtcorefw-api-dev", "aqtcorefw-authserver-dev")
$REPO_TOKEN = "{REPO}"
$MAGIC = [Text.Encoding]::ASCII.GetBytes("AQTSEC1")
$ITERATIONS = 200000

function Get-UserSecretsPath {
    param([string]$Id)
    if ($IS_WIN) { return [IO.Path]::Combine($env:APPDATA, "Microsoft", "UserSecrets", $Id, "secrets.json") }
    return [IO.Path]::Combine($HOME, ".microsoft", "usersecrets", $Id, "secrets.json")
}

function Read-Passphrase {
    param([switch]$Confirm)
    # Non-interactive use (automation/tests): passphrase from env var, never from an argument
    # (arguments end up in shell history and process listings).
    if ($env:AQT_SECRETS_PASSPHRASE) {
        if ($env:AQT_SECRETS_PASSPHRASE.Length -lt 12) { throw "Passphrase too short." }
        return $env:AQT_SECRETS_PASSPHRASE
    }
    $a = Read-Host "Bundle passphrase (min 12 chars)" -AsSecureString
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($a))
    if ($plain.Length -lt 12) { throw "Passphrase too short." }
    if ($Confirm) {
        $b = Read-Host "Repeat passphrase" -AsSecureString
        $plain2 = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($b))
        if ($plain -ne $plain2) { throw "Passphrases do not match." }
    }
    return $plain
}

# Derives 64 bytes: first 32 = AES key, last 32 = HMAC key.
function Get-Keys {
    param([string]$Passphrase, [byte[]]$Salt)
    $kdf = New-Object Security.Cryptography.Rfc2898DeriveBytes($Passphrase, $Salt, $ITERATIONS, [Security.Cryptography.HashAlgorithmName]::SHA256)
    $bytes = $kdf.GetBytes(64)
    $kdf.Dispose()
    return , $bytes
}

function Protect-Bytes {
    param([byte[]]$Plain, [string]$Passphrase)
    $salt = New-Object byte[] 16; $iv = New-Object byte[] 16
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create(); $rng.GetBytes($salt); $rng.GetBytes($iv)
    $keys = Get-Keys $Passphrase $salt
    $aes = [Security.Cryptography.Aes]::Create(); $aes.Key = [byte[]]$keys[0..31]; $aes.IV = $iv
    $cipher = $aes.CreateEncryptor().TransformFinalBlock($Plain, 0, $Plain.Length)
    $body = [byte[]]($MAGIC + $salt + $iv + $cipher)
    $hmac = New-Object Security.Cryptography.HMACSHA256(, [byte[]]$keys[32..63])
    return [byte[]]($body + $hmac.ComputeHash($body))
}

function Unprotect-Bytes {
    param([byte[]]$Data, [string]$Passphrase)
    $m = $MAGIC.Length
    if ($Data.Length -lt ($m + 32 + 32) -or [Text.Encoding]::ASCII.GetString($Data, 0, $m) -ne "AQTSEC1") { throw "Not an AQT secrets bundle." }
    $body = [byte[]]$Data[0..($Data.Length - 33)]
    $tag = [byte[]]$Data[($Data.Length - 32)..($Data.Length - 1)]
    $salt = [byte[]]$Data[$m..($m + 15)]
    $iv = [byte[]]$Data[($m + 16)..($m + 31)]
    $keys = Get-Keys $Passphrase $salt
    $hmac = New-Object Security.Cryptography.HMACSHA256(, [byte[]]$keys[32..63])
    $expected = $hmac.ComputeHash($body)
    $diff = 0; for ($i = 0; $i -lt 32; $i++) { $diff = $diff -bor ($expected[$i] -bxor $tag[$i]) }
    if ($diff -ne 0) { throw "Wrong passphrase or corrupted bundle." }
    $aes = [Security.Cryptography.Aes]::Create(); $aes.Key = [byte[]]$keys[0..31]; $aes.IV = $iv
    $cipher = [byte[]]$body[($m + 32)..($body.Length - 1)]
    return , $aes.CreateDecryptor().TransformFinalBlock($cipher, 0, $cipher.Length)
}

# Everything plaintext (staged files AND the intermediate zip) lives under $work, which the
# finally block removes even when a step throws.
$work = Join-Path ([IO.Path]::GetTempPath()) ("aqt-secrets-" + [guid]::NewGuid().ToString("N"))
$staging = Join-Path $work "content"
$zip = Join-Path $work "bundle.zip"
New-Item -ItemType Directory -Path $staging | Out-Null

# Owner-only permissions for plaintext secrets written on macOS/Linux.
function Protect-LocalFile {
    param([string]$Path)
    if (-not $IS_WIN) { & chmod 600 $Path }
}

# True when $Value is $ROOT itself or a path inside it (not a sibling like C:\AqtCoreFW2).
function Test-UnderRoot {
    param([string]$Value)
    if (-not $Value.StartsWith($ROOT, [StringComparison]::OrdinalIgnoreCase)) { return $false }
    return ($Value.Length -eq $ROOT.Length) -or ('\', '/' -contains $Value[$ROOT.Length])
}
try {
    if ($Action -eq "export") {
        $count = 0
        foreach ($id in $SECRET_IDS) {
            $src = Get-UserSecretsPath $id
            if (-not (Test-Path $src)) { continue }
            $json = Get-Content $src -Raw | ConvertFrom-Json
            foreach ($p in $json.PSObject.Properties) {
                if ($p.Value -is [string] -and (Test-UnderRoot $p.Value)) {
                    $rel = $p.Value.Substring($ROOT.Length).TrimStart('\', '/') -replace '\\', '/'
                    $p.Value = "$REPO_TOKEN/$rel"
                }
            }
            New-Item -ItemType Directory -Force -Path (Join-Path $staging "usersecrets") | Out-Null
            $json | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $staging "usersecrets/$id.json") -Encoding UTF8
            $count += @($json.PSObject.Properties).Count
        }
        Push-Location $ROOT
        $files = @(& git ls-files --others --ignored --exclude-standard other | Where-Object { $_ -notlike 'other/vgca_usb_token/tool/*' })
        Pop-Location
        foreach ($f in $files) {
            $dst = Join-Path (Join-Path $staging "repo") $f
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
            Copy-Item -LiteralPath (Join-Path $ROOT $f) -Destination $dst
        }
        Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zip -Force
        $pass = Read-Passphrase -Confirm
        [IO.File]::WriteAllBytes($Bundle, (Protect-Bytes ([IO.File]::ReadAllBytes($zip)) $pass))
        Protect-LocalFile $Bundle
        Remove-Item $zip -Force
        Write-Host "[OK] Exported $count user-secret key(s) + $($files.Count) file(s) -> $Bundle" -ForegroundColor Green
        Write-Host "     Transfer by AirDrop/USB only, then delete the bundle on both machines." -ForegroundColor Yellow
    } else {
        if (-not (Test-Path $Bundle)) { throw "Bundle not found: $Bundle" }
        $pass = Read-Passphrase
        [IO.File]::WriteAllBytes($zip, (Unprotect-Bytes ([IO.File]::ReadAllBytes($Bundle)) $pass))
        Expand-Archive -Path $zip -DestinationPath $staging -Force
        Remove-Item $zip -Force
        $repoFiles = Join-Path $staging "repo"
        $restored = 0
        if (Test-Path $repoFiles) {
            foreach ($f in Get-ChildItem $repoFiles -Recurse -File) {
                $rel = $f.FullName.Substring($repoFiles.Length).TrimStart('\', '/')
                $dst = Join-Path $ROOT $rel
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
                Copy-Item -LiteralPath $f.FullName -Destination $dst -Force
                Protect-LocalFile $dst
                $restored++
            }
        }
        $keys = 0
        foreach ($f in Get-ChildItem (Join-Path $staging "usersecrets") -Filter *.json -ErrorAction SilentlyContinue) {
            $json = Get-Content $f.FullName -Raw | ConvertFrom-Json
            foreach ($p in $json.PSObject.Properties) {
                if ($p.Value -is [string] -and $p.Value.StartsWith("$REPO_TOKEN/")) {
                    $parts = $p.Value.Substring($REPO_TOKEN.Length + 1) -split '/'
                    # [string[]] cast is required: an object[] argument binds to Combine(string)
                    # in Windows PowerShell and gets joined with spaces.
                    $p.Value = [IO.Path]::Combine([string[]](@($ROOT) + $parts))
                }
            }
            $dst = Get-UserSecretsPath $f.BaseName
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
            if (Test-Path $dst) { Copy-Item $dst "$dst.bak" -Force }
            $json | ConvertTo-Json -Depth 10 | Set-Content -Path $dst -Encoding UTF8
            Protect-LocalFile $dst
            $keys += @($json.PSObject.Properties).Count
        }
        Write-Host "[OK] Imported $keys user-secret key(s) + $restored file(s). Delete the bundle now: $Bundle" -ForegroundColor Green
    }
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
