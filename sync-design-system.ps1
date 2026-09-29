<#
  sync-design-system.ps1 — refresh Wend's bundled copy of the shared design-system.

  Wend vendors a COPY of the design-system into Wend.Api/wwwroot/design-system so the app is
  self-contained: no build step, and it works on any clone without anyone re-running anything.
  This script re-copies it from the source of truth whenever the design-system is updated.

  The copy itself is done by the workbench's extract tool (tools/extract.mjs), the same one the
  scaffolds use. It stamps a version header on tokens/index.css, refuses to overwrite files that
  were edited locally, and with -Check verifies the bundle against the canonical copy without
  touching anything.

  Only the person who owns the source-of-truth design-system needs to run this (Malin) — everyone
  else just gets the committed copy via `git pull`. After running, review the diff and commit.

  Usage:
    ./sync-design-system.ps1                 # sync to the workbench's current version
    ./sync-design-system.ps1 -Check          # report current / stale / drifted, change nothing
    ./sync-design-system.ps1 -Workbench 'D:\path\to\workbench'
#>
param(
  # The workbench checkout that holds libraries/design-system and tools/extract.mjs.
  [string]$Workbench = 'C:\Users\Nugget\Documents\Development\GitHub\repos\workbench',
  # Verify only: exit 1 when the bundle is stale, drifted or carries files removed upstream.
  [switch]$Check
)

$ErrorActionPreference = 'Stop'

$extractTool = Join-Path $Workbench 'tools\extract.mjs'
$source      = Join-Path $Workbench 'libraries\design-system'
# extract.mjs writes <target>\design-system, so the target is the folder that holds the bundle.
$target      = Join-Path $PSScriptRoot 'Wend.Api\wwwroot'
$dst         = Join-Path $target 'design-system'

if (-not (Test-Path $extractTool)) {
  Write-Error "Workbench extract tool not found at '$extractTool'. Pass -Workbench <path> if it lives elsewhere."
}
if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
  Write-Error 'Node.js is required to run the workbench extract tool.'
}

$extractArgs = @($extractTool, 'design-system', $target)
if ($Check) { $extractArgs += '--check' }
& node @extractArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# extract.mjs copies over the old bundle but never deletes, so a file removed upstream would
# linger as a stale copy. Anything in the bundle with no canonical twin is one of those.
$stale = Get-ChildItem $dst -Recurse -File | Where-Object {
  $rel = $_.FullName.Substring($dst.Length + 1)
  -not (Test-Path (Join-Path $source $rel))
}

if ($Check) {
  if ($stale) {
    Write-Host 'Files removed upstream but still bundled (re-run without -Check to prune):'
    $stale | ForEach-Object { Write-Host "  $($_.FullName.Substring($dst.Length + 1))" }
    exit 1
  }
  exit 0
}

$stale | ForEach-Object {
  Write-Host "Pruned stale file: $($_.FullName.Substring($dst.Length + 1))"
  Remove-Item $_.FullName -Force
}

$version  = (Get-Content (Join-Path $dst 'VERSION') -Raw).Trim()
$cssCount = (Get-ChildItem $dst -Recurse -Filter *.css | Measure-Object).Count
Write-Host "Design-system synced to v$version ($cssCount CSS files) -> Wend.Api/wwwroot/design-system"
Write-Host "Review with 'git status' / 'git diff', then commit."
