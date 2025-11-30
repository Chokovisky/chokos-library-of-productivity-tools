using System;
using System.IO;

namespace ChokoLPT.Shared.Services;

public static class LogService
{
    private static readonly string BaseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChokoLPT",
        "logs"
    );

    /// <summary>
    /// Returns the base log directory: %LocalAppData%\ChokoLPT\logs
    /// </summary>
    public static string GetLogDirectory() => BaseDirectory;

    /// <summary>
    /// Returns the full path for a given log file name under the shared log directory.
    /// </summary>
    public static string GetLogPath(string fileName)
    {
        return Path.Combine(BaseDirectory, fileName);
    }

    /// <summary>
    /// Safely appends a line to the specified log file under %LocalAppData%\ChokoLPT\logs.
    /// Never throws: any IO errors are swallowed to avoid crashing caller apps.
    /// </summary>
    public static void AppendLine(string fileName, string line)
    {
        try
        {
            if (!Directory.Exists(BaseDirectory))
            {
                Directory.CreateDirectory(BaseDirectory);
            }

            var path = Path.Combine(BaseDirectory, fileName);
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never break the host app
        }
    }
}