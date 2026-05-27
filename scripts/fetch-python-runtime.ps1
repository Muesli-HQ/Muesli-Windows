param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationParent,
    [string]$Version = "3.12.11",
    [string]$BuildStamp = "20250612",
    [string]$ExpectedSha256 = "7b93afa91931dbc37b307a81b8680b30193736b5ef29a44ef6452f702c306e7a",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $DestinationParent)) {
    throw "DestinationParent does not exist: $DestinationParent"
}

$assetName = "cpython-$Version+$BuildStamp-x86_64-pc-windows-msvc-install_only.tar.gz"
$downloadUrl = "https://github.com/astral-sh/python-build-standalone/releases/download/$BuildStamp/$assetName"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$cacheDir = Join-Path $repoRoot "artifacts\python-cache"
New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null
$cachedTarball = Join-Path $cacheDir $assetName

function Get-Sha256([string]$path) {
    (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$expected = $ExpectedSha256.ToLowerInvariant()

if ((Test-Path $cachedTarball) -and -not $Force) {
    $actual = Get-Sha256 $cachedTarball
    if ($actual -ne $expected) {
        Write-Host "Cached tarball SHA256 mismatch (expected $expected, got $actual). Re-downloading."
        Remove-Item -LiteralPath $cachedTarball -Force
    }
}

if (-not (Test-Path $cachedTarball)) {
    Write-Host "Downloading $assetName"
    $previous = $ProgressPreference
    $ProgressPreference = "SilentlyContinue"
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $downloadUrl -OutFile $cachedTarball
    } finally {
        $ProgressPreference = $previous
    }
    $actual = Get-Sha256 $cachedTarball
    if ($actual -ne $expected) {
        Remove-Item -LiteralPath $cachedTarball -Force -ErrorAction SilentlyContinue
        throw "SHA256 mismatch for $assetName. Expected $expected, got $actual."
    }
}

$pythonDir = Join-Path $DestinationParent "python"
if (Test-Path $pythonDir) {
    if (-not $Force) {
        # Reuse existing extraction only if the bundled python.exe runs and reports the expected version.
        $existingPython = Join-Path $pythonDir "python.exe"
        if (Test-Path $existingPython) {
            try {
                $reported = & $existingPython -c "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}')"
                if ($LASTEXITCODE -eq 0 -and $reported.Trim() -eq $Version) {
                    Write-Host "Bundled CPython $Version already in place at $pythonDir"
                    return
                }
            } catch {
            }
        }
    }
    Remove-Item -LiteralPath $pythonDir -Recurse -Force
}

Write-Host "Unpacking $assetName -> $DestinationParent"
$tarExe = Join-Path $env:SystemRoot "System32\tar.exe"
if (-not (Test-Path $tarExe)) {
    $tarExe = "tar"
}
& $tarExe -xzf $cachedTarball -C $DestinationParent
if ($LASTEXITCODE -ne 0) {
    throw "tar exit code $LASTEXITCODE while unpacking $assetName"
}

$bundledPython = Join-Path $pythonDir "python.exe"
if (-not (Test-Path $bundledPython)) {
    throw "Expected $bundledPython after unpacking, but it does not exist."
}

$license = Join-Path $pythonDir "LICENSE.txt"
if (Test-Path $license) {
    Copy-Item -LiteralPath $license -Destination (Join-Path $DestinationParent "THIRD-PARTY-NOTICES-PYTHON.txt") -Force
}

Write-Host "Bundled CPython $Version at $pythonDir"
