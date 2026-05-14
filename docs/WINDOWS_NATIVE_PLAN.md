# Windows Native Direction

The macOS product is already shipped. The Windows app should be a native Windows product, not a cross-platform Electron app.

## Decision

Use a native Windows shell:

- **WPF on .NET 8 for the first native build.**
- Keep the existing Electron app only as a working prototype/reference while porting.
- Keep ASR behind a worker boundary so the UI does not care whether the backend is Whisper CPU, Whisper CUDA, Parakeet, or a future packaged native engine.

WPF is the pragmatic first step because it gives us native Windows windows, tray, hotkeys, low memory overhead compared with Electron, fast custom dark UI work, and a straightforward path to installers. WinUI 3 can be revisited after the transcription product is stable, but it adds Windows App SDK packaging friction without improving ASR performance.

## Native App Shape

- `Muesli.Windows`: WPF desktop shell.
- `GlobalHotkeyService`: `RegisterHotKey` based F8/global shortcut handling.
- `DictationCoordinator`: one state machine for button-hold and hotkey dictation.
- `SettingsStore`: `%APPDATA%\muesli\windows-settings.json`.
- `TranscriptionWorkerClient`: persistent bridge to local ASR worker.
- `AudioCaptureService`: native WASAPI capture, replacing Electron/ffmpeg capture.

## ASR Model Tiers

- **Basic CPU laptops:** Whisper tiny/base quantized, persistent worker, no cold starts.
- **Better CPU/iGPU laptops:** Whisper small quantized.
- **NVIDIA laptops:** Parakeet optional fast path and Whisper CUDA fallback.
- **High-end GPU:** Whisper large-v3-turbo/turbo and Parakeet selectable.

## Migration Order

1. Finish model settings and backend selection in WPF.
2. Add native tray, toast, auto-start, active-app paste, installer, and updater.
3. Add meeting import/transcription screens after dictation is solid.
4. Replace Python sidecar with a native packaged inference backend only after UX and transcription behavior are stable.
