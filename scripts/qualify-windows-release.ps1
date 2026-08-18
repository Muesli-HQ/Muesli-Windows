param(
    [string]$PackagePath = "",
    [Parameter(Mandatory = $true)]
    [string]$AudioPath,
    [ValidateSet("automatic", "cpu", "cuda")]
    [string]$Provider = "automatic",
    [ValidateRange(2, 50)]
    [int]$Runs = 10,
    [ValidateRange(0, 10)]
    [double]$MaxRealtimeFactor = 0.20,
    [ValidateRange(0, 3600000)]
    [int]$MaxWarmWallMs = 0,
    [ValidateRange(0, 4096)]
    [int]$MaxWorkingSetGrowthMb = 256,
    [ValidateRange(0, 4096)]
    [int]$MaxPrivateMemoryGrowthMb = 256,
    [string]$ExpectedTranscriptSha256 = "",
    [string]$ExpectedSegmentSha256 = "",
    [string]$OutputDirectory = "",
    [switch]$RequireSignature,
    [switch]$SkipWpfLaunch
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
. (Join-Path $PSScriptRoot "read-release-properties.ps1")
$release = Get-MuesliReleaseProperties -Root $root
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Join-Path $root "artifacts\muesli-windows-$($release.Version)-win-x64.zip"
}
$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$resolvedAudio = (Resolve-Path -LiteralPath $AudioPath).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "artifacts\qualification\phase5-$Provider"
}
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
$extractDirectory = Join-Path $resolvedOutput "package"
$diagnosticPath = Join-Path $resolvedOutput "native-runtime.json"
$reportPath = Join-Path $resolvedOutput "release-qualification.json"
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

function Get-LogOffsets {
    $offsets = @{}
    $logDirectory = Join-Path $env:APPDATA "muesli\logs"
    if (Test-Path -LiteralPath $logDirectory) {
        Get-ChildItem -LiteralPath $logDirectory -Filter "muesli-*.log" -File -ErrorAction SilentlyContinue |
            ForEach-Object { $offsets[$_.FullName] = $_.Length }
    }
    return $offsets
}

function Get-NewLogLines {
    param([hashtable]$Offsets)
    $lines = [Collections.Generic.List[string]]::new()
    $logDirectory = Join-Path $env:APPDATA "muesli\logs"
    if (-not (Test-Path -LiteralPath $logDirectory)) { return $lines }
    foreach ($file in Get-ChildItem -LiteralPath $logDirectory -Filter "muesli-*.log" -File -ErrorAction SilentlyContinue) {
        $start = 0L
        if ($Offsets.ContainsKey($file.FullName)) { $start = [long]$Offsets[$file.FullName] }
        if ($file.Length -le $start) { continue }
        $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            $stream.Seek($start, [IO.SeekOrigin]::Begin) | Out-Null
            $reader = [IO.StreamReader]::new($stream)
            try {
                while (-not $reader.EndOfStream) { $lines.Add($reader.ReadLine()) }
            } finally { $reader.Dispose() }
        } finally { $stream.Dispose() }
    }
    return $lines
}

function Get-MachineEvidence {
    $cpu = @()
    $gpu = @()
    $memoryBytes = 0L
    try { $cpu = @(Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors) } catch {}
    try { $gpu = @(Get-CimInstance Win32_VideoController -ErrorAction Stop | Select-Object Name,DriverVersion,AdapterRAM) } catch {}
    try { $memoryBytes = [long](Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).TotalPhysicalMemory } catch {}
    return [ordered]@{
        machineName = $env:COMPUTERNAME
        osVersion = [Environment]::OSVersion.VersionString
        processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
        powershellVersion = $PSVersionTable.PSVersion.ToString()
        totalPhysicalMemoryBytes = $memoryBytes
        cpu = $cpu
        gpu = $gpu
    }
}

$logOffsets = Get-LogOffsets
$failures = [Collections.Generic.List[string]]::new()
$packageQaPassed = $true
$packageQaError = ""
$packageQaScript = Join-Path $PSScriptRoot "test-windows-package.ps1"
try {
    & $packageQaScript -ZipPath $resolvedPackage -WorkDir $extractDirectory -SkipLaunch | Out-Null
} catch {
    $packageQaPassed = $false
    $packageQaError = $_.Exception.Message
    $failures.Add("Package structure QA failed: $packageQaError")
}

