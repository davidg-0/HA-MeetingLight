using IniParser;
using IniParser.Model;
using HAMeetingLight.Services;

namespace HAMeetingLight.Configuration;

/// <summary>
/// Loads and validates configuration from config.ini
/// </summary>
public static class ConfigLoader
{
    private const string ConfigFileName = "config.ini";

    public static AppConfig LoadConfiguration()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);

        // Check if config file exists
        if (!File.Exists(configPath))
        {
            EventLogger.LogError("Configuration file missing: config.ini not found.");
            throw new ConfigurationException("Configuration file missing. Application cannot start.");
        }

        try
        {
            var parser = new FileIniDataParser();
            IniData data = parser.ReadFile(configPath);

            var config = new AppConfig();

            // Parse MQTT section
            if (!data.Sections.ContainsSection("MQTT"))
            {
                EventLogger.LogError("Invalid configuration: [MQTT] section is missing.");
                throw new ConfigurationException("Invalid configuration: MQTT Server required.");
            }

            var mqttSection = data["MQTT"];

            // MQTT Server is required
            if (string.IsNullOrWhiteSpace(mqttSection["Server"]))
            {
                EventLogger.LogError("Invalid configuration: MQTT Server parameter is missing or empty.");
                throw new ConfigurationException("Invalid configuration: MQTT Server required.");
            }

            config.Mqtt.Server = mqttSection["Server"];

            // Parse optional MQTT Port
            if (!string.IsNullOrWhiteSpace(mqttSection["Port"]))
            {
                if (int.TryParse(mqttSection["Port"], out int port) && port >= 1 && port <= 65535)
                {
                    config.Mqtt.Port = port;
                }
                else
                {
                    EventLogger.LogWarning($"Invalid MQTT Port value: '{mqttSection["Port"]}'. Using default: 1883");
                    config.Mqtt.Port = 1883;
                }
            }

            // Parse optional MQTT credentials
            config.Mqtt.Username = mqttSection["Username"] ?? string.Empty;
            config.Mqtt.Password = mqttSection["Password"] ?? string.Empty;

            // Parse Monitoring section
            if (data.Sections.ContainsSection("Monitoring"))
            {
                var monitoringSection = data["Monitoring"];

                // Parse optional PollingIntervalSeconds
                if (!string.IsNullOrWhiteSpace(monitoringSection["PollingIntervalSeconds"]))
                {
                    if (int.TryParse(monitoringSection["PollingIntervalSeconds"], out int interval) && interval >= 1 && interval <= 60)
                    {
                        config.Monitoring.PollingIntervalSeconds = interval;
                    }
                    else
                    {
                        EventLogger.LogWarning($"Invalid PollingIntervalSeconds value: '{monitoringSection["PollingIntervalSeconds"]}'. Using default: 2");
                        config.Monitoring.PollingIntervalSeconds = 10;
                    }
                }
            }

            // Parse Logging section
            if (data.Sections.ContainsSection("Logging"))
            {
                var loggingSection = data["Logging"];
                var value = loggingSection["LogToFile"];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var normalized = value.Trim().ToLowerInvariant();
                    config.Logging.LogToFile = normalized == "yes" || normalized == "true";
                }
            }

            return config;
        }
        catch (ConfigurationException)
        {
            throw; // Re-throw configuration exceptions
        }
        catch (Exception ex)
        {
            // Malformed configuration file
            EventLogger.LogError($"Configuration file is malformed: {ex.Message}");
            throw new ConfigurationException("Configuration file is malformed.");
        }
    }
}

public class ConfigurationException : Exception
{
    public ConfigurationException(string message) : base(message) { }
}

