using Microsoft.Win32;

namespace HAMeetingLight.Services;

/// <summary>
/// Monitors webcam and microphone activity via Windows Registry
/// </summary>
public class DeviceMonitor : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private readonly int _pollingIntervalMs;
    private bool _lastWebcamState = false;
    private bool _lastMicrophoneState = false;

    public event EventHandler<DeviceStateChangedEventArgs>? WebcamStateChanged;
    public event EventHandler<DeviceStateChangedEventArgs>? MicrophoneStateChanged;

    public bool IsWebcamActive => _lastWebcamState;
    public bool IsMicrophoneActive => _lastMicrophoneState;

    public DeviceMonitor(int pollingIntervalSeconds)
    {
        _pollingIntervalMs = pollingIntervalSeconds * 1000;
        _timer = new System.Threading.Timer(CheckDeviceStates, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        // Get initial states
        _lastWebcamState = CheckWebcamState();
        _lastMicrophoneState = CheckMicrophoneState();

        // Start polling
        _timer.Change(0, _pollingIntervalMs);
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void CheckDeviceStates(object? state)
    {
        try
        {
            // Check webcam
            bool currentWebcamState = CheckWebcamState();
            if (currentWebcamState != _lastWebcamState)
            {
                _lastWebcamState = currentWebcamState;
                WebcamStateChanged?.Invoke(this, new DeviceStateChangedEventArgs(currentWebcamState));
            }

            // Check microphone
            bool currentMicrophoneState = CheckMicrophoneState();
            if (currentMicrophoneState != _lastMicrophoneState)
            {
                _lastMicrophoneState = currentMicrophoneState;
                MicrophoneStateChanged?.Invoke(this, new DeviceStateChangedEventArgs(currentMicrophoneState));
            }
        }
        catch (Exception ex)
        {
            EventLogger.LogError($"Error checking device states: {ex.Message}");
        }
    }

    private bool CheckWebcamState()
    {
        const string regKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";
        
        try
        {
            // Check HKCU only (per PRD)
            using var key = Registry.CurrentUser.OpenSubKey(regKey);
            return CheckRegForDeviceInUse(key);
        }
        catch (Exception ex)
        {
            // Log error and assume device is off
            EventLogger.LogError($"Failed to access webcam registry: {ex.Message}");
            return false;
        }
    }

    private bool CheckMicrophoneState()
    {
        const string regKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
        
        try
        {
            // Check HKCU only (per PRD)
            using var key = Registry.CurrentUser.OpenSubKey(regKey);
            return CheckRegForDeviceInUse(key);
        }
        catch (Exception ex)
        {
            // Log error and assume device is off
            EventLogger.LogError($"Failed to access microphone registry: {ex.Message}");
            return false;
        }
    }

    private bool CheckRegForDeviceInUse(RegistryKey? key)
    {
        if (key == null) return false;

        try
        {
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                // Check NonPackaged subkeys
                if (subKeyName == "NonPackaged")
                {
                    using var nonpackagedKey = key.OpenSubKey(subKeyName);
                    if (nonpackagedKey == null) continue;

                    foreach (var nonpackagedSubKeyName in nonpackagedKey.GetSubKeyNames())
                    {
                        using var subKey = nonpackagedKey.OpenSubKey(nonpackagedSubKeyName);
                        if (subKey == null) continue;

                        if (IsDeviceActive(subKey))
                            return true;
                    }
                }
                else
                {
                    // Check individual application package subkeys
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    if (IsDeviceActive(subKey))
                        return true;
                }
            }
        }
        catch
        {
            // Ignore errors in individual key checks
        }

        return false;
    }

    private bool IsDeviceActive(RegistryKey subKey)
    {
        try
        {
            // Simplified detection: device is active if LastUsedTimeStop <= 0
            // This follows the example code logic
            if (!subKey.GetValueNames().Contains("LastUsedTimeStop"))
                return false;

            var endTime = subKey.GetValue("LastUsedTimeStop") is long value ? value : -1;
            return endTime <= 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}

public class DeviceStateChangedEventArgs : EventArgs
{
    public bool IsActive { get; }

    public DeviceStateChangedEventArgs(bool isActive)
    {
        IsActive = isActive;
    }
}
