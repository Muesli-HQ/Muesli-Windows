import {
  app,
  BrowserWindow,
  clipboard,
  dialog,
  ipcMain,
  Menu,
  nativeImage,
  screen,
  shell,
  Tray
} from "electron";
import { access, copyFile, mkdir, readFile, unlink, writeFile } from "node:fs/promises";
import { constants } from "node:fs";
import path from "node:path";
import os from "node:os";
import { spawn } from "node:child_process";
import type { ChildProcessWithoutNullStreams } from "node:child_process";
import type {
  AppConfig,
  AppSettings,
  AsrEngine,
  DictationHotkeyEvent,
  DictationRecord,
  DictationToastRequest,
  MeetingRecord,
  NativeDictationStartRequest,
  NativeDictationStopRequest,
  TranscribeCapturedAudioRequest,
  TranscribeFileRequest,
  WorkerTranscriptionResult
} from "../shared/contracts";

const isDev = !app.isPackaged;
const rendererUrl = "http://localhost:5173";
const settingsFileName = "settings.json";
const dictationsFileName = "dictations.json";
const meetingsFileName = "meetings.json";
const trayIconDataUrl =
  "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAMAAAAoLQ9TAAAAMFBMVEUAAAARERERERERERERERERERERERERERERERERERERERERERERERERERERERHc1d7SAAAAD3RSTlMAECAwQFBgcICPn6+/z9/vg3V1AAAAVElEQVQY02NgoBAw0tLS0tAQwMDAIMHIyMjAwMDw//8/AxMTE0MDA8P///8YGBhYWFiYmJgYGRmZkZGxsbGJkYGBiYmJh4eHh4eJgYGFiZWVgAAAwA0NQd9h9NP7QAAAABJRU5ErkJggg==";

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

let mainWindow: BrowserWindow | null = null;
let tray: Tray | null = null;
let isQuitting = false;
let hotkeyWatcher: ChildProcessWithoutNullStreams | null = null;
let transcriptionWorker: PersistentWorkerBridge | null = null;
let requestCounter = 0;
let toastWindow: BrowserWindow | null = null;
let toastTimer: NodeJS.Timeout | null = null;
let nativeCapture: {
  child: ChildProcessWithoutNullStreams;
  filePath: string;
  deviceName: string;
  startedAt: number;
} | null = null;

type WorkerEnvelope = {
  id: string;
  ok: boolean;
  result?: WorkerTranscriptionResult;
  error?: string;
};

function createWindow() {
  if (mainWindow) {
    return mainWindow;
  }

  const win = new BrowserWindow({
    width: 1120,
    height: 790,
    minWidth: 900,
    minHeight: 600,
    backgroundColor: "#111214",
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      backgroundThrottling: false
    }
  });

  win.on("close", (event) => {
    if (isQuitting) {
      return;
    }
    event.preventDefault();
    win.hide();
    win.setSkipTaskbar(true);
  });

  win.on("closed", () => {
    mainWindow = null;
  });

  if (isDev) {
    void win.loadURL(rendererUrl);
  } else {
    void win.loadFile(path.join(app.getAppPath(), "dist", "index.html"));
  }

  mainWindow = win;
  return win;
}

function showMainWindow() {
  const win = createWindow();
  win.show();
  win.setSkipTaskbar(false);
  if (win.isMinimized()) {
    win.restore();
  }
  win.focus();
}

