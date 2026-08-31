# Muesli macOS-to-Windows parity matrix

Audit date: 2026-08-01

Scope: authoritative inventory established in Phase 0 and updated through the bounded Phase 4 live-transcription implementation.

Windows target: `windows-native/Muesli.Windows` (`net8.0-windows`, WPF, x64).

macOS reference: local checkout of the macOS repo (`muesli-main`) at a developer-chosen path.

Product reference: internal product notes (not stored in this repository).

## How to read this document

This matrix is authoritative for the audited working tree, including its existing uncommitted changes. It supersedes feature-status statements in older roadmap and historical qualification documents where the code has moved on. The current operational model documentation is `WINDOWS_TRANSCRIPTION_MODELS.md`; older retained qualification reports may still describe the Parakeet-only build they tested.

The macOS implementation is the behavioral reference, not a library compatibility promise. CoreAudio process taps, ScreenCaptureKit, CoreML/ANE, EventKit, CloudKit, AppKit menu-bar APIs, macOS Accessibility/TCC, and Sparkle do not have drop-in WPF equivalents.

Status meanings:

| Status | Required evidence |
|---|---|
| **Complete and verified** | The Windows behavior exists, has proportionate automated tests, and has relevant qualification evidence. |
| **Implemented but insufficiently verified** | The behavior is present, but real-device, end-to-end, recovery, UI, packaging, or provider evidence is missing. |
| **Partial** | A useful subset exists, but material behavior or required resilience is absent. |
| **Missing** | No production Windows implementation was found. |
| **macOS-specific; Windows equivalent required** | The behavior matters, but the reference implementation depends on macOS-only facilities and must be redesigned with Windows APIs. |
| **Blocked by an external dependency or product decision** | Implementation cannot responsibly finish until the named external input is resolved. |
| **Explicitly out of scope** | The product or repository rules intentionally exclude it. |

“Implemented” never means “macOS parity.” A source file, button, or old benchmark is not sufficient verification by itself.

## Phase 0 baseline

Commands run against the audited working tree:

| Check | Result | Qualification boundary |
|---|---|---|
| `dotnet build windows-native\Muesli.Windows\Muesli.Windows.csproj --no-restore` | Passed; 0 warnings, 0 errors. | Debug source build only. |
| `dotnet test windows-native\Muesli.Windows.Tests\Muesli.Windows.Tests.csproj --no-restore` | Passed; 46 passed, 0 failed, 0 skipped. | One Windows test assembly; no live microphone, paste-target, calendar, cloud-provider, multi-model inference, installer, or GUI automation coverage. |
| `pwsh -NoProfile -File scripts\test-windows-package.ps1 -ZipPath artifacts\muesli-windows-0.2.0-win-x64.zip -SkipLaunch` | Passed; package structure accepted and packaged Sherpa CUDA runtime loaded. | Existing 278,498,918-byte artifact dated 2026-08-01 10:37:51; it was not rebuilt from this worktree. `-SkipLaunch` omitted packaged-app UI validation. The script still reports manual onboarding, model setup, shortcut/paste, and meeting-detection checks as required. |
| Existing `artifacts/qualification/phase5-cpu/release-qualification.json` | Historical qualification reports pass, unsigned package, WPF launch pass, deterministic 10-run Parakeet CPU stress. | Created 2026-07-31; useful retained evidence, not a fresh Phase 0 execution and not evidence for the newer model catalog. |
| Visible Debug WPF launch and render inspection | Passed; launched PID 16180, foregrounded the dashboard, visually inspected Dictations, Models, Settings/General, and Settings/Meetings, then restored Dictations in the foreground. | Phase 0 changed no UI. No microphone, dictation, meeting, download, provider, or destructive UI action was exercised. Models rendered Parakeet ready, cleanup disabled, and speaker labels needing verification, consistent with the classifications below. |
| Fresh log slice after launch marker | Passed; inspected only bytes appended after byte 53,555 in `muesli-2026-08-01.log`; no `ERROR`, `Unhandled UI exception`, or `XamlParseException`. | Slice shows Parakeet warmup on CUDA and explicitly reports speaker diarization “Needs verification”; it is launch/runtime evidence, not a transcript-quality test. |

The macOS test suite was inspected but was not run on Windows. Historical Windows qualification artifacts are cited only where their scope matches the current behavior.

## Phase 1 transcription-platform evidence

Phase 1 replaced the process-wide selector with persisted Dictation, Final meeting/import, and optional Live roles; Live remains explicitly Off. The source build passes with 0 warnings/errors and the complete Windows test project passes 56/56. `scripts/smoke-transcription-models.ps1 -Prepare -Runs 1` explicitly verified pinned archive/required-file integrity without activating recognizers, then completed real-audio CUDA inference for all seven catalog choices. Reports are retained under `artifacts/benchmarks/phase1-model-smoke/`. The rebuilt zip passed structure, native-runtime, fresh-machine, and visible-launch smoke. The rebuilt installer passed isolated install, real-audio packaged-runtime qualification, visible launch, uninstall, and user-data-preservation checks. The visible Debug UI rendered all seven cards, independent Dictation/Final selectors, Live Off, lifecycle actions, disk/status data, and the pinned model diagnostics dialog. The 6,438-byte log slice appended after the final launch marker contained no `ERROR`, `Unhandled UI exception`, or `XamlParseException`. The artifacts remain unsigned; signing is still decision D5/P8.

## Phase 2 dictation evidence

The Debug source build passes with zero errors (NuGet vulnerability-feed access emitted two environment/network warnings), and the complete Windows project passes 74/74 tests. The four Phase 2 PowerShell scripts parse successfully. Two fresh visible F8 silence exercises used the selected `parakeet-v3` role on CUDA, displayed `No speech detected`, added no history, left no fresh timestamped/merged WAV, and retained a 169,018-byte bounded benchmark alias. Settings/Dictation and Dictionary rendered the filler toggle, hands-free control, active-app delivery, and 0.90 threshold; the dashboard was restored foreground. A pre-existing real WAV produced the same 491-character deterministic output with recognizer reuse on explicit CPU (warm 1,821 ms, RTF 0.062) and CUDA (warm 1,694 ms, RTF 0.058). These smokes have no approved human reference, so WER/CER are intentionally null and do not qualify the required corpus. The final 4,626-byte rebuilt-launch slice contains no `ERROR`, `Unhandled UI exception`, `XamlParseException`, or dictation target-window-title field, and reserved Phase 2 crash-recovery temp count is zero. Physical route/disconnect/Bluetooth exercise, named-human corpus review, and Notepad/Chrome/Office/other-editor paste review remain required by `PHASE2_DICTATION_QUALIFICATION.md`; Phase 2 is therefore implemented but not fully qualified.

## Phase 3 meeting-session evidence

The Debug source build passes with zero errors (NuGet vulnerability-feed access emitted two environment/network warnings), and the complete Windows project passes 128/128 tests. The meeting-session qualification checker parses and fails closed when given non-human or incomplete evidence. A native CUDA pipeline smoke used two retained historical real-audio tracks with the selected `parakeet-v3` final-meeting role: two runs were deterministic, the recognizer was reused, the 55.89-second system track diarized to one speaker, and the warm run completed in 3,693 ms (RTF 0.066). This proves the separate-track finalization pipeline, not simultaneous capture or speaker-attribution quality. In the rebuilt visible app, manual capture entered Recording with the floating indicator, stopped into durable recovery, and—after a discovered header-only system track was excluded from native inference—recovered to a truthful **Needs attention** record with the 19-second microphone track retained. The recovered track and a historical system track both passed in-app play/pause/seek and exclusive file-reopen checks after leaving detail; rendered inspection also found and fixed the track selector's record-type label. The final fresh launch/recovery log slice contains none of `ERROR`, `Unhandled UI exception`, or `XamlParseException`, and no transcript assignment, window-title assignment, or WAV-path session diagnostic. Zoom/Teams/Meet, true process-target capture, Bluetooth/default-route/removal, suspend/resume, forced termination, disk-full, retention-off, and named-human transcript/separation qualification remain open; Phase 3 is therefore implemented but insufficiently verified.

## Parity matrix

### Models and model lifecycle

