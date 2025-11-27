using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace HKCheatsheetOverlay;

public partial class App : Application
{
    private static Mutex? _mutex;
    private const string MutexName = "ChokoLPT_HKCheatsheetOverlay_Mutex";

    // Custom message for singleton communication
    public const int WM_SHOW_OVERLAY = 0x8002;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChokoLPT",
        "logs",
        "hkcheatsheet_overlay_csharp.log"
    );

    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.AppendAllText(
                LogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}{Environment.NewLine}"
            );
        }
        catch
        {
            // Nunca deixar logging quebrar o app
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("OnStartup BEGIN. Args='" + string.Join(" ", e.Args) + "'");

        bool createdNew;
        _mutex = new Mutex(true, MutexName, out createdNew);
        Log("Mutex created. createdNew=" + createdNew);

        // flag: iniciar em background (pré-aquecer) sem mostrar UI
        bool startHidden = e.Args.Any(a => string.Equals(a, "--background", StringComparison.OrdinalIgnoreCase));

        if (!createdNew)
        {
            Log("Instance already running. Trying to send WM_SHOW_OVERLAY to existing window...");
            var existingWindow = FindWindow(null, "ChokoLPT - HK Cheatsheet Overlay");
            if (existingWindow != IntPtr.Zero)
            {
                Log("Existing window handle found: " + existingWindow);
                PostMessage(existingWindow, (uint)WM_SHOW_OVERLAY, IntPtr.Zero, IntPtr.Zero);
                Log("WM_SHOW_OVERLAY posted.");
            }
            else
            {
                Log("Existing window NOT found (FindWindow returned 0).");
            }

            Log("Shutting down this instance (secondary).");
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        try
        {
            Log("Creating MainWindow instance...");
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            if (startHidden)
            {
                // Modo warmup:
                // - Cria janela e pipeline WPF
                // - Registra WndProc (Loaded)
                // - Esconde imediatamente (sem taskbar)
                Log("StartHidden=true - warming up MainWindow em background (sem mostrar).");
                mainWindow.ShowInTaskbar = false;
                mainWindow.WindowState = WindowState.Minimized;

                // Deixa WPF criar o handle / disparar Loaded / registrar hook
                mainWindow.Opacity = 0;
                mainWindow.Show();
                mainWindow.Hide();
                mainWindow.Opacity = 1;

                Log("MainWindow warm-up concluído (instância residente, invisível).");
            }
            else
            {
                Log("MainWindow created. Calling Show()...");
                mainWindow.Show();
                Log("MainWindow.Show() called.");
            }
        }
        catch (Exception ex)
        {
            Log("EXCEPTION in OnStartup while creating/showing MainWindow: " + ex);
            // manter o padrão de erro visível, caso algo muito cedo quebre
            System.Windows.MessageBox.Show(
                "Erro crítico ao iniciar HKCheatsheetOverlay:\n\n" + ex,
                "HKCheatsheetOverlay - Erro",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            Shutdown(-1);
        }

        Log("OnStartup END.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log("OnExit BEGIN.");
        try
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            Log("Mutex liberado e descartado.");
        }
        catch (Exception ex)
        {
            Log("EXCEPTION in OnExit while releasing mutex: " + ex);
        }

        base.OnExit(e);
        Log("OnExit END.");
    }
}