function escapeHtml(value: string) {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function toastHtml(request: DictationToastRequest) {
  const stateLabel: Record<DictationToastRequest["state"], string> = {
    recording: "Recording",
    transcribing: "Transcribing",
    success: "Ready",
    error: "Needs attention"
  };
  return `
<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  <style>
    * { box-sizing: border-box; }
    html, body {
      margin: 0;
      width: 100%;
      height: 100%;
      overflow: hidden;
      color: rgba(255,255,255,.94);
      font-family: "Segoe UI", sans-serif;
      background: transparent;
    }
    .toast {
      width: 100%;
      height: 100%;
      display: grid;
      grid-template-columns: 38px minmax(0, 1fr);
      gap: 12px;
      align-items: center;
      padding: 14px 16px;
      border: 1px solid rgba(255,255,255,.12);
      border-radius: 18px;
      background: rgba(24,25,28,.94);
      box-shadow: 0 22px 60px rgba(0,0,0,.38);
      backdrop-filter: blur(18px);
    }
    .mark {
      width: 38px;
      height: 38px;
      display: grid;
      place-items: center;
      border-radius: 13px;
      background: rgba(107,163,247,.16);
    }
    .dot {
      width: 13px;
      height: 13px;
      border-radius: 999px;
      background: #6ba3f7;
    }
    .recording .dot {
      background: #ef4444;
      animation: pulse .75s ease-in-out infinite alternate;
    }
    .transcribing .dot { background: #f59e0b; }
    .success .dot { background: #34d399; }
    .error .dot { background: #f87171; }
    strong {
      display: block;
      font-size: 14px;
      letter-spacing: -.01em;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    span {
      display: block;
      margin-top: 3px;
      color: rgba(255,255,255,.56);
      font-size: 12px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    @keyframes pulse {
      from { transform: scale(.72); opacity: .66; }
      to { transform: scale(1.12); opacity: 1; }
    }
  </style>
</head>
<body>
  <div class="toast ${request.state}">
    <div class="mark"><div class="dot"></div></div>
    <div>
      <strong>${escapeHtml(request.title || stateLabel[request.state])}</strong>
      <span>${escapeHtml(request.message || stateLabel[request.state])}</span>
    </div>
  </div>
</body>
</html>`;
}

function showDictationToast(request: DictationToastRequest) {
  const width = 360;
  const height = 72;
  const area = screen.getPrimaryDisplay().workArea;
  const x = Math.round(area.x + area.width / 2 - width / 2);
  const y = Math.round(area.y + 24);

  if (!toastWindow || toastWindow.isDestroyed()) {
    toastWindow = new BrowserWindow({
      width,
      height,
      x,
      y,
      frame: false,
      resizable: false,
      movable: false,
      alwaysOnTop: true,
      skipTaskbar: true,
      focusable: false,
      transparent: true,
      backgroundColor: "#00000000",
      webPreferences: {
        contextIsolation: true,
        nodeIntegration: false
      }
    });
    toastWindow.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });
    toastWindow.setAlwaysOnTop(true, "screen-saver");
    toastWindow.on("closed", () => {
      toastWindow = null;
    });
  } else {
    toastWindow.setBounds({ x, y, width, height });
  }

  void toastWindow.loadURL(
    `data:text/html;charset=utf-8,${encodeURIComponent(toastHtml(request))}`
  );
  toastWindow.showInactive();

  if (toastTimer) {
    clearTimeout(toastTimer);
    toastTimer = null;
  }
  if (request.durationMs !== 0) {
    toastTimer = setTimeout(() => {
      toastWindow?.hide();
      toastTimer = null;
    }, request.durationMs || 2200);
  }
}

function createTray() {
  if (tray) {
    return tray;
  }

  const icon = nativeImage.createFromDataURL(trayIconDataUrl).resize({
    width: 16,
    height: 16
  });
  tray = new Tray(icon);
  tray.setToolTip("Muesli");
  tray.setContextMenu(
    Menu.buildFromTemplate([
      { label: "Open Muesli", click: showMainWindow },
      {
        label: "Quit",
        click: () => {
          isQuitting = true;
          app.quit();
        }
      }
    ])
  );
  tray.on("click", showMainWindow);
  return tray;
}

async function dataDir() {
  const dir = path.join(app.getPath("userData"), "data");
  await mkdir(dir, { recursive: true });
  return dir;
}

async function capturesDir() {
  const dir = path.join(app.getPath("userData"), "captures");
  await mkdir(dir, { recursive: true });
  return dir;
}

async function readJson<T>(fileName: string, fallback: T): Promise<T> {
  try {
    const raw = await readFile(path.join(await dataDir(), fileName), "utf8");
    return JSON.parse(raw) as T;
  } catch {
    return fallback;
  }
}

async function writeJson<T>(fileName: string, payload: T) {
  await writeFile(
    path.join(await dataDir(), fileName),
    JSON.stringify(payload, null, 2),
    "utf8"
  );
}

async function loadSettings(): Promise<AppSettings> {
  const settings = await readJson<Partial<AppSettings>>(
    settingsFileName,
    defaultSettings
  );
  return normalizeSettings(settings);
}

async function saveSettings(settings: AppSettings) {
  const normalized = normalizeSettings(settings);
  await writeJson(settingsFileName, normalized);
  await restartHotkeyWatcher(normalized.hotkey);
  return normalized;
}

function normalizeSettings(settings: Partial<AppSettings>): AppSettings {
  const normalized = { ...defaultSettings, ...settings };
  if (!Number.isFinite(normalized.maxDurationMs) || normalized.maxDurationMs < 0) {
    normalized.maxDurationMs = 0;
  }
  return normalized;
}

