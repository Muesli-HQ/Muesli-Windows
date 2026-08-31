# Muesli Windows parity execution plan

Plan date: 2026-08-01

Source of truth: `docs/WINDOWS_MACOS_PARITY_MATRIX.md`

Current bounded phase: Phase 4 real live meeting transcription is source-complete and re-qualified against the real artifact. Phase 5 detection/calendar and all later meeting/automation work remain deferred.

## Outcome and sequencing rule

Windows parity will be delivered as a sequence of bounded, independently accepted phases. A phase is complete only when its source, migration, automated tests, targeted qualification, visible WPF exercise, fresh-log inspection, and documentation gates pass. A build alone is never completion.

The ordering protects the local-first recording path first, establishes durable meeting ownership before streaming, and leaves security/backend-dependent features behind explicit decisions.

```text
P0 Inventory
  -> P1 Runtime truth and model lifecycle
       -> P2 Dictation/audio/onboarding
       -> P3 Resilient meeting capture/session lifecycle
            -> P4 Live transcript/VAD/final ownership
            -> P5 Detection/calendar/Join & Record
                 -> P6 Providers/hooks/automation
                      -> P8 Release shell/update/signing

Decision D2 -> P7A Computer Use
Decision D3 -> P7B Cross-platform text sync
P7A/P7B are separate security/product tracks and are not prerequisites for local recording parity.
```

P2 and P3 are separate bounded tasks after P1, and shared audio/persistence changes must be serialized. P4 cannot begin until P3 has a durable in-progress meeting journal and explicit finalization states. P5 calendar work can begin only after its OAuth configuration is available. P8 may prepare unsigned local packaging earlier, but release completion requires signing decision D5.

## Non-negotiable engineering rules

Every implementation phase must preserve these constraints:

- Native WPF/.NET only. No Python runtime/worker, Electron fallback, or web wrapper.
- Local audio and transcription remain local first. Network-backed summaries, calendar, sync, and planner behavior are explicit and opt-in.
- A selected transcription engine/model either runs or reports failure. Never silently substitute another engine.
- Raw capture and raw transcript remain recoverable when cleanup, diarization, summary, hook, export, or sync fails.
- Long operations have an owner, cancellation, progress, failure, retry, and deterministic shutdown behavior.
- Native audio, model, network, process, timer, tray, and WPF resources are disposed deterministically.
- Capture/model/media work stays off the WPF dispatcher; dispatcher work is bounded to state projection and rendering.
- New settings or records use an explicit schema migration with backup, rollback/recovery tests, and preservation of unknown/legacy-safe data.
- Provider keys/tokens remain in Windows Credential Manager or a stronger approved store, never JSON, logs, source, crash data, hooks, or support bundles.
- Existing unrelated working-tree changes remain untouched. No reset, stash, commit, or push is part of a parity phase unless separately requested.
- macOS API names do not justify a Windows dependency. Windows design starts from the required behavior and supported Windows APIs.

## Phase acceptance protocol

The following gate applies after every source/project update. Documentation-only changes use the same visible-launch/log gate and may reuse a successful build only when the phase did not explicitly require a fresh one.

1. Record the exact matrix IDs in scope and mark later IDs out of bounds for the task.
2. Inspect the corresponding macOS sources/tests and current Windows sources/tests again; the reference may have changed.
3. Write or update the failure/cancellation/persistence/recovery tests before claiming the state machine is complete.
4. Kill only the running `Muesli.exe` when necessary.
5. Run `dotnet build windows-native\Muesli.Windows\Muesli.Windows.csproj --no-restore`.
6. Run `dotnet test windows-native\Muesli.Windows.Tests\Muesli.Windows.Tests.csproj --no-restore` without a test filter.
7. Run the phase’s targeted qualification scripts and record build, model, provider, hardware, fixture, and timestamp identity.
8. Launch `windows-native\Muesli.Windows\bin\Debug\net8.0-windows\Muesli.exe` visibly with the dashboard foregrounded.
9. Exercise the changed UI and inspect the rendered state, not only process liveness.
10. Inspect only the log bytes written after the launch marker and reject new `ERROR`, `Unhandled UI exception`, or `XamlParseException` entries.
11. For packaging/update/signing changes, run zip, installer install/upgrade/uninstall, signing, and clean-machine smoke gates.
12. Update the parity matrix status only to the level proven by the evidence. Retained evidence becomes stale when relevant code/model/package inputs change.