| ID | Capability | macOS reference | Windows evidence and gap | Status | Assigned phase |
|---|---|---|---|---|---|
| MOD-01 | Offline Parakeet TDT transcription with CPU/accelerator selection | `Models.swift`, `TranscriptionRuntime.swift`, `FluidAudioBackend.swift`; `ModelsTests.swift`, `BackendTests.swift`, `TranscriptionRuntimeTests.swift` | `NativeParakeetClient.cs`, `NativeSherpaRuntime.cs`, and the role-scoped `NativeTranscriptionClient.cs`; retained CPU qualification plus fresh real-audio CUDA inference and pinned file verification pass. | **Complete and verified** | — |
| MOD-02 | Multiple offline ASR models | `Models.swift`, `ModelsView.swift`, WhisperKit/FluidAudio/Qwen/Cohere/SenseVoice/Indic/Gemma backends; model/runtime tests | The approved Windows catalog is seven sherpa-onnx offline choices: Parakeet, Whisper Tiny/Small/Medium English, SenseVoice Small, Qwen3-ASR 0.6B, and Cohere. Every entry passed pinned required-file verification and real-audio inference. Unsupported macOS CoreML/LiteRT choices are absent. | **Complete and verified** | — |
| MOD-03 | Background download, progress, cancel, delete, switch, update, and safe cache recovery | `ModelsView.swift`, `Models.swift`; `ModelsTests.swift`, the artifact-filter suite in `TranscriptionRuntimeTests.swift` | `TranscriptionModelLifecycleService.cs` and Models UI provide independent prepare/cancel/retry/verify/delete, disk size, statuses, diagnostics, serialized per-model/cross-process recovery, and sequential prepare-all without recognizer activation. Tests cover success, failure, cancellation, retry, concurrency, delete failure, routing, and safe disposal. Catalog artifacts are pinned rather than mutable “update” aliases. | **Complete and verified** | — |
| MOD-04 | Explicitly downloaded, opt-in live models: Nemotron 3.5 and Parakeet Realtime EOU | `Models.swift`, `ModelsView.swift`, `MeetingStreamingPartialSession.swift`; `NemotronStreamingTests.swift`, `MeetingStreamingPartialSessionTests.swift` | `StreamingModelPlatform.cs` exposes only the runnable multilingual Nemotron 3.5 sherpa-onnx artifact, with pinned archive/required-file/Silero hashes and independent lifecycle. Preparing never selects. CoreML Parakeet EOU is deliberately absent. Re-qualified 2026-08-01 from an empty cache: pinned download plus checksum verification in 78 s (685 MB installed), then real `ar.wav` decoding, real Silero boundaries, and a full-session run through the DLLs this build emits at CPU real-time factor 0.42. Long hardware/thermal qualification remains. | **Implemented but insufficiently verified** | P4 |
| MOD-05 | Optional local Qwen transcript cleanup model lifecycle | `TranscriptCleanupClient.swift`, `TranscriptCleanupPromptsManagerView.swift`; cleanup tests | `NativeTextCleanupService.cs` uses LLamaSharp only when enabled and a manually placed GGUF is present; failure returns raw text with disclosure. No guided download, managed version/hash, cancellation UI, or qualification. | **Partial** | P1 |
| MOD-06 | No silent transcription-engine fallback | Explicit model/runtime policies and tests | Strict role routing rejects unknown IDs, requires the selected model to be verified, and safely disposes the prior recognizer. CUDA/CPU changes stay inside the same engine/model. No Python or alternate hidden engine is present. | **Complete and verified** | — |

### Dictation, paste, hotkeys, microphone, audio routing, and text cleanup

| ID | Capability | macOS reference | Windows evidence and gap | Status | Assigned phase |
|---|---|---|---|---|---|
| DIC-01 | Hold-to-talk capture, local transcription, persistence | `AppScopedDictationRecorder.swift`, `DictationStore.swift`, dictation views; recorder/store tests | `DictationHotkeyStateMachine.cs`, `DictationCoordinator.cs`, `AudioCaptureService.cs`, `TranscriptionPipelineService.cs`, and `AppDataStore.cs` implement explicit capture, no-speech, cancellation, cleanup, and atomic history. Unit policy/persistence tests pass; the human microphone corpus is still required. | **Implemented but insufficiently verified** | P2 |
| DIC-02 | Paste at the original cursor with clipboard restoration and failure disclosure | macOS hotkey/paste path and Accessibility handling; hotkey/paste tests | `ActiveAppPasteService.cs` restores the original process/window handle, sends Ctrl+V, conditionally restores prior clipboard data, and supports an explicit second clipboard fallback. A delivered transcript is always added to history even when both paste and clipboard fail. Unit race/failure tests pass; four-app human qualification remains open. | **Implemented but insufficiently verified** | P2 |
| DIC-03 | Dictation history, copy, delete, date filter, and search | `DictationsView.swift`, `DictationRowView.swift`, `DictationStore.swift`; `DictationStoreTests.swift` | WPF implements copy/delete/filter/search and `AppDataStore.cs` atomically persists records; explicit deletion purges recoverable artifacts and recovery tests pass. Dashboard-level copy/filter/search exercise remains manual. | **Implemented but insufficiently verified** | P2 |
| HOT-01 | Configurable hold hotkey with exact modifier semantics | `HotkeyMonitor.swift`, `ShortcutHotkeyPolicy.swift`; shortcut/hotkey tests | `GlobalHotkeyService.cs` suppresses the configured trigger, reserves Escape for active cancellation, and `HotkeyGesture.cs` supports exact Win/Ctrl/Alt/Shift combinations. Parser/ABI tests pass; layout/elevation/manual qualification remains. | **Implemented but insufficiently verified** | P2 |
| HOT-02 | Double-tap hands-free recording with deterministic stop/cancel behavior | hotkey controller/policy and dictation tests | `DictationHotkeyStateMachine.cs` owns hold, second-tap window, hands-free, third-tap stop, timeout, and reset transitions; focused tests cover hold, lock, late stop, repeat, and cancellation reset. Real global-hook exercise remains. | **Implemented but insufficiently verified** | P2 |
| AUD-01 | Microphone enumeration, selection, fallback, and shared-mode capture | `AudioRouteController.swift`, route-aware recorders, audio session manager; route/session tests | `AudioCaptureService.cs` enumerates active capture endpoints, persists System default or an explicit device, and discloses unavailable-device fallback. Real device/privacy matrices remain open. | **Implemented but insufficiently verified** | P2 |
| AUD-02 | Route changes, Bluetooth/default-device changes, device removal, and recovery while recording | `RouteAwareDictationRecorder.swift`, `DictationAudioSessionManager.swift`, route-aware meeting recorder; route tests | Dictation and meeting capture consume `IMMNotificationClient` endpoint callbacks, rotate affected channels into finalized parts, and apply bounded restart policies. Meetings independently monitor mic/render health and preserve recoverable parts when both channels fail. Policy tests pass; physical default/unplug/Bluetooth qualification remains open. | **Implemented but insufficiently verified** | P2/P3 |
| AUD-03 | Media pause and audio ducking around dictation/meeting capture | `MediaPlaybackController.swift`, `AudioDuckingController.swift`; controller tests | No Windows implementation. Pause versus duck versus non-interference requires a product decision and was not part of the bounded Phase 2 request. | **Blocked by an external dependency or product decision** | Decision, then later phase |
| TXT-01 | Deterministic filler/disfluency removal | `FillerWordFilter.swift`; `FillerWordFilterTests.swift` | `FillerWordFilter.cs` removes the macOS filler/phrase set deterministically; settings schema 3 persists an on/off control and focused golden tests cover semantic `like`. Human corpus verification remains. | **Implemented but insufficiently verified** | P2 |
| TXT-02 | Personal dictionary: words, phrases, replacements, Jaro-Winkler correction | `CustomWordMatcher.swift`, dictionary correction detector; `CustomWordMatcherTests.swift`, `DictionaryCorrectionDetectorTests.swift` | `DictionaryCorrectionService.cs` uses longest-first token windows, punctuation preservation, per-token Jaro-Winkler, exact short words, one-character length bounds, and conservative persisted thresholds. Focused false-positive/phrase tests pass; human name/accent corpus remains. | **Implemented but insufficiently verified** | P2 |
| TXT-03 | Punctuation/segment formatting and cleanup ordering shared across dictation, import, and meetings | `TranscriptFormatter.swift`, cleanup pipeline; formatter/cleanup tests | Dictation now orders optional filler removal before dictionary correction and deliberately skips generative cleanup for paste latency. Cross-path meeting/import equivalence was explicitly outside this bounded dictation phase and remains partial. | **Partial** | Later cross-path phase |

### Meeting capture, live transcription, ownership, and recovery