async function listDictations() {
  return readJson<DictationRecord[]>(dictationsFileName, []);
}

async function saveDictation(record: Omit<DictationRecord, "id">) {
  const dictations = await listDictations();
  const saved: DictationRecord = {
    ...record,
    id: `dict_${Date.now()}`
  };
  await writeJson(dictationsFileName, [saved, ...dictations]);
  return saved;
}

async function listMeetings() {
  return readJson<MeetingRecord[]>(meetingsFileName, []);
}

async function saveMeeting(meeting: MeetingRecord) {
  const meetings = await listMeetings();
  await writeJson(meetingsFileName, [meeting, ...meetings]);
  return meeting;
}

function workerScriptPath() {
  return path.join(app.getAppPath(), "worker", "transcribe_worker.py");
}

function pythonCandidates() {
  return [
    process.env.MUESLI_PYTHON,
    path.join(os.homedir(), "projects", "muesli", ".venv", "Scripts", "python.exe"),
    path.join(os.homedir(), "AppData", "Local", "Programs", "Python", "Python312", "python.exe"),
    "python"
  ].filter((value): value is string => Boolean(value));
}

async function findPythonExecutable() {
  for (const candidate of pythonCandidates()) {
    try {
      if (candidate !== "python") {
        await access(candidate, constants.X_OK);
      }
      return candidate;
    } catch {
      continue;
    }
  }
  return null;
}

class PersistentWorkerBridge {
  private child: ChildProcessWithoutNullStreams | null = null;
  private pending = new Map<
    string,
    {
      resolve: (value: WorkerTranscriptionResult) => void;
      reject: (error: Error) => void;
    }
  >();
  private buffer = "";
  private stderr = "";

  constructor(private readonly python: string) {}

  start() {
    if (this.child) {
      return;
    }

    this.child = spawn(this.python, [workerScriptPath(), "server"], {
      windowsHide: true
    });
    this.child.stdout.on("data", (chunk) => this.handleStdout(chunk.toString()));
    this.child.stderr.on("data", (chunk) => {
      this.stderr += chunk.toString();
    });
    this.child.on("close", (code) => {
      const error = new Error(
        `Transcription worker exited with code ${code}. ${this.stderr.trim()}`
      );
      for (const request of this.pending.values()) {
        request.reject(error);
      }
      this.pending.clear();
      this.child = null;
      this.buffer = "";
    });
  }

  request(payload: {
    title: string;
    inputPath: string;
    source: string;
    asrEngine: AsrEngine;
    modelProfile: string;
    transcriptionMode?: string;
    languageHint?: string;
    audioBase64?: string;
  }) {
    this.start();
    if (!this.child) {
      return Promise.reject(new Error("Transcription worker did not start."));
    }

    const id = `req_${Date.now()}_${requestCounter++}`;
    const message = {
      id,
      command: "transcribe",
      payload: {
        title: payload.title,
        input_path: payload.inputPath,
        source: payload.source,
        asr_engine: payload.asrEngine,
        model: payload.modelProfile,
        transcription_mode: payload.transcriptionMode || "final",
        language_hint: payload.languageHint || "",
        audio_base64: payload.audioBase64 || ""
      }
    };

    return new Promise<WorkerTranscriptionResult>((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.child?.stdin.write(`${JSON.stringify(message)}\n`, (error) => {
        if (error) {
          this.pending.delete(id);
          reject(error);
        }
      });
    });
  }

  dispose() {
    this.child?.kill();
    this.child = null;
    this.pending.clear();
  }

  private handleStdout(chunk: string) {
    this.buffer += chunk;
    let newlineIndex = this.buffer.indexOf("\n");
    while (newlineIndex >= 0) {
      const line = this.buffer.slice(0, newlineIndex).trim();
      this.buffer = this.buffer.slice(newlineIndex + 1);
      if (line) {
        this.handleLine(line);
      }
      newlineIndex = this.buffer.indexOf("\n");
    }
  }

  private handleLine(line: string) {
    let envelope: WorkerEnvelope;
    try {
      envelope = JSON.parse(line) as WorkerEnvelope;
    } catch {
      return;
    }
    const pending = this.pending.get(envelope.id);
    if (!pending) {
      return;
    }
    this.pending.delete(envelope.id);
    if (!envelope.ok || !envelope.result) {
      pending.reject(new Error(envelope.error || "Transcription failed."));
      return;
    }
    if (this.stderr.trim()) {
      envelope.result.warnings.push(`Worker stderr: ${this.stderr.trim()}`);
      this.stderr = "";
    }
    pending.resolve(envelope.result);
  }
}