Each phase completion report must state: outcome delivered; files changed; persistent-data/settings migration; tests and commands; manual behavior; fresh-log result; known limitations; anything not verified; and whether the next phase is unblocked.

## Phase 0 — authoritative inventory

**Objective:** establish the behavioral gap and a dependency-ordered plan without implementing product behavior.

**Delivered artifacts:**

- `docs/WINDOWS_MACOS_PARITY_MATRIX.md`
- `docs/WINDOWS_EXECUTION_PLAN.md`

**Evidence baseline:** Debug WPF build passes with 0 warnings/errors; the complete Windows test project passes 46/46; the existing 0.2.0 zip passes structural/package runtime smoke with `-SkipLaunch`. The zip was not rebuilt, the installer was not exercised, and historical Phase 3–5 evidence remains historical. The rebuilt Debug app was visibly foregrounded; Dictations, Models, Settings/General, and Settings/Meetings rendered without an observed parse/layout failure; Dictations was restored in front. The log slice appended after the prelaunch byte marker contained none of the three forbidden patterns.

**Exit criteria:**

- Every requested feature family has an exact matrix status.
- Every Partial/Missing row has source/test mapping, Windows design, dependencies, acceptance criteria, and a later phase.
- Platform equivalents, explicit exclusions, and external decisions are visible.
- The rebuilt WPF dashboard is visibly launched and fresh logs are inspected before this phase is reported complete.

**Do not do in P0:** source changes, schema changes, feature implementation, architecture cleanup, release publication, or status inflation based on file presence.

## Phase 1 — runtime truth and model lifecycle

**Phase 1 transcription-platform outcome (2026-08-01): delivered.** Seven approved offline choices are pinned and real-audio qualified; Dictation and Final meeting/import roles persist and route independently; Live is Off; explicit lifecycle, cancellation/retry/delete/recovery, strict routing/disposal, schema migration, truthful diagnostics, and model documentation are implemented. Optional Qwen/GGUF cleanup remains the separately classified advanced/manual MOD-05 path and was not expanded into a new cleanup download product in this bounded phase.

**Matrix scope:** MOD-01 through MOD-03, MOD-05, DIAG-01 runtime portion, PRIV-01, and the model-related portion of TEST-01/QUAL-01.

**Objective:** make every currently advertised offline model and runtime state truthful, manageable, recoverable, and qualified before adding new user workflows.

**Implementation slices:**

1. Correct selected-model capability/runtime diagnostics and update privacy/model/package documentation to match the actual catalog.
2. Introduce a focused model-operation service/state machine with per-model download, verification, cancellation, retry, delete, update, and interrupted-operation recovery.
3. Qualify every approved advertised offline model on the supported CPU path and eligible CUDA path; either meet gates or remove/disable the advertisement explicitly. Do not fall back to a different model.
4. Turn local cleanup into a managed, versioned model lifecycle or leave it explicitly advanced/manual and unadvertised as ready.
5. Expand unit/fixture coverage for corrupt archives, wrong hashes, path traversal, disk full, cancellation, concurrency, restart, delete-in-use, and native disposal.

**Resolved catalog boundary:** the runnable offline set is the seven sherpa-onnx choices in `TranscriptionModelCatalog`. CoreML/LiteRT identifiers are excluded. Phase 4 separately adds the packaged-runtime-qualified Nemotron 3.5 live choice while preserving Off as the default.

**Acceptance:**

- Model UI exposes only real, working operations and stays responsive during them.
- Every enabled catalog entry has deterministic fixed-corpus CPU evidence and, where claimed, CUDA evidence.
- Diagnostics name the actual selected engine/model/provider and do not use Parakeet as a proxy for another model.
- Failed/cancelled operations leave the previous ready model intact and clean staging data on restart.
- Privacy/package/model docs agree with source and network behavior.
- Full test/build/visible UI/fresh-log protocol passes.

**Explicitly deferred:** live models/streaming, VAD, dictation route changes, meeting state redesign, calendar, sync, and Computer Use.

## Phase 2 — dictation, routing, and deterministic text pipeline

