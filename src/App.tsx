import { useEffect, useRef, useState } from "react";
import type {
  AppConfig,
  AppSettings,
  AsrEngine,
  DictationRecord,
  MeetingRecord,
  ModelProfile,
  PasteBehavior,
  WorkerTranscriptionResult
} from "../shared/contracts";

type Section =
  | "dictations"
  | "meetings"
  | "dictionary"
  | "models"
  | "shortcuts"
  | "settings"
  | "about";
type DictationState = "idle" | "preparing" | "recording" | "transcribing";

const defaultSettings: AppSettings = {
  hotkey: "F8",
  asrEngine: "whisper",
  dictationModelProfile: "base",
  pasteBehavior: "draft-and-active-app",
  maxDurationMs: 0,
  minimumHoldMs: 80,
  releaseDebounceMs: 80,
  inputDeviceId: null
};

const sections: Array<{ id: Section; label: string; icon: string }> = [
  { id: "dictations", label: "Dictations", icon: "♪" },
  { id: "meetings", label: "Meetings", icon: "●●" },
  { id: "dictionary", label: "Dictionary", icon: "▤" },
  { id: "models", label: "Models", icon: "⇩" },
  { id: "shortcuts", label: "Shortcuts", icon: "⌨" },
  { id: "settings", label: "Settings", icon: "⚙" },
  { id: "about", label: "About", icon: "ⓘ" }
];

const modelProfiles: ModelProfile[] = [
  "tiny",
  "base",
  "small",
  "medium",
  "large-v3-turbo"
];
const engines: AsrEngine[] = ["whisper", "parakeet-v3"];
const pasteBehaviors: PasteBehavior[] = [
  "draft",
  "clipboard",
  "draft-and-clipboard",
  "active-app",
  "draft-and-active-app"
];
const hotkeys = ["F6", "F7", "F8", "F9", "F10", "F11", "F12"];

function countWords(text: string) {
  return text.trim().split(/\s+/).filter(Boolean).length;
}

function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return date.toLocaleString([], {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit"
  });
}

function formatDuration(ms?: number) {
  if (!ms) {
    return "0s";
  }
  return `${Math.max(1, Math.round(ms / 1000))}s`;
}

function formatTimeOnly(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "--:--";
  }
  return date.toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit"
  });
}

function dayHeader(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "RECENT";
  }
  const today = new Date();
  const yesterday = new Date();
  yesterday.setDate(today.getDate() - 1);
  if (date.toDateString() === today.toDateString()) {
    return "TODAY";
  }
  if (date.toDateString() === yesterday.toDateString()) {
    return "YESTERDAY";
  }
  return date
    .toLocaleDateString([], { weekday: "short", day: "numeric", month: "short" })
    .toUpperCase();
}

function groupDictations(records: DictationRecord[]) {
  const groups = new Map<string, DictationRecord[]>();
  for (const record of records) {
    const header = dayHeader(record.timestamp);
    groups.set(header, [...(groups.get(header) || []), record]);
  }
  return [...groups.entries()].map(([header, items]) => ({ header, items }));
}

function pickRecorderMimeType() {
  return (
    [
      "audio/webm;codecs=opus",
      "audio/webm",
      "audio/ogg;codecs=opus",
      "audio/mp4"
    ].find((type) => MediaRecorder.isTypeSupported(type)) || ""
  );
}

function extensionForMimeType(mimeType: string) {
  if (mimeType.includes("ogg")) {
    return "ogg";
  }
  if (mimeType.includes("mp4")) {
    return "m4a";
  }
  return "webm";
}

function modelLabel(profile: ModelProfile) {
  const labels: Record<ModelProfile, string> = {
    tiny: "Whisper Tiny",
    base: "Whisper Base",
    small: "Whisper Small",
    medium: "Whisper Medium",
    "large-v3-turbo": "Whisper Large Turbo"
  };
  return labels[profile];
}

function previewText(text: string) {
  const normalized = text.replace(/\s+/g, " ").trim();
  return normalized.length > 72 ? `${normalized.slice(0, 72)}...` : normalized;
}

