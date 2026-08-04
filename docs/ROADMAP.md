# Roadmap

## Native Launch

- Seven pinned offline sherpa-onnx choices across five ASR families, with separate persisted dictation and final meeting/import roles and no automatic activation after download.
- Native Qwen/GGUF cleanup v1 through LLamaSharp, disabled by default.
- Native sherpa-onnx diarization for recorded meeting system audio.
- Model downloads and cache management from the WPF app.
- Zip and installer packaging without Python, venvs, or external transcription runtime setup.
- Native model initialization and transcription run off the WPF dispatcher, with verified-cache startup acceleration and deterministic latency/quality regression gates.
- Production dictation traces include target-app evidence and feed enforceable release-to-paste percentile gates; human-labelled corpora feed WER/CER release qualification.
- Recorded-meeting qualification measures the combined ASR, diarization, and speaker-merge path with deterministic output, model-reuse, provider, latency, and speaker-coverage gates.
- Manifest-driven import qualification verifies decoded duration, deterministic output, latency, transcript/reference quality, and explicit decode failures without Python.
- Packaged native release qualification records runtime hashes, repeated model reuse/determinism, latency, steady-state memory, machine/provider evidence, Authenticode state, and fresh-launch log health.

## Next

- Complete physical long-meeting, route-change, Bluetooth, CUDA-live, and human multilingual qualification for the opt-in Nemotron 3.5 live-meeting role; it remains Off by default.
- Manual end-to-end dictation QA on fresh and existing profiles.
- Manual recorded meeting QA with diarized transcript review.
- Curate human-speech fixtures for MP3, M4A, MP4, MOV, MKV, and OGG; WAV and WebM decoding are covered by the current local Phase 4 qualification.
- Complete Phase 5 qualification on separate clean CPU-only and supported NVIDIA release machines; the development laptop now exercises both providers but is not a substitute for two fresh machines.
- Add guided native Qwen cleanup model install/download once the target GGUF is selected.
- Code signing and release hardening.