**Bounded task scope (2026-08-01):** DIC-01/DIC-02/DIC-03 dictation behavior, HOT-01/HOT-02, AUD-01/AUD-02, TXT-01/TXT-02 dictation cleanup, FLOAT-01, privacy diagnostics, and dictation qualification. Meeting, summary, onboarding redesign, and media pause/duck were explicitly excluded from this task.

**Objective:** make the core dictation path robust across real Windows input/audio conditions and require human evidence before release qualification.

**Implementation outcome:** explicit hold/double-tap/hands-free states, Escape cancellation, original-target paste with durable clipboard/history fallback, default/selected endpoint notifications with loss-bounded segment rotation, real indicator levels, persisted filler filtering, conservative phrase-aware Jaro-Winkler dictionary correction, no-speech preflight, bounded benchmark audio, title-free dictation diagnostics, and schema-enforced CPU/CUDA plus paste-target qualification tooling are implemented. Source/unit qualification does not substitute for the still-required human corpus, physical route matrix, and Notepad/Chrome/Office/other-editor paste reviews.

**Implementation slices:**

1. Extract hands-free timing/transitions from WPF code-behind into a tested state machine.
2. Add Windows endpoint notifications and explicit capture behavior for default-device change, unplug, Bluetooth/profile change, and privacy denial.
3. Define and implement the dictation text pipeline: optional deterministic filler filtering, personal dictionary/Jaro-Winkler phrase matching, and persisted delivered text.
4. Feed bounded real audio levels to the floating indicator and test active-operation action ownership.
5. Enforce a named-human-reviewed corpus on CPU/CUDA and real paste-target qualification across supported desktop targets and integrity levels.

**Acceptance:**

- Unit tests cover hold and hands-free success/failure/cancel/reentry/shutdown.
- Route/device loss cannot create a fake success or discard already finalized audio without a visible recoverable state.
- Dictionary/filler ordering is deterministic for dictation, respects disabled settings, and applies conservative false-positive limits.
- Notepad, Chrome, Office, and another editable target pass without clipboard corruption; unsupported elevated targets fail clearly.
- Human-reviewed short/paragraph/dictionary/numbers/accent/silence/noise cases pass WER/CER, model identity, RTF, CPU, and CUDA gates.
- Real microphone UI exercise and fresh-log gate pass.

**Explicitly deferred:** media pause/duck product behavior, onboarding redesign, durable meeting journal, live meeting transcript, calendar, summary/provider work, and release updater.

## Phase 3 — resilient meeting capture and session lifecycle

**Matrix scope:** MTG-01, MTG-02, the capture/track-ownership part of PLAY-01, API-01, and related AUD-02/TEST-01/QUAL-01 evidence. Live transcription, VAD, meeting-content editing, imports, folders, calendar/detection expansion, summaries, hooks, sync, and Computer Use are not authorized by this phase.

**Objective:** make offline meeting capture resilient, truthful, recoverable, and testable before streaming is introduced.

**Implementation slices:**

1. Create a coordinator-owned state machine with Idle, Preparing, Recording, Degraded recording, Stopping, Finalizing, Completed, Failed, Cancelled, and Recoverable interruption states.
2. Capture microphone and meeting/system audio concurrently, preferring Windows process-tree loopback for a detected process and explicitly disclosing endpoint-loopback fallback.
3. Add endpoint notifications, Bluetooth/default-device transitions, bounded channel repair, health/silence/clipping/missing-channel warnings, and conservative source-specific auto-stop.
4. Add suspend/resume, device-loss, shutdown, crash-WAV repair, startup discovery, retained-audio ownership, and explicit recovery/finalization.
5. Add a disposable in-app player with microphone/system track selection, play/pause, and seek. Waveform rendering remains a separate content/UI enhancement.
6. Qualify representative Zoom, Teams, and Meet sessions with real remote speakers and the physical audio-route matrix.

**Implementation outcome (2026-08-01):** the source implementation and deterministic policy/state/recovery tests are delivered. Process-tree capture is guarded by OS build and detected PID and falls back explicitly to endpoint loopback. Session journals survive interruption; WAV headers are repaired when possible; header-only/sub-100 ms tracks are rejected before native inference; final state and retained ownership persist in meeting schema 2; playback is in-process. The source build passes with zero errors and the complete Windows test project passes 128/128. A visible manual record/stop/recovery exercise produced a truthful retained **Needs attention** record when no transcript was available, and in-app playback/file release passed. A historical-real-audio CUDA finalization smoke passed deterministic inference and one-speaker diarization, but it was not a simultaneous-capture or human transcript-quality qualification. This remains insufficiently verified until the physical/manual qualification matrix passes.

