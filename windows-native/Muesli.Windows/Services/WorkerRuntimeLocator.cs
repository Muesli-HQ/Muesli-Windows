using System.IO;

namespace Muesli.Windows.Services;

public static class WorkerRuntimeLocator
{
    public static string FindPythonExecutable()
    {
        var envPath = Environment.GetEnvironmentVariable("MUESLI_PYTHON");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        var repoRoot = FindRepoRoot();
        if (repoRoot is not null)
        {
            var devWorkerVenv = Path.Combine(repoRoot, ".venv-worker", "Scripts", "python.exe");
            if (File.Exists(devWorkerVenv))
            {
                return devWorkerVenv;
            }
        }

        var appLocalVenv = Path.Combine(AppContext.BaseDirectory, ".venv", "Scripts", "python.exe");
        if (File.Exists(appLocalVenv))
        {
            return appLocalVenv;
        }

        var parentVenv = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".venv", "Scripts", "python.exe"));
        if (File.Exists(parentVenv))
        {
            return parentVenv;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var userDevWorkerVenv = Path.Combine(userProfile, "projects", "muesli", ".venv-worker", "Scripts", "python.exe");
        if (File.Exists(userDevWorkerVenv))
        {
            return userDevWorkerVenv;
        }

        var userDevVenv = Path.Combine(userProfile, "projects", "muesli", ".venv", "Scripts", "python.exe");
        if (File.Exists(userDevVenv))
        {
            return userDevVenv;
        }

        var python312 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Python", "Python312", "python.exe");
        if (File.Exists(python312))
        {
            return python312;
        }

        var python311 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Python", "Python311", "python.exe");
        if (File.Exists(python311))
        {
            return python311;
        }

        return "python";
    }

    public static string FindWorkerScript()
    {
        var script = FindWorkerScriptOrNull();
        if (script is not null)
        {
            return script;
        }

        throw new FileNotFoundException("Could not find worker/transcribe_worker.py.");
    }

    public static string? FindWorkerScriptOrNull()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "worker", "transcribe_worker.py");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        var userProfileCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "projects",
            "muesli",
            "worker",
            "transcribe_worker.py");
        return File.Exists(userProfileCandidate) ? userProfileCandidate : null;
    }

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var workerScript = Path.Combine(directory.FullName, "worker", "transcribe_worker.py");
            if (File.Exists(workerScript))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