| ID | Capability | macOS reference | Windows evidence and gap | Status | Assigned phase |
|---|---|---|---|---|---|
| MTG-01 | Simultaneous mic (“You”) and system (“Others”) capture with retained local audio | `MeetingSession.swift`, `MeetingMicrophoneRecorder.swift`, `CoreAudioSystemRecorder.swift`, recording writer; recording/session tests | `MeetingRecordingCoordinator.cs`, `AudioCaptureService.cs`, and `SystemAudioCaptureService.cs` record separate tracks concurrently. Detected meetings attempt Windows process-tree loopback; unsupported/failed targeting produces an explicit endpoint-loopback warning. Per-track health, route rotation, and ownership are implemented, but real Zoom/Teams/Meet/Bluetooth separation is not freshly qualified. | **Implemented but insufficiently verified** | P3 |
| MTG-02 | Suspend/resume, cancellation, shutdown, crash/interruption recovery, and repair of in-progress meetings | `MeetingResumePolicy.swift`, `MeetingMicHealthTracker.swift`, `MeetingMicRepair.swift`, `MeetingTerminationPolicy.swift`; corresponding tests | `MeetingSessionStateMachine.cs`, the coordinator, and `MeetingSessionJournalStore.cs` provide all ten explicit states, bounded channel repair, power suspend/resume, shutdown checkpoints, startup discovery, interrupted-WAV repair, explicit recovery, cancellation, and truthful terminal states. Transition/policy/recovery tests pass; physical suspend, forced process termination, disk-full, and device-failure qualification remain open. | **Implemented but insufficiently verified** | P3 |
| LIVE-01 | Live meeting transcription and floating live transcript | `MeetingStreamingPartialSession.swift`, `LiveTranscriptView.swift`, `FloatingMeetingTranscriptPanel.swift`; streaming tests | `MeetingLiveTranscriptionSession.cs` consumes both capture streams off-dispatcher and `MeetingLiveTranscriptWindow.xaml(.cs)` renders committed and provisional tails. Dropped packets are recorded without any snapshot copy or dispatcher hop on the WASAPI callback thread, and provisional/level publication is coalesced to 200 ms while commits and finalization publish immediately. Journal checkpoints run on a coalesced background flush under the coordinator gate, never on the inference worker. Real inference and a full-session run passed; a physical simultaneous meeting/UI soak remains. | **Implemented but insufficiently verified** | P4 |
| LIVE-02 | VAD-driven natural-boundary chunk rotation | `StreamingVadController.swift`, `PCMChunkRecorder.swift`; `StreamingVadControllerTests.swift`, `PCMChunkRecorderTests.swift` | Native Silero produces speech-end boundaries. No fixed-duration rotation exists; `MaxSpeechDuration=0`. Boundary, tail, and real-VAD tests pass. Physical noisy-room qualification remains. | **Implemented but insufficiently verified** | P4 |
| LIVE-03 | Explicit final transcript ownership modes | `MeetingSession.swift`, `Models.swift`, streaming partial session; model/session tests | Settings/models show live-preview, final-transcript, and gap-recovery owners. Preview-only keeps the offline final owner; unified persists Nemotron as owner and uses the selected offline model only for measured gaps. Settings schema 4, journal schema 2, and completed-meeting schema 3 preserve the choice and migration tests pass. | **Complete and verified** | — |
| LIVE-04 | Gap detection, recovery transcription, deduplication, and final transcript reconciliation | `MeetingTranscriptHealthMonitor.swift`, `TranscriptReconciler.swift`, `MeetingChunkTimingTracker.swift`; health/reconciler tests | Bounded pressure and engine/interruption gaps persist with sample ranges and coalesce contiguous drops; gap clips alone route to the configured recovery model and overlapping identical text is suppressed. Measured-gap ranges are 16 kHz live-stream indices and are now rescaled to the retained track's native capture rate before clipping, so recovery reads the correct audio on non-16 kHz devices. Deterministic tests cover routing, dedupe, cancellation, and rate mapping; injected native crash during a physical meeting remains open. | **Implemented but insufficiently verified** | P4 |
| DIA-01 | Offline remote-speaker diarization and “You” attribution | FluidAudio diarizer/runtime policy; `DiarizerRuntimePolicyTests.swift`, import/session tests | `NativeDiarizationClient.cs` downloads ONNX models and diarizes system audio; `MeetingRecordingCoordinator.cs` labels mic as You and merges segments. Unit tests cover cleanup/ownership but no fresh multi-speaker quality or CPU/CUDA run. | **Implemented but insufficiently verified** | P3 |
| DIA-02 | Speaker rename/alias persistence and consistent application to transcript, notes, copy, and export | meeting detail/store views and navigation tests | `SpeakerAliasService.cs` and WPF commands persist and apply aliases, but the flow lacks comprehensive persistence/export/UI tests. | **Partial** | P3 |
| MTG-03 | Edit title, transcript, generated notes, and manual notes; retranscribe or re-summarize safely | `MeetingDetailView.swift`, `MeetingNotesView.swift`, store/navigation tests | Windows can regenerate summary and change speaker aliases, but title/transcript/manual-note editing and retranscription are absent. | **Partial** | P3 |

### Meeting detection, calendar, and Join & Record

| ID | Capability | macOS reference | Windows evidence and gap | Status | Assigned phase |
|---|---|---|---|---|---|
| DET-01 | Meeting detection using app/window plus mic/camera/media evidence with false-positive control | `MeetingDetector.swift`, `MeetingMonitor.swift`, `MeetingCandidateResolver.swift`, browser/media collectors; detector/candidate/browser tests | `MeetingDetectionService.cs` polls foreground/visible windows and browser UI Automation for Zoom, Teams, Meet, Webex, and titles. It has no mic/camera sensor correlation and no automated detector tests. | **Partial** | P5 |
| DET-02 | Detection lifecycle: prompt dedupe, suppress/dismiss, auto-stop when meeting ends, and recovery | notification controller, auto-stop/termination/signal policies; corresponding tests | `MeetingPromptService.cs` dedupes basic prompts and opens/cancels, but auto-stop and robust signal-state recovery are absent. | **Partial** | P5 |
| CAL-01 | Google Calendar auth, upcoming window/range, refresh, dismissal/pruning, and countdown | `GoogleCalendarAuthManager.swift`, `GoogleCalendarClient.swift`, `CalendarMonitor.swift`, `UpcomingMeetingsWindow.swift`; Google/disabled/upcoming/scheduled tests | No Windows calendar implementation or persistent calendar data/settings. | **Missing** | P5 |
| JOIN-01 | Extract platform URLs and offer Join & Record, Join Only, and Record Only with platform icons | `MeetingNotificationController.swift`, prompt state machine, start presentation; notification/prompt tests | Browser-detected URL prompts can Join & Record or Join Only. Calendar URLs, Record Only, full platform parsing, dismiss/prune semantics, and test coverage are absent. | **Partial** | P5 |
| API-01 | CoreAudio process tap/ScreenCaptureKit capture | CoreAudio/system-audio and source-window files/tests | Apple APIs cannot be ported literally. `WindowsProcessLoopbackCapture.cs` uses Windows process-tree loopback on build 20348+ when a detected live PID is available; otherwise `SystemAudioCaptureService.cs` explicitly identifies render-endpoint fallback and its unrelated-audio risk. Real conferencing-app attribution remains unqualified. | **Implemented but insufficiently verified** | P3 |
| API-02 | EventKit change notifications | Calendar monitor and scheduled notification tests | No EventKit on Windows. Google Calendar must use explicit OAuth plus REST incremental sync/polling or push infrastructure; Outlook/MAPI is not an assumed substitute. | **macOS-specific; Windows equivalent required** | P5 |

### Summaries, authentication, templates, notes, and organization

| ID | Capability | macOS reference | Windows evidence and gap | Status | Assigned phase |
|---|---|---|---|---|---|
| SUM-01 | Local deterministic summary plus explicit OpenAI/OpenRouter network providers with timeout/cancel/failure disclosure | `MeetingSummaryClient.swift`; `MeetingSummaryClientTests.swift` | `MeetingSummaryService.cs` implements local, OpenAI, and OpenRouter; cloud use is explicit, caller cancellation and timeout are handled, and failure visibly falls back to local. Tests cover success/fallback/timeout/cancel. Live credentials/network were not exercised. | **Implemented but insufficiently verified** | P6 |
| SUM-02 | Local Ollama and LM Studio/custom HTTP provider | meeting summary client/provider tests | No Windows provider implementation. | **Missing** | P6 |
| SUM-03 | ChatGPT Plus/Pro browser OAuth (PKCE), refresh/revoke, protected tokens | `ChatGPTAuthManager.swift`, ChatGPT responses client; `ChatGPTAuthTests.swift` | No implementation. A stable supported OAuth/backend contract and security review are required before work starts. | **Blocked by an external dependency or product decision** | Decision D1, then P6 |
| SUM-04 | Provider-based automatic meeting title generation | summary client and meeting title tests | Windows normally derives a title from detected/file name and does not expose a separately tested provider-title lifecycle. | **Partial** | P6 |
| SEC-01 | API-key storage and plaintext-settings migration | macOS keychain/auth storage tests | `SecretStore.cs` uses Windows Credential Manager; `SettingsStore.cs` migrates legacy plaintext provider keys, and `SecretsAndSettingsTests.cs` covers migration and clearing. | **Complete and verified** | — |
| TPL-01 | Built-in/custom meeting templates and re-summary with a selected template | `MeetingTemplates.swift`, templates manager; summary/navigation tests | Built-in/custom template CRUD and selected-template regeneration exist in WPF/AppDataStore. There are no focused template persistence, malformed-data, or UI tests. | **Implemented but insufficiently verified** | P6 |
| NOTE-01 | Manual notes persisted separately from generated summary | `MeetingDetailView.swift`, `MeetingNotesView.swift`, `MarkdownRichTextEditor.swift`; store/navigation tests | `PersistedMeeting` has summary/transcript but no manual-notes field or editor. | **Missing** | P3 |
| ORG-01 | Nested meeting folders with rename/reorder/move/delete persistence | meetings views/store; `DictationStoreTests.swift`, `MeetingsNavigationTests.swift` | Windows supports one-level folders, rename/reorder/move/delete. Parent/child nesting and focused recovery tests are absent. | **Partial** | P3 |
| SEARCH-01 | Search dictations and meetings by relevant text, with date filters | `SearchResultsView.swift`, store/views tests | WPF searches dictation text/model and meeting title/summary/transcript/metadata and supports date filtering. Manual notes cannot be indexed because they do not exist; no search UI tests. | **Implemented but insufficiently verified** | P3 |

