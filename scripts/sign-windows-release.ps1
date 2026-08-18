param(
    [string]$PublishDir = "",
    [string]$InstallerPath = "",
    [string]$CertificateThumbprint = "",
    [string]$TimestampUrl = "https://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
. (Join-Path $PSScriptRoot "read-release-properties.ps1")
$release = Get-MuesliReleaseProperties -Root $root
if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $root "publish\muesli-windows-win-x64"
}

$exe = Join-Path $PublishDir "Muesli.exe"
if (-not (Test-Path $exe)) {
    throw "Muesli.exe not found at $exe. Run the package script first."
}

$signtoolPath = $null
$signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if ($signtool) {
    $signtoolPath = $signtool.Source
} else {
    $sdkBin = "C:\Program Files (x86)\Windows Kits\10\bin"
    if (Test-Path $sdkBin) {
        $signtoolPath = Get-ChildItem -LiteralPath $sdkBin -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
}

if ([string]::IsNullOrWhiteSpace($signtoolPath)) {
    throw "signtool.exe was not found. Install Windows SDK, then rerun this script."
}

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $candidateInstaller = Join-Path $root "artifacts\MuesliSetup-$($release.Version)-win-x64.exe"
    if (Test-Path $candidateInstaller) {
        $InstallerPath = $candidateInstaller
    }
}

$targets = @($exe)
if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) {
    if (-not (Test-Path $InstallerPath)) {
        throw "Installer not found at $InstallerPath"
    }
    $targets += $InstallerPath
}

foreach ($target in $targets) {
    $args = @("sign", "/fd", "SHA256", "/tr", $TimestampUrl, "/td", "SHA256")
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $args += "/a"
    } else {
        $args += @("/sha1", $CertificateThumbprint)
    }
    $args += $target

    & $signtoolPath @args
    if ($LASTEXITCODE -ne 0) {
        throw "Signing failed for $target"
    }

    & $signtoolPath verify /pa /v $target
    if ($LASTEXITCODE -ne 0) {
        throw "Signature verification failed for $target"
    }

    Write-Host "Signed and verified $target"
}
