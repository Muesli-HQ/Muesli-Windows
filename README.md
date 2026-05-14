# Muesli for Windows

A native Windows clone of [Muesli](https://github.com/pHequals7/muesli) — a local-first, privacy-focused dictation and meeting transcription app.

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
- **Privacy First** — All processing local. Cloud APIs only if you add your own keys.

## Installation

### Download

1. Download the latest release: `muesli-windows-v1-win-x64.zip`
2. Extract to a folder
3. Run `setup-worker-runtime.ps1` to install Python dependencies
4. Launch `Muesli.exe`

### Optional Installer

```powershell
.\scripts\build-installer.ps1
# Output: artifacts\MuesliSetup-0.2.0-win-x64.exe
```

## Quick Start

1. **First Launch** — Complete onboarding: pick microphone, hotkey, indicator preference.
2. **Download a Model** — Models → Download base (recommended CPU fallback).
3. **Test Your Mic** — Models → Test microphone.
4. **Dictate** — Hold `F8`, speak, release.
5. **Detect Meetings** — Open Google Meet, Zoom, Teams, or Webex. Muesli prompts you to record.

## Tech Stack

- **UI:** WPF .NET 8, XAML, Inter font
- **Audio:** NAudio (WASAPI microphone + system loopback)
- **Transcription:** Python worker — Faster-Whisper, NVIDIA Parakeet (optional), Qwen post-processing
- **Packaging:** Self-contained `dotnet publish` + Inno Setup

## Development

### Prerequisites

- Windows 10/11 x64
- .NET 8 SDK
- Python 3.11 or 3.12
- Inno Setup 6 (optional, for installer)

### Build

```powershell
dotnet build .\windows-native\Muesli.Windows\Muesli.Windows.csproj -c Release
```

### Install Worker Runtime

```powershell
.\scripts\setup-worker-runtime.ps1
```

Optional post-processing:

```powershell
.\scripts\setup-worker-runtime.ps1 -WithPostProcessing
```

### Package

```powershell
.\scripts\package-windows-v1.ps1      # ZIP
.\scripts\build-installer.ps1          # EXE
```

### Package QA

```powershell
.\scripts\test-windows-package.ps1
```

## Architecture

The WPF app communicates with a Python worker process via JSON-RPC over stdin/stdout. The worker handles model loading, transcription, and optional post-processing. The UI doesn't care whether the backend is Whisper CPU, Whisper CUDA, Parakeet, or a future native engine.

## Configuration

Settings stored in `%APPDATA%\muesli\windows-settings.json`:

| Setting | Default | Description |
|---------|---------|-------------|
| `Hotkey` | `F8` | Global dictation shortcut |
| `AsrEngine` | `whisper` | `whisper` or `parakeet-v3` |
| `ModelProfile` | `base` | Whisper model size |
| `PasteBehavior` | `active-app` | `active-app` or `clipboard` |
| `Theme` | `dark` | `dark` or `light` |

Optional environment variables:
- `OPENAI_API_KEY`
- `OPENROUTER_API_KEY`
- `MUESLI_PYTHON`

## Known Limitations

- Python is not bundled yet; setup script creates a local `.venv`
- Not code-signed yet; Windows SmartScreen may warn on first launch
- System loopback capture can be blocked by some audio setups; mic recording still works
- Meeting speaker separation is basic (mic = `You`, loopback = `System audio`)
- Meeting detection is heuristic (app titles + browser URLs + process names)

## Roadmap

See [`docs/ROADMAP.md`](docs/ROADMAP.md).

## Contributing

Contributions welcome! Priority areas:
- Visual polish (match OG macOS design system)
- Meeting detection reliability
- GPU path testing on diverse NVIDIA hardware
- Bundling Python into the app for zero-dependency installs

Open an issue or PR at [github.com/Mvkd108/Muesli](https://github.com/Mvkd108/Muesli).

## Acknowledgements

Muesli Windows is a native clone of the original [Muesli](https://github.com/pHequals7/muesli) macOS app by [pHequals7](https://github.com/pHequals7). The macOS version remains the design and behavior mandate.

Built with:
- [Whisper](https://github.com/openai/whisper) by OpenAI
- [Faster-Whisper](https://github.com/SYSTRAN/faster-whisper)
- [NAudio](https://github.com/naudio/NAudio)
- [Inter](https://rsms.me/inter/) font family

## License

MIT License — free for personal and commercial use.