function App() {
  const [section, setSection] = useState<Section>("dictations");
  const [config, setConfig] = useState<AppConfig | null>(null);
  const [settings, setSettings] = useState<AppSettings>(defaultSettings);
  const [settingsDraft, setSettingsDraft] =
    useState<AppSettings>(defaultSettings);
  const [dictations, setDictations] = useState<DictationRecord[]>([]);
  const [meetings, setMeetings] = useState<MeetingRecord[]>([]);
  const [selectedMeetingId, setSelectedMeetingId] = useState<string | null>(null);
  const [dictationState, setDictationState] = useState<DictationState>("idle");
  const [draftText, setDraftText] = useState("");
  const [lastResult, setLastResult] = useState<WorkerTranscriptionResult | null>(
    null
  );
  const [dictionaryDraft, setDictionaryDraft] = useState("");
  const [meetingTitle, setMeetingTitle] = useState("Untitled meeting");
  const [pickedFile, setPickedFile] = useState("");
  const [isTranscribingFile, setIsTranscribingFile] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [audioInputs, setAudioInputs] = useState<MediaDeviceInfo[]>([]);
  const [micTest, setMicTest] = useState<{
    deviceId: string | null;
    rms: number;
    peak: number;
    status: "idle" | "testing" | "done" | "failed";
  }>({ deviceId: null, rms: 0, peak: 0, status: "idle" });

  const streamRef = useRef<MediaStream | null>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const recorderMimeTypeRef = useRef("");
  const chunksRef = useRef<Blob[]>([]);
  const nativeCaptureRef = useRef(false);
  const startedAtRef = useRef<number>(0);
  const releaseTimerRef = useRef<number | null>(null);
  const maxTimerRef = useRef<number | null>(null);
  const dictationStateRef = useRef<DictationState>("idle");
  const settingsRef = useRef<AppSettings>(defaultSettings);

  const selectedMeeting =
    meetings.find((meeting) => meeting.id === selectedMeetingId) ||
    meetings[0] ||
    null;
  const appName = config?.appName || "Muesli";
  const meetingFolders = meetings.length
    ? [{ name: "All Meetings", count: meetings.length }]
    : [];
  const modelCards = [
    {
      title: "Whisper",
      description: "Universal local transcription. Works on CPU and NVIDIA systems.",
      badge: "CPU safe",
      size: "varies",
      active: settings.asrEngine === "whisper",
      engine: "whisper" as AsrEngine
    },
    {
      title: "Parakeet",
      description: "Optional NVIDIA backend for fast English dictation when CUDA dependencies are installed.",
      badge: "NVIDIA",
      size: "~1 GB",
      active: settings.asrEngine === "parakeet-v3",
      engine: "parakeet-v3" as AsrEngine
    }
  ];
  const totalWords =
    dictations.reduce((sum, item) => sum + item.wordCount, 0) +
    (lastResult ? countWords(lastResult.transcriptText) : 0);
  const dictationGroups = groupDictations(dictations);
  const averageWpm = dictations.length
    ? Math.round(
        dictations.reduce((sum, item) => {
          const minutes = Math.max((item.durationMs || 1) / 60000, 0.1);
          return sum + item.wordCount / minutes;
        }, 0) / dictations.length
      )
    : 0;

  useEffect(() => {
    void bootstrap();
    void refreshAudioInputs();
  }, []);

  useEffect(() => {
    dictationStateRef.current = dictationState;
  }, [dictationState]);

  useEffect(() => {
    settingsRef.current = settings;
  }, [settings]);

  useEffect(() => {
    const unsubscribe = window.desktopApi.onDictationHotkey((event) => {
      const activeSettings = settingsRef.current;
      if (event.state === "down") {
        if (releaseTimerRef.current !== null) {
          window.clearTimeout(releaseTimerRef.current);
          releaseTimerRef.current = null;
        }
        void startDictation();
        return;
      }

      const heldMs = Date.now() - startedAtRef.current;
      if (heldMs < activeSettings.minimumHoldMs) {
        stopTracks();
        setDictationState("idle");
        return;
      }

      releaseTimerRef.current = window.setTimeout(() => {
        releaseTimerRef.current = null;
        void stopDictation();
      }, activeSettings.releaseDebounceMs);
    });

    return () => {
      unsubscribe();
      if (releaseTimerRef.current !== null) {
        window.clearTimeout(releaseTimerRef.current);
        releaseTimerRef.current = null;
      }
    };
  }, []);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.repeat || event.key.toUpperCase() !== settings.hotkey) {
        return;
      }
      event.preventDefault();
      void startDictation();
    };
    const onKeyUp = (event: KeyboardEvent) => {
      if (event.key.toUpperCase() !== settings.hotkey) {
        return;
      }
      event.preventDefault();
      const heldMs = Date.now() - startedAtRef.current;
      if (heldMs < settingsRef.current.minimumHoldMs) {
        stopTracks();
        dictationStateRef.current = "idle";
        setDictationState("idle");
        return;
      }
      void stopDictation();
    };

    window.addEventListener("keydown", onKeyDown);
    window.addEventListener("keyup", onKeyUp);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("keyup", onKeyUp);
    };
  }, [settings.hotkey]);

  async function bootstrap() {
    const [loadedConfig, loadedDictations, loadedMeetings] = await Promise.all([
      window.desktopApi.getConfig(),
      window.desktopApi.listDictations(),
      window.desktopApi.listMeetings()
    ]);
    setConfig(loadedConfig);
    setSettings(loadedConfig.settings);
    setSettingsDraft(loadedConfig.settings);
    setDictations(loadedDictations);
    setMeetings(loadedMeetings);
    setSelectedMeetingId(loadedMeetings[0]?.id || null);
  }

  async function refreshAudioInputs() {
    if (!navigator.mediaDevices?.enumerateDevices) {
      return;
    }
    try {
      await navigator.mediaDevices.getUserMedia({ audio: true });
      const devices = await navigator.mediaDevices.enumerateDevices();
      setAudioInputs(devices.filter((device) => device.kind === "audioinput"));
    } catch {
      const devices = await navigator.mediaDevices.enumerateDevices();
      setAudioInputs(devices.filter((device) => device.kind === "audioinput"));
    }
  }

  async function testMicrophone(deviceId: string | null) {
    setMicTest({ deviceId, rms: 0, peak: 0, status: "testing" });
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: deviceId ? { deviceId: { exact: deviceId } } : true
      });
      const context = new AudioContext();
      const source = context.createMediaStreamSource(stream);
      const analyser = context.createAnalyser();
      analyser.fftSize = 2048;
      source.connect(analyser);
      const data = new Float32Array(analyser.fftSize);
      let maxRms = 0;
      let maxPeak = 0;
      const startedAt = performance.now();

      await new Promise<void>((resolve) => {
        const tick = () => {
          analyser.getFloatTimeDomainData(data);
          let sum = 0;
          let peak = 0;
          for (const sample of data) {
            sum += sample * sample;
            peak = Math.max(peak, Math.abs(sample));
          }
          const rms = Math.sqrt(sum / data.length);
          maxRms = Math.max(maxRms, rms);
          maxPeak = Math.max(maxPeak, peak);
          if (performance.now() - startedAt >= 1400) {
            resolve();
            return;
          }
          window.requestAnimationFrame(tick);
        };
        tick();
      });

      stream.getTracks().forEach((track) => track.stop());
      await context.close();
      setMicTest({ deviceId, rms: maxRms, peak: maxPeak, status: "done" });
    } catch {
      setMicTest({ deviceId, rms: 0, peak: 0, status: "failed" });
    }
  }

  function stopTracks() {
    if (maxTimerRef.current !== null) {
      window.clearTimeout(maxTimerRef.current);
      maxTimerRef.current = null;
    }
    const recorder = recorderRef.current;
    if (recorder) {
      recorder.ondataavailable = null;
      recorder.onstop = null;
      if (recorder.state !== "inactive") {
        recorder.stop();
      }
      recorderRef.current = null;
    }
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
  }

  async function startDictation() {
    if (dictationStateRef.current !== "idle") {
      return;
    }

    setError(null);
    dictationStateRef.current = "preparing";
    setDictationState("preparing");
    startedAtRef.current = Date.now();
    chunksRef.current = [];

    try {
      const selectedInput = audioInputs.find(
        (device) => device.deviceId === settings.inputDeviceId
      );
      await window.desktopApi.startNativeDictationCapture({
        inputDeviceLabel: selectedInput?.label
      });
      nativeCaptureRef.current = true;
      dictationStateRef.current = "recording";
      setDictationState("recording");
      void window.desktopApi.showDictationToast({
        state: "recording",
        title: "Listening",
        message: `Release ${settings.hotkey} to transcribe`,
        durationMs: 0
      });
      if (settings.maxDurationMs > 0) {
        maxTimerRef.current = window.setTimeout(
          () => void stopDictation(),
          settings.maxDurationMs
        );
      }
      return;
    } catch (nativeError) {
      console.warn("Native dictation capture failed, falling back to browser capture.", nativeError);
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: settings.inputDeviceId
          ? { deviceId: { exact: settings.inputDeviceId } }
          : true
      });
      streamRef.current = stream;
      const mimeType = pickRecorderMimeType();
      const recorder = new MediaRecorder(
        stream,
        mimeType ? { mimeType } : undefined
      );
      recorderRef.current = recorder;
      recorderMimeTypeRef.current = mimeType;
      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) {
          chunksRef.current.push(event.data);
        }
      };
      recorder.onstop = () => {
        void finishDictation();
      };
      recorder.start();
      dictationStateRef.current = "recording";
      setDictationState("recording");
      void window.desktopApi.showDictationToast({
        state: "recording",
        title: "Listening",
        message: `Release ${settings.hotkey} to transcribe`,
        durationMs: 0
      });
      if (settings.maxDurationMs > 0) {
        maxTimerRef.current = window.setTimeout(
          () => void stopDictation(),
          settings.maxDurationMs
        );
      }
    } catch (caught) {
      stopTracks();
      dictationStateRef.current = "idle";
      setDictationState("idle");
      setError(caught instanceof Error ? caught.message : String(caught));
    }
  }

  async function stopDictation() {
    if (nativeCaptureRef.current) {
      nativeCaptureRef.current = false;
      if (maxTimerRef.current !== null) {
        window.clearTimeout(maxTimerRef.current);
        maxTimerRef.current = null;
      }
      dictationStateRef.current = "transcribing";
      setDictationState("transcribing");
      void window.desktopApi.showDictationToast({
        state: "transcribing",
        title: "Transcribing",
        message: "Converting speech to text...",
        durationMs: 0
      });
      try {
        const result = await window.desktopApi.stopNativeDictationCapture({
          title: "Dictation",
          source: "microphone",
          asrEngine: settings.asrEngine,
          modelProfile: settings.dictationModelProfile,
          transcriptionMode: "final",
          languageHint: "en"
        });
        await handleDictationResult(result);
      } catch (caught) {
        setError(caught instanceof Error ? caught.message : String(caught));
        void window.desktopApi.showDictationToast({
          state: "error",
          title: "Dictation failed",
          message: caught instanceof Error ? caught.message : String(caught),
          durationMs: 3400
        });
      } finally {
        dictationStateRef.current = "idle";
        setDictationState("idle");
      }
      return;
    }

    const recorder = recorderRef.current;
    if (!recorder) {
      stopTracks();
      dictationStateRef.current = "idle";
      setDictationState("idle");
      return;
    }
    if (recorder.state !== "inactive") {
      dictationStateRef.current = "transcribing";
      setDictationState("transcribing");
      void window.desktopApi.showDictationToast({
        state: "transcribing",
        title: "Transcribing",
        message: "Converting speech to text...",
        durationMs: 0
      });
      recorder.stop();
    }
  }

  async function finishDictation() {
    const chunks = chunksRef.current;
    const mimeType =
      recorderMimeTypeRef.current || chunks.find((chunk) => chunk.type)?.type || "audio/webm";
    stopTracks();

    if (!chunks.length) {
      dictationStateRef.current = "idle";
      setDictationState("idle");
      return;
    }

    dictationStateRef.current = "transcribing";
    setDictationState("transcribing");
    void window.desktopApi.showDictationToast({
      state: "transcribing",
      title: "Transcribing",
      message: "Converting speech to text...",
      durationMs: 0
    });
    try {
      const capture = new Blob(chunks, { type: mimeType });
      const audioBuffer = await capture.arrayBuffer();
      const result = await window.desktopApi.transcribeCapturedAudio({
        title: "Dictation",
        source: "microphone",
        asrEngine: settings.asrEngine,
        modelProfile: settings.dictationModelProfile,
        audioBuffer,
        fileExtension: extensionForMimeType(mimeType),
        transcriptionMode: "final",
        languageHint: "en"
      });
      const text = result.transcriptText.trim();
      result.warnings.push(
        `Renderer capture chunks: ${chunks.length}`,
        `Renderer capture bytes: ${audioBuffer.byteLength}`,
        `Renderer capture mime: ${mimeType || "browser default"}`
      );
      await handleDictationResult(result);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught));
      void window.desktopApi.showDictationToast({
        state: "error",
        title: "Dictation failed",
        message: caught instanceof Error ? caught.message : String(caught),
        durationMs: 3400
      });
    } finally {
      dictationStateRef.current = "idle";
      setDictationState("idle");
    }
  }

  async function handleDictationResult(result: WorkerTranscriptionResult) {
    const text = result.transcriptText.trim();
    setLastResult(result);
    setDraftText(text);
    if (!text) {
      setError(["Transcription returned empty text.", ...result.warnings].join(" "));
      void window.desktopApi.showDictationToast({
        state: "error",
        title: "No speech detected",
        message: "Try again or check microphone input.",
        durationMs: 3000
      });
      return;
    }
    const saved = await window.desktopApi.saveDictation({
      timestamp: new Date().toISOString(),
      durationMs: Date.now() - startedAtRef.current,
      text,
      wordCount: countWords(text),
      modelProfile: settings.dictationModelProfile,
      asrEngine: settings.asrEngine
    });
    setDictations((current) => [saved, ...current]);
    void window.desktopApi.showDictationToast({
      state: "success",
      title: "Dictation ready",
      message: previewText(text),
      durationMs: 2600
    });
    await applyOutput(text);
  }

  async function applyOutput(text: string) {
    if (
      settings.pasteBehavior === "clipboard" ||
      settings.pasteBehavior === "draft-and-clipboard"
    ) {
      await window.desktopApi.writeTextToClipboard(text);
    }
    if (
      settings.pasteBehavior === "active-app" ||
      settings.pasteBehavior === "draft-and-active-app"
    ) {
      await window.desktopApi.insertTextToActiveApp(text);
    }
  }

  async function saveSettings() {
    const saved = await window.desktopApi.saveSettings(settingsDraft);
    setSettings(saved);
    setSettingsDraft(saved);
  }

  async function pickAudioFile() {
    const file = await window.desktopApi.pickAudioFile();
    if (file) {
      setPickedFile(file);
    }
  }

  async function transcribeFile() {
    if (!pickedFile) {
      setError("Choose an audio or video file first.");
      return;
    }
    setIsTranscribingFile(true);
    setError(null);
    try {
      const meeting = await window.desktopApi.transcribeFile({
        title: meetingTitle.trim() || "Untitled meeting",
        filePath: pickedFile,
        source: "file",
        asrEngine: settings.asrEngine,
        modelProfile: settings.dictationModelProfile,
        transcriptionMode: "final",
        languageHint: "en"
      });
      setMeetings((current) => [meeting, ...current]);
      setSelectedMeetingId(meeting.id);
      setSection("meetings");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught));
    } finally {
      setIsTranscribingFile(false);
    }
  }

  return (
    <div className="app-frame">
      <div className="muesli-shell">
      <aside className="sidebar">
        <div className="brand">
          <div className="wave-mark" aria-hidden="true">
            {[0.45, 0.65, 0.9, 1, 0.45, 1, 0.9, 0.65, 0.45].map(
              (height, index) => (
                <span key={index} style={{ height: `${height * 28}px` }} />
              )
            )}
          </div>
          <div>
            <strong>{appName.toLowerCase()}</strong>
            <small>Local dictation</small>
          </div>
        </div>

        <div className="sidebar-search" aria-label="Search placeholder">
          <span>⌕</span>
          <input placeholder="Search..." readOnly />
        </div>

        <nav className="nav">
          {sections.slice(0, 5).map((item) => (
            <button
              key={item.id}
              className={section === item.id ? "active" : ""}
              type="button"
              onClick={() => setSection(item.id)}
            >
              <span>{item.icon}</span>
              {item.label}
            </button>
          ))}
          {meetingFolders.length ? (
            <div className="folder-list">
              {meetingFolders.map(({ name, count }) => (
                <button
                  key={name}
                  type="button"
                  className={`folder-row ${section === "meetings" ? "active" : ""}`}
                  onClick={() => setSection("meetings")}
                >
                  <span>▭</span>
                  {name}
                  <em>{count}</em>
                </button>
              ))}
            </div>
          ) : null}
        </nav>

        <div className="sidebar-bottom">
          {sections.slice(5).map((item) => (
            <button
              key={item.id}
              className={section === item.id ? "active" : ""}
              type="button"
              onClick={() => setSection(item.id)}
            >
              <span>{item.icon}</span>
              {item.label}
            </button>
          ))}
        </div>
      </aside>

      <main className="main">
        {error ? <div className="error">{error}</div> : null}

        {section === "dictations" ? (
          <section className="page dictations-page">
            <div className="stats-grid">
              <div className="stat-card"><span>●</span><strong>1</strong><small>day streak</small></div>
              <div className="stat-card"><span>A</span><strong>{totalWords >= 1000 ? `${(totalWords / 1000).toFixed(1)}k` : totalWords}</strong><small>words dictated</small></div>
              <div className="stat-card"><span>◷</span><strong>{averageWpm}</strong><small>avg WPM</small></div>
              <div className="stat-card"><span>●●</span><strong>{meetings.length || 0}</strong><small>meetings</small></div>
            </div>

            <div className="dictation-toolbar">
              <span>{dictationGroups[0]?.header || "TODAY"}</span>
              <div>
                <button type="button">filter</button>
                <button type="button">⌄</button>
              </div>
            </div>

            <div className="dictation-table">
              {dictationGroups.length ? (
                dictationGroups.map((group) => (
                  <div key={group.header} className="dictation-group">
                    {group.header !== (dictationGroups[0]?.header || "") ? (
                      <h3>{group.header}</h3>
                    ) : null}
                    {group.items.map((item) => (
                      <button
                        type="button"
                        className="dictation-row"
                        key={item.id}
                        onClick={() => setDraftText(item.text)}
                      >
                        <time>{formatTimeOnly(item.timestamp)}</time>
                        <p>{item.text}</p>
                        <span className="row-tools">
                          <em>{item.wordCount}w</em>
                          <em>copy</em>
                        </span>
                      </button>
                    ))}
                  </div>
                ))
              ) : (
                <div className="empty-state">
                  <strong>No dictations yet</strong>
                  <span>Hold {settings.hotkey} to start dictating.</span>
                </div>
              )}
            </div>

            <div className="dictation-control">
              <select
                value={settingsDraft.inputDeviceId || ""}
                onChange={(event) => {
                  const next = {
                    ...settingsDraft,
                    inputDeviceId: event.target.value || null
                  };
                  setSettingsDraft(next);
                  void window.desktopApi.saveSettings(next).then((saved) => {
                    setSettings(saved);
                    setSettingsDraft(saved);
                  });
                }}
              >
                <option value="">System default microphone</option>
                {audioInputs.map((device, index) => (
                  <option key={device.deviceId} value={device.deviceId}>
                    {device.label || `Microphone ${index + 1}`}
                  </option>
                ))}
              </select>
              <button
                className="primary"
                type="button"
                onPointerDown={(event) => {
                  event.currentTarget.setPointerCapture(event.pointerId);
                  void startDictation();
                }}
                onPointerUp={(event) => {
                  event.currentTarget.releasePointerCapture(event.pointerId);
                  void stopDictation();
                }}
                onPointerCancel={() => void stopDictation()}
              >
                {dictationState === "recording" ? "Release to transcribe" : "Hold to dictate"}
              </button>
              <button type="button" onClick={() => void testMicrophone(settingsDraft.inputDeviceId)}>
                {micTest.status === "testing" ? "Testing..." : "Test mic"}
              </button>
            </div>
          </section>
        ) : null}

        {section === "meetings" ? (
          <section className="page meetings-page">
            <div className="page-header">
              <div>
                <h1>Meetings</h1>
                <p>{meetings.length} meeting{meetings.length === 1 ? "" : "s"} · Open a meeting to review notes, transcript, and summaries</p>
              </div>
              <div className="header-actions">
                <button type="button">Newest first⌄</button>
                <button type="button">filter⌄</button>
                <button type="button">Manage Templates</button>
              </div>
            </div>
            <div className="meeting-cards">
              {meetings.length ? (
                meetings.map((meeting) => (
                  <button
                    type="button"
                    key={meeting.id}
                    className={`meeting-card ${
                      selectedMeeting?.id === meeting.id ? "selected" : ""
                    }`}
                    onClick={() => setSelectedMeetingId(meeting.id)}
                  >
                    <strong>{meeting.title}</strong>
                    <span>{formatDate(meeting.createdAt)} · {formatDuration(meeting.durationMs)}</span>
                    <p>{meeting.formattedNotes || meeting.rawTranscript || "Meeting summary will appear here."}</p>
                  </button>
                ))
              ) : (
                <div className="empty-state">
                  <strong>No meetings yet</strong>
                  <span>Transcribe an audio file to create the first meeting record.</span>
                </div>
              )}
            </div>
            <div className="capture-box compact-capture">
              <input value={meetingTitle} onChange={(event) => setMeetingTitle(event.target.value)} />
              <input value={pickedFile} readOnly placeholder="Audio/video file" />
              <button type="button" onClick={pickAudioFile}>Browse</button>
              <button type="button" disabled={isTranscribingFile} onClick={transcribeFile}>
                {isTranscribingFile ? "Transcribing..." : "Transcribe file"}
              </button>
            </div>
          </section>
        ) : null}

        {section === "dictionary" ? (
          <section className="page dictionary-page">
            <div className="page-header">
              <div>
                <h1>Dictionary</h1>
                <p>Add custom words for names, brands, and domain terms, and tune how aggressively each entry should fuzzy-match transcription errors.</p>
              </div>
              <button type="button">+ Add new</button>
            </div>
            <div className="dictionary-card">
              {(["muesli |", "newsly | muesli"]).map((line, index) => {
                const [word, replacement = ""] = line.split("|").map((part) => part.trim());
                return (
                  <div className="dictionary-row" key={index}>
                    <div className="dictionary-fields">
                      <input value={word} onChange={(event) => setDictionaryDraft(event.target.value)} />
                      <input value={replacement} placeholder="Replace with (optional)" onChange={(event) => setDictionaryDraft(event.target.value)} />
                    </div>
                    <label>
                      Matching threshold
                      <input type="range" min="0" max="1" step="0.01" defaultValue="0.85" />
                      <strong>0.85</strong>
                    </label>
                    <div className="row-actions"><button type="button">Delete</button><button type="button">Save</button></div>
                  </div>
                );
              })}
            </div>
          </section>
        ) : null}

        {section === "models" ? (
          <section className="page models-page">
            <div className="page-header">
              <div>
                <h1>Models</h1>
                <p>Download and manage transcription models. The active model is used for dictation.</p>
              </div>
            </div>
            <div className="model-list">
              {modelCards.map((card) => (
                <article key={card.title} className={`model-card ${card.active ? "selected" : ""}`}>
                  <div className="model-head">
                    <h2>{card.title} <small>{card.badge}</small></h2>
                    <span>{card.active ? "Active" : "Available"}</span>
                  </div>
                  <p>{card.description}</p>
                  <div className="model-form">
                    <label>Variant</label>
                    <select
                      value={settingsDraft.dictationModelProfile}
                      onChange={(event) =>
                        setSettingsDraft({
                          ...settingsDraft,
                          dictationModelProfile: event.target.value as ModelProfile
                        })
                      }
                    >
                      {modelProfiles.map((profile) => (
                        <option key={profile} value={profile}>{modelLabel(profile)}</option>
                      ))}
                    </select>
                    <em>{card.size}</em>
                  </div>
                  <p>{card.engine === "whisper" ? "Use Base/Small for CPU laptops and Large Turbo for NVIDIA systems." : "Install optional NVIDIA dependencies before selecting this backend."}</p>
                  {!card.active ? (
                    <button
                      type="button"
                      onClick={() => {
                        const next = { ...settingsDraft, asrEngine: card.engine };
                        setSettingsDraft(next);
                        void window.desktopApi.saveSettings(next).then((saved) => {
                          setSettings(saved);
                          setSettingsDraft(saved);
                        });
                      }}
                    >
                      Set Active
                    </button>
                  ) : null}
                </article>
              ))}
              <article className="model-card collapsed">
                <h2>› Experimental</h2>
                <p>Qwen and streaming backends. Hidden by default because these are still slower and less polished.</p>
              </article>
            </div>
          </section>
        ) : null}

        {section === "shortcuts" ? (
          <section className="page">
            <SettingsPanel
              title="Dictation hotkey"
              settingsDraft={settingsDraft}
              setSettingsDraft={setSettingsDraft}
              saveSettings={saveSettings}
              audioInputs={audioInputs}
              compact
            />
          </section>
        ) : null}

        {section === "settings" ? (
          <section className="page">
            <SettingsPanel
              title="Transcription pipeline"
              settingsDraft={settingsDraft}
              setSettingsDraft={setSettingsDraft}
              saveSettings={saveSettings}
              dataDir={config?.dataDir}
              audioInputs={audioInputs}
            />
          </section>
        ) : null}

        {section === "about" ? (
          <section className="page about-panel">
            <div className="wave-mark large" aria-hidden="true">
              {[0.45, 0.65, 0.9, 1, 0.45, 1, 0.9, 0.65, 0.45].map(
                (height, index) => (
                  <span key={index} style={{ height: `${height * 54}px` }} />
                )
              )}
            </div>
            <h2>Muesli</h2>
            <p>Local-first dictation and meeting transcription for Windows and macOS.</p>
          </section>
        ) : null}
      </main>
      </div>
    </div>
  );
}

