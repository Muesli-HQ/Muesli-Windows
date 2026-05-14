import argparse
import json
import subprocess
import sys
import time
import wave
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
WORKER = ROOT / "worker" / "transcribe_worker.py"


def wav_duration_ms(path: Path) -> int:
    try:
        with wave.open(str(path), "rb") as wav:
            frames = wav.getnframes()
            rate = wav.getframerate()
            if rate <= 0:
                return 0
            return int(frames / rate * 1000)
    except wave.Error:
        return 0


def parse_payload(payload: dict[str, Any], model: str, wall_ms: float, audio: Path) -> dict[str, Any]:
    warnings = payload.get("warnings", [])
    timings = {}
    backend = "unknown"
    for warning in warnings:
        if warning.startswith("ASR backend:"):
            backend = warning.split(":", 1)[1].strip()
        if warning.startswith("Timing worker "):
            key, value = warning.removeprefix("Timing worker ").split(" ms:", 1)
            timings[key.strip()] = float(value.strip())

    duration_ms = payload.get("durationMs") or wav_duration_ms(audio)
    infer_ms = timings.get("infer", 0.0)
    total_ms = timings.get("total", wall_ms)
    rtf = infer_ms / duration_ms if duration_ms else 0

    return {
        "ok": True,
        "model": model,
        "backend": backend,
        "audio_ms": duration_ms,
        "wall_ms": round(wall_ms, 1),
        "model_ms": round(timings.get("model", 0.0), 1),
        "infer_ms": round(infer_ms, 1),
        "worker_total_ms": round(total_ms, 1),
        "rtf": round(rtf, 3),
        "chars": len(payload.get("transcriptText", "")),
        "segments": len(payload.get("segments", [])),
        "mock": any("Mock worker output is active." in warning for warning in warnings),
    }


def run_once(audio: Path, model: str) -> dict:
    started = time.perf_counter()
    completed = subprocess.run(
        [
            sys.executable,
            str(WORKER),
            "transcribe",
            "--input",
            str(audio),
            "--title",
            audio.stem,
            "--source",
            "file",
            "--asr-engine",
            "whisper",
            "--model",
            model,
            "--transcription-mode",
            "final",
            "--language-hint",
            "en",
        ],
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        check=False,
    )
    wall_ms = (time.perf_counter() - started) * 1000

    if completed.returncode != 0:
        return {
            "ok": False,
            "model": model,
            "wall_ms": wall_ms,
            "error": completed.stderr.strip() or completed.stdout.strip(),
        }

    return parse_payload(json.loads(completed.stdout), model, wall_ms, audio)


def run_server_rows(audio: Path, models: list[str], runs: int) -> list[dict[str, Any]]:
    process = subprocess.Popen(
        [sys.executable, str(WORKER), "server"],
        cwd=str(ROOT),
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        bufsize=1,
    )
    assert process.stdin is not None
    assert process.stdout is not None

    rows: list[dict[str, Any]] = []
    try:
        request_index = 0
        for model in models:
            for run in range(1, runs + 1):
                request_index += 1
                request_id = f"bench_{request_index}"
                request = {
                    "id": request_id,
                    "command": "transcribe",
                    "payload": {
                        "title": audio.stem,
                        "input_path": str(audio),
                        "source": "file",
                        "asr_engine": "whisper",
                        "model": model,
                        "transcription_mode": "final",
                        "language_hint": "en",
                        "audio_base64": "",
                    },
                }
                started = time.perf_counter()
                process.stdin.write(json.dumps(request) + "\n")
                process.stdin.flush()
                line = process.stdout.readline()
                wall_ms = (time.perf_counter() - started) * 1000
                if not line:
                    rows.append({
                        "ok": False,
                        "model": model,
                        "run": run,
                        "wall_ms": round(wall_ms, 1),
                        "error": "Worker closed stdout.",
                    })
                    continue

                envelope = json.loads(line)
                if not envelope.get("ok"):
                    rows.append({
                        "ok": False,
                        "model": model,
                        "run": run,
                        "wall_ms": round(wall_ms, 1),
                        "error": envelope.get("error", "Transcription failed."),
                    })
                    continue

                row = parse_payload(envelope["result"], model, wall_ms, audio)
                row["run"] = run
                rows.append(row)
    finally:
        process.kill()
        process.wait(timeout=5)

    return rows


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--audio", required=True)
    parser.add_argument("--models", nargs="+", default=["tiny", "base", "small"])
    parser.add_argument("--runs", type=int, default=2)
    parser.add_argument("--mode", choices=["process", "server"], default="server")
    args = parser.parse_args()

    audio = Path(args.audio).expanduser().resolve()
    if not audio.exists():
        raise SystemExit(f"Audio file not found: {audio}")

    if args.mode == "server":
        rows = run_server_rows(audio, args.models, args.runs)
    else:
        rows = []
        for model in args.models:
            for run in range(1, args.runs + 1):
                row = run_once(audio, model)
                row["run"] = run
                rows.append(row)

    print(json.dumps({"audio": str(audio), "results": rows}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
