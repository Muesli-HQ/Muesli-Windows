# Muesli Windows

Native Windows app for Muesli: local-first dictation, meeting recording, transcription, notes, and searchable history.

## Features

- **Dictation**: hold the global shortcut, speak, release, and paste into the active app.
- **Native transcription**: seven pinned offline sherpa-onnx models cover Parakeet, Whisper, SenseVoice, Qwen3-ASR, and Cohere; dictation and final meeting/import roles are selected independently.
- **Native cleanup**: optional local Qwen/GGUF cleanup through LLamaSharp after transcription.
- **Meeting recording**: captures separate microphone and meeting-audio tracks with an explicit lifecycle, route repair, suspend/crash recovery, and retained-audio playback. Detected meetings attempt Windows process-tree capture and visibly disclose endpoint-loopback fallback.
- **Native speaker labels**: sherpa-onnx diarization labels recorded meeting system audio when models are cached.
- **Meeting notes**: local summaries by default, with OpenAI or OpenRouter available when you add keys.
- **History and search**: dictations, meetings, folders, filters, custom dictionary, and export.

## Install

Download and extract `muesli-windows-v1-win-x64.zip`, then run `Muesli.exe`.

The Python runtime has been removed. No Python, venv, or external transcription worker runtime is required. Model preparation is an explicit action in Models; selecting a role never starts a download or silently changes engines.

## Build

```powershell
dotnet build .\windows-native\Muesli.Windows\Muesli.Windows.csproj --no-restore
```

## Package

```powershell
.\scripts\package-windows-v1.ps1
.\scripts\test-windows-package.ps1
.\scripts\build-installer.ps1
```

Recorded-meeting and import regression checks are available through
`scripts\benchmark-native-meeting.ps1` and `scripts\test-media-imports.ps1`.
The fail-closed physical meeting-session matrix is documented in
`docs\PHASE3_QUALIFICATION.md` and checked by
`scripts\qualify-meeting-session-lifecycle.ps1`.
The packaged release gate is `scripts\qualify-windows-release.ps1`; it produces
machine-readable native-runtime, stress, memory, package-hash, signature, and
fresh-launch log evidence.

## Architecture

- **UI**: WPF .NET 8, XAML, Inter font.
- **Audio**: NAudio WASAPI microphone capture, Windows process-tree loopback when available for a detected meeting, and an explicitly disclosed render-endpoint loopback fallback.
- **ASR**: role-scoped `NativeTranscriptionClient` instances with sherpa-onnx offline ONNX models. Dictation has one recognizer owner; recorded meetings and imports share the separately selected final-model owner.
- **Cleanup**: `NativeTextCleanupService` with LLamaSharp / llama.cpp GGUF models.
- **Diarization**: `NativeDiarizationClient` with sherpa-onnx ONNX models.
- **Model caches**:
  - `%USERPROFILE%\.cache\muesli\native-parakeet`
  - `%USERPROFILE%\.cache\muesli\native-asr`
  - `%USERPROFILE%\.cache\muesli\native-diarization`
  - `%USERPROFILE%\.cache\muesli\native-cleanup`
- **App data**: `%APPDATA%\muesli`

## Development Notes

- Active app: `windows-native/Muesli.Windows/`
- Build before launch: `dotnet build windows-native\Muesli.Windows\Muesli.Windows.csproj --no-restore`
- Run debug build directly: `windows-native\Muesli.Windows\bin\Debug\net8.0-windows\Muesli.exe`
- Do not ship Python worker files, external transcription runtime setup files, or venv setup.

## Limitations

- Supported ASR families use the packaged CPU provider and select the packaged CUDA provider when compatible NVIDIA dependencies are available; provider fallback never changes the model or ASR engine.
- Qwen cleanup is disabled by default until a GGUF model is placed in the native-cleanup cache.
- Process-targeted loopback requires Windows build 20348 or newer and a live detected-process ID. When Windows blocks it, Muesli visibly falls back to render-endpoint loopback, which can include unrelated system sounds; mic recording can continue in a disclosed degraded state.
- Preparing a missing model requires an explicit network-backed download. Transcription itself fails closed when the selected role is missing or unverified.
- Live meeting transcription is off by default. The explicit Nemotron 3.5 option has passed packaged Windows real-inference and Silero-boundary qualification; preparing it is network-backed and never enables it automatically. Long physical-meeting, Bluetooth, route-change, CUDA-live, and human multilingual qualification remain open.
- Installer is not code-signed until a signing certificate is configured.

## Acknowledgements

- NVIDIA Parakeet and Qwen3-ASR
- OpenAI Whisper, SenseVoice, and Cohere Transcribe model artifacts
- sherpa-onnx
- NAudio
- Inter font family
