export type AudioSource = "microphone" | "system" | "mixed" | "file";

export type PasteBehavior =
  | "draft"
  | "clipboard"
  | "draft-and-clipboard"
  | "active-app"
  | "draft-and-active-app";

export type AsrEngine = "whisper" | "parakeet-v3";

export type ModelProfile = "tiny" | "base" | "small" | "medium" | "large-v3-turbo";

export type TranscriptionMode = "preview" | "final";

export type SessionStatus = "processing" | "completed" | "failed";

export interface TranscriptSegment {
  id: string;
  speaker: string;
  startMs: number;
  endMs: number;
  text: string;
  confidence?: number;
}

export interface DictationRecord {
  id: string;
  timestamp: string;
  durationMs: number;
  text: string;
  wordCount: number;
  appContext?: string;
  modelProfile: ModelProfile;
  asrEngine: AsrEngine;
}

export interface MeetingRecord {
  id: string;
  title: string;
  createdAt: string;
  startedAt: string;
  endedAt?: string;
  durationMs?: number;
  source: AudioSource;
  status: SessionStatus;
  rawTranscript: string;
  formattedNotes: string;
  segments: TranscriptSegment[];
  warnings: string[];
  modelProfile: ModelProfile;
  asrEngine: AsrEngine;
  filePath?: string;
}

export interface DictionaryEntry {
  word: string;
  replacement?: string;
  variants: string[];
  matchingThreshold: number;
}

export interface AppSettings {
  hotkey: string;
  asrEngine: AsrEngine;
  dictationModelProfile: ModelProfile;
  pasteBehavior: PasteBehavior;
  maxDurationMs: number;
  minimumHoldMs: number;
  releaseDebounceMs: number;
  inputDeviceId: string | null;
}

export interface AppConfig {
  appName: string;
  dataDir: string;
  selectedPython: string | null;
  pythonCandidates: string[];
  offlineMode: boolean;
  settings: AppSettings;
}

export interface WorkerTranscriptionResult {
  transcriptText: string;
  detectedLanguage: string;
  durationMs: number;
  segments: TranscriptSegment[];
  warnings: string[];
}

export interface TranscribeCapturedAudioRequest {
  title: string;
  source: Extract<AudioSource, "microphone">;
  asrEngine: AsrEngine;
  modelProfile: ModelProfile;
  audioBuffer: ArrayBuffer;
  fileExtension: string;
  transcriptionMode?: TranscriptionMode;
  languageHint?: string;
}

export interface NativeDictationStartRequest {
  inputDeviceLabel?: string;
}

export interface NativeDictationStopRequest {
  title: string;
  source: Extract<AudioSource, "microphone">;
  asrEngine: AsrEngine;
  modelProfile: ModelProfile;
  transcriptionMode?: TranscriptionMode;
  languageHint?: string;
}

export interface DictationToastRequest {
  state: "recording" | "transcribing" | "success" | "error";
  title: string;
  message?: string;
  durationMs?: number;
}

export interface TranscribeFileRequest {
  title: string;
  filePath: string;
  source: AudioSource;
  asrEngine: AsrEngine;
  modelProfile: ModelProfile;
  transcriptionMode?: TranscriptionMode;
  languageHint?: string;
}

export interface DictationHotkeyEvent {
  accelerator: string;
  pressedAt: string;
  state: "down" | "up";
}

export interface DesktopApi {
  getConfig: () => Promise<AppConfig>;
  saveSettings: (settings: AppSettings) => Promise<AppSettings>;
  listDictations: () => Promise<DictationRecord[]>;
  saveDictation: (record: Omit<DictationRecord, "id">) => Promise<DictationRecord>;
  listMeetings: () => Promise<MeetingRecord[]>;
  transcribeCapturedAudio: (
    request: TranscribeCapturedAudioRequest
  ) => Promise<WorkerTranscriptionResult>;
  startNativeDictationCapture: (
    request: NativeDictationStartRequest
  ) => Promise<void>;
  stopNativeDictationCapture: (
    request: NativeDictationStopRequest
  ) => Promise<WorkerTranscriptionResult>;
  showDictationToast: (request: DictationToastRequest) => Promise<void>;
  transcribeFile: (request: TranscribeFileRequest) => Promise<MeetingRecord>;
  pickAudioFile: () => Promise<string | null>;
  writeTextToClipboard: (text: string) => Promise<void>;
  insertTextToActiveApp: (text: string) => Promise<void>;
  openPath: (path: string) => Promise<void>;
  onDictationHotkey: (
    callback: (event: DictationHotkeyEvent) => void
  ) => () => void;
}