**Acceptance:**

- Crash, forced shutdown, device-loss, corrupt/missing track, suspend/resume, cancel, retry, and restart tests preserve recoverable state. Disk-full remains a manual qualification item until a deterministic filesystem fault injector is introduced.
- Completed means transcript and record are durably committed; partial/recoverable means exactly that and is visible.
- Playback releases files/devices on meeting switch and shutdown.
- Manual recordings never auto-stop. Detected recordings only auto-stop after the exact source has been observed and then remains absent through the grace/miss gates.
- Real multi-speaker CPU/CUDA meeting qualification, targeted/fallback attribution, physical route/device/Bluetooth/suspend checks, visible UI, and fresh-log gates pass.

**Explicitly deferred:** live transcription/VAD/reconciliation; meeting title/transcript/manual-note editing; import expansion; nested folders; calendar/detection expansion; summary/provider work; hooks/automation; sync; Computer Use; waveform rendering.

## Phase 4 — live transcription, VAD, final ownership, and reconciliation

**Matrix scope:** MOD-04, LIVE-01 through LIVE-04, FLOAT-02, and related TEST-01/QUAL-01.

**Objective:** add the two product-defined live modes without weakening durable offline capture.

**Hardening pass (2026-08-01, second bounded Phase 4 task).** A review of the delivered implementation found and fixed five defects, all covered by new tests: dropped packets performed an O(committed) snapshot copy, took the contended state lock, and raised a dispatcher-bound event *on the WASAPI capture callback thread*, which loaded the audio thread hardest exactly when the machine was already too slow to keep up; every processed packet published a snapshot, so a long meeting drove `string.Join` over all committed segments plus a layout pass at capture-callback rate; live journal checkpoints performed synchronous disk I/O and an unsynchronized `_journal` mutation on the inference worker; several VAD boundaries drained in one packet were committed one-per-boundary, so all but the first returned empty text and fabricated a `speech-without-committed-text` gap over speech that had in fact been transcribed; and measured-gap ranges, which are 16 kHz live-stream indices, were used directly as frame offsets into the retained WAV, so on any non-16 kHz capture device gap recovery re-transcribed the wrong region of audio. Publication is now coalesced to 200 ms with immediate flushes on commit/gap/finalization, drops are recorded lock-light and drained by the worker into a bounded, coalescing ledger, checkpoints run on a background flush under the coordinator gate, boundaries drained together commit one truthful span, and gap ranges are rescaled to the track's native rate. Ownership labels moved out of `MainWindow.xaml.cs` into `LiveTranscriptOwnershipDescriptor` so Settings, Models, the journal, and the tests share one definition; the `LiveModelChoice` picker also rendered its compiler-generated record form (`LiveModelChoice { Id = , Label = Off }`) in the closed ComboBox and now follows the codebase's `ToString()` convention. Test count moved 141 → 165.

**Implementation outcome (2026-08-01):** Windows uses the packaged sherpa-onnx 1.13.4 online transducer and native Silero VAD with the pinned multilingual Nemotron 3.5 560 ms INT8 artifact. The durable Phase 3 recorders publish shared normalized PCM to a bounded background session; no second capture owner exists. Only VAD speech ends commit text, partials remain provisional, queue loss is recorded as measured gaps, and shutdown/cancellation dispose recognizer streams and VAD deterministically. Preview-only and unified live-and-final ownership are persisted and displayed, with measured-gap clips routed to the configured offline final model only in unified mode. Settings schema 4, session-journal schema 2, and completed-meeting schema 3 retain the live preview, final, and gap-recovery owners. The floating WPF transcript is a subscriber and waveform levels are opt-in on hover. Real multilingual audio inference and real Silero boundaries passed with the built app DLLs; physical long-session/hardware qualification remains open.

**Implementation slices:**

