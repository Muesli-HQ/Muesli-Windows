param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Muesli",
    [switch]$RemoveUserData,
    [switch]$ForceUserDataRemoval
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

if ($RemoveUserData) {
    $appData = Join-Path $env:APPDATA "muesli"
    if (Test-Path $appData) {
        $confirmed = $ForceUserDataRemoval
        if (-not $confirmed) {
            Write-Warning "This permanently removes Muesli settings, history, logs, and retained recordings from '$appData'."
            $answer = Read-Host "Type DELETE to confirm user-data removal"
            $confirmed = $answer -ceq "DELETE"
        }

        if ($confirmed) {
            Remove-Item -LiteralPath $appData -Recurse -Force
            Write-Host "Muesli user data removed from $appData"
        } else {
            Write-Host "User-data removal was not confirmed. Data was preserved."
        }
    }
} else {
    Write-Host "Muesli user data was preserved. Rerun with -RemoveUserData to request confirmed removal."
}

Write-Host "Muesli uninstalled."