### Import, playback, export, hooks, and automation

| ID | Capability | macOS reference | Windows evidence and gap | Status | Assigned phase |
|---|---|---|---|---|---|
| IMP-01 | Import supported media for transcription, diarization, title, summary, and history | `AudioFileImportController.swift`; `AudioFileImportControllerTests.swift` | WPF advertises WAV/MP3/M4A/AAC/MP4/MOV/MKV/WebM/OGG and transcribes with the selected ASR. Retained evidence qualifies only local WAV/WebM; import diarization, cancellation recovery, and the other advertised formats are unproven or absent. | **Partial** | P3 |
| PLAY-01 | In-app meeting playback, seek, waveform, and correct track selection | `MeetingRecordingPlayerView.swift`, media controller; `MediaPlaybackControllerTests.swift` | `MeetingRecordingPlaybackService.cs` and the meeting detail view provide in-process microphone/system track selection, play/pause, seek, time state, and deterministic close on switch/refresh/shutdown. Track-selection tests pass. Waveform rendering and physical output-device-loss qualification are absent. | **Partial** | Future content/UI phase |
| EXP-01 | Export notes/transcript/full meeting as PDF or Markdown and auto-open | `MeetingExporter.swift`; `MeetingExporterTests.swift` | `MeetingExporter.cs` supports Markdown/PDF modes, aliases, and auto-open. No export unit/golden-page tests or fresh manual export were run; QuestPDF community-license eligibility remains a release check. | **Implemented but insufficiently verified** | P3 |
| HOOK-01 | Post-meeting executable hook with JSON stdin, timeout, cancellation/process-tree termination, and logs | `MeetingHookRunner.swift`; `MeetingHookRunnerTests.swift`, `MeetingHookIntegrationTests.swift` | `PostMeetingAutomationService.cs` runs an explicit user-selected `.exe` without a shell, sends a documented schema-v1 JSON stdin payload, bounds and redacts output, captures exit state, uses timeout/cancellation plus a kill-on-close Job Object, retries nonzero failures, and persists per-meeting diagnostics only after durable completion. Phase 9 tests cover disabled/missing/hostile Unicode paths, payload policy, timeout, cancellation/shutdown, crash, oversized output, descendant termination, parent-exit inherited pipes, and completion gating. | **Implemented and verified** | P6 |
| AUTO-01 | Automatic Markdown/PDF export with atomic collision-safe naming | `MeetingMarkdownAutoExporter.swift`; `MeetingMarkdownAutoExporterTests.swift` | Optional Markdown export supports Notes, Transcript, or Full Meeting into an explicit user-owned destination. Exclusive atomic publication preserves collisions; hashed claims and crash-recoverable manifests provide concurrency/restart idempotency. PDF auto-export is not included in this requested slice. | **Markdown implemented and verified; PDF missing** | P6 |
| FOLLOW-01 | Configurable post-meeting follow-up/linked workflow | `MeetingFollowUpPolicy.swift`; `MeetingFollowUpTests.swift` | No Windows implementation. | **Missing** | P6 |

### Computer Use and cross-device sync

| ID | Capability | macOS reference | Windows evidence and gap | Status | Assigned phase |
|---|---|---|---|---|---|
| CU-01 | Optional voice-driven Computer Use planner/executor with bounded tools, observation, timeout, trace, and cancellation | `ComputerUsePlannerClient.swift`, `ComputerUsePlannerRuntime.swift`, `ComputerUseExecutor.swift`, `ComputerUseToolRegistry.swift`, browser automation/observation/overlay; planner/executor tests | Windows implements a separately activated, disabled-by-default OpenAI planner with one-use voice provenance, strict schema validation, re-observe/replan execution, UI Automation allowlists, loopback origin-pinned DevTools browser actions, local minimum-risk confirmation, bounded timeout/action counts, stop/shutdown cancellation, visible status/action coordinates, and a structurally redacted bounded trace. Window/page text and screenshots remain deliberately unavailable pending masking qualification. | **Implemented with privacy-scoped observation** | P7A / Phase 10 |
| SYNC-01 | Private text sync and iPhone bridge with conflicts, tombstones, origin, and no audio | `MuesliICloudSyncEngine.swift`, `IPhoneBridgeLinks.swift`, `SyncOriginDisplay.swift`, `DictationStore.swift`; iCloud callback, bridge identity, store sync tests | CloudKit/iCloud cannot be assumed on Windows and no backend-agnostic Windows implementation exists. | **macOS-specific; Windows equivalent required** | Decision D3, then P7 |
| SYNC-02 | Sync audio recordings | Product reference explicitly states audio is never synced. | No Windows implementation, by design. | **Explicitly out of scope** | — |

### Onboarding, tray, floating surfaces, and dashboard shell

| ID | Capability | macOS reference | Windows evidence and gap | Status | Assigned phase |
|---|---|---|---|---|---|
| ONB-01 | Resumable first-launch flow with model, real OS permissions, hotkey, complete dictation test, and optional summary setup | `OnboardingFlow.swift`, `OnboardingProgress.swift`, onboarding views/controller; onboarding tests | A WPF onboarding window configures name, mic, hotkey/hands-free, model prepare, live dictation test, startup, indicator, and summary. Draft settings save on changes, but only a completion flag models progress; Windows mic/privacy and paste-target permissions are not explicit gates; there are no flow/resume tests. | **Partial** | P2 |
| TRAY-01 | Rich status/tray menu for open, record state, recent/upcoming items, settings, update, and quit | `StatusBarController.swift`, recent/upcoming controllers; QoL/upcoming tests | `TrayIconService.cs` offers Open Muesli and Quit with double-click open. Recording/calendar/recent/update state is absent. | **Partial** | P8 |
| FLOAT-01 | Draggable floating recording/transcribing indicator with live level waveform, accent/position, click-to-stop/cancel | `FloatingIndicatorController.swift`; appearance/QoL tests | `ToastNotificationService.cs` has idle/recording/transcribing/success/error states, deterministic disposal, drag position, stop/cancel actions, and throttled peak levels from WASAPI instead of fake animation. Manual DPI/multi-monitor/click qualification remains. | **Implemented but insufficiently verified** | P2 |
| FLOAT-02 | Optional waveform-hover live meeting preview | floating meeting transcript/live transcript views and tests | `MeetingLiveTranscriptWindow.xaml(.cs)` shows microphone/system level bars on pointer enter when `ShowLiveWaveformOnHover` is set; the setting is off by default and persisted in settings schema 4. Levels come from measured capture peaks, not animation. Manual DPI/multi-monitor hover review remains. | **Implemented but insufficiently verified** | P4 |
| SHELL-01 | Light/dark adaptive WPF dashboard and core navigation | SwiftUI dashboard/appearance code and tests | `App.xaml`, `MainWindow.xaml(.cs)` implement themes, sidebar pages, history, meetings, dictionary, models, settings, and about. This audit will visually inspect the rebuilt dashboard, but no automated accessibility/layout/DPI test suite exists. | **Implemented but insufficiently verified** | P8 |
| START-01 | Launch at login with state refresh | macOS login item settings/approval behavior | `StartupRegistrationService.cs` uses the current-user Run key and WPF settings. No installer/uninstaller/elevation/state-refresh qualification. | **Implemented but insufficiently verified** | P8 |
| INSTANCE-01 | Single-instance activation and shutdown ownership | app lifecycle/reference behavior | `SingleInstanceCoordinator.cs`, `App.xaml.cs`; `ModelAndSingleInstanceTests.cs` covers acquire/deny/reacquire. Visible second-instance activation is not manually exercised, but the operational ownership contract is tested. | **Complete and verified** | — |
| INSIGHT-01 | Insights/analyzer and optional contribution/share flow | `InsightsView.swift`, `InsightsShareView.swift`, `InsightsWordAnalyzer.swift`; `InsightsTests.swift` | Windows has dashboard count/time-saved cards but no equivalent analyzer or contribution flow. | **Partial** | Decision D4, then P8 |
| SOUND-01 | Configurable user feedback sounds | macOS sound/appearance settings | No dedicated Windows sound feedback service or settings were found. | **Missing** | P8 |

