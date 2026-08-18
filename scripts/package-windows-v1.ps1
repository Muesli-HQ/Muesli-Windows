param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$SentryDsn = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SentryDsn) -and -not [string]::IsNullOrWhiteSpace($env:SENTRY_DSN)) {
    $SentryDsn = $env:SENTRY_DSN
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
. (Join-Path $PSScriptRoot "read-release-properties.ps1")
$release = Get-MuesliReleaseProperties -Root $root
$project = Join-Path $root "windows-native\Muesli.Windows\Muesli.Windows.csproj"
$publishRoot = Join-Path $root "publish"
$publishDir = Join-Path $publishRoot "muesli-windows-$Runtime"
$artifactsDir = Join-Path $root "artifacts"
$portableZipName = "muesli-windows-$($release.Version)-$Runtime.zip"
$zipPath = Join-Path $artifactsDir $portableZipName
$lastPublishFile = Join-Path $artifactsDir "last-publish-dir.txt"

if (Test-Path $publishDir) {
    Get-Process Muesli -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $publishDir -Recurse -Force
            break
        } catch {
            if ($attempt -eq 5) {
                $timestamp = Get-Date -Format "yyyyMMddHHmmss"
                $publishDir = Join-Path $publishRoot "muesli-windows-$Runtime-$timestamp"
                Write-Warning "Could not clean existing publish directory. Publishing to '$publishDir' instead. Last error: $($_.Exception.Message)"
                break
            }
            Start-Sleep -Milliseconds (350 * $attempt)
        }
    }
}
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$publishArgs = @(
    $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=false",
    "-p:PublishReadyToRun=true",
    "-o", $publishDir
)
if (-not [string]::IsNullOrWhiteSpace($SentryDsn)) {
    $publishArgs += "-p:SentryDsn=$SentryDsn"
    Write-Host "Embedding Sentry DSN into release build."
}
dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE. No release package was produced." }

$satelliteCultureDirs = @(
    "cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant"
)
foreach ($culture in $satelliteCultureDirs) {
    $cultureDir = Join-Path $publishDir $culture
    if (Test-Path $cultureDir) {
        Remove-Item -LiteralPath $cultureDir -Recurse -Force
    }
}

# Public release packages intentionally exclude symbols and repository-only qualification
# tooling. Symbols can be produced as a separate controlled artifact when needed.
Get-ChildItem -LiteralPath $publishDir -Recurse -File -Filter "*.pdb" -ErrorAction SilentlyContinue |
    Remove-Item -Force

$readme = @"
Muesli for Windows $($release.Version)
========================

Run:
  Muesli.exe

Install:
  powershell -ExecutionPolicy Bypass -File .\install-windows.ps1
  Optional:
    powershell -ExecutionPolicy Bypass -File .\install-windows.ps1 -StartAtLogin

Current $($release.Channel) requirements:
  - Windows x64.
  - No Python, venv, or external worker runtime is required.

Supported environments:
  - $($release.SupportedEnvironments -join "`n  - ")
  - Windows 10 22H2 uses truthful endpoint-loopback for meeting system audio; process-targeted capture is not promised or attempted there.
  - Current serviced Windows 11 x64 releases may attempt process-tree loopback when supported and fall back to disclosed endpoint-loopback when unavailable.

Default shortcut:
  Hold the configured shortcut to dictate. Release it to transcribe and paste into the previously focused app.
  The default is F8, and it can be changed in Settings.

