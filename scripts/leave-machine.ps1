#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Run before walking away from a dev machine (only one machine works at a time). Lists
  everything that would NOT reach the other machine through git, and optionally pushes.

.DESCRIPTION
  For this repo and ~/claude-home (if present) it reports: uncommitted/untracked files,
  stashes, local branches ahead of upstream, branches with no upstream, dirty worktrees,
  and changes inside gitignored plans/ that only the re-included plans carry.
  With -Push it first syncs ~/.claude into claude-home (claude-home.ps1 push), then pushes
  the current branch of this repo to every remote listed in -Remotes.

  Exit code 0 = safe to switch machines; otherwise the number of blocking findings.

.EXAMPLE
  pwsh scripts/leave-machine.ps1            # report only
  pwsh scripts/leave-machine.ps1 -Push      # sync claude-home, push, then re-check
#>
param(
    [switch]$Push,
    [string[]]$Remotes = @("origin", "bitbucket")
)

$ErrorActionPreference = "Continue"
$ROOT = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$CLAUDE_HOME = Join-Path $HOME "claude-home"
$script:blocking = 0

function Ok    { param([string]$m) Write-Host "  [OK]    $m" -ForegroundColor Green }
function Block { param([string]$m) Write-Host "  [BLOCK] $m" -ForegroundColor Red; $script:blocking++ }
function Note  { param([string]$m) Write-Host "  [NOTE]  $m" -ForegroundColor Yellow }

function Test-Repo {
    param([string]$Path, [string]$Label)
    Write-Host "`n== $Label ($Path) ==" -ForegroundColor Cyan
    Push-Location $Path
    try {
        $status = @(& git status --porcelain)
        if ($status.Count -eq 0) { Ok "working tree clean" }
        else {
            Block "$($status.Count) uncommitted/untracked path(s):"
            $status | Select-Object -First 15 | ForEach-Object { Write-Host "          $_" -ForegroundColor DarkYellow }
        }
        $stashes = @(& git stash list)
        if ($stashes.Count -gt 0) { Block "$($stashes.Count) stash(es): stashes never leave this machine (pop + commit WIP)" }

        foreach ($line in (& git for-each-ref --format='%(refname:short)|%(upstream:short)|%(upstream:track)' refs/heads)) {
            $name, $up, $track = $line -split '\|', 3
            if (-not $up) { Note "branch '$name' has no upstream (push it if the other machine needs it)"; continue }
            if ($track -match 'ahead (\d+)') { Block "branch '$name' is $($Matches[1]) commit(s) ahead of $up" }
        }

        $wt = $null
        foreach ($l in (& git worktree list --porcelain)) {
            if ($l -like 'worktree *') { $wt = $l.Substring(9) }
            if ($wt -and $l -eq '' ) { $wt = $null }
            if ($wt -and ($l -like 'HEAD *') -and ((Resolve-Path $wt).Path -ne (Resolve-Path $Path).Path)) {
                $d = @(& git -C $wt status --porcelain 2>$null)
                if ($d.Count -gt 0) { Block "worktree $wt has $($d.Count) uncommitted change(s) (worktrees are local-only)" }
            }
        }
    } finally { Pop-Location }
}

# In-flight plan edits inside gitignored plans/ never travel; warn about recently touched ones.
function Test-IgnoredPlans {
    $plans = Join-Path $ROOT "plans"
    if (-not (Test-Path $plans)) { return }
    $cutoff = (Get-Date).AddDays(-2)
    $recent = @(Get-ChildItem $plans -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -gt $cutoff } |
        Where-Object { & git -C $ROOT check-ignore -q $_.FullName; $LASTEXITCODE -eq 0 })
    if ($recent.Count -gt 0) {
        $dirs = $recent | ForEach-Object { $_.FullName.Substring($plans.Length + 1).Split('\', '/')[0] } | Select-Object -Unique
        Note "gitignored plan(s) edited in the last 48h will NOT reach the other machine: $($dirs -join ', ')"
        Note "  re-include in .gitignore (see the macos-dual-machine-dev pair) if still in flight"
    }
}

if ($Push) {
    if (Test-Path (Join-Path $CLAUDE_HOME "scripts/claude-home.ps1")) {
        Write-Host "`n== Syncing ~/.claude -> claude-home ==" -ForegroundColor Cyan
        & (Get-Process -Id $PID).Path -NoProfile -File (Join-Path $CLAUDE_HOME "scripts/claude-home.ps1") push
        if ($LASTEXITCODE -ne 0) { Write-Host "  claude-home push failed (exit $LASTEXITCODE)" -ForegroundColor Red }
    }
    $branch = (& git -C $ROOT rev-parse --abbrev-ref HEAD)
    foreach ($r in $Remotes) {
        Write-Host "`n== git push $r $branch ==" -ForegroundColor Cyan
        & git -C $ROOT push -u $r $branch
    }
}

Test-Repo -Path $ROOT -Label "AqtCoreFW"
Test-IgnoredPlans
if (Test-Path (Join-Path $CLAUDE_HOME ".git")) { Test-Repo -Path $CLAUDE_HOME -Label "claude-home" }
else { Note "~/claude-home not set up: Claude rules/skills/memory changes stay on this machine" }

Write-Host ""
if ($script:blocking -eq 0) { Write-Host "SAFE TO SWITCH MACHINES" -ForegroundColor Green }
else { Write-Host "NOT SAFE: $script:blocking blocking finding(s)" -ForegroundColor Red }
exit $script:blocking