### Diagnostics, privacy, updates, signing, packaging, tests and qualification evidence

| ID | Capability | macOS reference | Windows evidence and gap | Status | Assigned phase |
|---|---|---|---|---|---|
| DIAG-01 | Redacted logs, runtime status, support bundle/incident guidance, cleanup, and actionable failures | `DiagnosticIncident*.swift`, diagnostic catalog/reporter; `DiagnosticIncidentTests.swift` | `AppLogService.cs`, role-aware `RuntimeDiagnosticsService.cs`, per-model diagnostics, capture cleanup, and headless qualification exist; redaction/runtime tests pass. Runtime/readiness reporting now names Dictation and Final meeting independently and keeps Live explicitly Off. There is still no structured incident/support-bundle export UI. | **Partial** | P8 |
| PRIV-01 | Local-first audio/transcription, explicit network summaries, secret exclusion, deletion/retention controls, accurate disclosure | privacy/auth/storage behavior | Runtime is local-first; model acquisition is explicit; summary network use is explicit; tests cover log redaction, transient cleanup, recording retention, secret migration, and model lifecycle deletion/failure paths. `WINDOWS_PRIVACY.md` and the model documentation describe the seven-model download behavior. Destructive deletion was fault-tested rather than exercised against the user's prepared model caches. | **Implemented but insufficiently verified** | Every phase/P8 |
| UPD-01 | Signed automatic update check/download/install with failure guidance | Sparkle integration, appcast scripts, `UpdateFailureGuidanceTests.swift` | About opens GitHub Releases. No update service, signed feed, rollback, or update failure UI exists. | **Missing** | P8 |
| SIGN-01 | Authenticode-signed executable and installer with timestamp | macOS codesign/notarization/release scripts | `sign-windows-release.ps1` exists, but the current qualification explicitly reports `NotSigned`. A production code-signing certificate and secure signing environment are external dependencies. | **Blocked by an external dependency or product decision** | Decision D5, then P8 |
| PKG-01 | Self-contained x64 zip and Inno Setup installer/uninstaller preserving user data by default | macOS build/sign/notarize/DMG/release/update verification scripts | The rebuilt 0.2.0 zip passed structure, allow/deny, native CUDA runtime, fresh-machine, and visible-launch smoke. The rebuilt Inno installer passed isolated current-user install, packaged real-audio runtime qualification, visible launch, uninstall, and preservation of `%APPDATA%\muesli`. The artifacts remain unsigned and upgrade/clean-VM gates remain for P8. | **Partial** | P8 |
| TEST-01 | Automated success, failure, cancellation, persistence, recovery, UI, and packaging coverage | Broad Swift test suite spanning the listed services, plus release scripts | Windows has 195 passing tests through the finalization pipeline. Finalization coverage adds cross-channel timestamp normalization (late start, mid-meeting restart, clamped/missing anchors), chronological merge, one-local-plus-one-remote, multiple remote speakers, first-appearance speaker numbering, greatest-overlap attribution, silence, missing microphone, missing system audio, both channels missing, diarization failure fallback, merge determinism, time-bounded duplicate removal, speaker-prefix preservation through dictionary and cleanup, alias boundaries, journal schema 3 migration, and a gated real multi-speaker diarization qualification. New coverage includes streaming catalog/settings, pinned-file corruption/cancellation, PCM normalization, VAD commits, grouped-boundary commits, provisional state, publication coalescing, capture-thread drop isolation, bounded drop-range retention, dedupe, cancellation/disposal/shutdown, ownership-descriptor projection, gap-recovery routing/dedupe/cancellation/sample-rate mapping, journal and completed-record ownership persistence, and a gated real-model/real-VAD/full-session qualification. UI, physical hardware, long-session, provider, and later-phase workflows still lack complete automation. | **Partial** | Every phase |
| QUAL-01 | Repeatable CPU/CUDA, microphone/paste target, meeting apps, media formats, diarization, installer, shutdown, and fresh-log gates | macOS dev/canary/release/update scripts | Windows benchmark/meeting/media/package/release scripts and retained artifacts exist. Phase 1 freshly verified and inferred real audio with all seven catalog models on CUDA, then passed rebuilt zip and isolated installer runtime/launch/uninstall gates. CPU-only seven-family, live microphone/paste-target, broader hardware, meeting-app, provider, update, signing, and clean-VM gates remain assigned to their owning phases. | **Partial** | Every phase/P8 |
| API-03 | macOS TCC/Accessibility handoff and permissions | onboarding/permission/reference tests | Windows must use Windows microphone privacy settings, integrity/UIPI-aware paste diagnostics, and startup/elevation behavior; macOS permission APIs cannot be reused. | **macOS-specific; Windows equivalent required** | P2 |
| API-04 | Sparkle/AppKit menu bar, codesign/notarization | updater/status-bar/release scripts | Windows equivalents are a WPF/NotifyIcon surface, Authenticode, installer signing, and a separately designed signed update channel. | **macOS-specific; Windows equivalent required** | P8 |
| OOS-01 | Python worker/runtime, Electron fallback, or web wrapper | None in the native reference architecture. | Repository rules prohibit these architectures. | **Explicitly out of scope** | — |
| OOS-02 | Silent fallback between transcription engines or fake success/data | Product behavior requires explicit ownership and local-first operation. | Repository rules prohibit these behaviors. | **Explicitly out of scope** | — |
| OOS-03 | Literal reuse of macOS frameworks/models on Windows | macOS implementation uses Apple-only frameworks and model formats. | Only behaviorally equivalent Windows-native designs are in scope. | **Explicitly out of scope** | — |

## Actionable parity backlog for every Partial or Missing item

This section is the implementation contract for all matrix rows marked **Partial** or **Missing**. Rows marked “implemented but insufficiently verified” are governed by the verification debt section after this backlog. Blocked and platform-equivalent items are governed by the decision/equivalence ledgers.

### P1 — runtime truth, models, diagnostics, and disclosure

#### MOD-02 — multiple offline ASR models

- **macOS files/tests:** `Models.swift`, `ModelsView.swift`, `TranscriptionRuntime.swift`; `ModelsTests.swift`, `BackendTests.swift`, `TranscriptionRuntimeTests.swift`.
- **Windows files:** `TranscriptionModelCatalog.cs`, `NativeOfflineAsrClient.cs`, `NativeParakeetClient.cs`, `NativeTranscriptionClient.cs`, `NativeSherpaRuntime.cs`, model settings in `MainWindow.xaml(.cs)`, `ModelAndSingleInstanceTests.cs`.
- **Windows-native design:** retain an explicit model-to-runtime adapter registry; expose each model’s language/runtime/size/final-owner capabilities; never substitute another model; keep inference off the dispatcher.
- **Dependencies:** redistributable Sherpa/ONNX artifacts and hashes; model licenses; supported CPU/CUDA operators; product decision on which macOS models are promised on Windows.
- **Acceptance:** every advertised model downloads atomically, verifies, transcribes a fixed corpus on CPU and eligible CUDA, reports its actual engine/model/provider, cancels cleanly, recovers from corrupt/interrupted cache, and passes first-run/cached-run gates with no fallback.
- **Phase:** P1.

#### MOD-03 — complete model lifecycle

- **macOS files/tests:** `ModelsView.swift`, `Models.swift`; `ModelsTests.swift`, artifact-filter tests.
- **Windows files:** model clients/catalog, `SafeArchiveExtractor.cs`, `ModelSetupArtifactCleaner.cs`, model UI in `MainWindow.xaml(.cs)`.
- **Windows-native design:** a focused model-operation state machine per model (`NotInstalled`, `Downloading`, `Verifying`, `Ready`, `Failed`, `Deleting`) with progress, cancellation token ownership, retry, per-model delete/update, shutdown disposal, and atomic staging.
- **Dependencies:** stable cache schema and free-space policy.
- **Acceptance:** unit tests cover success, HTTP/hash/archive failure, cancellation, concurrent prepare, delete-in-use, restart recovery, and stale staging cleanup; UI exposes only working actions and remains responsive.
- **Phase:** P1.

#### MOD-04, LIVE-01, FLOAT-02 — live model lifecycle, transcription, and preview

