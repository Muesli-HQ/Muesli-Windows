namespace Muesli.Windows;

public partial class App : System.Windows.Application
{
    private readonly Services.AppLogService _logService = new();

    public static bool StartedInBackground
    {
        get
        {
            var hasBackgroundArg = Environment.GetCommandLineArgs().Any(arg =>
            arg.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--startup", StringComparison.OrdinalIgnoreCase));
            if (hasBackgroundArg)
            {
                return true;
            }

            return LooksLikeLoginStartupWithoutBackgroundArg();
        }
    }

    private static bool LooksLikeLoginStartupWithoutBackgroundArg()
    {
        if (!Services.StartupRegistrationService.IsEnabled())
        {
            return false;
        }

        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return uptime < TimeSpan.FromMinutes(5);
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        _logService.Info($"Muesli starting. Background={StartedInBackground}. Version={Environment.Version}.");
        if (Services.StartupRegistrationService.IsEnabled() &&
            !Services.StartupRegistrationService.IsRegisteredForBackgroundLaunch())
        {
            try
            {
                Services.StartupRegistrationService.SetEnabled(true);
                _logService.Info("Repaired startup registration to use --background.");
            }
            catch (Exception exception)
            {
                _logService.Error("Could not repair startup registration.", exception);
            }
        }

        DispatcherUnhandledException += (_, args) =>
        {
            _logService.Error("Unhandled UI exception.", args.Exception);
            args.Handled = true;
            System.Windows.MessageBox.Show(
                "Muesli hit an unexpected error. The details were saved to the logs folder.",
                "Muesli",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                _logService.Error("Unhandled app-domain exception.", exception);
            }
            else
            {
                _logService.Error($"Unhandled app-domain exception object: {args.ExceptionObject}");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logService.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        var window = new MainWindow();
        MainWindow = window;

        if (!window.OpenDashboardOnLaunch)
        {
            window.ParkForBackgroundLaunch();
            window.StartRuntime(showOnboarding: false);
            window.SetBackgroundStatus();
            return;
        }

        window.Show();
    }
}