Notes:
  - This build is local-first. Seven offline sherpa-onnx models cover Parakeet, Whisper, SenseVoice, Qwen3-ASR, and Cohere.
  - Dictation and final meeting/import roles are selected independently. Live meeting transcription is Off until a packaged streaming model is qualified.
  - Model preparation is explicit and network-backed. Selecting or downloading does not activate a recognizer.
  - Archives and every runtime-required model file are verified against pinned SHA-256 hashes before use.
  - This Wave 0 package includes the CPU Sherpa provider. It does not claim NVIDIA acceleration; a version-matched CUDA provider still has to pass the Wave 5 packaging and hardware qualification gates.
  - Supported offline models emit timestamped segments for recorded meetings and native diarization alignment where the backend supplies token timing.
  - Recorded meetings use native sherpa-onnx diarization for speaker labels when system audio is available.
  - Qwen cleanup can run locally through native LLamaSharp / llama.cpp with a GGUF model and does not use Python.
  - Meeting summaries can use local fallback, OpenAI, or OpenRouter. Enter provider keys in Settings, or set OPENAI_API_KEY / OPENROUTER_API_KEY.
  - Parakeet model cache is stored in `%USERPROFILE%\.cache\muesli\native-parakeet`.
  - Other offline ASR caches are stored in `%USERPROFILE%\.cache\muesli\native-asr`.
  - Diarization model cache is stored in `%USERPROFILE%\.cache\muesli\native-diarization`.
  - Cleanup model cache is stored in `%USERPROFILE%\.cache\muesli\native-cleanup`.
  - The Models page includes runtime diagnostics, model cache status, and cache management.
  - Use Models for independent Prepare, Cancel, Retry, Verify, Delete, disk size, status, and diagnostics actions.
  - Settings includes "Start Muesli when I sign in"; it launches the app in the background tray using --background.
  - Settings are stored in `%APPDATA%\muesli\windows-settings.json`.
  - Dictations, meetings, and dictionary data are stored in `%APPDATA%\muesli\data`.
  - Logs are stored in `%APPDATA%\muesli\logs`; use About > Open Logs when reporting issues.
"@

Set-Content -LiteralPath (Join-Path $publishDir "README-WINDOWS.txt") -Value $readme -Encoding UTF8
$releaseNotes = @"
Muesli Windows v$($release.Version) Release Notes
===================================

Release channel: $($release.Channel)

This build is a native Windows WPF clone of the shipped macOS Muesli app.

Highlights:
  - Seven pinned offline transcription choices with separate dictation and final meeting/import roles.
  - Push-to-talk and captured custom shortcuts, including alternatives when F8 is taken.
  - Active-app paste after dictation.
  - Meeting detection prompts for Google Meet, Zoom, Teams, and Webex foreground windows.
  - Meeting recording, import, notes, transcripts, folders, and search.
  - First-run onboarding for microphone, shortcut, startup, indicator, and model readiness.
  - Native Parakeet, Whisper, SenseVoice, Qwen3-ASR, and Cohere ASR through sherpa-onnx ONNX.
  - Explicit model preparation with pinned archive/file SHA-256 verification and visible progress.
  - Self-contained Sherpa ONNX 1.13.4 CPU runtime with native startup diagnostics.
  - Native sherpa-onnx speaker diarization for recorded meeting transcripts.
  - Optional native Qwen/GGUF cleanup through LLamaSharp, disabled by default.

Known release requirements:
  - Supported environments: $($release.SupportedEnvironments -join "; ").
  - Windows 10 22H2 uses endpoint-loopback for meeting system audio; process-targeted capture is not supported or promised.
  - Current serviced Windows 11 x64 releases attempt process-tree loopback only when the live target and operating-system capability are present; endpoint-loopback fallback is disclosed.
  - Selecting a role never downloads or activates a model; a missing selected role fails closed until prepared.
  - The public package currently uses the CPU provider. NVIDIA provider packaging remains a separately qualified release task.
  - Native diarization models download into the user model cache on first recorded meeting use.
  - Native Qwen cleanup requires a compatible GGUF model in the native-cleanup cache.
  - Installer is not code-signed until a real signing certificate is configured.

