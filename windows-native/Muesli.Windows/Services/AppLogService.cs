using System.Diagnostics;
using System.IO;

namespace Muesli.Windows.Services;

public sealed class AppLogService
{
    public string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "muesli",
        "logs");

    public string CurrentLogPath => Path.Combine(LogDirectory, $"muesli-{DateTime.Now:yyyy-MM-dd}.log");

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? exception = null)
    {
        var detail = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        Write("ERROR", detail);
    }

    public void OpenLogDirectory()
    {
        Directory.CreateDirectory(LogDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = LogDirectory,
            UseShellExecute = true
        });
    }

    private void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(
                CurrentLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {level} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break dictation, startup, or shutdown.
        }
    }
}
