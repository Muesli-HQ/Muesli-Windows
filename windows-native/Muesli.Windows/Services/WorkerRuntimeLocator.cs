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

        var repoRoot = FindRepoRoot();
        if (repoRoot is not null)
        {
            var devWorkerVenv = Path.Combine(repoRoot, ".venv-worker", "Scripts", "python.exe");
            if (File.Exists(devWorkerVenv))
            {
                return devWorkerVenv;
            }
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

    public static string? FindSetupScriptOrNull()
    {
        var direct = Path.Combine(AppContext.BaseDirectory, "setup-worker-runtime.ps1");
        if (File.Exists(direct))
        {
            return direct;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "scripts", "setup-worker-runtime.ps1");
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
            "scripts",
            "setup-worker-runtime.ps1");
        return File.Exists(userProfileCandidate) ? userProfileCandidate : null;
    }

    public static bool HasWorkerRequirementsLayout()
    {
        var workerScript = FindWorkerScriptOrNull();
        if (string.IsNullOrWhiteSpace(workerScript))
        {
            return false;
        }

        var workerDirectory = Path.GetDirectoryName(workerScript);
        if (string.IsNullOrWhiteSpace(workerDirectory))
        {
            return false;
        }

        return File.Exists(Path.Combine(workerDirectory, "requirements.txt")) &&
               File.Exists(Path.Combine(workerDirectory, "requirements-diarization.txt")) &&
               File.Exists(Path.Combine(workerDirectory, "requirements-postprocess.txt")) &&
               File.Exists(Path.Combine(workerDirectory, "requirements-parakeet.txt"));
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
