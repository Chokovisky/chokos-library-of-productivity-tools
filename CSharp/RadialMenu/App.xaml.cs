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
    
    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Console.WriteLine(line);
        Console.Out.Flush();
        
        // Also log to file for debugging
        try
        {
            File.AppendAllText("radialmenu_debug.log", line + Environment.NewLine);
        }
        catch { }
    }
    
    protected override void OnStartup(StartupEventArgs e)
    {
        // Attach to parent console (so we can see output in terminal)
        AttachConsole(ATTACH_PARENT_PROCESS);
        
        // Clear old log
        try { File.Delete("radialmenu_debug.log"); } catch { }
        
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