- **macOS files/tests:** `Models.swift`, `MeetingStreamingPartialSession.swift`, `LiveTranscriptView.swift`, `FloatingMeetingTranscriptPanel.swift`; `NemotronStreamingTests.swift`, `MeetingStreamingPartialSessionTests.swift`.
- **Windows files:** `StreamingModelPlatform.cs`, `MeetingLiveTranscriptionSession.cs`, `StreamingPcmNormalizer.cs`, `MeetingGapRecoveryService.cs`, `MeetingLiveTranscriptWindow.xaml(.cs)`, capture/coordinator integration, settings/journal migrations, and `Phase4LiveTranscriptionTests.cs`.
- **Windows-native design:** separate live-model adapters and an explicit streaming session state machine fed from shared WASAPI capture buffers; bounded background inference; revisioned partial/final events; independent UI subscriber; live transcription disabled by default and model download not equal to activation.
- **Dependencies:** Windows-compatible Nemotron 3.5 and Parakeet Realtime EOU runtimes/artifacts/licenses and realistic hardware targets.
- **Acceptance:** both product modes meet language/ownership rules, cancellation/shutdown leave no native resources, live failures do not corrupt durable final capture, UI stays responsive, and long-session/route-change tests pass.
- **Phase:** P4.

#### MOD-05 — managed local cleanup model

- **macOS files/tests:** `TranscriptCleanupClient.swift`, cleanup prompt settings/tests.
- **Windows files:** `NativeTextCleanupService.cs`, cleanup settings/cache UI in `MainWindow.xaml(.cs)`.
- **Windows-native design:** explicit managed GGUF catalog/download with hash, progress/cancel/delete; serialized background LLamaSharp requests; raw-transcript preservation; no summary/transcription-engine fallback.
- **Dependencies:** approved GGUF, license, size/quality targets.
- **Acceptance:** lifecycle and timeout/cancel/OOM/corrupt-model tests; qualification proves dispatcher responsiveness and raw text survives every failure.
- **Phase:** P1.

#### DIAG-01, PRIV-01 — accurate diagnostics and privacy

- **macOS files/tests:** `DiagnosticIncident*.swift`, `DiagnosticErrorCatalog.swift`, telemetry/privacy configuration; diagnostic/telemetry tests.
- **Windows files:** `AppLogService.cs`, `RuntimeDiagnosticsService.cs`, `RuntimeStatusMapper.cs`, `CaptureStorageService.cs`, `SecretStore.cs`, `WINDOWS_PRIVACY.md`, capture/privacy tests.
- **Windows-native design:** per-selected-model runtime diagnostics, redacted support bundle with user preview, bounded log retention, explicit network-provider display, and tested deletion inventory. No secrets/transcript/title/URL leakage.
- **Dependencies:** support-bundle product format and telemetry decision; no telemetry is introduced by default.
- **Acceptance:** docs match every model/network path; redaction adversarial tests pass; selected-model diagnostics are truthful; delete/retain/backup/quarantine behavior is exercised; support output contains no credential or raw sensitive content.
- **Phase:** P1 for truth fixes, P8 for support UX.

### P2 — dictation reliability, routing, cleanup, and onboarding

#### HOT-02 — hands-free state machine

- **macOS files/tests:** hotkey monitor/policy and dictation controller tests.
- **Windows files:** double-tap/timer logic in `MainWindow.xaml.cs`, `GlobalHotkeyService.cs`, `DictationCoordinator.cs`.
- **Windows-native design:** extract an explicit press/release/double-tap state machine with monotonic timing, one capture owner, cancel/stop transitions, and shutdown reset.
- **Dependencies:** product timing thresholds and conflict behavior.
- **Acceptance:** tests cover single hold, valid/late double tap, repeated presses, transcription overlap, start/stop failure, escape/click stop, cancellation, lost hook, and shutdown; visible manual run proves no stuck recording.
- **Phase:** P2.

#### AUD-03 — media coordination

- **macOS files/tests:** route-aware recorders, `DictationAudioSessionManager.swift`, `MediaPlaybackController.swift`, `AudioDuckingController.swift`; route/ducking/playback tests.
- **Windows files:** no dictation media pause/duck coordinator; nearest files are the audio capture services.
- **Windows-native design:** Windows audio-session volume/ducking and media-key behavior only with reversible state restoration.
- **Dependencies:** supported Bluetooth/headset matrix and product choice for pause versus duck.
- **Acceptance:** product chooses pause, duck, or no interference; media state is restored exactly; no dispatcher blocking or leaked audio-session objects.
- **Phase:** blocked by product decision; outside the bounded Phase 2 dictation request.

#### TXT-01, TXT-02, TXT-03 — deterministic text pipeline

- **macOS files/tests:** `FillerWordFilter.swift`, `CustomWordMatcher.swift`, `TranscriptFormatter.swift`; filler, custom-word, dictionary-detector, formatter tests.
- **Windows files:** `FillerWordFilter.cs`, `DictionaryCorrectionService.cs`, `TranscriptionPipelineService.cs`, settings/UI, and `Phase2DictationTests.cs`.
- **Windows-native design:** dictation uses optional deterministic filler filtering followed by longest-first Jaro-Winkler phrase/replacement matching with punctuation boundaries. Generative cleanup remains explicitly excluded from latency-sensitive dictation.
- **Dependencies:** exact product defaults, language policy, compatibility migration if dictionary schema changes.
- **Acceptance:** golden tests cover boundaries, casing, punctuation, overlapping phrases, false-positive limits, disabled settings, ordering, and migration; human-reviewed names/accent corpus passes WER/CER. Cross-path meeting/import semantics remain TXT-03 debt outside this bounded phase.
- **Phase:** P2.

#### ONB-01 — resumable Windows onboarding

- **macOS files/tests:** `OnboardingFlow.swift`, `OnboardingProgress.swift`, onboarding views/controller; flow/progress tests.
- **Windows files:** onboarding builder in `MainWindow.xaml.cs`, `SettingsStore.cs`, audio/model/hotkey/paste services.
- **Windows-native design:** focused onboarding state service with a versioned persisted step/checkpoint; Windows microphone privacy verification; model-operation subscription; real capture-transcribe-paste test; optional explicit cloud/local summary setup; cancellation and restart resume.
- **Dependencies:** settings schema migration and Windows privacy/UIPI UX decisions.
- **Acceptance:** tests cover each step, skip/back, crash/restart, model/network/mic/hotkey/paste failure, cancellation, settings persistence, and version upgrade; first-run manual QA works from a clean profile.
- **Phase:** P2.

#### FLOAT-01 — live floating indicator

- **macOS files/tests:** `FloatingIndicatorController.swift` and appearance/QoL tests.
- **Windows files:** `ToastNotificationService.cs`, capture coordinators, indicator settings in WPF.
- **Windows-native design:** keep the existing native WPF surface but feed throttled real audio levels from capture services; formalize state/action ownership and deterministic close/dispose behavior.
- **Dependencies:** accessibility and multi-monitor/DPI design targets.
- **Acceptance:** state/action tests plus manual multi-monitor/DPI/theme checks; click-to-stop/cancel targets the active operation; level events are bounded and do not retain capture objects.
- **Phase:** P2.

### P3 — durable meetings and content management

#### DIC-03 — history operations

- **macOS files/tests:** dictation store/views and `DictationStoreTests.swift`.
- **Windows files:** `MainWindow.xaml(.cs)`, `AppDataStore.cs`, `AtomicJsonFile.cs`.
- **Windows-native design:** keep view logic separate from atomic repository operations; define undo/confirmation and corrupt-file recovery behavior.
- **Dependencies:** persistence schema/version strategy.
- **Acceptance:** tests cover copy/delete/filter/search, restart persistence, concurrent save, backup recovery, invalid data, and UI selection after deletion.
- **Phase:** P3.

#### MTG-02 — in-progress meeting lifecycle and recovery

- **macOS files/tests:** resume, health, repair, termination, recording writer files and their tests.
- **Windows files:** `MeetingSessionStateMachine.cs`, `MeetingRecordingCoordinator.cs`, `MeetingAudioHealthMonitor.cs`, `MeetingCapturePolicies.cs`, `MeetingSessionJournalStore.cs`, `AudioCaptureService.cs`, `SystemAudioCaptureService.cs`, `WindowsProcessLoopbackCapture.cs`, `AppDataStore.cs`, `MainWindow.xaml(.cs)`, and `Phase3MeetingLifecycleTests.cs`.
- **Windows-native design:** explicit ten-state lifecycle; concurrent separate-channel ownership; versioned session journal with relative track parts, selected model, capture mode, repair counters, and retention; endpoint callbacks; bounded repair; power suspend/resume; startup WAV repair/adoption; explicit recovery and deterministic shutdown. Manual recording never auto-stops; detected recording uses an exact-source grace policy.
- **Dependencies:** persistent schema migration and retention policy.
- **Acceptance:** deterministic state/policy/recovery tests pass; real Zoom/Teams/Meet, process-target/fallback, default-device/Bluetooth/removal, suspend/resume, forced interruption, disk-full, playback, retained/deleted ownership, visible UI, and fresh-log qualification pass; never report completed without a durable non-empty transcript and record.
- **Phase:** P3.

