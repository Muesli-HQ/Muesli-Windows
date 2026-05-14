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
$zipPath = Join-Path $artifactsDir "muesli-windows-v1-$Runtime.zip"
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

Get-ChildItem -LiteralPath $publishDir -Directory -Recurse -Filter "__pycache__" -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force
Get-ChildItem -LiteralPath $publishDir -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in @(".pyc", ".pyo") } |
    Remove-Item -Force

$readme = @"
Muesli for Windows v1
=====================

Run:
  Muesli.exe

Install:
  powershell -ExecutionPolicy Bypass -File .\install-windows.ps1
  Optional:
    powershell -ExecutionPolicy Bypass -File .\install-windows.ps1 -WithPostProcessing -StartAtLogin
    powershell -ExecutionPolicy Bypass -File .\install-windows.ps1 -WithParakeet

Current v1 requirements:
  - Windows x64.
  - Python 3.11 or 3.12 available through `py`, `python`, or `python3` for first-time setup.
  - Verify Python/runtime compatibility without installing packages:
      powershell -ExecutionPolicy Bypass -File .\setup-worker-runtime.ps1 -CheckOnly
  - Verify the extracted/installed package shape:
      powershell -ExecutionPolicy Bypass -File .\fresh-machine-qa.ps1
  - Run setup-worker-runtime.ps1 once from this folder to create a local .venv beside Muesli.exe:
      powershell -ExecutionPolicy Bypass -File .\setup-worker-runtime.ps1
  - Optional Qwen post-processing dependencies, only needed if transcript cleanup is enabled:
      powershell -ExecutionPolicy Bypass -File .\setup-worker-runtime.ps1 -WithPostProcessing
      set MUESLI_ALLOW_MODEL_DOWNLOAD=1 for the first Qwen model download, or preinstall the model in `%USERPROFILE%\.cache\muesli`.
  - Optional NVIDIA Parakeet backend dependencies, only for CUDA/NVIDIA machines:
      powershell -ExecutionPolicy Bypass -File .\setup-worker-runtime.ps1 -WithParakeet

Default shortcut:
  Hold the configured shortcut to dictate. Release it to transcribe and paste into the previously focused app.
  The default is F8, and it can be changed in Settings.

Notes:
  - This build is local-first and uses Whisper through the bundled worker script.
  - NVIDIA Parakeet v3 is selectable as an optional backend when the Parakeet runtime dependencies are installed.
  - Optional Qwen post-processing can clean grammar, punctuation, and dictated lists after transcription.
  - The Models page can download the selected Whisper model and the optional Qwen cleanup model into the local cache.
  - Meeting summaries can use local fallback, OpenAI, or OpenRouter. Enter provider keys in Settings, or set OPENAI_API_KEY / OPENROUTER_API_KEY.
  - Model cache is stored in `%USERPROFILE%\.cache\muesli`.
  - The Models page includes runtime diagnostics, model cache status, and cache management.
  - Use Models > First-run Setup to check worker readiness, cache the selected Whisper model, open logs, or open the model cache.
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
  - Local-first dictation with Faster-Whisper / Whisper.
  - Push-to-talk and captured custom shortcuts, including alternatives when F8 is taken.
  - Active-app paste after dictation.
  - Meeting detection prompts for Google Meet, Zoom, Teams, and Webex foreground windows.
  - Meeting recording, import, notes, transcripts, folders, and search.
  - First-run onboarding for microphone, shortcut, startup, indicator, and model readiness.
  - Optional NVIDIA Parakeet backend path.
  - Optional Qwen transcript cleanup.

Known release requirements:
  - Python 3.11 or 3.12 is required for first-time local worker setup.
  - Qwen cleanup requires optional post-processing dependencies and a downloaded local model.
  - Parakeet requires NVIDIA/CUDA-capable hardware and optional Parakeet dependencies.
  - Installer is not code-signed until a real signing certificate is configured.

Fresh-machine validation checklist:
  1. Install using MuesliSetup-0.2.0-win-x64.exe.
  2. Confirm onboarding appears once.
  3. Pick a microphone and a shortcut that is not reserved by the test laptop.
  4. Run Models > First-run Setup > Check setup.
  5. Download Whisper base and verify dictation with paste into Notepad and Chrome.
  6. Reboot if Launch at login is enabled and confirm Muesli starts in the tray/background.
  7. Test Google Meet in Chrome and Zoom desktop meeting detection prompts.
  8. Test recorded meeting transcript and Notes/Transcript switching.
  9. If available, test NVIDIA/CUDA diagnostics and Parakeet selection.
  10. If enabled, test Qwen cleanup on dictation, imported meeting, and recorded meeting.
"@
Set-Content -LiteralPath (Join-Path $publishDir "RELEASE-NOTES.txt") -Value $releaseNotes -Encoding UTF8

$shipChecklist = @"
Muesli Windows Ship Checklist
=============================

Build:
  [ ] dotnet build .\windows-native\Muesli.Windows\Muesli.Windows.csproj -c Release
  [ ] .\scripts\build-installer.ps1
  [ ] .\scripts\test-windows-package.ps1

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
  [ ] Clipboard fallback works.

Runtime / models:
  [ ] Python 3.11/3.12 detected.
  [ ] Whisper dependencies OK.
  [ ] Whisper base cached and works offline.
  [ ] tiny/base/small tested on low-end CPU.
  [ ] CUDA status reported correctly on NVIDIA machine.
  [ ] Parakeet dependencies and fallback behavior tested on NVIDIA machine.
  [ ] Qwen dependencies and local model status tested.

Meetings:
  [ ] Google Meet Chrome prompt appears and does not open Outlook.
  [ ] Zoom desktop prompt appears.
  [ ] Teams prompt appears.
  [ ] Webex prompt appears.
  [ ] Dismiss, Join Only, and Join & Record all behave correctly.
  [ ] Recording creates notes/transcript and optional retained audio.

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
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "setup-worker-runtime.ps1") -Destination (Join-Path $publishDir "setup-worker-runtime.ps1") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "install-windows.ps1") -Destination (Join-Path $publishDir "install-windows.ps1") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "uninstall-windows.ps1") -Destination (Join-Path $publishDir "uninstall-windows.ps1") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "test-windows-package.ps1") -Destination (Join-Path $publishDir "test-windows-package.ps1") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "fresh-machine-qa.ps1") -Destination (Join-Path $publishDir "fresh-machine-qa.ps1") -Force
Set-Content -LiteralPath $lastPublishFile -Value $publishDir -Encoding UTF8

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath

Write-Host "Created $zipPath"
