# Captures the trainer window for every tab into docs/screenshots.
# The game (or a fake bridge) must be running so the UI has data to show.
# ASCII-only file.

param(
    [string]$Exe = 'E:\01TestProject\GK2Trainer\src\GK2Trainer.App\bin\Debug\net10.0-windows\GK2Trainer.exe',
    [string]$OutDir = 'E:\01TestProject\GK2Trainer\docs\screenshots'
)

$ErrorActionPreference = 'Stop'

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
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

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
    Write-Output "$($tab.Name).png  ${width}x${height}  printwindow=$ok"
}

Get-Process -Name 'GK2Trainer' -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Output 'done'