1. Add a managed, opt-in lifecycle for the qualified Windows Nemotron 3.5 artifact; keep the non-runnable CoreML Parakeet Realtime EOU choice absent.
2. Feed shared capture PCM to a bounded streaming session without adding a second uncontrolled audio owner.
3. Add Silero VAD natural-boundary chunk rotation and durable timestamp/checkpoint metadata.
4. Persist and display final-transcript ownership: Nemotron live-and-final with configured model only for gaps; Parakeet EOU preview with a separate selected final model.
5. Add transcript health/gap ledger and deterministic recovery/reconciliation with raw contribution retention.
6. Add the floating live transcript/waveform-hover UI as a subscriber, not an owner of capture or transcription.

**Dependencies:** P3 journal/schema; D6 live artifacts/licenses/runtimes; measured CPU/GPU budgets.

**Acceptance:**

- Live transcription remains off by default; downloading does not activate it.
- Settings always identifies the final owner and prevents invalid live/final combinations.
- VAD corpus tests avoid mid-sentence fixed cuts within defined thresholds.
- Injected loss, duplication, reordering, slow model, native failure, cancellation, and app shutdown reconcile or visibly retain recoverable gaps.
- A live-model failure never stops durable capture or silently changes the final owner.
- Long-session latency/memory/thermal qualification and visible floating UI/fresh-log gates pass.

**Explicitly deferred:** calendar/detection, hooks, sync, Computer Use, updater.

## Phase 5 — meeting detection, Google Calendar, and Join & Record

**Matrix scope:** DET-01, DET-02, CAL-01, JOIN-01, API-02, and the calendar-dependent tray work only if explicitly included in the bounded task.

**Objective:** detect real meetings with controlled false positives and offer complete scheduled/detected meeting actions.

**Implementation slices:**

1. Build independent Windows signal collectors for process/window/browser URL, WASAPI sessions/mic activity, and camera/device state where supported.
2. Add a candidate resolver with hysteresis, dedupe/dismiss, prompt lifecycle, and auto-stop policy.
3. Implement explicit Google OAuth and Credential Manager token storage, then incremental calendar sync for today/two/three-day ranges, dismissal/pruning, and offline/backoff behavior.
4. Implement and test URL/platform extraction plus Join & Record, Join Only, and Record Only commands.

**Dependencies:** approved Google OAuth client/scopes/verification; supported Windows/call-app matrix. No Outlook/MAPI assumption is allowed.

**Acceptance:**

- Camera-only and mic-only nonmeeting activity does not prompt under the approved policy.
- Zoom, Teams, Meet, and Webex join/leave/rejoin are exercised; unsupported signals are disclosed.
- Auth denial/refresh/revoke, event update/delete, network failure/backoff, dismiss/prune, duplicate suppression, and all three actions are tested.
- Recording start/stop failures remain visible and cannot create a fake scheduled-meeting success.
- Secrets/URLs are redacted from logs/support output; visible UI and fresh-log gates pass.

## Phase 6 — summary parity, hooks, and automation

**Matrix scope:** SUM-01 live verification, SUM-02, SUM-04, TPL-01 verification, HOOK-01, AUTO-01, FOLLOW-01. SUM-03 only after D1.

**Objective:** complete explicit provider choices and safe post-meeting automation after meeting persistence is trustworthy.

**Implementation slices:**

1. Add Ollama and LM Studio/custom adapters with health/timeout/cancel/redaction and generated-title results.
2. Add ChatGPT subscription OAuth only if D1 produces a supported contract; otherwise keep it visibly unavailable and blocked.
3. Harden template persistence/re-summary and ensure manual notes are never overwritten.
4. Add post-persistence hooks using JSON stdin, bounded output, timeout/cancel, and a Windows Job Object for process-tree termination.
5. Add idempotent auto-export and only the approved follow-up destination/workflow.

**Acceptance:**

- Provider/network use is explicit; no cross-provider silent fallback; local deterministic fallback is labeled.
- Live opt-in provider checks and offline contract tests cover malformed responses, invalid auth, timeout/cancel, and secret/log safety.
- Hook failure/timeout/child process/shutdown cannot invalidate or block the durable meeting.
- Auto-export handles collision, concurrency, restart, retry, disk full, and cancellation without duplicates.
- Visible UI/fresh-log gates pass.

## Phase 7 — decision-gated security tracks

