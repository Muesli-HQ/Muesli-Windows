param(
    [string]$Configuration = "Debug",
    [string]$AudioPath,
    [string]$OutputDirectory,
    [string]$CatalogPath,
    [int]$Runs = 1,
    [switch]$Prepare
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $repoRoot "windows-native\Muesli.Windows\bin\$Configuration\net10.0-windows\Muesli.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Muesli executable not found: $executable"
}

if ([string]::IsNullOrWhiteSpace($AudioPath)) {
    $AudioPath = Join-Path $env:APPDATA "muesli\captures\last-dictation.wav"
}
$AudioPath = [IO.Path]::GetFullPath($AudioPath)
if (-not (Test-Path -LiteralPath $AudioPath -PathType Leaf)) {
    throw "Real smoke-test audio not found: $AudioPath"
}
if ((Get-Item -LiteralPath $AudioPath).Length -le 44) {
    throw "Smoke-test audio is empty: $AudioPath"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\benchmarks\phase1-model-smoke"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

# Preserve the invoking Windows profile when qualification is launched by a
# restricted runner that substitutes SpecialFolder.UserProfile in child GUIs.
if ([string]::IsNullOrWhiteSpace($env:MUESLI_NATIVE_PARAKEET_CACHE)) {
    $env:MUESLI_NATIVE_PARAKEET_CACHE = Join-Path $env:USERPROFILE ".cache\muesli\native-parakeet"
}
if ([string]::IsNullOrWhiteSpace($env:MUESLI_NATIVE_ASR_CACHE)) {
    $env:MUESLI_NATIVE_ASR_CACHE = Join-Path $env:USERPROFILE ".cache\muesli\native-asr"
}

if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
    $CatalogPath = Join-Path $repoRoot "qualification\cpu-catalog\advertised-cpu-models.json"
}
$CatalogPath = [IO.Path]::GetFullPath($CatalogPath)
if (-not (Test-Path -LiteralPath $CatalogPath -PathType Leaf)) {
    throw "Advertised CPU catalog not found: $CatalogPath"
}
$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
if ($catalog.publicPackageCpuOnly -ne $true -or $catalog.cudaProviderIncluded -eq $true) {
    throw "Advertised CPU catalog must remain publicPackageCpuOnly=true and cudaProviderIncluded=false."
}
$modelIds = @($catalog.models | ForEach-Object { $_.id })
if ($modelIds.Count -eq 0) {
    throw "Advertised CPU catalog contains no model IDs: $CatalogPath"
}
Write-Host "CPU catalog $($catalog.catalogId) status=$($catalog.status); models=$($modelIds.Count)"

$failures = [Collections.Generic.List[string]]::new()
foreach ($modelId in $modelIds) {
    $outputPath = Join-Path $OutputDirectory "$modelId.json"
    if ($Prepare) {
        Write-Host "Explicit preparation/checksum verification: $modelId"
        $prepareOutputPath = Join-Path $OutputDirectory "$modelId-prepare.json"
        $prepareArguments = @(
            "--prepare-model",
            "--model", $modelId,
            "--output", $prepareOutputPath
        )
        $prepareProcess = Start-Process -FilePath $executable -ArgumentList $prepareArguments -WorkingDirectory (Split-Path $executable) -Wait -PassThru
        if ($prepareProcess.ExitCode -ne 0) {
            $failures.Add("$modelId explicit preparation exited with code $($prepareProcess.ExitCode)")
            continue
        }
    }
    Write-Host "Real-audio smoke: $modelId"
    $arguments = @(
        "--benchmark-native",
        "--audio", $AudioPath,
        "--runs", [Math]::Max(1, $Runs).ToString(),
        "--model", $modelId,
        "--output", $outputPath
    )
    $process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory (Split-Path $executable) -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        $failures.Add("$modelId exited with code $($process.ExitCode)")
        continue
    }
    if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
        $failures.Add("$modelId produced no report")
        continue
    }
    $report = Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json
    $successful = @($report.Report.Results | Where-Object { $_.Success -eq $true })
    if ($report.Model -ne $modelId -or $successful.Count -eq 0) {
        $failures.Add("$modelId report did not contain successful inference for the requested model")
    }
}

if ($failures.Count -gt 0) {
    throw "Transcription model smoke failed: $($failures -join '; ')"
}

Write-Host "All $($modelIds.Count) supported offline models completed real-audio inference."
