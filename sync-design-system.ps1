<#
  sync-design-system.ps1 — refresh Wend's bundled copy of the shared design-system.

  Wend vendors a COPY of the design-system into Wend.Api/wwwroot/design-system so the app is
  self-contained: no build step, and it works on any clone without anyone re-running anything.
  This script re-copies it from the canonical source whenever the design-system is updated.

  Only the person who owns the canonical design-system needs to run this (Malin) — everyone
  else just gets the committed copy via `git pull`. After running, review the diff and commit.

  Usage:
    ./sync-design-system.ps1
    ./sync-design-system.ps1 -Source 'D:\path\to\design-system'
#>
param(
  # Canonical design-system folder (the single source of truth).
  [string]$Source = 'C:\Users\Nugget\Documents\Development\_template\design-system'
)

$ErrorActionPreference = 'Stop'

# Destination is resolved relative to THIS script, so it works from any working directory.
$dst = Join-Path $PSScriptRoot 'Wend.Api\wwwroot\design-system'

if (-not (Test-Path $Source)) {
  Write-Error "Canonical design-system not found at '$Source'. Pass -Source <path> if it lives elsewhere."
}

# The parts Wend uses. (gallery/showcase are intentionally left out.)
$parts = 'tokens','base','primitives','components','compositions','utilities','theme'

# Mirror cleanly: wipe the old bundle first so files removed upstream don't linger as stale copies.
if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
New-Item -ItemType Directory -Force $dst | Out-Null

foreach ($part in $parts) {
  $from = Join-Path $Source $part
  if (-not (Test-Path $from)) { Write-Error "Missing design-system part: $from" }
  Copy-Item $from (Join-Path $dst $part) -Recurse -Force
}
Copy-Item (Join-Path $Source 'VERSION') (Join-Path $dst 'VERSION') -Force

$version  = (Get-Content (Join-Path $dst 'VERSION') -Raw).Trim()
$cssCount = (Get-ChildItem $dst -Recurse -Filter *.css | Measure-Object).Count
Write-Host "Design-system synced to v$version ($cssCount CSS files) -> Wend.Api/wwwroot/design-system"
Write-Host "Review with 'git status' / 'git diff', then commit."