Phase 7 is deliberately split. Neither track starts merely because earlier phases finish.

### P7A — Computer Use

**Matrix scope:** CU-01.

**Phase 10 outcome:** D2 is resolved by the versioned threat model in `COMPUTER_USE.md`. The Windows implementation is disabled by default, OpenAI-only with explicit model/key selection, application/domain allowlists, privacy-minimized single-window UI Automation observation, loopback origin-pinned DevTools browser control, local confirmation policy, bounded re-observe/replan execution, cancellation, and a redacted local trace. Window/page text and screenshots remain unavailable pending masking qualification.

Use Windows UI Automation, Win32 input only where explicitly allowed, Windows.Graphics.Capture or another approved observation path, and a bounded planner/tool registry. Require observable state, per-action policy/consent, cancellation, timeout, audit trace, and fail-closed behavior. Browser automation boundaries and screenshot retention must be explicit.

**Acceptance:** planner/executor schema and allowlist tests; prompt injection/untrusted screen content policy; timeout/cancel/shutdown; privilege/UIPI boundary; destructive-action confirmation; trace redaction; live sandboxed workflow qualification. No dictation alone may grant a broader action surface.

### P7B — cross-platform text sync and iPhone bridge

**Matrix scope:** SYNC-01; SYNC-02 remains permanently out of scope.

**Entry gate:** D3 approved backend/protocol/security/migration design.

Implement a backend-agnostic local sync journal with versioned records, device identity, origin, conflicts, tombstones, retry/backoff, and explicit account state. Use Credential Manager. Sync text/metadata only; never upload audio.

**Acceptance:** conflict/tombstone/offline/retry/duplicate/out-of-order/device-reset/account-switch/key-loss/deletion tests; iPhone compatibility fixtures; privacy/export/delete-account behavior; no audio path can enter an upload payload; visible origin and failure state.

## Phase 8 — desktop shell and release readiness

**Matrix scope:** TRAY-01, SHELL-01/START-01 verification, INSIGHT-01 after D4, SOUND-01 if approved, DIAG-01 support UX, UPD-01, SIGN-01 after D5, PKG-01, and release portions of TEST-01/QUAL-01.

**Objective:** make the native Windows experience distributable, supportable, updatable, and signed after product behavior is stable.

**Implementation slices:**

1. Drive a richer NotifyIcon menu from real app state; add recent/upcoming/record/update commands only when operational.
2. Finish accessibility, keyboard, DPI, multi-monitor, theme, startup/install state, and optional feedback-sound behavior.
3. Add redacted support-bundle/incident guidance and selected-model/provider identity.
4. Implement a signed update manifest/channel with staged replacement and rollback.
5. Authenticode-sign executable and installer with timestamp; package self-contained x64 artifacts; validate install/upgrade/uninstall/data preservation.
6. Run full CPU/CUDA/hardware/media/meeting/UI/package/installer/update qualification on clean supported Windows environments.

**Dependencies:** D5 is mandatory for release completion; D4 governs contribution telemetry/insights; release hosting and clean test machines.

**Acceptance:**

- No tray/menu/button action is a placeholder or no-op.
- Signed publisher identity validates for app and installer; tampered artifacts/feed are rejected.
- Clean install, upgrade, rollback, uninstall-with-data-preserved, and explicitly confirmed data removal pass.
- Package contains no Python/Electron/web/worker/debug/internal qualification artifacts.
- Updater handles offline, bad signature/hash, in-use file, disk full, cancel, and rollback.
- Full tests and targeted qualifications pass, dashboard is foregrounded, rendered state is inspected, and fresh logs contain none of the forbidden entries.

## Persistent-data and settings migration plan

Phase 0 changes no persistent data or settings. Future migrations must be versioned, atomic, backup-aware, and reversible where feasible.

| Phase | Expected migration surface | Required invariant |
|---|---|---|
| P1 | Model catalog/version/readiness metadata if persisted; cleanup model metadata. | Existing selected Parakeet/default settings remain valid; missing/removed model becomes an explicit unresolved selection, never a hidden fallback. |
| P2 | Settings schema 3 adds persisted filler-word removal; existing dictionary records retain their fields while matching becomes more conservative. | Existing selected model roles and settings persist; filler removal defaults on for migrated users and can be disabled; dictionary records preserve exact phrase/replacement data. |
| P3 | Meeting schema: manual notes, raw/edited transcript provenance, session status/journal, track metadata, folder parent/order, retranscription state. | Existing meetings/folders load losslessly; flat folders become roots; failed migration restores backup; generated and manual notes never merge implicitly. |
| P4 | Live model IDs, ownership enum, final model ID, VAD/checkpoint/gap/reconciliation metadata. | Legacy meetings are marked offline-final; no inferred live owner; raw contributions remain available. |
| P5 (finalization) | Session-journal schema 3 adds `MicrophonePartOffsetsMs`/`SystemPartOffsetsMs`, the meeting-time anchor of each captured part. | Schema 2 journals load unchanged and migrate to contiguous anchors, which is exactly the placement they already assumed; a missing or short anchor list falls back to contiguous rather than dropping parts; a recorded anchor can never rewind into previously captured audio. |
| P5 | Calendar settings, event cache cursor, dismissal/tombstone state; tokens only in Credential Manager. | No token in JSON/logs; disabled calendar performs no network work; stale dismissals prune deterministically. |
| P6 | Provider endpoints/nonsecret options, hook settings, auto-export destination/format/idempotency state. | Secrets stay protected; invalid executable/path disables with visible error; existing meetings do not retroactively run automation. |
| P7 | Sync identity/journal/version/conflict/tombstone/origin fields. | Audio is structurally excluded; local data remains usable offline and before sign-in; account removal policy is explicit. |
| P8 | Update channel/version state, shell preferences, support-bundle consent. | No update secret; release/stable channel cannot be switched silently; user data survives upgrade/uninstall by default. |

Migration tests must cover: current-to-new, one or more representative legacy schemas, already-migrated idempotence, unknown/newer schema refusal, corrupt primary with valid backup, corrupt backup, interrupted atomic replace, concurrent writers, and restart after failure.

## Evidence strategy

### Automated layers

1. **Pure state/policy tests:** hotkey, meeting lifecycle, model operation, final owner, VAD/reconciliation, detection resolver, calendar notification, provider, hook, sync, and updater transitions.
2. **Persistence/recovery tests:** settings and every durable record/journal migration, backup/quarantine, crash checkpoints, concurrency, cancellation, retry, and cleanup.
3. **Native adapter tests:** deterministic WAV fixtures, device abstraction failures, ONNX/Sherpa/LLamaSharp resource disposal, model hash/archive failures, decoder formats, job-object process termination.
4. **Contract tests:** provider/calendar/update/sync HTTP fixtures; no live secrets required for the normal suite.
5. **WPF behavior tests:** only meaningful navigation/action/state projection and accessibility contracts; visual/manual checks remain required.
6. **Packaging tests:** self-contained content allow/deny lists, native loader diagnostics, signing, installer install/upgrade/uninstall, updater tamper/rollback.

### Qualification evidence identity

Every generated qualification manifest must record:

- UTC timestamp and git/worktree identity or source hash;
- app/package version and executable SHA-256;
- Windows version, architecture, CPU, GPU/driver, RAM;
- selected engine/model/artifact hashes/provider and whether cache was cold/warm;
- audio/media fixture hash and duration;
- requested versus actual provider/device;
- pass/fail gates, warnings, cancellation/failure injection, memory/latency measurements;
- fresh log slice start marker and forbidden-entry result.

Evidence without enough identity to prove what ran is diagnostic material, not an acceptance result.

## Product/external decisions required

The detailed decision ledger lives in the parity matrix. The execution order must respect it:

- **D1:** supported ChatGPT subscription OAuth/backend contract.
- **D2:** Computer Use provider, allowlist, consent, observation/retention, and safety model.
- **D3:** cross-platform sync backend, identity, encryption, conflicts, deletion, and iPhone protocol.
- **D4:** local insights versus opt-in contribution/telemetry behavior.
- **D5:** Authenticode certificate, protected signing, timestamp, release feed, and updater.
- **D6:** promised Windows offline/live model catalog, licenses, artifacts, and performance/quality gates.

Unresolved decisions are not permission to ship a placeholder, fake state, private API imitation, or silent fallback. The affected UI must be absent or honestly disabled with a reason only if that disabled state is itself part of the approved bounded phase.