function SettingsPanel({
  title,
  settingsDraft,
  setSettingsDraft,
  saveSettings,
  dataDir,
  audioInputs,
  compact = false
}: {
  title: string;
  settingsDraft: AppSettings;
  setSettingsDraft: (settings: AppSettings) => void;
  saveSettings: () => Promise<void>;
  dataDir?: string;
  audioInputs: MediaDeviceInfo[];
  compact?: boolean;
}) {
  return (
    <section className="panel single-panel">
      <div className="panel-heading">
        <div>
          <p className="eyebrow">Settings</p>
          <h2>{title}</h2>
        </div>
        <button type="button" onClick={() => void saveSettings()}>
          Save
        </button>
      </div>
      <div className="settings-grid">
        <label>
          Hotkey
          <select
            value={settingsDraft.hotkey}
            onChange={(event) =>
              setSettingsDraft({ ...settingsDraft, hotkey: event.target.value })
            }
          >
            {hotkeys.map((hotkey) => (
              <option key={hotkey} value={hotkey}>
                {hotkey}
              </option>
            ))}
          </select>
        </label>
        <label>
          Output
          <select
            value={settingsDraft.pasteBehavior}
            onChange={(event) =>
              setSettingsDraft({
                ...settingsDraft,
                pasteBehavior: event.target.value as PasteBehavior
              })
            }
          >
            {pasteBehaviors.map((behavior) => (
              <option key={behavior} value={behavior}>
                {behavior}
              </option>
            ))}
          </select>
        </label>
        {!compact ? (
          <>
            <label>
              Microphone
              <select
                value={settingsDraft.inputDeviceId || ""}
                onChange={(event) =>
                  setSettingsDraft({
                    ...settingsDraft,
                    inputDeviceId: event.target.value || null
                  })
                }
              >
                <option value="">System default</option>
                {audioInputs.map((device, index) => (
                  <option key={device.deviceId} value={device.deviceId}>
                    {device.label || `Microphone ${index + 1}`}
                  </option>
                ))}
              </select>
            </label>
            <label>
              ASR engine
              <select
                value={settingsDraft.asrEngine}
                onChange={(event) =>
                  setSettingsDraft({
                    ...settingsDraft,
                    asrEngine: event.target.value as AsrEngine
                  })
                }
              >
                {engines.map((engine) => (
                  <option key={engine} value={engine}>
                    {engine}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Model
              <select
                value={settingsDraft.dictationModelProfile}
                onChange={(event) =>
                  setSettingsDraft({
                    ...settingsDraft,
                    dictationModelProfile: event.target.value as ModelProfile
                  })
                }
              >
                {modelProfiles.map((profile) => (
                  <option key={profile} value={profile}>
                    {modelLabel(profile)}
                  </option>
                ))}
              </select>
            </label>
          </>
        ) : null}
      </div>
      {dataDir ? <p className="muted">Data directory: {dataDir}</p> : null}
    </section>
  );
}

export default App;
