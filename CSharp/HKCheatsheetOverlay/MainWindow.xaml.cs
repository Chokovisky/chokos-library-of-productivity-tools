using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace HKCheatsheetOverlay;

public partial class MainWindow : Window
{
    #region Native Methods
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    
    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
    
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
    
    #endregion
    
    #region Fields
    
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChokoLPT",
        "hotkeys.json"
    );
    
    private HotkeyConfig? _config;
    private string _activeProfile = "Normal";

    // Cache de config para evitar re-read/parse em todo toggle
    private bool _configLoaded = false;
    private DateTime _lastConfigWriteTimeUtc = DateTime.MinValue;

    private List<string> _contexts = new();
    private List<string> _filteredContexts = new();
    private Dictionary<string, List<string>> _contextGroups = new();
    private int _selectedContextIndex = -1;

    // Logging de performance (para medir gargalos do Toggle)
    private static readonly string PerfLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChokoLPT",
        "logs",
        "hkcheatsheet_overlay_perf.log"
    );

    private static void PerfLog(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(PerfLogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.AppendAllText(
                PerfLogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}{Environment.NewLine}"
            );
        }
        catch
        {
            // não deixar logging quebrar o overlay
        }
    }
    
    private bool _isF1Held = false;
    private bool _isF2Held = false;
    
    private string? _argActiveExe;
    private int? _argX, _argY;
    private bool _profileFromArgs = false;
    
    private static readonly Dictionary<string, string> AhkModifiers = new()
    {
        ["<^"] = "LCTRL", [">^"] = "RCTRL", ["^"] = "CTRL",
        ["<!"] = "LALT", [">!"] = "RALT", ["!"] = "ALT",
        ["<+"] = "LSHIFT", [">+"] = "RSHIFT", ["+"] = "SHIFT",
        ["<#"] = "LWIN", [">#"] = "RWIN", ["#"] = "WIN"
    };
    
    private static readonly Dictionary<string, string> KeyTranslations = new()
    {
        ["SC029"] = "`", ["Space"] = "SPACE", ["Return"] = "ENTER",
        ["Escape"] = "ESC", ["BackSpace"] = "BKSP", ["Delete"] = "DEL",
        ["Insert"] = "INS", ["Home"] = "HOME", ["End"] = "END",
        ["PgUp"] = "PGUP", ["PgDn"] = "PGDN", ["Tab"] = "TAB",
        ["CapsLock"] = "CAPS", ["NumLock"] = "NUM", ["ScrollLock"] = "SCRL",
        ["PrintScreen"] = "PRTSC", ["Pause"] = "PAUSE", ["Break"] = "BRK"
    };
    
    #endregion
    
    #region Initialization
    
    public MainWindow()
    {
        try
        {
            InitializeComponent();
            ParseCommandLineArgs(Environment.GetCommandLineArgs().Skip(1).ToArray());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro no construtor:\n{ex}", "Dashboard Error");
        }
    }
    
    private void ParseCommandLineArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--exe":
                case "-e":
                    if (i + 1 < args.Length)
                        _argActiveExe = args[++i];
                    break;
                case "--x":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int x))
                        _argX = x;
                    break;
                case "--y":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int y))
                        _argY = y;
                    break;
                case "--profile":
                case "-p":
                    if (i + 1 < args.Length)
                    {
                        _activeProfile = args[++i];
                        _profileFromArgs = true;
                    }
                    break;
            }
        }
    }
    
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Hook for receiving singleton toggle messages
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
            
            LoadConfig();
            DetectActiveContext();
            PositionWindow();
            BuildUI();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro no Window_Loaded:\n{ex}", "Dashboard Error");
        }
    }
    
    /// <summary>
    /// Handle Windows messages - for singleton toggle e comandos vindos do AHK
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == App.WM_SHOW_OVERLAY)
        {
            // Protocolo:
            // wParam =  0  → Toggle() (Alt+F1)
            // wParam =  1  → CycleContext(+1)  (F2 + scroll para baixo)
            // wParam = -1  → CycleContext(-1)  (F2 + scroll para cima)
            int command = wParam.ToInt32();

            if (command == 0)
            {
                Dispatcher.BeginInvoke(() => Toggle());
            }
            else if (command == 1)
            {
                Dispatcher.BeginInvoke(() => CycleContext(+1));
            }
            else if (command == -1)
            {
                Dispatcher.BeginInvoke(() => CycleContext(-1));
            }

            handled = true;
        }
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Toggle visibility (com logging de performance por etapa)
    /// </summary>
    public void Toggle()
    {
        var swTotal = Stopwatch.StartNew();
        PerfLog("Toggle() START");

        // Para toggles disparados via WM_SHOW_OVERLAY (AHK → PostMessage),
        // garantimos que exe/mouse/perfil venham sempre do estado ATUAL:
        // - exe ativo: GetForegroundWindow / ProcessName
        // - mouse: GetCursorPos
        // - perfil: Profiles.Active do hotkeys.json
        // Os argumentos de linha de comando são respeitados apenas no primeiro
        // render da janela (startup), não nos toggles subsequentes.
        _argActiveExe = null;
        _argX = null;
        _argY = null;
        _profileFromArgs = false;

        if (IsVisible)
        {
            Hide();
            swTotal.Stop();
            PerfLog($"Toggle() - Hide only | Total={swTotal.ElapsedMilliseconds}ms");
        }
        else
        {
            // Refresh data before showing, medindo cada etapa
            var sw = Stopwatch.StartNew();

            LoadConfig();
            PerfLog($"Toggle() step LoadConfig = {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            DetectActiveContext();
            PerfLog($"Toggle() step DetectActiveContext = {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            PositionWindow();
            PerfLog($"Toggle() step PositionWindow = {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            BuildUI();
            PerfLog($"Toggle() step BuildUI = {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            Show();
            Activate();
            PerfLog($"Toggle() step Show+Activate = {sw.ElapsedMilliseconds}ms");

            swTotal.Stop();
            PerfLog($"Toggle() END | Total={swTotal.ElapsedMilliseconds}ms");
        }
    }
    
    #endregion
    
    #region Positioning
    
    private void PositionWindow()
    {
        POINT cursorPos;
        if (_argX.HasValue && _argY.HasValue)
        {
            cursorPos = new POINT { X = _argX.Value, Y = _argY.Value };
        }
        else
        {
            GetCursorPos(out cursorPos);
        }
        
        var monitor = MonitorFromPoint(cursorPos, MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        
        if (GetMonitorInfo(monitor, ref monitorInfo))
        {
            var workArea = monitorInfo.rcWork;
            int monitorWidth = workArea.Right - workArea.Left;
            int monitorHeight = workArea.Bottom - workArea.Top;
            
            Left = workArea.Left + (monitorWidth - Width) / 2;
            Top = workArea.Top + (monitorHeight - Height) / 2;
        }
    }
    
    #endregion
    
    #region Config Loading
    
    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var writeTime = File.GetLastWriteTimeUtc(ConfigPath);

                // Se já carregamos esta versão do arquivo, não re-ler/parsear JSON.
                if (_configLoaded && writeTime == _lastConfigWriteTimeUtc && _config is not null)
                {
                    // Apenas refaz grupos/contexts para o perfil atual
                    BuildContextGroups();
                    FilterContextsByProfile();
                    return;
                }

                var json = File.ReadAllText(ConfigPath);
                _config = JsonSerializer.Deserialize<HotkeyConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _lastConfigWriteTimeUtc = writeTime;
                _configLoaded = true;
                
                if (!_profileFromArgs && _config?.Profiles?.Active != null)
                {
                    _activeProfile = _config.Profiles.Active;
                }
                
                _contexts.Clear();
                if (_config?.Contexts != null)
                {
                    foreach (var ctx in _config.Contexts.Keys)
                    {
                        _contexts.Add(ctx);
                    }
                }
                
                BuildContextGroups();
                FilterContextsByProfile();
            }
            else
            {
                LoadDemoData();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao carregar config:\n{ex.Message}\n\nUsando dados demo.", "Dashboard");
            LoadDemoData();
        }
    }
    
    private void LoadDemoData()
    {
        _configLoaded = true;
        _lastConfigWriteTimeUtc = DateTime.MinValue;

        _config = new HotkeyConfig
        {
            Profiles = new ProfilesConfig
            {
                Available = new[] { "Normal", "Gamer" },
                Active = "Normal",
                Meta = new Dictionary<string, ProfileMeta>
                {
                    ["Normal"] = new() { Icon = "💼", Description = "Produtividade" },
                    ["Gamer"] = new() { Icon = "🎮", Description = "Gaming" }
                }
            },
            Contexts = new Dictionary<string, string>
            {
                ["Excel"] = "ahk_exe EXCEL.EXE",
                ["Planilhas"] = "Google Sheets || Planilhas",
                ["Word"] = "ahk_exe WINWORD.EXE",
                ["Notion"] = "ahk_exe Notion.exe",
                ["Valorant"] = "ahk_exe valorant.exe"
            },
            ContextGroups = new Dictionary<string, string[]>
            {
                ["Planilhas"] = new[] { "Excel", "Planilhas" }
            },
            Hotkeys = new List<HotkeyItem>
            {
                new() { Id = "1", Key = "CapsLock & q", Description = "Fixar no Topo", Profiles = new[] { "Normal" }, Enabled = true },
                new() { Id = "2", Key = "CapsLock & w", Description = "Borderless", Profiles = new[] { "Normal" }, Enabled = true },
                new() { Id = "3", Key = "CapsLock & e", Description = "Opacidade (Scroll)", Profiles = new[] { "Normal" }, Enabled = true },
                new() { Id = "4", Key = "CapsLock & s", Description = "Ciclar Case", Profiles = new[] { "Normal" }, Enabled = true },
                new() { Id = "5", Key = "!w", Description = "Navegar Cima", Profiles = new[] { "Normal" }, Context = "Planilhas", Enabled = true },
                new() { Id = "6", Key = "CapsLock & p", Description = "Exportar PDF", Profiles = new[] { "Normal" }, Context = "Excel", Enabled = true },
                new() { Id = "7", Key = "F1", Description = "Arma 1", Profiles = new[] { "Gamer" }, Context = "Valorant", Enabled = true },
            }
        };
        
        _contexts = new List<string> { "Excel", "Planilhas", "Word", "Notion", "Valorant" };
        BuildContextGroups();
        FilterContextsByProfile();
    }
    
    private void BuildContextGroups()
    {
        _contextGroups.Clear();

        // 1) Se JSON já define context_groups, respeitamos 100%.
        if (_config?.ContextGroups != null && _config.ContextGroups.Count > 0)
        {
            foreach (var (groupName, members) in _config.ContextGroups)
            {
                _contextGroups[groupName] = members.ToList();
            }
        }
        else
        {
            // 2) Heurística automática: agrupa contextos cuja condição engloba outra.
            //    Ex.: "Planilhas" com cond "ahk_exe EXCEL.EXE || Google Sheets"
            //    engloba "Excel" com cond "ahk_exe EXCEL.EXE".
            if (_config?.Contexts != null)
            {
                var names = _config.Contexts.Keys.ToList();

                foreach (var outer in names)
                {
                    if (!_config.Contexts.TryGetValue(outer, out var outerCond) || string.IsNullOrWhiteSpace(outerCond))
                        continue;

                    foreach (var inner in names)
                    {
                        if (outer == inner)
                            continue;

                        if (!_config.Contexts.TryGetValue(inner, out var innerCond) || string.IsNullOrWhiteSpace(innerCond))
                            continue;

                        // Se a condição "outer" contém a "inner", consideramos que outer engloba inner.
                        if (outerCond.Contains(innerCond, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!_contextGroups.TryGetValue(outer, out var members))
                            {
                                members = new List<string>();
                                _contextGroups[outer] = members;
                            }

                            if (!members.Contains(inner))
                                members.Add(inner);
                            if (!members.Contains(outer))
                                members.Add(outer);
                        }
                    }
                }
            }
        }

        // 3) Garante que todo contexto apareça em ALGUM grupo
        foreach (var ctx in _contexts)
        {
            bool inGroup = _contextGroups.Values.Any(members => members.Contains(ctx));
            if (!inGroup)
            {
                _contextGroups[ctx] = new List<string> { ctx };
            }
        }
    }
    
    private void FilterContextsByProfile()
    {
        _filteredContexts.Clear();
        
        if (_config?.Hotkeys == null) return;
        
        foreach (var (groupName, members) in _contextGroups)
        {
            var hasHotkey = _config.Hotkeys.Any(h => 
                h.Enabled && 
                members.Contains(h.Context ?? "") && 
                (h.Profiles == null || h.Profiles.Length == 0 || h.Profiles.Contains(_activeProfile)));
            
            if (hasHotkey)
            {
                _filteredContexts.Add(groupName);
            }
        }
    }
    
    private void DetectActiveContext()
    {
        try
        {
            string? exeName = _argActiveExe;
            
            if (string.IsNullOrEmpty(exeName))
            {
                var hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(hwnd, out uint processId);
                    var process = Process.GetProcessById((int)processId);
                    exeName = process.ProcessName + ".exe";
                }
            }
            
            if (string.IsNullOrEmpty(exeName)) return;
            
            for (int i = 0; i < _filteredContexts.Count; i++)
            {
                var groupName = _filteredContexts[i];
                var members = _contextGroups.GetValueOrDefault(groupName) ?? new List<string> { groupName };
                
                foreach (var ctx in members)
                {
                    if (_config?.Contexts?.TryGetValue(ctx, out var condition) == true)
                    {
                        if (condition.Contains(exeName, StringComparison.OrdinalIgnoreCase))
                        {
                            _selectedContextIndex = i;
                            return;
                        }
                    }
                }
            }
            
            _selectedContextIndex = -1;
        }
        catch
        {
            _selectedContextIndex = -1;
        }
    }
    
    #endregion
    
    #region UI Building
    
    private void BuildUI()
    {
        BuildProfiles();
        BuildContexts();
        BuildHotkeys();
        UpdateHotkeyCount();
    }
    
    private void BuildProfiles()
    {
        ProfilesPanel.Children.Clear();
        
        var profiles = _config?.Profiles?.Available ?? new[] { "Normal", "Gamer" };
        
        foreach (var profile in profiles)
        {
            var isActive = profile == _activeProfile;
            var meta = _config?.Profiles?.Meta?.GetValueOrDefault(profile);
            var icon = meta?.Icon ?? (profile == "Gamer" ? "🎮" : "💼");
            
            var border = new Border
            {
                Background = isActive 
                    ? (Brush)FindResource("AccentBrush") 
                    : (Brush)FindResource("BgTertiaryBrush"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Tag = profile
            };
            
            border.MouseLeftButtonDown += Profile_Click;
            
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock 
            { 
                Text = icon, 
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(new TextBlock 
            { 
                Text = profile,
                Foreground = isActive ? Brushes.White : (Brush)FindResource("TextSecondaryBrush"),
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            });
            
            border.Child = stack;
            ProfilesPanel.Children.Add(border);
        }
    }
    
    private void BuildContexts()
    {
        ContextsPanel.Children.Clear();
        
        AddContextItem("Global", -1);
        
        for (int i = 0; i < _filteredContexts.Count; i++)
        {
            AddContextItem(_filteredContexts[i], i);
        }
    }
    
    private void AddContextItem(string name, int index)
    {
        var isActive = (_selectedContextIndex == index);
        
        var border = new Border
        {
            Background = isActive 
                ? new SolidColorBrush(Color.FromRgb(0, 60, 40)) 
                : Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 2),
            Cursor = Cursors.Hand,
            Tag = index
        };
        
        border.MouseLeftButtonDown += Context_Click;
        
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        
        stack.Children.Add(new TextBlock
        {
            Text = isActive ? "▸" : "•",
            Foreground = isActive 
                ? (Brush)FindResource("AccentGreenBrush") 
                : (Brush)FindResource("TextMutedBrush"),
            Width = 16,
            FontWeight = FontWeights.Bold
        });
        
        stack.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = isActive 
                ? (Brush)FindResource("AccentGreenBrush") 
                : (Brush)FindResource("TextSecondaryBrush"),
            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal
        });
        
        border.Child = stack;
        ContextsPanel.Children.Add(border);
    }
    
    private void BuildHotkeys()
    {
        GlobalHotkeysPanel.Children.Clear();
        ContextHotkeysPanel.Children.Clear();
        
        if (_config?.Hotkeys == null) return;
        
        var profileHotkeys = _config.Hotkeys
            .Where(h => h.Enabled && (h.Profiles == null || h.Profiles.Length == 0 || h.Profiles.Contains(_activeProfile)))
            .ToList();
        
        var globalHotkeys = profileHotkeys.Where(h => string.IsNullOrEmpty(h.Context)).ToList();
        foreach (var hk in globalHotkeys)
        {
            GlobalHotkeysPanel.Children.Add(CreateHotkeyRow(hk, false, null));
        }
        
        GlobalHeader.Text = $"GLOBAL ({globalHotkeys.Count})";
        
        if (_selectedContextIndex >= 0 && _selectedContextIndex < _filteredContexts.Count)
        {
            var groupName = _filteredContexts[_selectedContextIndex];
            var members = _contextGroups.GetValueOrDefault(groupName) ?? new List<string> { groupName };
            
            var contextHotkeys = profileHotkeys
                .Where(h => members.Contains(h.Context ?? ""))
                .ToList();
            
            bool showMemberLabel = members.Count > 1;
            
            ContextHeader.Text = $"{groupName.ToUpper()} ({contextHotkeys.Count})";
            ContextHeader.Foreground = (Brush)FindResource("AccentGreenBrush");
            ContextHeaderBorder.Background = new SolidColorBrush(Color.FromRgb(10, 30, 20));
            ContextEmptyState.Visibility = contextHotkeys.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            
            foreach (var hk in contextHotkeys)
            {
                var memberLabel = showMemberLabel ? hk.Context : null;
                ContextHotkeysPanel.Children.Add(CreateHotkeyRow(hk, true, memberLabel));
            }
        }
        else
        {
            ContextHeader.Text = "DESKTOP";
            ContextHeader.Foreground = (Brush)FindResource("TextSecondaryBrush");
            ContextHeaderBorder.Background = (Brush)FindResource("BgTertiaryBrush");
            ContextEmptyState.Visibility = Visibility.Visible;
        }
    }
    
    private Border CreateHotkeyRow(HotkeyItem hk, bool isContext, string? memberLabel)
    {
        var border = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 6, 6, 6),
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(4)
        };
        
        var grid = new Grid();
        // Coluna 0 = badges de teclas (auto)   |   Coluna 1 = descrição (ocupa resto)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        
        var badgesPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var keys = FormatHotkey(hk.Key ?? "");
        
        foreach (var key in keys)
        {
            badgesPanel.Children.Add(CreateKeyBadge(key, isContext));
        }
        
        Grid.SetColumn(badgesPanel, 0);
        grid.Children.Add(badgesPanel);
        
        var descPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0) // afasta descrição dos badges (evita sobreposição)
        };
        
        if (!string.IsNullOrEmpty(memberLabel))
        {
            var labelBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(0, 0, 6, 0)
            };
            labelBorder.Child = new TextBlock
            {
                Text = memberLabel?.ToUpperInvariant(),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            };
            descPanel.Children.Add(labelBorder);
        }
        
        descPanel.Children.Add(new TextBlock
        {
            Text = hk.Description ?? "",
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        
        Grid.SetColumn(descPanel, 1);
        grid.Children.Add(descPanel);
        
        border.Child = grid;
        return border;
    }
    
    private Border CreateKeyBadge(string key, bool isContext)
    {
        var bgColor = isContext 
            ? Color.FromRgb(10, 40, 24)
            : Color.FromRgb(31, 28, 8);
        
        var fgBrush = isContext
            ? (Brush)FindResource("AccentGreenBrush")
            : (Brush)FindResource("AccentGoldBrush");
        
        return new Border
        {
            Background = new SolidColorBrush(bgColor),
            BorderBrush = fgBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 3, 0),
            Child = new TextBlock
            {
                Text = key,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                Foreground = fgBrush
            }
        };
    }
    
    private void UpdateHotkeyCount()
    {
        var count = _config?.Hotkeys?
            .Count(h => h.Enabled && (h.Profiles == null || h.Profiles.Length == 0 || h.Profiles.Contains(_activeProfile))) ?? 0;
        
        HotkeyCount.Text = $"{count} hotkeys • {_activeProfile}";
    }
    
    #endregion
    
    #region Hotkey Parsing
    
    private List<string> FormatHotkey(string hotkeyStr)
    {
        if (string.IsNullOrWhiteSpace(hotkeyStr))
            return new List<string> { "?" };
        
        hotkeyStr = hotkeyStr.Trim();
        
        if (hotkeyStr.Contains(" & "))
        {
            var parts = hotkeyStr.Split(" & ", 2);
            var result = new List<string> { TranslateKey(parts[0].Trim()) };
            if (parts.Length > 1)
            {
                result.AddRange(ParseSingleKey(parts[1].Trim()));
            }
            return result;
        }
        
        if (hotkeyStr.Contains(" + "))
        {
            return hotkeyStr.Split(" + ").Select(k => TranslateKey(k.Trim())).ToList();
        }
        
        return ParseSingleKey(hotkeyStr);
    }
    
    private List<string> ParseSingleKey(string key)
    {
        var result = new List<string>();
        var remaining = key;
        
        while (remaining.Length > 0)
        {
            if (remaining.Length >= 2)
            {
                var twoChar = remaining[..2];
                if (AhkModifiers.TryGetValue(twoChar, out var mod2))
                {
                    result.Add(mod2);
                    remaining = remaining[2..];
                    continue;
                }
            }
            
            var oneChar = remaining[..1];
            if (AhkModifiers.TryGetValue(oneChar, out var mod1))
            {
                result.Add(mod1);
                remaining = remaining[1..];
                continue;
            }
            
            break;
        }
        
        if (!string.IsNullOrEmpty(remaining))
        {
            result.Add(TranslateKey(remaining));
        }
        
        return result.Count > 0 ? result : new List<string> { "?" };
    }
    
    private string TranslateKey(string key)
    {
        if (KeyTranslations.TryGetValue(key, out var translated))
            return translated;
        
        return key.ToUpperInvariant();
    }
    
    #endregion
    
    #region Event Handlers
    
    private void Profile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string profile)
        {
            _activeProfile = profile;
            FilterContextsByProfile();
            _selectedContextIndex = -1;
            BuildUI();
        }
    }
    
    private void Context_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is int index)
        {
            _selectedContextIndex = index;
            BuildContexts();
            BuildHotkeys();
        }
    }
    
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide(); // Hide instead of Close to keep singleton running
                break;
            case Key.F1:
                _isF1Held = true;
                e.Handled = true;
                break;
            case Key.F2:
                _isF2Held = true;
                e.Handled = true;
                break;
        }
    }
    
    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // Comportamento opcional da spec desativado:
        // não esconder o overlay automaticamente ao perder foco.
        // Fechamento agora é só por:
        // - ESC (Window_KeyDown)
        // - Toggle via AHK (WinClose) se desejado
    }
    
    private void Window_KeyUp(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F1:
                _isF1Held = false;
                break;
            case Key.F2:
                _isF2Held = false;
                break;
        }
    }
    
    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isF2Held)
        {
            CycleContext(e.Delta > 0 ? -1 : 1);
            e.Handled = true;
            return;
        }
        
        if (_isF1Held)
        {
            GlobalScroller.ScrollToVerticalOffset(GlobalScroller.VerticalOffset - e.Delta / 3);
            e.Handled = true;
        }
    }
    
    private void CycleContext(int direction)
    {
        _selectedContextIndex += direction;
        
        if (_selectedContextIndex < -1)
            _selectedContextIndex = _filteredContexts.Count - 1;
        if (_selectedContextIndex >= _filteredContexts.Count)
            _selectedContextIndex = -1;
        
        BuildContexts();
        BuildHotkeys();
    }
    
    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Hide(); // Hide instead of Close
        }
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }
    
    #endregion
}

