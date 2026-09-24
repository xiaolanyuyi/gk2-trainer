# Packages the trainer as a single executable.
#
#   powershell -File tools\publish.ps1                   # needs .NET 10 runtime on the target
#   powershell -File tools\publish.ps1 -SelfContained     # fully standalone (zip ~40 MB)
#   powershell -File tools\publish.ps1 -GameDir "<...>\Graveyard Keeper 2"
#
# The plugin is embedded in the app, so one exe can install everything. The game
# directory is only needed to build the plugin and is auto-detected when omitted.
# ASCII-only file.

param(
    [switch]$SelfContained,
    [string]$Configuration = 'Release',
    [string]$GameDir = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$plugin = Join-Path $root 'plugin\GK2Trainer.Plugin.csproj'
$project = Join-Path $root 'src\GK2Trainer.App\GK2Trainer.App.csproj'
$output = if ($SelfContained) { Join-Path $root 'dist\standalone' } else { Join-Path $root 'dist' }

if (-not $GameDir) {
    $GameDir = & "$PSScriptRoot\find-game.ps1"
}
if (-not $GameDir) {
    throw "找不到游戏目录（构建插件需要游戏程序集）。请加 -GameDir `"<...>\Graveyard Keeper 2`""
}

if (Test-Path $output) { Remove-Item $output -Recurse -Force }

Write-Host "game directory: $GameDir"
Write-Host "building plugin ..."
& dotnet build $plugin -c $Configuration -p:GameDir=$GameDir --nologo | Select-Object -Last 2

$arguments = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", "win-x64",
    "-p:PublishSingleFile=true",
    "-o", $output
)

if ($SelfContained) {
    $arguments += "--self-contained", "true"
    $arguments += "-p:IncludeNativeLibrariesForSelfExtract=true"
} else {
    $arguments += "--self-contained", "false"
}

Write-Host "dotnet $($arguments -join ' ')"
& dotnet @arguments | Select-Object -Last 3

Write-Host ""
Write-Host "输出目录: $output"
Get-ChildItem $output | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } }
