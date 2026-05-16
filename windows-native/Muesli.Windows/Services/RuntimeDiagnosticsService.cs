using System.Diagnostics;
using System.IO;

namespace Muesli.Windows.Services;

public sealed class RuntimeDiagnosticsService
{
    public string ModelCacheDirectory =>
        Environment.GetEnvironmentVariable("MUESLI_MODEL_CACHE") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "muesli");

    public async Task<RuntimeDiagnostics> InspectAsync()
    {
        var cacheDirectory = ModelCacheDirectory;
        Directory.CreateDirectory(cacheDirectory);

        var python = WorkerRuntimeLocator.FindPythonExecutable();
        var pythonVersion = await RunProcessAsync(python, "--version", TimeSpan.FromSeconds(8));
        var dependencyCheck = await RunProcessAsync(
            python,
            "-c \"import faster_whisper; import av; print('Whisper worker dependencies OK')\"",
            TimeSpan.FromSeconds(12));
        var postProcessCheck = await RunProcessAsync(
            python,
            "-c \"import transformers; import torch; print('Qwen post-processing dependencies OK')\"",
            TimeSpan.FromSeconds(12));
        var parakeetCheck = await RunProcessAsync(
            python,
            "-c \"import nemo.collections.asr; print('Parakeet dependencies OK')\"",
            TimeSpan.FromSeconds(12));
        var cudaCheck = await RunProcessAsync(
            python,
            "-c \"import torch; print('CUDA available' if torch.cuda.is_available() else 'CUDA not available')\"",
            TimeSpan.FromSeconds(12));
        var diarizationCheck = await RunProcessAsync(
            python,
            "-c \"import numpy as np; np.NaN = np.nan if not hasattr(np, 'NaN') else np.NaN; np.NAN = np.nan if not hasattr(np, 'NAN') else np.NAN; import torchaudio; torchaudio.set_audio_backend = lambda x: None; import pyannote.audio; import soundfile; import torch; print('OK - pyannote diarization dependencies installed.')\"",
            TimeSpan.FromSeconds(15));

        var workerPath = WorkerRuntimeLocator.FindWorkerScriptOrNull();
        var cacheSizeBytes = Directory.Exists(cacheDirectory)
            ? Directory.EnumerateFiles(cacheDirectory, "*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .Sum(file => file.Length)
            : 0;

        var hfToken = Environment.GetEnvironmentVariable("HF_TOKEN");
        var tokenStatus = string.IsNullOrWhiteSpace(hfToken)
            ? "HF_TOKEN not set. pyannote gated models may fail unless already cached and accessible."
            : "HF_TOKEN set.";

        return new RuntimeDiagnostics(
            python,
            NormalizeOutput(pythonVersion),
            NormalizeOutput(dependencyCheck),
            NormalizeOutput(postProcessCheck),
            NormalizeOutput(parakeetCheck),
            NormalizeOutput(cudaCheck),
            NormalizeOutput(diarizationCheck),
            tokenStatus,
            workerPath ?? "worker/transcribe_worker.py not found",
            cacheDirectory,
            cacheSizeBytes,
            BuildModelStatus(cacheDirectory));
    }

    public bool IsWhisperModelCached(string model)
    {
        var cacheDirectory = ModelCacheDirectory;
        if (!Directory.Exists(cacheDirectory))
        {
            return false;
        }

        var candidates = new[]
        {
            Path.Combine(cacheDirectory, model),
            Path.Combine(cacheDirectory, $"models--Systran--faster-whisper-{model}"),
            Path.Combine(cacheDirectory, $"models--openai--whisper-{model}")
        };

        return candidates.Any(Directory.Exists);
    }

    public void OpenModelCacheDirectory()
    {
        Directory.CreateDirectory(ModelCacheDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = ModelCacheDirectory,
            UseShellExecute = true
        });
    }

    public void ClearModelCache()
    {
        if (!Directory.Exists(ModelCacheDirectory))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(ModelCacheDirectory))
        {
            Directory.Delete(directory, recursive: true);
        }

        foreach (var file in Directory.EnumerateFiles(ModelCacheDirectory))
        {
            File.Delete(file);
        }
    }

    private static string BuildModelStatus(string cacheDirectory)
    {
        var known = new Dictionary<string, string>
        {
            ["tiny"] = "tiny",
            ["base"] = "base",
            ["small"] = "small",
            ["medium"] = "medium",
            ["large-v3-turbo"] = "turbo",
            ["Qwen cleanup"] = "models--Qwen--Qwen2.5-3B-Instruct",
            ["Parakeet v3"] = "models--nvidia--parakeet-tdt-0.6b-v3"
        };

        return string.Join(Environment.NewLine, known.Select(item =>
        {
            var present = Directory.Exists(Path.Combine(cacheDirectory, item.Value)) ||
                          Directory.Exists(Path.Combine(cacheDirectory, $"models--Systran--faster-whisper-{item.Value}")) ||
                          Directory.Exists(Path.Combine(cacheDirectory, $"models--openai--whisper-{item.Value}")) ||
                          Directory.Exists(Path.Combine(cacheDirectory, item.Value.Replace("/", "--")));
            return $"{item.Key}: {(present ? "cached" : "not cached")}";
        }));
    }

    private static string NormalizeOutput(ProcessResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            return result.Output.Trim();
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return result.Error.Trim();
        }

        return result.ExitCode == 0 ? "OK" : $"Failed with exit code {result.ExitCode}";
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, TimeSpan timeout)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ProcessResult(-1, "", "Could not start process.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var waitTask = process.WaitForExitAsync();
            var exited = await Task.WhenAny(waitTask, Task.Delay(timeout));
            if (exited != waitTask)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Process may have exited between timeout and kill.
                }

                return new ProcessResult(-1, "", "Timed out.");
            }

            return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (Exception exception)
        {
            return new ProcessResult(-1, "", exception.Message);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}

public sealed record RuntimeDiagnostics(
    string PythonExecutable,
    string PythonVersion,
    string DependencyStatus,
    string PostProcessingDependencyStatus,
    string ParakeetDependencyStatus,
    string CudaStatus,
    string DiarizationDependencyStatus,
    string DiarizationTokenStatus,
    string WorkerScript,
    string ModelCacheDirectory,
    long ModelCacheBytes,
    string ModelStatus)
{
    public string ModelCacheSize => ModelCacheBytes switch
    {
        >= 1_073_741_824 => $"{ModelCacheBytes / 1_073_741_824.0:0.0} GB",
        >= 1_048_576 => $"{ModelCacheBytes / 1_048_576.0:0.0} MB",
        >= 1024 => $"{ModelCacheBytes / 1024.0:0.0} KB",
        _ => $"{ModelCacheBytes} B"
    };

    public string Summary =>
        $"Python: {PythonExecutable} ({PythonVersion}){Environment.NewLine}" +
        $"Whisper dependencies: {DependencyStatus}{Environment.NewLine}" +
        $"Qwen dependencies: {PostProcessingDependencyStatus}{Environment.NewLine}" +
        $"Parakeet dependencies: {ParakeetDependencyStatus}{Environment.NewLine}" +
        $"GPU: {CudaStatus}{Environment.NewLine}" +
        $"Diarization dependencies: {DiarizationDependencyStatus}{Environment.NewLine}" +
        $"Diarization token: {DiarizationTokenStatus}{Environment.NewLine}" +
        $"Worker: {WorkerScript}{Environment.NewLine}" +
        $"Cache: {ModelCacheDirectory} ({ModelCacheSize}){Environment.NewLine}" +
        ModelStatus;
}