#region Data Models

public class HotkeyConfig
{
    public ProfilesConfig? Profiles { get; set; }
    public Dictionary<string, string>? Contexts { get; set; }
    public Dictionary<string, string[]>? ContextGroups { get; set; }
    public List<HotkeyItem>? Hotkeys { get; set; }
}

public class ProfilesConfig
{
    public string[]? Available { get; set; }
    public string? Active { get; set; }
    public Dictionary<string, ProfileMeta>? Meta { get; set; }
}

public class ProfileMeta
{
    public string? Icon { get; set; }
    public string? Description { get; set; }
}

public class HotkeyItem
{
    public string? Id { get; set; }
    public string? Key { get; set; }
    public string? Description { get; set; }
    public string[]? Profiles { get; set; }
    public string? Context { get; set; }

    // Alguns JSONs antigos podem ter "enabled" como string ("true"/"false") ou número (0/1).
    // Este converter torna o parser tolerante a esses formatos.
    [JsonConverter(typeof(FlexibleBoolConverter))]
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Converter tolerante para campos bool (aceita bool, string "true"/"false", "0"/"1", números).
/// </summary>
public sealed class FlexibleBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.Number:
                try
                {
                    if (reader.TryGetInt64(out var num))
                        return num != 0;
                }
                catch
                {
                    // ignore e cai para false mais abaixo
                }
                break;
            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    return false;

                // Tenta bool direto
                if (bool.TryParse(s, out var b))
                    return b;

                // Tenta número em string
                if (long.TryParse(s, out var numStr))
                    return numStr != 0;

                // Alguns casos tipo "yes"/"no"
                s = s.Trim().ToLowerInvariant();
                if (s is "y" or "yes" or "sim")
                    return true;
                if (s is "n" or "no" or "nao" or "não")
                    return false;
                break;
        }

        // Valor inesperado: em vez de estourar exceção e matar o dashboard,
        // assume false para continuar exibindo o resto das hotkeys.
        return false;
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}

#endregion
