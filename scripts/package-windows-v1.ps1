param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "windows-native\Muesli.Windows\Muesli.Windows.csproj"
$publishRoot = Join-Path $root "publish"
$publishDir = Join-Path $publishRoot "muesli-windows-$Runtime"
$artifactsDir = Join-Path $root "artifacts"
$zipPath = Join-Path $artifactsDir "muesli-windows-0.2.0-$Runtime.zip"
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

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=true `
    -o $publishDir

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
Muesli for Windows 0.2.0
========================

Run:
  Muesli.exe

Install:
  powershell -ExecutionPolicy Bypass -File .\install-windows.ps1
  Optional:
    powershell -ExecutionPolicy Bypass -File .\install-windows.ps1 -StartAtLogin

Current v1 requirements:
  - Windows x64.
  - No Python, venv, or external worker runtime is required.

Default shortcut:
  Hold the configured shortcut to dictate. Release it to transcribe and paste into the previously focused app.
  The default is F8, and it can be changed in Settings.

Notes:
  - This build is local-first. Seven offline sherpa-onnx models cover Parakeet, Whisper, SenseVoice, Qwen3-ASR, and Cohere.
  - Dictation and final meeting/import roles are selected independently. Live meeting transcription is Off until a packaged streaming model is qualified.
  - Model preparation is explicit and network-backed. Selecting or downloading does not activate a recognizer.
  - Archives and every runtime-required model file are verified against pinned SHA-256 hashes before use.
  - The package includes the version-matched Sherpa CUDA provider. NVIDIA GPU acceleration requires CUDA Toolkit 12.x and cuDNN 9.x dependencies.
  - On an NVIDIA system, install the optional dependencies once with:
      powershell -ExecutionPolicy Bypass -File .\install-parakeet-cuda-runtime.ps1
    Restart Muesli afterward. CPU-only systems do not need this step.
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
Muesli Windows v0.2.0 Release Notes
===================================

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
  - Version-matched Sherpa ONNX 1.13.4 CUDA provider with explicit CUDA 12/cuDNN 9 diagnostics.
  - Native sherpa-onnx speaker diarization for recorded meeting transcripts.
  - Optional native Qwen/GGUF cleanup through LLamaSharp, disabled by default.

Known release requirements:
  - Selecting a role never downloads or activates a model; a missing selected role fails closed until prepared.
  - Supported models use CUDA when its optional NVIDIA dependency pack is installed; otherwise the same model and engine use CPU.
  - Native diarization models download into the user model cache on first recorded meeting use.
  - Native Qwen cleanup requires a compatible GGUF model in the native-cleanup cache.
  - Installer is not code-signed until a real signing certificate is configured.

Fresh-machine validation checklist:
  1. Install using MuesliSetup-0.2.0-win-x64.exe.
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

$shipChecklist = @"
Muesli Windows Human Ship Checklist
===================================

This checklist is intentionally human-owned. Automated build or benchmark JSON does not
mark any item below complete.

Installer / ZIP:
  [ ] Installer launches and completes on a fresh Windows account.
  [ ] ZIP extracts and smoke test launches Muesli.exe.
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
  [ ] Supported models report CUDA on a supported NVIDIA test machine after install-parakeet-cuda-runtime.ps1.
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
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "install-parakeet-cuda-runtime.ps1") -Destination (Join-Path $publishDir "install-parakeet-cuda-runtime.ps1") -Force
Copy-Item -LiteralPath (Join-Path $root "THIRD-PARTY-NOTICES.md") -Destination (Join-Path $publishDir "THIRD-PARTY-NOTICES.md") -Force
Copy-Item -LiteralPath (Join-Path $root "licenses") -Destination (Join-Path $publishDir "licenses") -Recurse -Force
Set-Content -LiteralPath $lastPublishFile -Value $publishDir -Encoding UTF8

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath

Write-Host "Created $zipPath"
