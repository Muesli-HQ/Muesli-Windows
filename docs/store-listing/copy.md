# Muesli — Microsoft Store listing copy

Every text field that the Partner Center submission form asks for. Update here first, then paste into Partner Center.

---

## App name

`Muesli`

(Fallback if taken: `Muesli Dictation`)

---

## Short description (≤200 chars)

> Local-first dictation and meeting transcription for Windows. Speak with a global hotkey; transcribe meetings offline using bundled Whisper. Your audio never leaves your device.

---

## Description (≤10,000 chars)

> Muesli runs entirely on your laptop. No cloud transcription. No data leaves your machine.
>
> **Dictate anywhere.** Hold the dictation hotkey (default F8), speak, release. Muesli transcribes locally and pastes the text into whichever app you were just using — Notepad, Word, Outlook, Teams, Slack, your browser.
>
> **Record meetings offline.** Muesli auto-detects active Google Meet, Zoom, Microsoft Teams, and Webex windows and offers to record. It captures your microphone plus optional system audio loopback so the other side of the call is included.
>
> **Transcribe with Whisper or Parakeet.** Bundled CPython 3.12 + Faster-Whisper run on CPU out of the box. NVIDIA GPU owners can opt into the Parakeet fast path. Optional Qwen post-processing cleans up grammar and dictated lists.
>
> **Privacy by default.**
> - All transcription is on-device. Bundled Whisper models run inside a CPython 3.12 subprocess that ships inside the installer.
> - The microphone is only open while you hold the dictation hotkey or have started a meeting recording.
> - System audio loopback only activates after you explicitly click **Join & Record** on the meeting-detection toast. No silent background capture.
> - The global keyboard hook inspects key codes only to detect the configured shortcut. No keystroke content is logged, transmitted, or stored.
> - Crash reporting is opt-in and off by default.
> - Cloud meeting summaries (OpenAI / OpenRouter) require your own API key.
>
> **Features.** Custom dictionary for consistent terminology · organized history with folders and search · dark / light theme · floating recording indicator · optional NVIDIA Parakeet GPU fast path · auto-update via in-app delta downloads.
>
> **Sensitive Windows surfaces and why each exists**
> - Global keyboard hook (low-level): so the dictation hotkey works in every application. Inspects key codes only.
> - Microphone capture (WASAPI): so dictation and meeting recording have audio. Active only during those operations.
> - System audio loopback (WASAPI render loopback): so the other side of a meeting is captured. Only after you explicitly opt in per meeting.
> - Bundled CPython 3.12 runtime: so Faster-Whisper can run as a stdin/stdout JSON-RPC subprocess. Fully bundled inside the installer; no remote download or execution.
> - Network egress: optional Sentry crash reporting (opt-in), optional Whisper model download from Hugging Face on first launch, optional cloud LLM calls (only when you supply your own key), Velopack delta updates from github.com/Muesli-HQ/Muesli-Windows/releases.
>
> Settings live in `%APPDATA%\muesli\`. Model weights cache to `%USERPROFILE%\.cache\muesli\`. Source code at github.com/Muesli-HQ/Muesli-Windows.

---

## Search terms (Partner Center allows up to 7)

```
dictation
whisper
transcription
meeting
local
offline
speech-to-text
```

---

## Category / subcategory

`Productivity → Other`

---

## Privacy policy URL

`https://github.com/Muesli-HQ/Muesli-Windows#privacy`

---

## Support URL

`https://github.com/Muesli-HQ/Muesli-Windows/issues`

---

## Age rating

Complete the IARC questionnaire in Partner Center. Answer "no" to every concerning category. Expected outcome: `E / 3+`.

---

## Installer settings (Partner Center → Packages)

- **Installer file:** `Muesli-win-Setup.exe` (download from the GitHub draft Release built by `.github/workflows/release-package.yml`).
- **Installer type:** EXE.
- **Silent install arguments:** `--silent`
- **Silent uninstall command path:** `%LocalAppData%\Muesli\Update.exe`
- **Silent uninstall arguments:** `--uninstall --silent`
- **Success return codes:** `0`
- **Restart behavior:** never requires restart.
- **Recommendation:** let Microsoft host the binary (do NOT link to the GitHub Release URL) — the Store CDN gives the strongest SmartScreen reputation signal.

The SHA-256 of `Muesli-win-Setup.exe` is printed at the end of the `release-package.yml` workflow summary; copy it into Partner Center's binary checksum field.

---

## Notes for certification (highest-leverage field)

> Muesli is a local-first dictation and meeting transcription tool. Sensitive Windows surfaces and why they exist:
>
> - **Global low-level keyboard hook (`WH_KEYBOARD_LL`):** detects the user-configured dictation hotkey (default `F8`) across all apps. The hook inspects key codes only to identify the shortcut; no keystroke content is logged, transmitted, or persisted. Source at `windows-native/Muesli.Windows/Services/GlobalHotkeyService.cs`.
> - **Microphone capture (WASAPI):** only active while the user holds the dictation hotkey or has started a meeting recording. Source at `Services/AudioCaptureService.cs`.
> - **System audio loopback (WASAPI render device loopback):** only active while the user has started a meeting recording — initiated by the user clicking "Join & Record" on the meeting-detection toast. Source at `Services/SystemAudioCaptureService.cs`.
> - **Bundled CPython 3.12 runtime (~85 MB):** runs the Faster-Whisper transcription worker as a stdin/stdout JSON-RPC subprocess. Required because Faster-Whisper has no native .NET binding. The runtime is fully bundled inside the installer; no remote code download or execution.
> - **Network egress:** optional Sentry crash reporting (opt-in, off by default); optional Whisper model download from Hugging Face on first launch (data files only — `.bin` / `.gguf`); optional cloud LLM calls (OpenAI / OpenRouter) only when the user enters their own API key.
> - **Auto-update:** Velopack delta updates from `github.com/Muesli-HQ/Muesli-Windows/releases`. EXE/MSI Store listings explicitly permit in-app updaters.
>
> Privacy summary: `https://github.com/Muesli-HQ/Muesli-Windows#privacy`

---

## Images (uploaded separately from Partner Center)

See [`images/`](images/) — placeholder filenames documented there map 1:1 to Partner Center upload slots:

- `store-logo-300.png` — 300×300 PNG, required Store logo.
- `store-logo-1080.png` — 1080×1080 PNG, recommended high-resolution logo.
- `hero-2400x1200.png` — 2400×1200 PNG, wide promotional banner.
- `screenshot-01-onboarding.png` … `screenshot-06-models.png` — 3–9 PNG screenshots, 1366×768 minimum.

Capture these on a clean Windows 11 VM with the production-signed installer applied (no SmartScreen warning visible).