Fresh-machine validation checklist:
  1. Install using MuesliSetup-$($release.Version)-win-x64.exe.
  2. Confirm onboarding appears once.
  3. Pick a microphone and a shortcut that is not reserved by the test laptop.
  4. Run Models > Check setup.
  5. Explicitly prepare the selected dictation model, confirm selection is unchanged, then verify dictation with paste into Notepad and Chrome.
  6. Reboot if Launch at login is enabled and confirm Muesli starts in the tray/background.
  7. Test Google Meet in Chrome and Zoom desktop meeting detection prompts.
  8. Test recorded meeting transcript and Notes/Transcript switching.
  9. Test imported meeting transcription.
  10. Confirm no Python or external runtime setup is requested.

Automated gates completed during package creation do not replace the human checks above.
Signing, target-application paste confirmation, transcription-quality review, and a true
fresh-machine installer run require separate release evidence.
"@
Set-Content -LiteralPath (Join-Path $publishDir "RELEASE-NOTES.txt") -Value $releaseNotes -Encoding UTF8

$releaseMetadata = [ordered]@{
    schemaVersion = 1
    product = "Muesli for Windows"
    version = $release.Version
    channel = $release.Channel
    targetFramework = "net10.0-windows"
    runtime = $Runtime
    architecture = "x64"
    selfContained = $true
    minimumWindowsVersion = $release.MinimumWindowsVersion
    supportedEnvironments = $release.SupportedEnvironments
    package = [ordered]@{
        portableZip = $portableZipName
        installer = "MuesliSetup-$($release.Version)-win-x64.exe"
        releaseNotes = "RELEASE-NOTES.txt"
    }
    update = [ordered]@{
        channel = $release.Channel
        version = $release.Version
        package = $portableZipName
        releaseNotes = "RELEASE-NOTES.txt"
    }
    meetingAudioCapture = [ordered]@{
        windows10 = "Endpoint loopback only; process-targeted capture is not supported on Windows 10 22H2."
        windows11 = "Process-tree loopback is attempted only when the serviced OS and live target support it; endpoint-loopback fallback is disclosed."
    }
    transcriptionRuntime = [ordered]@{
        cpuProviderIncluded = $true
        cudaProviderIncluded = $false
        cudaQualificationModule = "Wave 5"
    }
}
$releaseMetadata | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $publishDir $release.MetadataFileName) -Encoding UTF8

$shipChecklist = @"
Muesli Windows Human Ship Checklist
===================================

This checklist is intentionally human-owned. Automated build or benchmark JSON does not
mark any item below complete.

Installer / ZIP:
  [ ] Muesli-win-Setup.exe (Velopack) installs cleanly to %LocalAppData%\Muesli.
  [ ] ZIP extracts and smoke test launches Muesli.exe.
  [ ] About > Check Now picks up a newer published release.
  [ ] uninstall-windows.ps1 removes shortcuts/startup registration.
  [ ] install-windows.ps1 can set StartAtLogin when requested.

Core dictation:
  [ ] First-run onboarding appears only once.
  [ ] F8 works when available.
  [ ] Shortcut capture works when F8 is already in use.
  [ ] Hold shortcut records and release transcribes.
  [ ] Active-app paste works in Notepad, Chrome, Word, Outlook, Teams, Slack/Discord.
  [ ] qualify-dictation-target.ps1 records a passing, human-confirmed trace for each supported target app.
  [ ] summarize-dictation-latency.ps1 passes the release-to-paste median/p95 gates on at least 20 successful dictations.
  [ ] Clipboard fallback works.

Runtime / models:
  [ ] Native diarization model status reports correctly.
  [ ] Every offline model reports Missing, Downloading, Verifying, Ready/Selected, Failed, Runtime unavailable, or Deletion failed accurately.
  [ ] The package reports CPU truthfully and does not claim a CUDA provider is included.
  [ ] Before a later NVIDIA release, stage a version-matched CUDA provider and pass the dedicated GPU hardware matrix.
  [ ] Qwen cleanup status reports Disabled, Needs model, Ready, or Runtime unavailable.
  [ ] Every catalog archive and required runtime file passes pinned SHA-256 verification and works offline after preparation.
  [ ] Every supported family completes real-audio inference; Parakeet is additionally tested on a CPU-only machine.

