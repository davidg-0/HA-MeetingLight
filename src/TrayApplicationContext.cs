using HAMeetingLight.Configuration;
using HAMeetingLight.Services;
using System.Reflection;

namespace HAMeetingLight;

/// <summary>
/// System tray application context
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _microphoneStatus;
    private readonly ToolStripMenuItem _webcamStatus;
    private readonly DeviceMonitor _deviceMonitor;
    private readonly MqttService _mqttService;
    private readonly Icon _iconConnected;
    private readonly Icon _iconDisconnected;
    private readonly Icon _iconCam;
    private readonly Icon _iconMic;

    public TrayApplicationContext(AppConfig config)
    {
        // Initialize services
        _deviceMonitor = new DeviceMonitor(config.Monitoring.PollingIntervalSeconds);
        _mqttService = new MqttService(config.Mqtt);

        // Create context menu
        _contextMenu = new ContextMenuStrip();
        
        _microphoneStatus = new ToolStripMenuItem("Microphone: Off") { Enabled = false };
        _webcamStatus = new ToolStripMenuItem("Webcam: Off") { Enabled = false };
        var exitItem = new ToolStripMenuItem("Exit", null, OnExit);

        _contextMenu.Items.Add(_microphoneStatus);
        _contextMenu.Items.Add(_webcamStatus);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(exitItem);

        // Create tray icon
        // Load icons from embedded resources
        var assembly = Assembly.GetExecutingAssembly();
        using var connectedStream = assembly.GetManifestResourceStream("HAMeetingLight.icons.app.ico");
        using var disconnectedStream = assembly.GetManifestResourceStream("HAMeetingLight.icons.app-off.ico");
        using var camStream = assembly.GetManifestResourceStream("HAMeetingLight.icons.app-cam.ico");
        using var micStream = assembly.GetManifestResourceStream("HAMeetingLight.icons.app-mic.ico");
        _iconConnected = connectedStream != null ? new Icon(connectedStream) : SystemIcons.Application;
        _iconDisconnected = disconnectedStream != null ? new Icon(disconnectedStream) : SystemIcons.Application;
        _iconCam = camStream != null ? new Icon(camStream) : _iconConnected;
        _iconMic = micStream != null ? new Icon(micStream) : _iconConnected;

        _trayIcon = new NotifyIcon
        {
            Icon = _iconDisconnected, // start as disconnected until MQTT connects
            ContextMenuStrip = _contextMenu,
            Visible = true,
            Text = "HA-MeetingLight"
        };

        // Both clicks show context menu
        _trayIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                // Show context menu on left click
                var methodInfo = typeof(NotifyIcon).GetMethod("ShowContextMenu", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                methodInfo?.Invoke(_trayIcon, null);
            }
        };

        // Subscribe to device state changes
        _deviceMonitor.WebcamStateChanged += OnWebcamStateChanged;
        _deviceMonitor.MicrophoneStateChanged += OnMicrophoneStateChanged;

        // Subscribe to MQTT connection status changes
        _mqttService.ConnectionStatusChanged += OnMqttConnectionStatusChanged;

        // Start the application
        Task.Run(async () => await StartAsync());
    }

    private async Task StartAsync()
    {
        // Startup sequence
        // 1. Configuration already loaded (by Program.cs)
        // 2. Initialize device monitoring
        _deviceMonitor.Start();

        // 3. Attempt to connect to MQTT broker
        await _mqttService.ConnectAsync();

        // 4. Publish initial device states once MQTT is connected
        if (_mqttService.IsConnected)
        {
            await PublishInitialStates();
        }

        // Update UI with initial states
        UpdateStatusLabels();
        UpdateTrayIcon();
    }

    private async Task PublishInitialStates()
    {
        await _mqttService.PublishDeviceStateAsync("webcam", _deviceMonitor.IsWebcamActive);
        await _mqttService.PublishDeviceStateAsync("microphone", _deviceMonitor.IsMicrophoneActive);
    }

    private void OnWebcamStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        // Update UI
        if (_trayIcon.ContextMenuStrip?.InvokeRequired ?? false)
        {
            _trayIcon.ContextMenuStrip.Invoke(() => _webcamStatus.Text = $"Webcam: {(e.IsActive ? "On" : "Off")}");
        }
        else
        {
            _webcamStatus.Text = $"Webcam: {(e.IsActive ? "On" : "Off")}";
        }

        // Publish to MQTT
        Task.Run(async () => await _mqttService.PublishDeviceStateAsync("webcam", e.IsActive));

        // Update tray icon based on latest states
        UpdateTrayIcon();
    }

    private void OnMicrophoneStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        // Update UI
        if (_trayIcon.ContextMenuStrip?.InvokeRequired ?? false)
        {
            _trayIcon.ContextMenuStrip.Invoke(() => _microphoneStatus.Text = $"Microphone: {(e.IsActive ? "On" : "Off")}");
        }
        else
        {
            _microphoneStatus.Text = $"Microphone: {(e.IsActive ? "On" : "Off")}";
        }

        // Publish to MQTT
        Task.Run(async () => await _mqttService.PublishDeviceStateAsync("microphone", e.IsActive));

        // Update tray icon based on latest states
        UpdateTrayIcon();
    }

    private void OnMqttConnectionStatusChanged(object? sender, MqttConnectionStatusEventArgs e)
    {
        // Show system tray notification
        _trayIcon.ShowBalloonTip(3000, "HA-MeetingLight", e.Message, ToolTipIcon.Info);

        // Republish states when connection is re-established
        if (e.IsConnected)
        {
            Task.Run(async () => await PublishInitialStates());
        }

        // Update tray icon based on connection and device states
        UpdateTrayIcon();
    }

    private void UpdateStatusLabels()
    {
        if (_trayIcon.ContextMenuStrip?.InvokeRequired ?? false)
        {
            _trayIcon.ContextMenuStrip.Invoke(() =>
            {
                _webcamStatus.Text = $"Webcam: {(_deviceMonitor.IsWebcamActive ? "On" : "Off")}";
                _microphoneStatus.Text = $"Microphone: {(_deviceMonitor.IsMicrophoneActive ? "On" : "Off")}";
            });
        }
        else
        {
            _webcamStatus.Text = $"Webcam: {(_deviceMonitor.IsWebcamActive ? "On" : "Off")}";
            _microphoneStatus.Text = $"Microphone: {(_deviceMonitor.IsMicrophoneActive ? "On" : "Off")}";
        }
    }

    private void UpdateTrayIcon()
    {
        Icon iconToUse;
        if (!_mqttService.IsConnected)
        {
            iconToUse = _iconDisconnected;
        }
        else if (_deviceMonitor.IsWebcamActive)
        {
            iconToUse = _iconCam;
        }
        else if (_deviceMonitor.IsMicrophoneActive)
        {
            iconToUse = _iconMic;
        }
        else
        {
            iconToUse = _iconConnected;
        }

        if (_trayIcon.ContextMenuStrip?.InvokeRequired ?? false)
        {
            _trayIcon.ContextMenuStrip.Invoke(() => _trayIcon.Icon = iconToUse);
        }
        else
        {
            _trayIcon.Icon = iconToUse;
        }
    }

    private async void OnExit(object? sender, EventArgs e)
    {
        // Publish "off" to both devices on shutdown
        if (_mqttService.IsConnected)
        {
            await _mqttService.PublishDeviceStateAsync("webcam", false);
            await _mqttService.PublishDeviceStateAsync("microphone", false);
            await Task.Delay(500); // Brief delay to ensure messages are sent
        }

        // Disconnect from MQTT
        await _mqttService.DisconnectAsync();

        // Clean up
        _deviceMonitor.Stop();
        _deviceMonitor.Dispose();
        _mqttService.Dispose();

        // Remove tray icon
        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon?.Dispose();
            _contextMenu?.Dispose();
            _deviceMonitor?.Dispose();
            _mqttService?.Dispose();
            _iconConnected?.Dispose();
            _iconDisconnected?.Dispose();
            _iconCam?.Dispose();
            _iconMic?.Dispose();
        }
        base.Dispose(disposing);
    }
}
