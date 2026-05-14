param(
    [string]$InstallDir = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

if (-not (Test-Path (Join-Path $InstallDir "Muesli.exe"))) {
    throw "Muesli.exe was not found in '$InstallDir'. Run this script from the installed or extracted Muesli folder, or pass -InstallDir."
}

$required = @(
    "Muesli.exe",
    "Muesli.dll",
    "README-WINDOWS.txt",
    "RELEASE-NOTES.txt",
    "SHIP-CHECKLIST.txt",
    "setup-worker-runtime.ps1",
    "worker\transcribe_worker.py",
    "worker\requirements.txt",
    "worker\requirements-postprocess.txt",
    "worker\requirements-parakeet.txt",
    "Assets\menu_m_template@2x.png"
)

foreach ($item in $required) {
    $path = Join-Path $InstallDir $item
    if (-not (Test-Path $path)) {
        throw "Missing required file: $item"
    }
}

$forbiddenPatterns = @("Outlook.Application", "Microsoft.Office.Interop.Outlook", "MAPI")
$sourceFiles = Get-ChildItem -LiteralPath $InstallDir -File -Recurse -Include "*.cs", "*.xaml", "*.py", "*.ps1", "*.txt" -ErrorAction SilentlyContinue
foreach ($pattern in $forbiddenPatterns) {
    $hit = $sourceFiles | Select-String -Pattern $pattern -SimpleMatch -List | Select-Object -First 1
    if ($hit) {
        throw "Forbidden Outlook/MAPI reference found in $($hit.Path): $pattern"
    }
}

$setup = Join-Path $InstallDir "setup-worker-runtime.ps1"
& powershell -NoProfile -ExecutionPolicy Bypass -File $setup -CheckOnly

$appData = Join-Path $env:APPDATA "muesli"
$settings = Join-Path $appData "windows-settings.json"
$dataDir = Join-Path $appData "data"
$logsDir = Join-Path $appData "logs"

Write-Host "Fresh-machine QA passed for $InstallDir"
Write-Host "Settings path: $settings"
Write-Host "Data path: $dataDir"
Write-Host "Logs path: $logsDir"
Write-Host "Manual checks still required: onboarding once, shortcut capture, dictation paste, meeting detection, Qwen/Parakeet optional runtime tests."