async function getTranscriptionWorker() {
  const python = await findPythonExecutable();
  if (!python) {
    throw new Error("Python was not found. Set MUESLI_PYTHON or install Python.");
  }
  if (!transcriptionWorker) {
    transcriptionWorker = new PersistentWorkerBridge(python);
  }
  return transcriptionWorker;
}

async function runWorker(payload: {
  title: string;
  inputPath: string;
  source: string;
  asrEngine: AsrEngine;
  modelProfile: string;
  transcriptionMode?: string;
  languageHint?: string;
  audioBase64?: string;
}) {
  const worker = await getTranscriptionWorker();
  return worker.request(payload);
}

async function transcribeCapturedAudio(request: TranscribeCapturedAudioRequest) {
  const extension = sanitizeExtension(request.fileExtension);
  const capturePath = path.join(
    await capturesDir(),
    `dictation_${Date.now()}.${extension}`
  );

  await writeFile(capturePath, Buffer.from(request.audioBuffer));
  const debugCapturePath = path.join(await capturesDir(), `last-dictation.${extension}`);
  await copyFile(capturePath, debugCapturePath).catch(() => undefined);
  try {
    const result = await runWorker({
      title: request.title,
      inputPath: `memory://dictation.${extension}`,
      source: request.source,
      asrEngine: request.asrEngine,
      modelProfile: request.modelProfile,
      transcriptionMode: request.transcriptionMode,
      languageHint: request.languageHint,
      audioBase64: Buffer.from(request.audioBuffer).toString("base64")
    });
    result.warnings.push(
      `Captured audio file: ${path.basename(capturePath)}`,
      `Last capture saved: ${debugCapturePath}`,
      `Captured audio bytes: ${Buffer.byteLength(Buffer.from(request.audioBuffer))}`,
      `Captured audio extension: ${extension}`
    );
    return result;
  } finally {
    await unlink(capturePath).catch(() => undefined);
  }
}

async function listDirectShowAudioDevices() {
  const output = await new Promise<string>((resolve) => {
    const child = spawn(
      "ffmpeg",
      ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"],
      { windowsHide: true }
    );
    let combined = "";
    child.stdout.on("data", (chunk) => {
      combined += chunk.toString();
    });
    child.stderr.on("data", (chunk) => {
      combined += chunk.toString();
    });
    child.on("error", () => resolve(""));
    child.on("close", () => resolve(combined));
  });

  return [...output.matchAll(/"([^"]+)" \(audio\)/g)].map((match) => match[1]);
}

function pickDirectShowDevice(devices: string[], preferredLabel?: string) {
  const normalizedPreferred = preferredLabel?.trim().toLowerCase();
  if (normalizedPreferred) {
    const exact = devices.find(
      (device) => device.trim().toLowerCase() === normalizedPreferred
    );
    if (exact) {
      return exact;
    }
    const fuzzy = devices.find((device) => {
      const normalizedDevice = device.toLowerCase();
      return (
        normalizedDevice.includes(normalizedPreferred) ||
        normalizedPreferred.includes(normalizedDevice)
      );
    });
    if (fuzzy) {
      return fuzzy;
    }
  }

  return (
    devices.find((device) => /microphone array/i.test(device)) ||
    devices.find(
      (device) =>
        /microphone/i.test(device) &&
        !/steam|streaming|virtual|cable|monitor/i.test(device)
    ) ||
    devices[0]
  );
}

async function startNativeDictationCapture(request: NativeDictationStartRequest) {
  if (nativeCapture) {
    return;
  }

  const devices = await listDirectShowAudioDevices();
  const deviceName = pickDirectShowDevice(devices, request.inputDeviceLabel);
  if (!deviceName) {
    throw new Error("No DirectShow microphone was found for native capture.");
  }

  const capturePath = path.join(await capturesDir(), `native_${Date.now()}.wav`);
  const child = spawn(
    "ffmpeg",
    [
      "-hide_banner",
      "-y",
      "-f",
      "dshow",
      "-i",
      `audio=${deviceName}`,
      "-ac",
      "1",
      "-ar",
      "16000",
      "-c:a",
      "pcm_s16le",
      capturePath
    ],
    { windowsHide: true }
  );

  let stderr = "";
  child.stderr.on("data", (chunk) => {
    stderr += chunk.toString();
  });
  child.on("close", (code) => {
    if (nativeCapture?.child === child) {
      nativeCapture = null;
    }
    if (code !== 0 && code !== 255 && stderr.trim()) {
      console.error(stderr);
    }
  });
  child.on("error", (error) => {
    if (nativeCapture?.child === child) {
      nativeCapture = null;
    }
    console.error(error);
  });

  nativeCapture = {
    child,
    filePath: capturePath,
    deviceName,
    startedAt: Date.now()
  };
}

