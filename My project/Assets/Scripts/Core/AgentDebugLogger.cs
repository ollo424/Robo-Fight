using System;
using System.IO;
using UnityEngine;

public static class AgentDebugLogger
{
    private const string SessionId = "6c05a1";
    private const string DefaultRunId = "run1";
    private static readonly object FileLock = new object();

    private static string LogPath
    {
        get
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "debug-6c05a1.log"));
            return path.Replace("\\", "/");
        }
    }

    public static void Log(string hypothesisId, string location, string message, string dataJson, string runId = DefaultRunId)
    {
        try
        {
            string line =
                "{\"sessionId\":\"" + SessionId + "\"," +
                "\"runId\":\"" + Escape(runId) + "\"," +
                "\"hypothesisId\":\"" + Escape(hypothesisId) + "\"," +
                "\"location\":\"" + Escape(location) + "\"," +
                "\"message\":\"" + Escape(message) + "\"," +
                "\"data\":" + (string.IsNullOrWhiteSpace(dataJson) ? "{}" : dataJson) + "," +
                "\"timestamp\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}";

            lock (FileLock)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Intentionally swallowed for runtime safety during debugging.
        }
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }
}
