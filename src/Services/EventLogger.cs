using System;
using System.IO;

namespace HAMeetingLight.Services;

/// <summary>
/// Handles logging to local info.log file
/// </summary>
public static class EventLogger
{
    private const string LogFileName = "info.log";
    private static readonly object FileLock = new();
    private static readonly string LogFilePath;
    private static bool _logToFileEnabled = false;

    static EventLogger()
    {
        LogFilePath = Path.Combine(AppContext.BaseDirectory, LogFileName);
    }

    public static void Configure(bool logToFile)
    {
        _logToFileEnabled = logToFile;
    }

    public static void LogInformation(string message)
    {
        try
        {
            AppendLine(message);
        }
        catch
        {
            // Silently fail if we can't write to log file
        }
    }

    public static void LogWarning(string message)
    {
        try
        {
            AppendLine(message);
        }
        catch
        {
            // Silently fail if we can't write to log file
        }
    }

    public static void LogError(string message)
    {
        try
        {
            AppendLine(message);
        }
        catch
        {
            // Silently fail if we can't write to log file
        }
    }

    private static void AppendLine(string message)
    {
        if (!_logToFileEnabled)
        {
            return;
        }

        lock (FileLock)
        {
            File.AppendAllText(LogFilePath, message + Environment.NewLine);
        }
    }
}