async function stopNativeDictationCapture(request: NativeDictationStopRequest) {
  const capture = nativeCapture;
  if (!capture) {
    throw new Error("Native dictation capture is not running.");
  }
  nativeCapture = null;

  await new Promise<void>((resolve) => {
    const timeout = setTimeout(() => {
      capture.child.kill();
      resolve();
    }, 1800);
    capture.child.once("close", () => {
      clearTimeout(timeout);
      resolve();
    });
    capture.child.stdin.write("q", () => undefined);
    capture.child.stdin.end();
  });

  const debugCapturePath = path.join(await capturesDir(), "last-dictation.wav");
  await copyFile(capture.filePath, debugCapturePath).catch(() => undefined);
  const audio = await readFile(capture.filePath);
  try {
    const result = await runWorker({
      title: request.title,
      inputPath: "memory://dictation.wav",
      source: request.source,
      asrEngine: request.asrEngine,
      modelProfile: request.modelProfile,
      transcriptionMode: request.transcriptionMode,
      languageHint: request.languageHint,
      audioBase64: audio.toString("base64")
    });
    result.warnings.push(
      "Capture path: native DirectShow WAV",
      `Native capture device: ${capture.deviceName}`,
      `Last capture saved: ${debugCapturePath}`,
      `Captured audio bytes: ${audio.byteLength}`,
      `Captured audio extension: wav`,
      `Native capture held ms: ${Date.now() - capture.startedAt}`
    );
    return result;
  } finally {
    await unlink(capture.filePath).catch(() => undefined);
  }
}

function sanitizeExtension(fileExtension: string) {
  const normalized = fileExtension.trim().replace(/^\./, "").toLowerCase();
  return /^[a-z0-9]+$/.test(normalized) ? normalized : "webm";
}

async function transcribeFile(request: TranscribeFileRequest) {
  const startedAt = new Date();
  const result = await runWorker({
    title: request.title,
    inputPath: request.filePath,
    source: request.source,
    asrEngine: request.asrEngine,
    modelProfile: request.modelProfile,
    transcriptionMode: request.transcriptionMode,
    languageHint: request.languageHint
  });
  const endedAt = new Date();

  return saveMeeting({
    id: `meet_${Date.now()}`,
    title: request.title,
    createdAt: startedAt.toISOString(),
    startedAt: startedAt.toISOString(),
    endedAt: endedAt.toISOString(),
    durationMs: result.durationMs,
    source: request.source,
    status: "completed",
    rawTranscript: result.transcriptText,
    formattedNotes: "",
    segments: result.segments,
    warnings: result.warnings,
    modelProfile: request.modelProfile,
    asrEngine: request.asrEngine,
    filePath: request.filePath
  });
}

async function pickAudioFile() {
  const result = await dialog.showOpenDialog({
    title: "Select an audio or video file",
    properties: ["openFile"],
    filters: [
      {
        name: "Media",
        extensions: ["wav", "mp3", "m4a", "aac", "mp4", "mov", "mkv", "webm", "ogg"]
      }
    ]
  });
  return result.canceled ? null : result.filePaths[0] || null;
}

async function pasteIntoActiveApp() {
  const command = [
    "$wshell = New-Object -ComObject wscript.shell",
    "Start-Sleep -Milliseconds 120",
    "$wshell.SendKeys('^v')"
  ].join("; ");

  await new Promise<void>((resolve, reject) => {
    const child = spawn(
      "powershell.exe",
      ["-NoProfile", "-NonInteractive", "-Command", command],
      { windowsHide: true }
    );
    let stderr = "";
    child.stderr.on("data", (chunk) => {
      stderr += chunk.toString();
    });
    child.on("error", reject);
    child.on("close", (code) => {
      if (code === 0) {
        resolve();
      } else {
        reject(new Error(stderr || `Paste failed with code ${code}.`));
      }
    });
  });
}

async function insertTextToActiveApp(text: string) {
  const previousClipboard = clipboard.readText();
  clipboard.writeText(text);
  try {
    await pasteIntoActiveApp();
  } finally {
    setTimeout(() => clipboard.writeText(previousClipboard), 250);
  }
}

