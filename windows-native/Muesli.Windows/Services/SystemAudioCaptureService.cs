using System.IO;
using NAudio.Wave;

namespace Muesli.Windows.Services;

public sealed class SystemAudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private WaveFileWriter? _writer;
    private string? _capturePath;
    private DateTimeOffset _startedAt;

    public Task StartAsync()
    {
        StopAndCleanup();

        _capture = new WasapiLoopbackCapture();
        _capturePath = Path.Combine(CaptureDirectory(), $"meeting-system-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav");
        _writer = new WaveFileWriter(_capturePath, _capture.WaveFormat);
        _startedAt = DateTimeOffset.UtcNow;

        _capture.DataAvailable += (_, args) => _writer?.Write(args.Buffer, 0, args.BytesRecorded);
        _capture.RecordingStopped += (_, _) =>
        {
            _writer?.Dispose();
            _writer = null;
        };
        _capture.StartRecording();
        return Task.CompletedTask;
    }

    public async Task<CapturedAudio> StopAsync()
    {
        if (_capture is null || _capturePath is null)
        {
            throw new InvalidOperationException("System audio capture is not running.");
        }

        var capture = _capture;
        _capture = null;
        capture.StopRecording();
        capture.Dispose();

        _writer?.Dispose();
        _writer = null;

        await WaitForFileFlushAsync(_capturePath);
        var normalizedPath = Path.Combine(CaptureDirectory(), "last-meeting-system.wav");
        NormalizeToWhisperWav(_capturePath, normalizedPath);
        var bytes = await File.ReadAllBytesAsync(normalizedPath);
        var heldMs = (int)(DateTimeOffset.UtcNow - _startedAt).TotalMilliseconds;
        var result = new CapturedAudio(_capturePath, normalizedPath, bytes, heldMs, 0, 0);
        _capturePath = null;
        return result;
    }

    public void Dispose()
    {
        StopAndCleanup();
    }

    private static async Task WaitForFileFlushAsync(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > 44)
                {
                    return;
                }
            }
            catch (IOException)
            {
            }

            await Task.Delay(40);
        }
    }

    private static void NormalizeToWhisperWav(string sourcePath, string destinationPath)
    {
        using var reader = new AudioFileReader(sourcePath);
        var monoProvider = reader.ToMono();
        var waveProvider = monoProvider.ToWaveProvider16();
        using var resampler = new MediaFoundationResampler(waveProvider, new WaveFormat(16000, 16, 1))
        {
            ResamplerQuality = 60
        };
        WaveFileWriter.CreateWaveFile(destinationPath, resampler);
    }

    private static string CaptureDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "muesli",
            "captures");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private void StopAndCleanup()
    {
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
        _writer?.Dispose();
        _writer = null;
    }
}
