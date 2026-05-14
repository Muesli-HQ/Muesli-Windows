# Transcription Benchmarks

Benchmarked on the current development laptop using:

```powershell
python .\scripts\benchmark_transcription.py --audio "$env:APPDATA\muesli\captures\last-dictation.wav" --models tiny base small --runs 3 --mode server
```

Audio sample: `last-dictation.wav`, 7.63 seconds.

`server` mode is the realistic app path: one Python worker stays alive and caches loaded models.

## CUDA Float16

| Model | First run wall | Warm wall | Warm infer | Warm RTF |
| --- | ---: | ---: | ---: | ---: |
| tiny | 3604 ms | 152-155 ms | 151-154 ms | 0.020 |
| base | 674 ms | 192-194 ms | 191-194 ms | 0.025 |
| small | 1086 ms | 261-265 ms | 261-264 ms | 0.034-0.035 |
| medium | 5189 ms | 481 ms | 480 ms | 0.063 |
| large-v3-turbo | 228713 ms | 346 ms | 346 ms | 0.045 |

`large-v3-turbo` first run included heavyweight model cache/download/load cost. Warm runtime is the relevant dictation number after the model is loaded.

## CPU Int8

```powershell
$env:MUESLI_DEVICE='cpu'
$env:MUESLI_COMPUTE_TYPE='int8'
python .\scripts\benchmark_transcription.py --audio "$env:APPDATA\muesli\captures\last-dictation.wav" --models tiny base small --runs 3 --mode server
```

| Model | First run wall | Warm wall | Warm infer | Warm RTF |
| --- | ---: | ---: | ---: | ---: |
| tiny | 3607 ms | 313-326 ms | 312-326 ms | 0.041-0.043 |
| base | 1070 ms | 533-544 ms | 532-543 ms | 0.070-0.071 |
| small | 2593 ms | 1517-1538 ms | 1517-1538 ms | 0.199-0.202 |

## Release Build Timing

| Task | Time |
| --- | ---: |
| `dotnet build` | 0.96 s |
| `scripts\package-windows-v1.ps1` | 7.12 s |

## Current Recommendation

- Default low-end CPU profile: `base`.
- Lowest-latency CPU profile: `tiny`.
- Balanced NVIDIA profile: `small`.
- High-end NVIDIA profile: `large-v3-turbo`, but preload it before first dictation to avoid the first-run delay.
