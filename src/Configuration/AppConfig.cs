namespace HAMeetingLight.Configuration;

/// <summary>
/// Application configuration model
/// </summary>
public class AppConfig
{
    public MqttConfig Mqtt { get; set; } = new();
    public MonitoringConfig Monitoring { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
}

public class MqttConfig
{
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 1883;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class MonitoringConfig
{
    public int PollingIntervalSeconds { get; set; } = 10;
}

public class LoggingConfig
{
    public bool LogToFile { get; set; } = false;
}
