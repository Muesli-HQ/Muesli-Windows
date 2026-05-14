using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Muesli.Windows.Services;

public sealed class AudioCaptureService : IDisposable
{
    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
    private string? _capturePath;
    private long _sampleCount;
    private double _sumSquares;
    private float _peak;
    private DateTimeOffset _startedAt;

    public IReadOnlyList<string> ListCaptureDevices()
    {
        var devices = _deviceEnumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(device => device.FriendlyName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();

        return devices.Count > 0 ? devices : ["System default microphone"];
    }

    public string PickPreferredDeviceName()
    {
        var devices = ListCaptureDevices();
        return devices.FirstOrDefault(IsPreferredPhysicalMicrophone) ??
               devices.FirstOrDefault(device => device.Contains("microphone", StringComparison.OrdinalIgnoreCase) && !IsLikelyVirtualDevice(device)) ??
               devices.FirstOrDefault() ??
               "System default microphone";
    }

    public Task StartAsync(string? preferredDeviceName)
    {
        StopAndCleanup();

        var device = PickDevice(preferredDeviceName);
        _capture = new WasapiCapture(device)
        {
            ShareMode = AudioClientShareMode.Shared
        };

        _capturePath = Path.Combine(CaptureDirectory(), $"native-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav");
        _writer = new WaveFileWriter(_capturePath, _capture.WaveFormat);
        _sampleCount = 0;
        _sumSquares = 0;
        _peak = 0;
        _startedAt = DateTimeOffset.UtcNow;

        _capture.DataAvailable += OnDataAvailable;
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
            throw new InvalidOperationException("Audio capture is not running.");
        }

        var capture = _capture;
        _capture = null;
        capture.StopRecording();
        capture.Dispose();

        _writer?.Dispose();
        _writer = null;

        await WaitForFileFlushAsync(_capturePath);
        var normalizedPath = Path.Combine(CaptureDirectory(), "last-dictation.wav");
        NormalizeToWhisperWav(_capturePath, normalizedPath);
        var bytes = await File.ReadAllBytesAsync(normalizedPath);

        var rms = _sampleCount == 0 ? 0 : Math.Sqrt(_sumSquares / _sampleCount);
        var heldMs = (int)(DateTimeOffset.UtcNow - _startedAt).TotalMilliseconds;
        var result = new CapturedAudio(_capturePath, normalizedPath, bytes, heldMs, rms, _peak);
        _capturePath = null;
        return result;
    }

    public Task CancelAsync()
    {
        var path = _capturePath;
        StopAndCleanup();
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Cancel should be best-effort and never throw into the UI.
            }
        }

        _capturePath = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopAndCleanup();
        _deviceEnumerator.Dispose();
    }

    private MMDevice PickDevice(string? preferredDeviceName)
    {
        var devices = _deviceEnumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .ToList();

        if (!string.IsNullOrWhiteSpace(preferredDeviceName) &&
            !preferredDeviceName.Equals("System default microphone", StringComparison.OrdinalIgnoreCase))
        {
            var exact = devices.FirstOrDefault(device =>
                device.FriendlyName.Equals(preferredDeviceName, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }

            var fuzzy = devices.FirstOrDefault(device =>
                device.FriendlyName.Contains(preferredDeviceName, StringComparison.OrdinalIgnoreCase) ||
                preferredDeviceName.Contains(device.FriendlyName, StringComparison.OrdinalIgnoreCase));
            if (fuzzy is not null)
            {
                return fuzzy;
            }
        }

        var preferred = devices.FirstOrDefault(device => IsPreferredPhysicalMicrophone(device.FriendlyName));
        if (preferred is not null)
        {
            return preferred;
        }

        var physical = devices.FirstOrDefault(device =>
            device.FriendlyName.Contains("microphone", StringComparison.OrdinalIgnoreCase) &&
            !IsLikelyVirtualDevice(device.FriendlyName));
        if (physical is not null)
        {
            return physical;
        }

        return _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        AccumulateLevels(e.Buffer, e.BytesRecorded, _capture?.WaveFormat);
    }

    private void AccumulateLevels(byte[] buffer, int bytesRecorded, WaveFormat? format)
    {
        if (format is null)
        {
            return;
        }

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            for (var offset = 0; offset + 4 <= bytesRecorded; offset += 4)
            {
                var sample = BitConverter.ToSingle(buffer, offset);
                AddSample(sample);
            }
            return;
        }

        if (format.BitsPerSample == 16)
        {
            for (var offset = 0; offset + 2 <= bytesRecorded; offset += 2)
            {
                var sample = BitConverter.ToInt16(buffer, offset) / 32768f;
                AddSample(sample);
            }
        }
    }

    private void AddSample(float sample)
    {
        var absolute = Math.Abs(sample);
        _peak = Math.Max(_peak, absolute);
        _sumSquares += sample * sample;
        _sampleCount++;
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
                // Recorder is still closing the WAV writer.
            }

            await Task.Delay(40);
        }
    }

    private static void NormalizeToWhisperWav(string sourcePath, string destinationPath)
    {
        using var reader = new AudioFileReader(sourcePath);
        var monoProvider = reader.ToMono();
        var outFormat = new WaveFormat(16000, 16, 1);
        var waveProvider = monoProvider.ToWaveProvider16();
        using var resampler = new MediaFoundationResampler(waveProvider, outFormat)
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

    private static bool IsPreferredPhysicalMicrophone(string deviceName)
    {
        return deviceName.Contains("microphone array", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("realtek", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyVirtualDevice(string deviceName)
    {
        return deviceName.Contains("steam", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("streaming", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("virtual", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("cable", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("monitor", StringComparison.OrdinalIgnoreCase);
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

public sealed record CapturedAudio(
    string FilePath,
    string LastCapturePath,
    byte[] Bytes,
    int HeldMs,
    double Rms,
    float Peak);
