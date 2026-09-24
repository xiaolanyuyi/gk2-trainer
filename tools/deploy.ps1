# Builds the plugin and copies it into the game's BepInEx\plugins folder.
#
#   powershell -File tools\deploy.ps1
#   powershell -File tools\deploy.ps1 -GameDir "D:\Steam\steamapps\common\Graveyard Keeper 2"
#
# The game directory is auto-detected (running process -> app settings -> Steam
# libraries) when -GameDir is omitted.

param(
    [string]$GameDir = '',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $GameDir) {
    $GameDir = & "$PSScriptRoot\find-game.ps1"
}
if (-not $GameDir) {
    throw "找不到游戏目录，请显式指定：powershell -File tools\deploy.ps1 -GameDir `"<...>\Graveyard Keeper 2`""
}
Write-Host "game directory: $GameDir"

$project = Join-Path $root 'plugin\GK2Trainer.Plugin.csproj'
$plugins = Join-Path $GameDir 'BepInEx\plugins'
$output = Join-Path $root 'plugin\bin'

Write-Host "building $project ..."
& dotnet build $project -c $Configuration -p:GameDir=$GameDir --nologo | Select-Object -Last 3

$dll = Get-ChildItem $output -Recurse -Filter 'GK2Trainer.Plugin.dll' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $dll) { throw 'build output not found' }

if (-not (Test-Path $plugins)) { New-Item -ItemType Directory -Path $plugins -Force | Out-Null }

# A running game memory-maps the plugin dll, which blocks overwriting it but
# not renaming it. Move the old one aside first, then copy the new build in;
# the stale copy is ignored because BepInEx only loads *.dll.
$target = Join-Path $plugins 'GK2Trainer.Plugin.dll'
if (Test-Path $target) {
    $stale = Join-Path $plugins 'GK2Trainer.Plugin.dll.old'
    Remove-Item $stale -Force -ErrorAction SilentlyContinue
    try {
        Rename-Item $target $stale -ErrorAction Stop
        Remove-Item $stale -Force -ErrorAction SilentlyContinue
        Write-Host 'note: replaced a plugin that was still mapped by a running game'
    } catch {
        Remove-Item $target -Force -ErrorAction SilentlyContinue
    }
}

Copy-Item $dll.FullName $plugins -Force

Write-Host ""
Write-Host "deployed: $($dll.FullName) -> $plugins"
Write-Host "a running game keeps the old code until it is restarted."
Write-Host "after restarting, check:"
Write-Host "  $(Join-Path $GameDir 'BepInEx\LogOutput.log')"