function hotkeyToVirtualKey(hotkey: string) {
  const normalized = hotkey.trim().toUpperCase();
  if (!/^F([1-9]|1[0-2])$/.test(normalized)) {
    throw new Error(`Unsupported hotkey ${hotkey}. Use F1-F12 for now.`);
  }
  return 0x6f + Number(normalized.slice(1));
}

async function restartHotkeyWatcher(hotkey: string) {
  stopHotkeyWatcher();
  const virtualKey = hotkeyToVirtualKey(hotkey);
  const command = [
    "Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class K { [DllImport(\"user32.dll\")] public static extern short GetAsyncKeyState(int vKey); }'",
    `$vk = ${virtualKey}`,
    "$down = $false",
    "while ($true) {",
    "  $pressed = ([K]::GetAsyncKeyState($vk) -band 0x8000) -ne 0",
    "  if ($pressed -and -not $down) { $down = $true; Write-Output 'down' }",
    "  if (-not $pressed -and $down) { $down = $false; Write-Output 'up' }",
    "  Start-Sleep -Milliseconds 20",
    "}"
  ].join("; ");

  hotkeyWatcher = spawn(
    "powershell.exe",
    ["-NoProfile", "-NonInteractive", "-Command", command],
    { windowsHide: true }
  );

  hotkeyWatcher.stdout.on("data", (chunk) => {
    for (const line of chunk.toString().split(/\r?\n/)) {
      const state = line.trim();
      if (state !== "down" && state !== "up") {
        continue;
      }
      const event: DictationHotkeyEvent = {
        accelerator: hotkey,
        pressedAt: new Date().toISOString(),
        state
      };
      for (const win of BrowserWindow.getAllWindows()) {
        win.webContents.send("dictation:hotkey", event);
      }
    }
  });
}

function stopHotkeyWatcher() {
  hotkeyWatcher?.kill();
  hotkeyWatcher = null;
}

ipcMain.handle("app:get-config", async (): Promise<AppConfig> => {
  const settings = await loadSettings();
  return {
    appName: "Muesli",
    dataDir: await dataDir(),
    selectedPython: await findPythonExecutable(),
    pythonCandidates: pythonCandidates(),
    offlineMode: true,
    settings
  };
});

ipcMain.handle("settings:save", async (_, settings: AppSettings) =>
  saveSettings(settings)
);
ipcMain.handle("dictations:list", async () => listDictations());
ipcMain.handle("dictations:save", async (_, record: Omit<DictationRecord, "id">) =>
  saveDictation(record)
);
ipcMain.handle("meetings:list", async () => listMeetings());
ipcMain.handle("transcription:captured-audio", async (_, request: TranscribeCapturedAudioRequest) =>
  transcribeCapturedAudio(request)
);
ipcMain.handle("dictation:native-start", async (_, request: NativeDictationStartRequest) =>
  startNativeDictationCapture(request)
);
ipcMain.handle("dictation:native-stop", async (_, request: NativeDictationStopRequest) =>
  stopNativeDictationCapture(request)
);
ipcMain.handle("dictation:toast", async (_, request: DictationToastRequest) => {
  showDictationToast(request);
});
ipcMain.handle("transcription:file", async (_, request: TranscribeFileRequest) =>
  transcribeFile(request)
);
ipcMain.handle("dialog:pick-audio-file", async () => pickAudioFile());
ipcMain.handle("system:write-clipboard", async (_, text: string) => {
  clipboard.writeText(text);
});
ipcMain.handle("system:insert-text-active-app", async (_, text: string) =>
  insertTextToActiveApp(text)
);
ipcMain.handle("system:open-path", async (_, targetPath: string) => {
  await shell.openPath(targetPath);
});

app.whenReady().then(async () => {
  createWindow();
  createTray();
  await mkdir(await capturesDir(), { recursive: true });
  const settings = await loadSettings();
  await restartHotkeyWatcher(settings.hotkey);

  app.on("activate", showMainWindow);
});

app.on("window-all-closed", () => {
  // Keep hotkey dictation available from the tray.
});

app.on("before-quit", () => {
  isQuitting = true;
});

app.on("will-quit", () => {
  stopHotkeyWatcher();
  transcriptionWorker?.dispose();
  transcriptionWorker = null;
  if (toastTimer) {
    clearTimeout(toastTimer);
    toastTimer = null;
  }
  toastWindow?.destroy();
  toastWindow = null;
  tray?.destroy();
  tray = null;
});
