using System.IO;
using System.Linq;
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

    /// <summary>
    /// Indica se esta instância foi iniciada em modo de warmup (--background).
    /// Usado pelo MainWindow para evitar roubar foco/lockar cursor no startup.
    /// </summary>
    public static bool IsBackgroundStartup { get; private set; }

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChokoLPT",
        "logs"
    );

    private static readonly string LogFilePath = Path.Combine(LogDirectory, "radialmenu_debug.log");
    
    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        
        // Log only to file under %LocalAppData%\ChokoLPT\logs.
        // Do NOT write to stdout, so stdout stays clean for the final selection id.
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
        
        // Detect warmup flag
        IsBackgroundStartup = e.Args.Any(a =>
            string.Equals(a, "--background", StringComparison.OrdinalIgnoreCase));

        Log("App.OnStartup called");
        Log($"Args: [{string.Join(", ", e.Args)}]");
        Log($"IsBackgroundStartup={IsBackgroundStartup}");
        
        base.OnStartup(e);
        
        // Remove --background from args before passar pro MainWindow
        var effectiveArgs = e.Args
            .Where(a => !string.Equals(a, "--background", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        
        if (effectiveArgs.Length > 0)
        {
            Log("Creating MainWindow with effective args");
            MainWindow = new MainWindow(effectiveArgs);
        }
        else
        {
            Log("Creating MainWindow without args (stdin/demo mode / warmup)");
            MainWindow = new MainWindow(null);
        }
        
        var mainWindow = (MainWindow)MainWindow;
        
        if (IsBackgroundStartup)
        {
            // Warmup invisível:
            // - Cria janela e pipeline WPF
            // - Registra WndProc (Loaded)
            // - Esconde imediatamente (sem taskbar), sem roubar foco/lockar mouse
            Log("Warmup mode: initializing MainWindow in background (hidden).");
            mainWindow.ShowInTaskbar = false;
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Opacity = 0;
            mainWindow.Show();
            mainWindow.Hide();
            mainWindow.Opacity = 1;
            Log("Warmup completed, resident window hidden.");
        }
        else
        {
            Log("Calling MainWindow.Show() (normal mode)");
            mainWindow.Show();
            Log("MainWindow.Show() completed");
        }
    }
}