## Phase 1 handoff and next-phase readiness

The transcription-model platform portion of P1 is complete and its final gates are green: clean Debug build, 56/56 Windows tests, pinned preparation plus real-audio CUDA inference for all seven families, rebuilt zip smoke, isolated installer install/runtime/visible-launch/uninstall smoke, visible role/lifecycle UI inspection, and a clean fresh-log slice. P2 is technically unblocked. Live models, VAD, calendar, and later meeting architecture remain deferred to their assigned phases.

## Phase 2 handoff and next-phase readiness

Phase 2 source, unit, visible no-speech, privacy-lifecycle, and explicit CPU/CUDA model-identity smokes are green. It is not acceptance-complete: no agent may author the named human approvals for the dictation reference corpus or the four paste-target reports, and the physical default-device/disconnect/Bluetooth matrix has not been exercised. Those gates are specified in `PHASE2_DICTATION_QUALIFICATION.md` and fail closed. The next feature phase is not unblocked until a human operator supplies and passes that evidence; meeting and summary work must not be used to bypass it.

## Meeting-finalization pipeline hardening (2026-08-01)

A bounded pass over the finalization pipeline (final ASR → timestamp normalization → chronological merge → diarization → identity/alias → reconciliation → cleanup/dictionary → health warnings → persistence) found and fixed two defects and closed the evidence gap.

**Cross-channel timestamp normalization was missing.** Final ASR and diarization report positions inside a *concatenated* track, but the journal recorded only part filenames — no meeting-time anchor. Concatenation removes the wall-clock silence between parts, so any channel that started late, was repaired mid-meeting, or resumed after a suspend drifted against the other channel, and the chronological merge interleaved remote speech at the wrong point in the conversation. Session-journal schema 3 now records a per-part anchor; the coordinator stamps it at every real capture start, including repair restarts; and `MeetingTranscriptTimeline` maps transcript and diarization segments back onto meeting time before the merge. Schema 2 journals migrate to contiguous anchors, which is exactly what they already assumed.

**Duplicate removal could delete legitimate repeated speech.** The live session suppressed any consecutive identical text on a channel regardless of how far apart the two utterances were, so a speaker who genuinely said "no" twice lost the second line. Suppression is now bounded to a 1 s re-emission window.

Verified as already correct and left alone: speaker-prefix preservation (the cleanup service refuses a rewrite that changes the prefix structure, and dictionary correction only rewrites line bodies), cancellation during finalization, and recoverable-failure retry via `FinalizeRecoverableAsync`.

**Real multi-speaker evidence.** Three genuinely different human voices were concatenated into one remote track (A, B, C, then A again) and run through the real diarizer on the CUDA provider: 4 segments, 2 distinct speaker identities, 1567 ms, and the returning voice was correctly re-identified as `speaker_0` rather than a new speaker. The diarizer clustered the two short non-English voices B and C into one identity — a real model limitation on brief clips, recorded here rather than hidden. Test count moved 165 → 195.

## Phase 4 handoff and next-phase readiness

Phase 4 source, schema, and automated gates are green: clean Debug build with zero errors, 165/165 Windows tests both with and without the qualification artifact, restored-from-empty model preparation with pinned checksum verification, real `ar.wav` decoding and real Silero boundaries through the DLLs the WPF build emits, a full `MeetingLiveTranscriptionSession` run at CPU real-time factor 0.42, a visible dashboard exercise of both ownership modes in Settings and Models, and a fresh-log slice with no `ERROR`, `Unhandled UI exception`, or `XamlParseException`.

Phase 4 is **not** acceptance-complete. Its remaining gates are physical and cannot be authored by an agent: long-session latency/memory/thermal soak, a simultaneous multi-speaker Zoom/Teams/Meet meeting with live transcript visible, Bluetooth/default-route transitions during live capture, injected native crash mid-meeting, CUDA live inference (the live recognizer pins the CPU provider today), and human multilingual accuracy review. The real-time factor above is one fixture on one machine and is not a hardware matrix.

Phase 5 detection work (DET-01/DET-02/JOIN-01) is technically unblocked and does not depend on those open gates, because it does not share the streaming session or capture ownership. CAL-01 remains blocked on an approved Google OAuth client configuration and must not be started before it exists.
