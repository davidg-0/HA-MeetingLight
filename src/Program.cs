using HAMeetingLight.Configuration;
using HAMeetingLight.Services;
using System.Threading;

namespace HAMeetingLight;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Ensure only one instance of the application is running
        bool createdNew;
        using var mutex = new Mutex(true, "HAMeetingLightMutex", out createdNew);
        if (!createdNew)
        {
            // Another instance is already running, exit silently
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        try
        {
            // Load and validate configuration
            var config = ConfigLoader.LoadConfiguration();

            // Enable optional file logging based on configuration
            EventLogger.Configure(config.Logging.LogToFile);

            // Start and minimize to system tray (no main window)
            Application.Run(new TrayApplicationContext(config));
        }
        catch (ConfigurationException ex)
        {
            // Show notification and terminate
            ShowErrorNotification(ex.Message);
            return;
        }
        catch (Exception ex)
        {
            // Unexpected errors
            EventLogger.LogError($"Fatal error: {ex.Message}");
            ShowErrorNotification($"Fatal error: {ex.Message}");
            return;
        }
    }

    private static void ShowErrorNotification(string message)
    {
        // Show brief notification before exit
        using var notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Error,
            Visible = true
        };

        notifyIcon.ShowBalloonTip(5000, "HA-MeetingLight", message, ToolTipIcon.Error);
        
        // Wait for notification to display
        System.Threading.Thread.Sleep(5000);
        
        notifyIcon.Visible = false;
    }
}
