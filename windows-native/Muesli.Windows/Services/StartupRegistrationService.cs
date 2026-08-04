using Microsoft.Win32;

namespace Muesli.Windows.Services;

public static class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Muesli";

    public static bool IsEnabled()
    {
        return !string.IsNullOrWhiteSpace(GetRegisteredCommand());
    }

    public static string? GetRegisteredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(AppName) as string;
    }

    public static bool IsRegisteredForBackgroundLaunch()
    {
        var value = GetRegisteredCommand();
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains("--background", StringComparison.OrdinalIgnoreCase);
    }

    public static string DescribeState()
    {
        if (!IsEnabled())
        {
            return "Not registered to start with Windows.";
        }

        return IsRegisteredForBackgroundLaunch()
            ? "Starts with Windows in the background."
            : "Starts with Windows.";
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            throw new InvalidOperationException("Could not open the Windows startup registry key.");
        }

        if (!enabled)
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Could not determine the Muesli executable path.");
        }

        key.SetValue(AppName, $"\"{executablePath}\" --background", RegistryValueKind.String);
    }
}
