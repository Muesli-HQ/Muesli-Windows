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
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit $LASTEXITCODE." }

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

& (Join-Path $PSScriptRoot "fetch-python-runtime.ps1") -DestinationParent $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "fetch-python-runtime.ps1 exited with code $LASTEXITCODE"
}

$bundledPython = Join-Path $publishDir "python\python.exe"
$siteTarget = Join-Path $publishDir "python\site-packages-muesli"
$workerRequirements = Join-Path $publishDir "worker\requirements.txt"
if (-not (Test-Path $workerRequirements)) {
    throw "Worker requirements file missing at $workerRequirements after publish."
}

New-Item -ItemType Directory -Force -Path $siteTarget | Out-Null
& $bundledPython -m pip install --upgrade pip --no-warn-script-location
if ($LASTEXITCODE -ne 0) { throw "Bundled pip self-upgrade failed (exit $LASTEXITCODE)." }
& $bundledPython -m pip install --no-warn-script-location --target $siteTarget -r $workerRequirements
if ($LASTEXITCODE -ne 0) { throw "Bundled pip install of worker requirements failed (exit $LASTEXITCODE)." }

Get-ChildItem -LiteralPath $siteTarget -Directory -Recurse -Filter "__pycache__" -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force
Get-ChildItem -LiteralPath $siteTarget -File -Recurse -ErrorAction SilentlyContinue |
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
    powershell -ExecutionPolicy Bypass -File .\install-windows.ps1 -StartAtLogin

Current v1 requirements:
  - Windows x64.
  - No system Python required. A bundled CPython 3.12 runtime ships in this folder
    under `python\`, with base Whisper worker dependencies pre-installed in
    `python\site-packages-muesli`.
  - Verify the extracted/installed package shape:
      powershell -ExecutionPolicy Bypass -File .\fresh-machine-qa.ps1

Optional add-ons (download extra dependencies into the bundled runtime):
  - Qwen transcript cleanup:
      powershell -ExecutionPolicy Bypass -File .\setup-worker-runtime.ps1 -WithPostProcessing
      set MUESLI_ALLOW_MODEL_DOWNLOAD=1 for the first Qwen model download, or preinstall the model in `%USERPROFILE%\.cache\muesli`.
  - NVIDIA Parakeet backend (CUDA/NVIDIA machines):
      powershell -ExecutionPolicy Bypass -File .\setup-worker-runtime.ps1 -WithParakeet
  - Speaker diarization (pyannote):
      powershell -ExecutionPolicy Bypass -File .\setup-worker-runtime.ps1 -WithDiarization

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
  - No system Python required. CPython 3.12 ships bundled in this package.
  - Qwen cleanup requires optional post-processing dependencies and a downloaded local model.
  - Parakeet requires NVIDIA/CUDA-capable hardware and optional Parakeet dependencies.
  - Installer is not code-signed until a real signing certificate is configured.

Fresh-machine validation checklist:
  1. Install using Muesli-win-Setup.exe from artifacts\velopack.
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
  [ ] .\scripts\package-windows-v1.ps1     (produces ZIP + Velopack Setup.exe)
  [ ] .\scripts\test-windows-package.ps1

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
  [ ] Clipboard fallback works.

Runtime / models:
  [ ] Bundled CPython 3.12 detected (worker python resolves via 'bundled' in logs).
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
