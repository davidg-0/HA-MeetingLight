using HAMeetingLight.Configuration;
using MQTTnet;
using MQTTnet.Protocol;
using System.Text.Json;

namespace HAMeetingLight.Services;

/// <summary>
/// Manages MQTT connection and message publishing
/// </summary>
public class MqttService : IDisposable
{
    private readonly MqttConfig _config;
    private readonly IMqttClient _mqttClient;
    private readonly string _hostname;
    private readonly System.Threading.Timer _reconnectTimer;
    private bool _isConnected = false;
    private bool _hasNotifiedConnected = false;
    private bool _hasNotifiedDisconnected = false;

    public event EventHandler<MqttConnectionStatusEventArgs>? ConnectionStatusChanged;

    public bool IsConnected => _isConnected;

    public MqttService(MqttConfig config)
    {
        _config = config;
        _mqttClient = new MqttClientFactory().CreateMqttClient();

        // Get hostname
        try
        {
            _hostname = Environment.MachineName;
        }
        catch
        {
            _hostname = "UNKNOWN-PC";
            EventLogger.LogError("Failed to determine hostname. Using 'UNKNOWN-PC' as fallback.");
        }

        // Set up reconnect timer (retry every 60 seconds)
        _reconnectTimer = new System.Threading.Timer(ReconnectCallback, null, Timeout.Infinite, Timeout.Infinite);

        // Handle disconnection events
        _mqttClient.DisconnectedAsync += OnDisconnected;
    }

    public async Task ConnectAsync()
    {
        try
        {
            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(_config.Server, _config.Port)
                .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
                .WithCleanSession();

            // Add credentials if provided
            if (!string.IsNullOrWhiteSpace(_config.Username))
            {
                optionsBuilder.WithCredentials(_config.Username, _config.Password);
            }

            var options = optionsBuilder.Build();

            var result = await _mqttClient.ConnectAsync(options);

            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                _isConnected = true;

                // Notify connection success (only if not already notified)
                if (!_hasNotifiedConnected)
                {
                    EventLogger.LogInformation("MQTT server connected.");
                    ConnectionStatusChanged?.Invoke(this, new MqttConnectionStatusEventArgs(true, "MQTT server connected."));
                    _hasNotifiedConnected = true;
                    _hasNotifiedDisconnected = false;
                }

                // Publish Home Assistant discovery configs for sensors
                await PublishDiscoveryConfigAsync();
            }
            else
            {
                throw new Exception($"Connection failed with result: {result.ResultCode}");
            }
        }
        catch (Exception ex)
        {
            _isConnected = false;

            // Notify connection failure (only if not already notified)
            if (!_hasNotifiedDisconnected)
            {
                EventLogger.LogError($"MQTT connection failed: {ex.Message}");
                ConnectionStatusChanged?.Invoke(this, new MqttConnectionStatusEventArgs(false, "MQTT server connection failed."));
                _hasNotifiedDisconnected = true;
                _hasNotifiedConnected = false;
            }

            // Start reconnect timer
            _reconnectTimer.Change(60000, Timeout.Infinite);
        }
    }

    private Task OnDisconnected(MqttClientDisconnectedEventArgs args)
    {
        if (_isConnected)
        {
            _isConnected = false;
            EventLogger.LogWarning("MQTT connection lost. Will attempt to reconnect.");
            
            // Only notify if state changed
            if (!_hasNotifiedDisconnected)
            {
                ConnectionStatusChanged?.Invoke(this, new MqttConnectionStatusEventArgs(false, "MQTT server connection failed."));
                _hasNotifiedDisconnected = true;
                _hasNotifiedConnected = false;
            }

            // Start reconnect timer
            _reconnectTimer.Change(60000, Timeout.Infinite);
        }

        return Task.CompletedTask;
    }

    private async void ReconnectCallback(object? state)
    {
        if (!_isConnected)
        {
            await ConnectAsync();
        }
    }

    private async Task PublishDiscoveryConfigAsync()
    {
        if (!_isConnected) return;

        try
        {
            // Topics: homeassistant/binary_sensor...
            var baseDiscoveryPrefix = "homeassistant/binary_sensor";
            var baseStateTopic = $"HA-MeetingLight/{_hostname}";

            var deviceObject = new Dictionary<string, object?>
            {
                { "identifiers", new[] { $"{_hostname}" } },
                { "name", $"{_hostname}" },
                { "manufacturer", "HA-MeetingLight" },
                { "model", "HA-MeetingLight Windows agent" }
            };

            // Webcam
            var webcamConfigTopic = $"{baseDiscoveryPrefix}/{_hostname}/{_hostname}_webcam/config";
            var webcamPayload = new Dictionary<string, object?>
            {
                { "name", $"Webcam" },
                { "unique_id", $"{_hostname}_webcam" },
                { "state_topic", $"{baseStateTopic}/webcam" },
                { "payload_on", "on" },
                { "payload_off", "off" },
            //    { "device_class", "occupancy" },
                { "device", deviceObject }
            };

            // Microphone
            var microphoneConfigTopic = $"{baseDiscoveryPrefix}/{_hostname}/{_hostname}_microphone/config";
            var microphonePayload = new Dictionary<string, object?>
            {
                { "name", $"Microphone" },
                { "unique_id", $"{_hostname}_microphone" },
                { "state_topic", $"{baseStateTopic}/microphone" },
                { "payload_on", "on" },
                { "payload_off", "off" },
            //    { "device_class", "sound" },
                { "device", deviceObject }
            };

            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };

            var webcamMessage = new MqttApplicationMessageBuilder()
                .WithTopic(webcamConfigTopic)
                .WithPayload(JsonSerializer.Serialize(webcamPayload, jsonOptions))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                .WithRetainFlag(true)
                .Build();

            var microphoneMessage = new MqttApplicationMessageBuilder()
                .WithTopic(microphoneConfigTopic)
                .WithPayload(JsonSerializer.Serialize(microphonePayload, jsonOptions))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                .WithRetainFlag(true)
                .Build();

            await _mqttClient.PublishAsync(webcamMessage);
            await _mqttClient.PublishAsync(microphoneMessage);
        }
        catch (Exception ex)
        {
            EventLogger.LogError($"Failed to publish Home Assistant discovery config: {ex.Message}");
        }
    }

    public async Task PublishDeviceStateAsync(string deviceType, bool isActive)
    {
        if (!_isConnected) return;

        try
        {
            // Topic format HA-MeetingLight/{PC-HOSTNAME}/{deviceType}
            var topic = $"HA-MeetingLight/{_hostname}/{deviceType}";
            
            // Payload is "on" or "off"
            var payload = isActive ? "on" : "off";

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce) // QoS 0
                .WithRetainFlag(true) // Retain flag
                .Build();

            await _mqttClient.PublishAsync(message);
        }
        catch (Exception ex)
        {
            EventLogger.LogError($"Failed to publish {deviceType} state: {ex.Message}");
        }
    }

    public async Task DisconnectAsync()
    {
        _reconnectTimer.Change(Timeout.Infinite, Timeout.Infinite);

        if (_isConnected)
        {
            try
            {
                await _mqttClient.DisconnectAsync();
            }
            catch (Exception ex)
            {
                EventLogger.LogError($"Error disconnecting from MQTT: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _reconnectTimer?.Dispose();
        _mqttClient?.Dispose();
    }
}

public class MqttConnectionStatusEventArgs : EventArgs
{
    public bool IsConnected { get; }
    public string Message { get; }

    public MqttConnectionStatusEventArgs(bool isConnected, string message)
    {
        IsConnected = isConnected;
        Message = message;
    }
}
