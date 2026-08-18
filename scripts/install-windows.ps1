param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Muesli",
    [switch]$StartAtLogin,
    [switch]$NoDesktopShortcut
)

$ErrorActionPreference = "Stop"

$source = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not (Test-Path (Join-Path $source "Muesli.exe"))) {
    throw "Run this script from the extracted Muesli package folder."
}

$sourceResolved = (Resolve-Path $source).Path.TrimEnd('\')
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$installResolved = (Resolve-Path $InstallDir).Path.TrimEnd('\')
Get-ChildItem -LiteralPath $source -Force |
    Where-Object {
        $child = $_.FullName.TrimEnd('\')
        -not ($child.Equals($installResolved, [StringComparison]::OrdinalIgnoreCase) -or
              $installResolved.StartsWith($child + "\", [StringComparison]::OrdinalIgnoreCase))
    } |
    Copy-Item -Destination $InstallDir -Recurse -Force

$exe = Join-Path $InstallDir "Muesli.exe"
$shell = New-Object -ComObject WScript.Shell
$programs = [Environment]::GetFolderPath("Programs")
$startMenuDir = Join-Path $programs "Muesli"
New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null
$shortcut = $shell.CreateShortcut((Join-Path $startMenuDir "Muesli.lnk"))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $InstallDir
$shortcut.IconLocation = $exe
$shortcut.Save()

if (-not $NoDesktopShortcut) {
    $desktop = [Environment]::GetFolderPath("DesktopDirectory")
    $desktopShortcut = $shell.CreateShortcut((Join-Path $desktop "Muesli.lnk"))
    $desktopShortcut.TargetPath = $exe
    $desktopShortcut.WorkingDirectory = $InstallDir
    $desktopShortcut.IconLocation = $exe
    $desktopShortcut.Save()
}

if ($StartAtLogin) {
    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    New-Item -Path $runKey -Force | Out-Null
    New-ItemProperty -Path $runKey -Name "Muesli" -Value "`"$exe`" --background" -PropertyType String -Force | Out-Null
}

Write-Host "Muesli installed to $InstallDir"
Write-Host "Start Menu shortcut: $startMenuDir\Muesli.lnk"
Write-Host "Run Muesli from the Start Menu, or launch $exe"
