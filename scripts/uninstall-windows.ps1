param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Muesli",
    [switch]$KeepUserData
)

$ErrorActionPreference = "Stop"

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Remove-ItemProperty -Path $runKey -Name "Muesli" -ErrorAction SilentlyContinue

$programs = [Environment]::GetFolderPath("Programs")
$startMenuDir = Join-Path $programs "Muesli"
if (Test-Path $startMenuDir) {
    Remove-Item -LiteralPath $startMenuDir -Recurse -Force
}

$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "Muesli.lnk"
if (Test-Path $desktopShortcut) {
    Remove-Item -LiteralPath $desktopShortcut -Force
}

if (Test-Path $InstallDir) {
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
}

if (-not $KeepUserData) {
    $appData = Join-Path $env:APPDATA "muesli"
    if (Test-Path $appData) {
        Remove-Item -LiteralPath $appData -Recurse -Force
    }
}

Write-Host "Muesli uninstalled."
