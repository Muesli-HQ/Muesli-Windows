param(
    [string]$ZipPath = "",
    [string]$WorkDir = "$env:TEMP\muesli-v1-qa",
    [switch]$SkipLaunch
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($ZipPath)) {
    $ZipPath = Join-Path $root "artifacts\muesli-windows-v1-win-x64.zip"
}

if (-not (Test-Path $ZipPath)) {
    throw "Package not found: $ZipPath"
}

if (Test-Path $WorkDir) {
    Get-Process Muesli -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    try {
        Remove-Item -LiteralPath $WorkDir -Recurse -Force
    } catch {
        $timestamp = Get-Date -Format "yyyyMMddHHmmss"
        $WorkDir = "$WorkDir-$timestamp"
        Write-Warning "Could not clean previous QA directory. Using '$WorkDir' instead. Last error: $($_.Exception.Message)"
    }
}
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
Expand-Archive -LiteralPath $ZipPath -DestinationPath $WorkDir -Force

$required = @(
    "Muesli.exe",
    "Muesli.dll",
    "README-WINDOWS.txt",
    "RELEASE-NOTES.txt",
    "SHIP-CHECKLIST.txt",
    "setup-worker-runtime.ps1",
    "fresh-machine-qa.ps1",
    "install-windows.ps1",
    "uninstall-windows.ps1",
    "worker\transcribe_worker.py",
    "worker\requirements.txt",
    "worker\requirements-postprocess.txt",
    "worker\requirements-parakeet.txt",
    "Assets\menu_m_template@2x.png"
)

foreach ($item in $required) {
    $path = Join-Path $WorkDir $item
    if (-not (Test-Path $path)) {
        throw "Package missing required file: $item"
    }
}

$setupScript = Get-Content (Join-Path $WorkDir "setup-worker-runtime.ps1") -Raw
[scriptblock]::Create($setupScript) | Out-Null

$installScript = Get-Content (Join-Path $WorkDir "install-windows.ps1") -Raw
[scriptblock]::Create($installScript) | Out-Null

$uninstallScript = Get-Content (Join-Path $WorkDir "uninstall-windows.ps1") -Raw
[scriptblock]::Create($uninstallScript) | Out-Null

$freshQaScript = Get-Content (Join-Path $WorkDir "fresh-machine-qa.ps1") -Raw
[scriptblock]::Create($freshQaScript) | Out-Null

if (-not $SkipLaunch) {
    $process = Start-Process -FilePath (Join-Path $WorkDir "Muesli.exe") -WorkingDirectory $WorkDir -PassThru
    Start-Sleep -Seconds 8
    if ($process.HasExited) {
        throw "Packaged app exited during smoke test with code $($process.ExitCode)"
    }

    Get-Process Muesli -ErrorAction SilentlyContinue | Stop-Process -Force
}

Write-Host "Package QA passed: $ZipPath"
Write-Host "Extracted to: $WorkDir"
