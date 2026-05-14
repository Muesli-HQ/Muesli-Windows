param(
    [switch]$WithPostProcessing,
    [switch]$WithParakeet,
    [switch]$CheckOnly
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (Test-Path (Join-Path $root "Muesli.exe")) {
    $appDir = $root
} else {
    $appDir = Resolve-Path (Join-Path $PSScriptRoot "..\publish\muesli-windows-win-x64")
}

$venv = Join-Path $appDir ".venv"
$python = Join-Path $venv "Scripts\python.exe"
$requirements = Join-Path $appDir "worker\requirements.txt"
$postRequirements = Join-Path $appDir "worker\requirements-postprocess.txt"
$parakeetRequirements = Join-Path $appDir "worker\requirements-parakeet.txt"

if (-not (Test-Path $requirements)) {
    throw "Could not find worker requirements at $requirements"
}

function Find-PythonLauncher {
    $candidates = @(
        @{ File = "py"; Args = @("-3.12") },
        @{ File = "py"; Args = @("-3.11") },
        @{ File = "python"; Args = @() },
        @{ File = "python3"; Args = @() }
    )

    foreach ($candidate in $candidates) {
        try {
            if (-not (Get-Command $candidate.File -ErrorAction SilentlyContinue)) {
                continue
            }
            $versionArgs = @()
            $versionArgs += $candidate.Args
            $versionArgs += @("-c", "import sys; raise SystemExit(0 if sys.version_info[:2] in [(3, 11), (3, 12)] else 1)")
            & $candidate.File @versionArgs | Out-Null
            if ($LASTEXITCODE -eq 0) {
                return $candidate
            }
        } catch {
        }
    }

    throw "Supported Python was not found. Install Python 3.11 or 3.12, then run setup-worker-runtime.ps1 again. Python 3.13+ is not used for this v1 package."
}

if (-not (Test-Path $python)) {
    if ($CheckOnly) {
        $launcher = Find-PythonLauncher
        Write-Host "Supported Python launcher found: $($launcher.File) $($launcher.Args -join ' ')"
        return
    }

    $launcher = Find-PythonLauncher
    $venvArgs = @()
    $venvArgs += $launcher.Args
    $venvArgs += @("-m", "venv", $venv)
    & $launcher.File @venvArgs
}

if ($CheckOnly) {
    & $python -c "import sys; raise SystemExit(0 if sys.version_info[:2] in [(3, 11), (3, 12)] else 1)"
    if ($LASTEXITCODE -ne 0) {
        throw "Existing worker runtime uses an unsupported Python version. Delete .venv and rerun setup with Python 3.11 or 3.12."
    }

    Write-Host "Muesli worker runtime check passed at $python"
    return
}

& $python -m pip install --upgrade pip
& $python -m pip install -r $requirements

if ($WithPostProcessing) {
    if (-not (Test-Path $postRequirements)) {
        throw "Could not find post-processing requirements at $postRequirements"
    }
    & $python -m pip install -r $postRequirements
}

if ($WithParakeet) {
    if (-not (Test-Path $parakeetRequirements)) {
        throw "Could not find Parakeet requirements at $parakeetRequirements"
    }
    & $python -m pip install -r $parakeetRequirements
}

Write-Host "Muesli worker runtime is ready at $python"