Meetings:
  [ ] Google Meet Chrome prompt appears and does not open Outlook.
  [ ] Zoom desktop prompt appears.
  [ ] Teams prompt appears.
  [ ] Webex prompt appears.
  [ ] Dismiss, Join Only, and Join & Record all behave correctly.
  [ ] Recording creates notes/transcript and optional retained audio.
  [ ] Recorded-meeting ASR and speaker diarization are reviewed against approved media.

UI clone fidelity:
  [ ] Sidebar spacing and selected states match OG screenshots.
  [ ] Floating indicator idle/recording/transcribing states match OG closely.
  [ ] Meeting detection toast matches OG layout/timing.
  [ ] Dictation rows are selectable/copyable.
  [ ] Search results show dictations and meetings.
  [ ] Settings panes avoid placeholder/fake data.

Release:
  [ ] Artifact sizes recorded.
  [ ] Code signing certificate applied when available.
  [ ] Release notes reviewed.
  [ ] Known limitations documented.
"@
Set-Content -LiteralPath (Join-Path $publishDir "SHIP-CHECKLIST.txt") -Value $shipChecklist -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "install-windows.ps1") -Destination (Join-Path $publishDir "install-windows.ps1") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "uninstall-windows.ps1") -Destination (Join-Path $publishDir "uninstall-windows.ps1") -Force
Copy-Item -LiteralPath (Join-Path $root "THIRD-PARTY-NOTICES.md") -Destination (Join-Path $publishDir "THIRD-PARTY-NOTICES.md") -Force
Copy-Item -LiteralPath (Join-Path $root "licenses") -Destination (Join-Path $publishDir "licenses") -Recurse -Force
Set-Content -LiteralPath $lastPublishFile -Value $publishDir -Encoding UTF8

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath

Write-Host "Created $zipPath"

$velopackDir = Join-Path $artifactsDir "velopack"
$velopackVersion = ([xml](Get-Content (Join-Path $root "windows-native\Muesli.Windows\Muesli.Windows.csproj"))).Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($velopackVersion)) {
    throw "Could not read <Version> from Muesli.Windows.csproj for Velopack packaging."
}

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "Installing vpk CLI globally"
    dotnet tool install -g vpk
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool install -g vpk failed." }
    $toolsBin = Join-Path $env:USERPROFILE ".dotnet\tools"
    if (-not ($env:PATH -split ';' | Where-Object { $_ -eq $toolsBin })) {
        $env:PATH = "$toolsBin;$env:PATH"
    }
}

New-Item -ItemType Directory -Force -Path $velopackDir | Out-Null
try {
    $prevManifest = Join-Path $velopackDir "releases.win.json"
    if (Test-Path $prevManifest) { Remove-Item -LiteralPath $prevManifest -Force }
    Invoke-WebRequest `
        -Uri "https://github.com/Muesli-HQ/Muesli-Windows/releases/latest/download/releases.win.json" `
        -OutFile $prevManifest -UseBasicParsing -ErrorAction Stop
    Write-Host "Pulled previous release manifest for delta packaging."
} catch {
    Write-Host "No previous release manifest available; full package only."
}

$icon = Join-Path $root "windows-native\Muesli.Windows\Assets\muesli.ico"
$packArgs = @(
    "pack",
    "--packId", "Muesli",
    "--packVersion", $velopackVersion,
    "--packDir", $publishDir,
    "--mainExe", "Muesli.exe",
    "--outputDir", $velopackDir,
    "--packAuthors", "Muesli",
    "--packTitle", "Muesli"
)
if (Test-Path $icon) { $packArgs += @("--icon", $icon) }
& vpk @packArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed with exit $LASTEXITCODE." }

Write-Host "Velopack artifacts written to $velopackDir"
