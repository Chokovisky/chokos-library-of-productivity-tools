using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace RadialMenu;

public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
    
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();
    
    private const int ATTACH_PARENT_PROCESS = -1;

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChokoLPT",
        "logs"
    );

    private static readonly string LogFilePath = Path.Combine(LogDirectory, "radialmenu_debug.log");
    
    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Console.WriteLine(line);
        Console.Out.Flush();
        
        // Also log to file for debugging under %LocalAppData%\ChokoLPT\logs
        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }
            
            File.AppendAllText(LogFilePath, line + Environment.NewLine);
        }
        catch
        {
            // Never let logging crash the app
        }
    }
    
    protected override void OnStartup(StartupEventArgs e)
    {
        // Attach to parent console (so we can see output in terminal)
        AttachConsole(ATTACH_PARENT_PROCESS);
        
        // Clear old log under %LocalAppData%\ChokoLPT\logs
        try
        {
            if (File.Exists(LogFilePath))
            {
                File.Delete(LogFilePath);
            }
        }
        catch
        {
            // Ignore log cleanup failures
        }
        
        Log("App.OnStartup called");
        Log($"Args: [{string.Join(", ", e.Args)}]");
        
        base.OnStartup(e);
        
        // Parse command line arguments
        var args = e.Args;
        if (args.Length > 0)
        {
            Log("Creating MainWindow with args");
            MainWindow = new MainWindow(args);
        }
        else
        {
            Log("Creating MainWindow without args (stdin/demo mode)");
            MainWindow = new MainWindow(null);
        }
        
        Log("Calling MainWindow.Show()");
        MainWindow.Show();
        Log("MainWindow.Show() completed");
    }
}
