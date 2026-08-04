# Third-Party Notices

Muesli redistributes or downloads the components below. This notice is informational and
does not replace the applicable license texts in `licenses/` or upstream terms.

## Application libraries and runtimes

### QuestPDF 2026.5.0

- Copyright: Marcin Ziabek / QuestPDF contributors.
- Source: https://github.com/QuestPDF/QuestPDF
- License: the license shipped in the 2026.5.0 NuGet package; this build selects
  `LicenseType.Community`.
- Use: PDF meeting export.
- Important: the Community license is not universally available. The legal entity that
  builds or distributes Muesli must confirm eligibility under QuestPDF's current License
  Selection Guide or obtain the appropriate paid license. No eligibility conclusion is
  made here.
- Included text: `licenses/QuestPDF-2026.5.0.md`.

QuestPDF's redistributed native/content dependencies in this package include:

- Skia: Copyright Google Inc. and contributors; BSD 3-Clause license;
  https://skia.org/; text in `licenses/BSD-3-Clause.txt`.
- qpdf: Copyright Jay Berkenbilt and contributors; Apache License 2.0;
  https://github.com/qpdf/qpdf; text in `licenses/Apache-2.0.txt`.
- Lato font: Copyright 2010-2014 Łukasz Dziedzic and contributors; SIL Open Font
  License 1.1; https://www.latofonts.com/; the upstream `LatoFont/OFL.txt` is
  also retained verbatim in the published package.

### NAudio 2.2.1

- Copyright: Mark Heath and contributors.
- Source: https://github.com/naudio/NAudio
- License: MIT.
- Use: WASAPI microphone and system-audio capture.
- Included text: `licenses/MIT.txt`.

### SharpCompress 0.48.1

- Copyright: Adam Hathcock and contributors.
- Source: https://github.com/adamhathcock/sharpcompress
- License: MIT.
- Use: reading model archives.
- Included text: `licenses/MIT.txt`.

### LLamaSharp 0.27.0 and llama.cpp native backend

- Copyright: LLamaSharp and llama.cpp contributors.
- Sources: https://github.com/SciSharp/LLamaSharp and
  https://github.com/ggml-org/llama.cpp
- License: MIT.
- Use: optional local GGUF transcript cleanup (disabled by default).
- Included text: `licenses/MIT.txt`.

### sherpa-onnx 1.13.4

- Copyright: The Next-gen Kaldi development team and contributors.
- Source: https://github.com/k2-fsa/sherpa-onnx
- License: Apache License 2.0.
- Use: native Parakeet ASR and speaker diarization.
- Included text: `licenses/Apache-2.0.txt`.

### ONNX Runtime

- Copyright: Microsoft Corporation and contributors.
- Source: https://github.com/microsoft/onnxruntime
- License: MIT.
- Use: native ONNX model execution redistributed through sherpa-onnx runtime assets.
- Included text: `licenses/MIT.txt`.

### Microsoft .NET runtime and managed support libraries

- Copyright: .NET Foundation and contributors; Microsoft Corporation and contributors.
- Sources: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf
- License: MIT for the redistributed .NET runtime and the Microsoft managed packages used
  by this build. Individual notices embedded in Microsoft binaries remain applicable.
- Use: self-contained Windows runtime, WPF, JSON, logging abstractions, and related support.
- Included text: `licenses/MIT.txt`.

### CommunityToolkit.HighPerformance and Ix.Async support libraries

- Copyright: .NET Foundation, Microsoft, and contributors.
- Sources: https://github.com/CommunityToolkit/dotnet and
  https://github.com/dotnet/reactive
- License: MIT.
- Use: transitive managed dependencies of the application libraries.
- Included text: `licenses/MIT.txt`.

## Fonts

### Inter

- Copyright: The Inter Project Authors.
- Source: https://github.com/rsms/inter
- License: SIL Open Font License 1.1.
- Use: application typography.
- Included text: `licenses/OFL-1.1.txt`.

## Downloaded model artifacts

The following models are downloaded on demand into the user's model cache. They are not
embedded in the primary ZIP or installer, but their attribution and terms apply after
download.

### NVIDIA Parakeet TDT 0.6B v3

- Creator and attribution: NVIDIA Corporation, `nvidia/parakeet-tdt-0.6b-v3`.
- Model card: https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3
- License: Creative Commons Attribution 4.0 International (CC BY 4.0).
- Use: multilingual speech recognition. Muesli uses sherpa-onnx-converted ONNX artifacts
  from the pinned k2-fsa release.
- Included text: `licenses/CC-BY-4.0.txt`.

### NVIDIA Nemotron 3.5 ASR Streaming 0.6B

- Creator and attribution: NVIDIA Corporation, `nvidia/nemotron-3.5-asr-streaming-0.6b`.
- Source/model card: https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b
- Converted artifact source: https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models
- Use: opt-in local live meeting transcription through sherpa-onnx. The artifact downloads only after an explicit user action and is not bundled in the installer. The upstream model-card license terms apply.

### Silero VAD

- Creator and attribution: Silero Team, `snakers4/silero-vad`.
- Source and license: https://github.com/snakers4/silero-vad (MIT).
- Use: opt-in local speech-boundary detection for live meeting transcription. The pinned ONNX artifact downloads only with the live model and is not bundled in the installer.

### pyannote segmentation 3.0

- Copyright: 2023 CNRS and contributors.
- Model card: https://huggingface.co/pyannote/segmentation-3.0
- License: MIT.
- Use: speaker segmentation via a sherpa-onnx-converted ONNX artifact from the pinned
  k2-fsa release.
- Included text: `licenses/MIT.txt`.

### NVIDIA TitaNet speaker embedding model

- Creator and attribution: NVIDIA Corporation,
  `nvidia/speakerverification_en_titanet_large`.
- Model card: https://huggingface.co/nvidia/speakerverification_en_titanet_large
- License: Creative Commons Attribution 4.0 International (CC BY 4.0).
- Use: speaker embeddings via a sherpa-onnx-converted ONNX artifact from the pinned
  k2-fsa release.
- Included text: `licenses/CC-BY-4.0.txt`.

## Optional NVIDIA acceleration dependencies

The primary Muesli package includes the version-matched sherpa-onnx CUDA provider, but it
does not bundle the NVIDIA CUDA Toolkit or cuDNN dependency DLLs. The optional
`install-parakeet-cuda-runtime.ps1` script copies selected CUDA 12 DLLs from the user's
existing CUDA Toolkit installation and downloads the pinned official cuDNN 9.10.2.21
archive directly from NVIDIA over HTTPS after the user runs the script.

CUDA Toolkit and cuDNN are governed by NVIDIA's applicable license terms, not by an
open-source license in this repository. Users and distributors must review and accept the
current NVIDIA terms before installing or redistributing those components. See:

- https://docs.nvidia.com/cuda/eula/index.html
- https://docs.nvidia.com/deeplearning/cudnn/latest/reference/eula.html

Muesli makes no legal conclusion about eligibility to use or redistribute NVIDIA software.
