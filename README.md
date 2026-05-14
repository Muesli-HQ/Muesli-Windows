# Muesli

Windows-first clone of the Muesli local dictation and meeting transcription app.

This project is intentionally separate from `projects/meeting-scribe`. Meeting Scribe is only a reference for proven Windows plumbing; this repository owns the clone product, UI, contracts, and packaging path.

## Product Direction

- Muesli-like frontend across Windows and macOS.
- Local-first dictation by default.
- Whisper as the portable baseline ASR engine.
- NVIDIA Parakeet can be added as an optional accelerated backend, not the default.
- Meeting transcription, notes, exports, and calendar features are staged after the dictation vertical slice.

## Development

```powershell
dotnet build .\windows-native\Muesli.Windows\Muesli.Windows.csproj
```

Python worker dependencies:

```powershell
.\scripts\setup-worker-runtime.ps1
```

Set `MUESLI_PYTHON` if Python is not on PATH.

## Windows v1 Package

```powershell
.\scripts\package-windows-v1.ps1
```

Output:

```text
artifacts\muesli-windows-v1-win-x64.zip
```

See `docs\WINDOWS_V1_RELEASE.md` for clean-install QA, known limits, and signing notes.
