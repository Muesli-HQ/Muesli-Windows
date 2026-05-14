# Roadmap

## Milestone 1: Dictation Vertical Slice

- Muesli-style dashboard shell.
- Global push-to-talk hotkey.
- Microphone capture from renderer.
- Local Whisper transcription through Python worker.
- Paste or clipboard output.
- Dictation history.

## Milestone 2: Muesli Data Model

- Replace JSON persistence with SQLite.
- Add dictations, meetings, meeting folders, templates, and app settings tables.
- Add JSON CLI compatible with the Muesli agent contract.

## Milestone 3: Meeting Transcription

- Manual meeting recording.
- Microphone capture.
- WASAPI loopback capture for system audio on Windows.
- VAD chunking.
- Transcript reconciliation.

## Milestone 4: Notes and Export

- OpenAI/OpenRouter meeting notes.
- Template selection.
- Markdown and PDF export.

## Explicit Non-Goals For MVP

- Personal Sanskrit/Indian-language seed packs.
- Laptop-specific CUDA assumptions.
- ChatGPT subscription OAuth.
- Camera-based meeting detection.
- Speaker diarization before the basic meeting pipeline is reliable.