#### LIVE-02, LIVE-03, LIVE-04 — VAD, final owner, and reconciliation

- **macOS files/tests:** `StreamingVadController.swift`, `PCMChunkRecorder.swift`, `MeetingTranscriptHealthMonitor.swift`, `TranscriptReconciler.swift`, chunk timing/session files; VAD/recorder/health/reconciler tests.
- **Windows files:** nearest are capture services, `MeetingRecordingCoordinator.cs`, `NativeOfflineAsrClient.cs`, and persisted settings/models.
- **Windows-native design:** Silero VAD ONNX worker over bounded PCM buffers; natural-boundary chunk journal; explicit `LivePreviewModelId`, `FinalTranscriptModelId`, and ownership enum; timestamped gap ledger; recovery transcription; deterministic overlap reconciliation while retaining raw contributions.
- **Dependencies:** live model work, VAD model/license, schema migrations, reconciliation thresholds.
- **Acceptance:** no mid-word fixed rotation under corpus tests; ownership always visible; Nemotron continuous transcript is normal final with configured model only for detected gaps; Parakeet EOU is preview-only with selected final model; injected dropped/duplicate/out-of-order chunks reconcile without silent loss.
- **Phase:** P4 (design can begin after P3 journaling).

#### DIA-02, MTG-03, NOTE-01 — editable meeting record

- **macOS files/tests:** `MeetingDetailView.swift`, `MeetingNotesView.swift`, rich text editor, store/navigation/title tests.
- **Windows files:** `AppDataStore.cs`, `SpeakerAliasService.cs`, meeting/detail handlers in `MainWindow.xaml(.cs)`, exporter/search.
- **Windows-native design:** versioned record fields for manual notes and edit provenance; debounced atomic save; explicit raw versus edited transcript; safe retranscribe/re-summary jobs with cancel/retry; one alias projection used by display/copy/export.
- **Dependencies:** schema migration and merge policy for future sync.
- **Acceptance:** migrations preserve existing JSON; edit/restart/recovery tests pass; aliases and edits appear consistently; failed retranscription leaves prior record intact; manual notes never get overwritten by generated notes.
- **Phase:** P3.

#### ORG-01 — nested folders

- **macOS files/tests:** meetings/store/navigation files and tests.
- **Windows files:** `PersistedMeetingFolder`/`AppDataStore.cs`, folder UI in `MainWindow.xaml(.cs)`.
- **Windows-native design:** add nullable parent ID and stable order with cycle prevention; migrate flat folders as roots; repository service owns move/delete semantics.
- **Dependencies:** schema migration and UX depth decision.
- **Acceptance:** migration, nested create/rename/reorder/move, cycle prevention, delete/reparent, restart, corrupt-parent recovery, and search/filter tests.
- **Phase:** P3.

#### IMP-01 — import parity

- **macOS files/tests:** `AudioFileImportController.swift`; import tests.
- **Windows files:** import handlers in `MainWindow.xaml.cs`, `NativeTranscriptionClient.cs`, `NativeDiarizationClient.cs`, summary/data services, media qualification scripts.
- **Windows-native design:** cancellable background import job with Media Foundation/FFmpeg-free supported decoder path already in product dependencies, explicit progress, normalized PCM, diarization, title/summary, atomic commit, retry without duplicate records.
- **Dependencies:** confirmed decoder support/licensing for every advertised extension and test corpus.
- **Acceptance:** each advertised format passes valid/corrupt/unsupported/cancel/restart tests; multi-speaker import diarizes; failed jobs leave no fake meeting or orphan temp file.
- **Phase:** P3.

#### PLAY-01 — in-app playback

- **macOS files/tests:** meeting player/media controller and playback tests.
- **Windows files:** `MeetingRecordingPlaybackService.cs`, retained paths/session state in `AppDataStore.cs`, playback controls and deterministic close handlers in `MainWindow.xaml(.cs)`, and Phase 3 track-selection tests.
- **Windows-native design:** disposable NAudio player with one playback owner, seek/duration, and microphone/system track selection. Device-loss hardening and asynchronously cached waveform remain future content/UI work; decoding/state projection must stay bounded off the dispatcher.
- **Dependencies:** UX for mic/system/mix selection and supported formats.
- **Acceptance:** Phase 3 requires play/pause/seek/track selection, missing-file disclosure, and resource release on switch/shutdown. Full PLAY-01 additionally requires device-loss tests and manual waveform validation.
- **Phase:** Phase 3 capture subset; future content/UI phase for full parity.

### P5 — detection and calendar

#### DET-01, DET-02 — signal-based meeting lifecycle

- **macOS files/tests:** detector/monitor/candidate, browser/media/sensor collectors, auto-stop/signal/termination policies and tests.
- **Windows files:** `MeetingDetectionService.cs`, `MeetingPromptService.cs`, WPF integration.
- **Windows-native design:** independent signal collectors for process/window/browser URL, WASAPI session/mic use, and Windows camera privacy/device activity where reliably available; candidate resolver with hysteresis; prompt/recording/auto-stop state machine; explicit unsupported-signal diagnostics.
- **Dependencies:** Windows-version support matrix and false-positive/false-negative policy.
- **Acceptance:** fixtures and live Zoom/Teams/Meet/Webex checks cover camera-only, mic-only, nonmeeting browser, duplicate windows, join/leave/rejoin, app crash, dismiss, and auto-stop without losing capture.
- **Phase:** P5.

#### CAL-01, JOIN-01 — Google Calendar and Join & Record

- **macOS files/tests:** Google auth/client, calendar monitor, upcoming window, notification/prompt/scheduled policies; Google/disabled/upcoming/notification tests.
- **Windows files:** no calendar service; `MeetingPromptService.cs` supplies a partial URL prompt.
- **Windows-native design:** Google OAuth PKCE via system browser and loopback/custom redirect as approved; refresh tokens in Credential Manager; incremental REST sync with bounded polling/retry; versioned dismissal/prune store; tested URL parser; prompt state machine with Join & Record, Join Only, Record Only.
- **Dependencies:** Google OAuth client configuration/verification, scopes/privacy text, product refresh window and offline behavior.
- **Acceptance:** auth/refresh/revoke/denial, network failure/backoff, today/2/3-day ranges, event update/delete, dismiss/prune, duplicate suppression, URL/platform parsing, all three actions, shutdown, and secret-redaction tests; fresh signed/package callback test before release.
- **Phase:** P5.

### P6 — providers and automation

#### SUM-02, SUM-04 — local providers and generated titles

- **macOS files/tests:** `MeetingSummaryClient.swift` and summary/title tests.
- **Windows files:** `MeetingSummaryService.cs`, provider settings/credentials in WPF/`SettingsStore.cs`/`SecretStore.cs`.
- **Windows-native design:** explicit provider adapters for Ollama and LM Studio/custom endpoints with opt-in network/local labeling, health checks, bounded timeout/cancel, structured result containing title and notes, and no cross-provider fallback unless the UI states the local deterministic fallback.
- **Dependencies:** API compatibility/version targets and safe URL policy.
- **Acceptance:** success, malformed response, unreachable server, timeout, cancellation, auth, redaction, title validation, and local fallback disclosure tests; live provider smoke is opt-in and records no secrets.
- **Phase:** P6.

#### HOOK-01 — post-meeting hooks

- **macOS files/tests:** `MeetingHookRunner.swift`; runner/integration tests and `scripts/test-meeting-hook.sh`.
- **Windows files:** `PostMeetingAutomationService.cs`, settings/persistence integration in `SettingsStore.cs`, `AppDataStore.cs`, and `MainWindow.xaml(.cs)`, `Phase9AutomationTests.cs`, and `Muesli.Automation.TestHost`.
- **Windows-native design:** `ProcessStartInfo` with explicit executable path, JSON stdin, redirected bounded output, timeout/cancel, Windows Job Object for descendant termination, redacted logs, and post-persistence isolation so hook failure cannot invalidate a meeting.
- **Dependencies:** payload schema and executable trust/consent UX.
- **Acceptance:** success/nonzero/timeout/cancel/large output/missing executable/child process/shutdown tests; JSON excludes audio/secrets unless explicitly specified; meeting remains saved on every hook failure.
- **Phase:** P6.

#### AUTO-01, FOLLOW-01 — export and follow-up automation