$executable = Join-Path $extractDirectory "Muesli.exe"
$diagnosticExitCode = -1
$diagnostic = $null
$previousProvider = $env:MUESLI_PARAKEET_PROVIDER
$previousDiarizationProvider = $env:MUESLI_DIARIZATION_PROVIDER
try {
    if ($Provider -eq "automatic") {
        Remove-Item Env:MUESLI_PARAKEET_PROVIDER -ErrorAction SilentlyContinue
        Remove-Item Env:MUESLI_DIARIZATION_PROVIDER -ErrorAction SilentlyContinue
    } else {
        $env:MUESLI_PARAKEET_PROVIDER = $Provider
        $env:MUESLI_DIARIZATION_PROVIDER = $Provider
    }
    $env:MUESLI_NATIVE_PARAKEET_CACHE = Join-Path $env:USERPROFILE ".cache\muesli\native-parakeet"
    $env:MUESLI_NATIVE_DIARIZATION_CACHE = Join-Path $env:USERPROFILE ".cache\muesli\native-diarization"

    if ($packageQaPassed -and (Test-Path -LiteralPath $executable)) {
        $arguments = @(
            "--diagnose-native",
            "--audio", $resolvedAudio,
            "--runs", $Runs.ToString([Globalization.CultureInfo]::InvariantCulture),
            "--output", $diagnosticPath
        )
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $executable
        $startInfo.WorkingDirectory = $extractDirectory
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.Arguments = ($arguments | ForEach-Object { '"' + ([string]$_).Replace('"', '\"') + '"' }) -join " "
        $process = [Diagnostics.Process]::Start($startInfo)
        $process.WaitForExit()
        $diagnosticExitCode = $process.ExitCode
        if (Test-Path -LiteralPath $diagnosticPath) {
            $diagnostic = Get-Content -LiteralPath $diagnosticPath -Raw | ConvertFrom-Json
        }
    }

    if ($null -eq $diagnostic) {
        $failures.Add("The packaged app did not produce native runtime evidence.")
    } elseif ($diagnosticExitCode -ne 0 -or -not $diagnostic.Report.Passed) {
        $failures.Add("Packaged native runtime qualification failed with exit code $diagnosticExitCode.")
    } else {
        $stress = $diagnostic.Report.Stress
        if ($null -eq $stress) {
            $failures.Add("Native stress evidence is missing.")
        } else {
            if ($Provider -ne "automatic" -and $stress.Provider -ne $Provider) {
                $failures.Add("Provider '$($stress.Provider)' does not match required '$Provider'.")
            }
            if ($MaxRealtimeFactor -gt 0 -and $stress.WarmRealtimeFactor -gt $MaxRealtimeFactor) {
                $failures.Add("Warm RTF $($stress.WarmRealtimeFactor) exceeds $MaxRealtimeFactor.")
            }
            if ($MaxWarmWallMs -gt 0 -and $stress.WarmWallMs -gt $MaxWarmWallMs) {
                $failures.Add("Warm wall $($stress.WarmWallMs) ms exceeds $MaxWarmWallMs ms.")
            }
            $maxWorkingSetGrowthBytes = [long]$MaxWorkingSetGrowthMb * 1MB
            $maxPrivateGrowthBytes = [long]$MaxPrivateMemoryGrowthMb * 1MB
            if ($stress.SteadyStateWorkingSetGrowthBytes -gt $maxWorkingSetGrowthBytes) {
                $failures.Add("Steady-state working-set growth $($stress.SteadyStateWorkingSetGrowthBytes) bytes exceeds $MaxWorkingSetGrowthMb MB.")
            }
            if ($stress.SteadyStatePrivateMemoryGrowthBytes -gt $maxPrivateGrowthBytes) {
                $failures.Add("Steady-state private-memory growth $($stress.SteadyStatePrivateMemoryGrowthBytes) bytes exceeds $MaxPrivateMemoryGrowthMb MB.")
            }
            if (-not [string]::IsNullOrWhiteSpace($ExpectedTranscriptSha256) -and
                $stress.TranscriptSha256 -ne $ExpectedTranscriptSha256.ToLowerInvariant()) {
                $failures.Add("Transcript SHA-256 changed.")
            }
            if (-not [string]::IsNullOrWhiteSpace($ExpectedSegmentSha256) -and
                $stress.SegmentLayoutSha256 -ne $ExpectedSegmentSha256.ToLowerInvariant()) {
                $failures.Add("Segment-layout SHA-256 changed.")
            }
        }
    }

    $signature = if (Test-Path -LiteralPath $executable) { Get-AuthenticodeSignature -LiteralPath $executable } else { $null }
    if ($RequireSignature -and ($null -eq $signature -or $signature.Status -ne "Valid")) {
        $failures.Add("A valid Authenticode signature is required.")
    }

    $wpfLaunchPassed = $true
    $wpfLaunchError = ""
    if (-not $SkipWpfLaunch -and $packageQaPassed) {
        try {
            $wpf = Start-Process -FilePath $executable -WorkingDirectory $extractDirectory -PassThru
            Start-Sleep -Seconds 8
            if ($wpf.HasExited) { throw "Packaged WPF app exited with code $($wpf.ExitCode)." }
            Stop-Process -Id $wpf.Id -Force -ErrorAction SilentlyContinue
        } catch {
            $wpfLaunchPassed = $false
            $wpfLaunchError = $_.Exception.Message
            $failures.Add("Packaged WPF launch failed: $wpfLaunchError")
        }
    }

    $newLogLines = @(Get-NewLogLines -Offsets $logOffsets)
    $forbiddenLogLines = @($newLogLines | Where-Object { $_ -match '\] ERROR\b|Unhandled UI exception|XamlParseException' })
    if ($forbiddenLogLines.Count -gt 0) {
        $failures.Add("Fresh application logs contain $($forbiddenLogLines.Count) forbidden error entries.")
    }

    $packageHash = (Get-FileHash -LiteralPath $resolvedPackage -Algorithm SHA256).Hash.ToLowerInvariant()
    $packageInfo = Get-Item -LiteralPath $resolvedPackage
    $report = [ordered]@{
        schemaVersion = 1
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        releaseVersion = $release.Version
        releaseChannel = $release.Channel
        minimumWindowsVersion = $release.MinimumWindowsVersion
        supportedEnvironments = $release.SupportedEnvironments
        passed = $failures.Count -eq 0
        failures = $failures
        machine = Get-MachineEvidence
        providerRequested = $Provider
        runs = $Runs
        package = [ordered]@{
            path = $resolvedPackage
            bytes = $packageInfo.Length
            sha256 = $packageHash
            structureQaPassed = $packageQaPassed
            structureQaError = $packageQaError
            executableSignatureStatus = if ($null -eq $signature) { "Unavailable" } else { [string]$signature.Status }
            executableSigner = if ($null -eq $signature -or $null -eq $signature.SignerCertificate) { "" } else { $signature.SignerCertificate.Subject }
        }
        gates = [ordered]@{
            maxRealtimeFactor = $MaxRealtimeFactor
            maxWarmWallMs = $MaxWarmWallMs
            maxWorkingSetGrowthMb = $MaxWorkingSetGrowthMb
            maxPrivateMemoryGrowthMb = $MaxPrivateMemoryGrowthMb
            signatureRequired = [bool]$RequireSignature
        }
        nativeDiagnosticPath = $diagnosticPath
        nativeDiagnostic = $diagnostic
        wpfLaunchPassed = $wpfLaunchPassed
        wpfLaunchError = $wpfLaunchError
        forbiddenFreshLogEntries = $forbiddenLogLines
    }
    $report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Host "Phase 5 release qualification report: $reportPath"
    if ($failures.Count -gt 0) {
        throw "Phase 5 release qualification failed: $($failures -join '; ')"
    }
    Write-Host "Phase 5 release qualification passed."
    Get-Content -LiteralPath $reportPath -Raw
} finally {
    $env:MUESLI_PARAKEET_PROVIDER = $previousProvider
    $env:MUESLI_DIARIZATION_PROVIDER = $previousDiarizationProvider
}
