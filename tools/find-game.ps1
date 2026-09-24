# Locates the Graveyard Keeper 2 installation and prints its path.
#
#   $game = & "$PSScriptRoot\find-game.ps1"
#
# Search order:
#   1. the running game process (works for any install location, Steam or not)
#   2. the trainer's own settings file (%LOCALAPPDATA%\GK2Trainer\settings.json)
#   3. Steam library folders from the registry / libraryfolders.vdf
#
# Prints nothing when the game cannot be found.
# ASCII-only file.

$candidates = New-Object System.Collections.Generic.List[string]

function Add-Candidate([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return }
    if (-not $candidates.Contains($path)) { $candidates.Add($path) }
}

# 1. running process
foreach ($process in (Get-Process -Name 'GraveyardKeeper2' -ErrorAction SilentlyContinue)) {
    try { Add-Candidate (Split-Path $process.MainModule.FileName -Parent) } catch { }
}

# 2. trainer settings
$settings = Join-Path $env:LOCALAPPDATA 'GK2Trainer\settings.json'
if (Test-Path $settings) {
    try { Add-Candidate ((Get-Content $settings -Raw | ConvertFrom-Json).GameDirectory) } catch { }
}

# 3. steam libraries
$libraries = New-Object System.Collections.Generic.List[string]
foreach ($root in @(
        'HKEY_CURRENT_USER\Software\Valve\Steam',
        'HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam',
        'HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam')) {
    foreach ($name in @('SteamPath', 'InstallPath')) {
        try {
            $value = (Get-ItemProperty -Path "Registry::$root" -Name $name -ErrorAction Stop).$name
            if ($value -and -not $libraries.Contains($value)) { $libraries.Add($value) }
        } catch { }
    }
}
foreach ($library in $libraries.ToArray()) {
    $vdf = Join-Path $library 'steamapps\libraryfolders.vdf'
    if (-not (Test-Path $vdf)) { continue }
    foreach ($match in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s*"([^"]+)"')) {
        $path = $match.Groups[1].Value.Replace('\\', '\')
        if (-not $libraries.Contains($path)) { $libraries.Add($path) }
    }
}
foreach ($library in $libraries) {
    Add-Candidate (Join-Path $library 'steamapps\common\Graveyard Keeper 2')
}

foreach ($candidate in $candidates) {
    if (Test-Path (Join-Path $candidate 'GraveyardKeeper2.exe')) {
        Write-Output $candidate
        return
    }
}
