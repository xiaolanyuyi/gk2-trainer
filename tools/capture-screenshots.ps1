# Captures the trainer window for every tab into docs\screenshots.
#
#   powershell -File tools\capture-screenshots.ps1                 # needs the game running for live data
#   powershell -File tools\capture-screenshots.ps1 -CropExisting   # just re-crop the current PNGs
#
# The top header row (which shows the local game path) is cropped away by default,
# so the published screenshots never contain machine specific paths.
# ASCII-only file.

param(
    [string]$Exe = '',
    [string]$OutDir = '',
    [int]$CropTop = 72,
    [switch]$CropExisting
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $Exe) {
    $Exe = Join-Path $root 'src\GK2Trainer.App\bin\Debug\net10.0-windows\GK2Trainer.exe'
}
if (-not $OutDir) {
    $OutDir = Join-Path $root 'docs\screenshots'
}

Add-Type -AssemblyName System.Drawing
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

function Crop-Top([string]$path, [int]$pixels) {
    if ($pixels -le 0) { return }

    # GDI+ refuses to save over the file it read from, so load from memory and
    # write through a temp file.
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $stream = New-Object System.IO.MemoryStream(, $bytes)
    $image = [System.Drawing.Image]::FromStream($stream)
    try {
        $rect = New-Object System.Drawing.Rectangle 0, $pixels, $image.Width, ($image.Height - $pixels)
        $crop = $image.Clone($rect, $image.PixelFormat)
        try {
            $temp = $path + '.tmp'
            $crop.Save($temp, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $crop.Dispose() }
    } finally {
        $image.Dispose()
        $stream.Dispose()
    }

    Move-Item ($path + '.tmp') $path -Force
}

if ($CropExisting) {
    foreach ($file in Get-ChildItem $OutDir -Filter '*.png') {
        Crop-Top $file.FullName $CropTop
        Write-Output "cropped $($file.Name) (top $CropTop px)"
    }
    return
}

$code = @'
using System;
using System.Runtime.InteropServices;

public class Win {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
Add-Type -TypeDefinition $code -Language CSharp

$tabs = @(
    @{ Index = 0; Name = '01-resources' },
    @{ Index = 1; Name = '02-speed' },
    @{ Index = 2; Name = '03-health' },
    @{ Index = 3; Name = '04-perks' },
    @{ Index = 4; Name = '05-tech' },
    @{ Index = 5; Name = '06-inventory' },
    @{ Index = 6; Name = '07-log' }
)

foreach ($tab in $tabs) {
    Get-Process -Name 'GK2Trainer' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 600

    Start-Process -FilePath $Exe -ArgumentList "--tab=$($tab.Index)" | Out-Null
    Start-Sleep -Seconds 5

    $proc = Get-Process -Name 'GK2Trainer' -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $proc) { Write-Output "no window for tab $($tab.Index)"; continue }

    $handle = $proc.MainWindowHandle
    [void][Win]::ShowWindow($handle, 9)
    Start-Sleep -Milliseconds 400

    $rect = New-Object Win+RECT
    [void][Win]::GetWindowRect($handle, [ref]$rect)
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    $ok = [Win]::PrintWindow($handle, $dc, 2)
    $graphics.ReleaseHdc($dc)
    $graphics.Dispose()

    $file = Join-Path $OutDir ($tab.Name + '.png')
    $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()

    Crop-Top $file $CropTop
    Write-Output "$($tab.Name).png  ${width}x${height}  printwindow=$ok"
}

Get-Process -Name 'GK2Trainer' -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Output 'done'
