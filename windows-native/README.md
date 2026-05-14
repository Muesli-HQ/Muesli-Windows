# Muesli Windows Native

This is the Windows-native app path. The Electron app remains in the repository only as a prototype/reference while the native port is built.

## Stack

- WPF / .NET 8 desktop app
- Native `RegisterHotKey` global shortcut handling
- Native WASAPI microphone capture via NAudio
- Native WASAPI loopback capture for meeting system audio where Windows allows it
- Local ASR worker boundary for Whisper CPU, Whisper CUDA, and optional NVIDIA Parakeet
- Persistent dictations, meetings, dictionary entries, and settings under `%APPDATA%\muesli`

## Build

```powershell
dotnet build .\windows-native\Muesli.Windows\Muesli.Windows.csproj
```

## Run

```powershell
dotnet run --project .\windows-native\Muesli.Windows\Muesli.Windows.csproj
```

## Immediate Porting Work

1. Bundle or bootstrap Python/faster-whisper dependencies.
2. Replace extractive meeting summaries with the macOS-style template/LLM summary pipeline.
3. Add real Parakeet backend.
4. Add installer packaging once dictation works end-to-end.
5. Replace the Python sidecar with a packaged native inference backend if startup/deployment demands it.
