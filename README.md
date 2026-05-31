# Muesli Windows

The native Windows app for Muesli — a local-first, privacy-focused dictation and meeting transcription tool.

## What is Muesli?

Muesli runs entirely on your laptop. No cloud transcription. No data leaves your machine.

- **Dictate anywhere** with a global hotkey (`F8` by default). Speak, release, and your transcript is pasted into the active app.
- **Record meetings** with automatic detection of Google Meet, Zoom, Microsoft Teams, and Webex.
- **Transcribe locally** using open-source Whisper models (CPU or NVIDIA GPU).
- **Clean up transcripts** with optional local Qwen post-processing.
- **Keep everything** in a searchable, persistent history.

## Features

- **Dictation** — Hold `F8`, speak, release. Auto-paste or clipboard.
- **Local Transcription** — Whisper runs on-device. No internet needed.
- **Meeting Recording** — Auto-detects active meeting windows, captures mic + system audio.
- **Meeting Summaries** — Auto-generated notes via local rules, OpenAI, or OpenRouter.
- **Custom Dictionary** — Phrase → replacement pairs for consistent terminology.
- **Organized History** — Dictations and meetings with folders, search, filtering.
- **Dark & Light Theme** — Native WPF theming.
- **Floating Indicator** — Compact pill showing recording/transcribing state.
- **GPU Acceleration** — Optional NVIDIA Parakeet fast path.
- **Privacy First** — All processing local. Cloud APIs only if you add your own keys. Crash reporting is opt-in.

## Installation

Download `Muesli-win-Setup.exe` from the [latest release](https://github.com/Muesli-HQ/Muesli-Windows/releases/latest) and run it. Muesli installs into `%LocalAppData%\Muesli` and launches itself when it's done.

On first launch, Muesli walks you through onboarding (microphone, hotkey, optional crash reporting) and then downloads the base Whisper model in the background. After that, dictation works offline.

### Updates

Muesli checks for updates on every launch and notifies you when one is ready. You can also check manually from **About → Check Now**, which becomes **Restart and update** once an update has downloaded. Updates are delivered as Velopack delta packages so subsequent versions are small downloads.

> If you installed an earlier v0.2.x build via the Inno Setup `.exe`, uninstall it first from **Settings → Apps** before running `Muesli-win-Setup.exe`. The new installer lives at a different path and won't auto-replace the old one.

## Quick Start

1. **First Launch** — Complete onboarding: pick microphone, hotkey, indicator preference, crash reporting opt-in.
2. **Wait for the Base Model** — Muesli auto-downloads the recommended Whisper `base` model after onboarding. A toast tells you when it's ready.
3. **Test Your Mic** — Models → Test microphone.
4. **Dictate** — Hold `F8`, speak, release.
5. **Detect Meetings** — Open Google Meet, Zoom, Teams, or Webex. Muesli prompts you to record.

## Architecture

The WPF app communicates with a Python worker process via JSON-RPC over stdin/stdout. The worker handles model loading, transcription, and optional post-processing. The UI doesn't care whether the backend is Whisper CPU, Whisper CUDA, Parakeet, or a future native engine.

A bundled CPython 3.12 runtime ships inside the installer at `<install>\python\`, with the base Whisper worker dependencies preinstalled into `python\site-packages-muesli\`. End users never need to install Python themselves.

## Configuration

Settings stored in `%APPDATA%\muesli\windows-settings.json`:

| Setting | Default | Description |
|---------|---------|-------------|
| `Hotkey` | `F8` | Global dictation shortcut |
| `AsrEngine` | `whisper` | `whisper` or `parakeet-v3` |
| `ModelProfile` | `base` | Whisper model size |
| `PasteBehavior` | `active-app` | `active-app` or `clipboard` |
| `Theme` | `dark` | `dark` or `light` |
| `CrashReportingEnabled` | `false` | Opt-in Sentry crash reporting |

Optional environment variables:
- `OPENAI_API_KEY`, `OPENROUTER_API_KEY` — meeting summary providers (or enter in Settings).
- `MUESLI_PYTHON` — override the bundled Python with a custom interpreter.
- `MUESLI_SKIP_AUTODOWNLOAD=1` — skip the first-launch base-model auto-download.
- `MUESLI_SENTRY_DSN` — override the embedded Sentry DSN (dev / staging).

## Known Limitations

- Not code-signed yet; Windows SmartScreen may warn on first launch.
- System loopback capture can be blocked by some audio setups; mic recording still works.
- Meeting speaker separation is basic (mic = `You`, loopback = `System audio`) unless diarization is enabled.
- Meeting detection is heuristic (app titles + browser URLs + process names).

## Roadmap

See [`docs/ROADMAP.md`](docs/ROADMAP.md).

## For Contributors

Want to build from source, hack on the WPF UI, or work on the Python worker directly? You don't need the installer.

### Prerequisites

- Windows 10/11 x64
- .NET 8 SDK
- Python 3.11 or 3.12 on PATH (only for running unpackaged — the installer bundles its own copy)

### Build & run from source

```powershell
.\scripts\setup-worker-runtime.ps1
dotnet run --project .\windows-native\Muesli.Windows\Muesli.Windows.csproj
```

Optional add-ons (install extra worker deps into the same `.venv`):

```powershell
.\scripts\setup-worker-runtime.ps1 -WithPostProcessing   # Qwen cleanup
.\scripts\setup-worker-runtime.ps1 -WithParakeet         # NVIDIA Parakeet
.\scripts\setup-worker-runtime.ps1 -WithDiarization      # pyannote
```

### Package

```powershell
.\scripts\package-windows-v1.ps1       # ZIP + Velopack Setup.exe + .nupkgs
.\scripts\test-windows-package.ps1     # Smoke test
```

Releases are produced by `.github/workflows/release-package.yml` (manual `workflow_dispatch`). The workflow embeds `secrets.SENTRY_DSN` into the Release build and uploads the zip + Velopack artifacts.

### Tech stack

- **UI:** WPF .NET 8, XAML, Inter font
- **Audio:** NAudio (WASAPI microphone + system loopback)
- **Transcription:** Python worker — Faster-Whisper, NVIDIA Parakeet (optional), Qwen post-processing
- **Crash reporting:** Sentry (opt-in)
- **Updates:** Velopack 1.0.x with GitHub Releases as the source

## Contributing

Contributions welcome! Priority areas:
- Visual polish (match OG macOS design system)
- Meeting detection reliability
- GPU path testing on diverse NVIDIA hardware
- Code signing once a certificate is available

Open an issue or PR at [github.com/Muesli-HQ/Muesli-Windows](https://github.com/Muesli-HQ/Muesli-Windows).

## Acknowledgements

Muesli is developed across native platform apps in the Muesli organization. The Windows app follows the same product direction and design language as the macOS app.

Built with:
- [Whisper](https://github.com/openai/whisper) by OpenAI
- [Faster-Whisper](https://github.com/SYSTRAN/faster-whisper)
- [NAudio](https://github.com/naudio/NAudio)
- [Velopack](https://github.com/velopack/velopack)
- [python-build-standalone](https://github.com/astral-sh/python-build-standalone)
- [Inter](https://rsms.me/inter/) font family

## License

MIT License — free for personal and commercial use.
