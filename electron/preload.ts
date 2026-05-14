import { contextBridge, ipcRenderer } from "electron";
import type {
  AppSettings,
  DictationToastRequest,
  DesktopApi,
  DictationHotkeyEvent,
  DictationRecord,
  NativeDictationStartRequest,
  NativeDictationStopRequest,
  TranscribeCapturedAudioRequest,
  TranscribeFileRequest
} from "../shared/contracts";

const api: DesktopApi = {
  getConfig: () => ipcRenderer.invoke("app:get-config"),
  saveSettings: (settings: AppSettings) =>
    ipcRenderer.invoke("settings:save", settings),
  listDictations: () => ipcRenderer.invoke("dictations:list"),
  saveDictation: (record: Omit<DictationRecord, "id">) =>
    ipcRenderer.invoke("dictations:save", record),
  listMeetings: () => ipcRenderer.invoke("meetings:list"),
  transcribeCapturedAudio: (request: TranscribeCapturedAudioRequest) =>
    ipcRenderer.invoke("transcription:captured-audio", request),
  startNativeDictationCapture: (request: NativeDictationStartRequest) =>
    ipcRenderer.invoke("dictation:native-start", request),
  stopNativeDictationCapture: (request: NativeDictationStopRequest) =>
    ipcRenderer.invoke("dictation:native-stop", request),
  showDictationToast: (request: DictationToastRequest) =>
    ipcRenderer.invoke("dictation:toast", request),
  transcribeFile: (request: TranscribeFileRequest) =>
    ipcRenderer.invoke("transcription:file", request),
  pickAudioFile: () => ipcRenderer.invoke("dialog:pick-audio-file"),
  writeTextToClipboard: (text: string) =>
    ipcRenderer.invoke("system:write-clipboard", text),
  insertTextToActiveApp: (text: string) =>
    ipcRenderer.invoke("system:insert-text-active-app", text),
  openPath: (path: string) => ipcRenderer.invoke("system:open-path", path),
  onDictationHotkey: (callback: (event: DictationHotkeyEvent) => void) => {
    const listener = (
      _event: Electron.IpcRendererEvent,
      payload: DictationHotkeyEvent
    ) => callback(payload);
    ipcRenderer.on("dictation:hotkey", listener);
    return () => ipcRenderer.removeListener("dictation:hotkey", listener);
  }
};

contextBridge.exposeInMainWorld("desktopApi", api);