- **macOS files/tests:** auto-export/follow-up policy files and tests.
- **Windows files:** `MeetingExporter.cs` is the reusable export primitive; Markdown automation is implemented by `PostMeetingAutomationService.cs`. The separate follow-up workflow remains decision-gated.
- **Windows-native design:** post-persistence job queue with idempotency key; atomic filename reservation; explicit formats/destination; retry and visible failure; follow-up adapter only after its destination/product contract is chosen.
- **Dependencies:** destination semantics and follow-up product decision.
- **Acceptance:** collision/concurrency/retry/cancel/disk-full/restart tests; no duplicate outputs; automation failures are visible and never roll back the meeting.
- **Phase:** P6.

### P8 — shell and release

#### TRAY-01, SOUND-01, INSIGHT-01 — desktop shell parity

- **macOS files/tests:** status/recent/upcoming/insights/sound/appearance sources and QoL/upcoming/insights tests.
- **Windows files:** `TrayIconService.cs`, WPF dashboard/stat cards, no sound/insights services.
- **Windows-native design:** NotifyIcon menu driven by immutable app state; recent/upcoming/record actions call real commands; MediaPlayer/SystemSounds service with user setting and deterministic disposal; insights remain local unless an explicit contribution decision is approved.
- **Dependencies:** calendar work; product decisions on contribution telemetry and feedback sounds.
- **Acceptance:** menu state/action/shutdown tests, no no-op entries, theme/DPI keyboard checks, local insight golden tests, explicit consent and redaction if contribution is enabled.
- **Phase:** P8, after D4.

#### UPD-01, PKG-01 — update and release path

- **macOS files/tests:** Sparkle/updater integration, appcast/update scripts, update guidance tests, build/sign/notarize/DMG/release scripts.
- **Windows files:** About link, `scripts/package-windows-v1.ps1`, `scripts/build-installer.ps1`, `installers/muesli-windows.iss`, install/uninstall/package/fresh-machine/release/sign scripts.
- **Windows-native design:** signed release manifest/feed and updater with signature/hash verification, staged replacement/rollback, explicit progress/cancel/failure; preserve user data; Authenticode-sign every executable/installer before publication.
- **Dependencies:** D5 signing certificate, release hosting/feed, update framework/product decision, CI secrets.
- **Acceptance:** fresh package/installer install-upgrade-uninstall on clean supported Windows VMs; signed artifact and feed verification; tamper/network/disk/full/in-use/rollback tests; launch foreground; no fresh forbidden logs; no Python/Electron/worker files.
- **Phase:** P8.

#### TEST-01, QUAL-01 — evidence system

- **macOS files/tests:** the broad `native/MuesliNative/Tests/MuesliTests` suite and `scripts/dev-test.sh`, canary, release, update, packaged CLI scripts.
- **Windows files:** `Muesli.Windows.Tests`, benchmark/qualification/package/install scripts, CI workflow, retained artifacts/docs.
- **Windows-native design:** state-machine/unit tests first; deterministic audio fixtures; contract tests for network providers; WPF UI automation only for meaningful flows; hardware/manual scripts produce timestamped manifests with model/provider/hardware/build identity.
- **Dependencies:** test media redistribution, supported hardware/VM matrix, CI runtime budget.
- **Acceptance:** every future phase proves success, failure, cancellation, persistence, recovery, shutdown, and fresh-log behavior; evidence cannot be reused after relevant source/model/package change; no “complete” status without the defined manual/hardware gate.
- **Phase:** continuous, release gate in P8.

## Verification debt for implemented features

These items already exist but cannot move to **Complete and verified** until the named evidence is fresh and representative:

| IDs | Required evidence |
|---|---|
| MOD-01, DIC-01, AUD-01 | Real microphone hold/release transcription on CPU and CUDA, cold/cached model, cancel/failure, and shutdown; validate model/provider diagnostics and fresh logs. |
| DIC-02, HOT-01 | Paste-target qualification in Notepad, Chromium contenteditable/text fields, Office/UWP where supported, focus race, elevated-target disclosure, alternative keyboard layout, and no clipboard corruption. |
| MTG-01, DIA-01 | Long Zoom/Teams/Meet sessions with mic/system separation, Bluetooth/default-device changes, real multiple remote speakers, save-audio on/off, cancel/failure/retry, CPU/CUDA, and transcript/track inspection. |
| SUM-01 | Opt-in live OpenAI/OpenRouter checks with explicit network disclosure, invalid credential, timeout/cancel, secret/log inspection, and provider response fixtures. |
| TPL-01, SEARCH-01, EXP-01 | Persistence/recovery tests, malformed records, aliases/manual content projection, export golden validation, and manual UI/file-open checks. |
| SHELL-01, START-01 | Keyboard/accessibility, 100–200% DPI, multi-monitor, light/dark, close-to-tray, startup registry/install/uninstall, and visible relaunch checks. |

## External-decision ledger

| Decision | Blocks | Required decision/dependency | Exit condition |
|---|---|---|---|
| D1 | SUM-03 | Supported ChatGPT subscription OAuth/API contract, client registration, token storage/refresh/revoke/security review. Do not imitate private browser/session APIs. | Written supported integration contract plus threat/privacy review and test credentials. |
| D2 | CU-01 | Planner provider, action allowlist, per-action consent, observation/screenshot retention, browser automation boundary, audit/undo policy. | **Resolved for Phase 10:** OpenAI only, explicit model/key; application/domain allowlists; confirmation for all mutating/browser/invoke actions; UIA metadata only; text/screenshots off; loopback origin-pinned DevTools; bounded redacted local trace; fail-closed without automatic action retry. See `COMPUTER_USE.md`. |
| D3 | SYNC-01 | Cross-platform backend, account/identity model, encryption/key recovery, conflict/tombstone schema, iPhone compatibility, service privacy/operations. CloudKit is not available as the Windows answer. | Approved protocol/backend and migration/security design. |
| D4 | INSIGHT-01 | Whether contribution/telemetry exists on Windows; exact opt-in, schema, endpoint, and retention. | Written privacy/product decision. Local-only insights can proceed independently. |
| D5 | SIGN-01, UPD-01, PKG-01 | Production Authenticode certificate, timestamp service, protected CI signing, release feed/hosting, updater selection. | Signed test executable and installer validate on clean Windows with an approved publisher identity. |
| D6 | MOD-02, MOD-04 | Promised Windows model set, artifacts/licenses, hardware and quality/latency thresholds. | Versioned catalog decision and redistribution approvals. |

## Required Windows equivalents for macOS-only facilities

| macOS facility | Required Windows-native behavior |
|---|---|
| CoreAudio process tap / ScreenCaptureKit | Windows process-tree loopback on supported builds for a detected process; explicitly disclosed render-endpoint loopback otherwise. Preserve separate mic/system tracks and visible health warnings. |
| CoreML/ANE, Metal, LiteRT-LM | ONNX Runtime/Sherpa/LLamaSharp or another explicitly selected in-process native runtime; model formats must be Windows-compatible and separately qualified. |
| EventKit notifications | Explicit Google Calendar OAuth plus REST incremental sync/polling or approved push infrastructure. Do not assume Outlook/MAPI. |
| CloudKit/iCloud | Product-approved cross-platform sync backend/protocol with Windows Credential Manager and the same text-only/conflict/tombstone invariants. |
| macOS Accessibility/TCC | Windows microphone privacy checks, foreground/integrity/UIPI-aware paste diagnostics, and clear Settings deep links where available. |
| AppKit status item | WPF/Win32 NotifyIcon with real command/state integration. |
| Sparkle, codesign, notarization, DMG | Signed Windows update channel, Authenticode, timestamping, self-contained zip and signed Inno Setup installer. |
| Camera/media attribution APIs | Best-effort Windows camera/microphone/audio-session signal collectors with explicit unsupported-state behavior and a tested candidate resolver. |

## Authoritative conclusions

1. The Windows app already has a credible native local-first foundation: in-process WPF/.NET, WASAPI capture, Sherpa/ONNX transcription and diarization, protected summary credentials, atomic JSON persistence, package scripts, and no Python/Electron/web fallback.
2. That foundation is not broad product parity. Live transcription, VAD rotation, final-owner modes, gap recovery/reconciliation, calendar, full Join & Record, manual notes, hooks, auto-export/follow-ups, automatic updates, and sound feedback are missing; several other surfaces are partial.
3. The largest remaining risk is evidence breadth, not compilation. One hundred twenty-eight passing tests cover the transcription platform, dictation policies, and the Phase 3 meeting lifecycle, but live meeting hardware/process attribution, provider, later-phase, update/signing, and macOS-equivalent workflow gates remain open.
4. Sync and Computer Use must remain decision-gated. A literal CloudKit or macOS automation port is impossible and a speculative substitute would create security and product divergence.
5. Signing remains externally blocked. The current zip and installer have fresh local smoke evidence, but they cannot be called release-ready while unsigned and without clean-VM upgrade/update qualification.
