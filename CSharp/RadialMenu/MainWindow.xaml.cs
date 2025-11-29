using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace RadialMenu;

public partial class MainWindow : Window
{
    #region Native Methods
    
    [DllImport("user32.dll")]
    private static extern bool ClipCursor(ref RECT lpRect);
    
    [DllImport("user32.dll")]
    private static extern bool ClipCursor(IntPtr lpRect);
    
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
    
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
    
    [DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);
    
    [DllImport("user32.dll")]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);
    
    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
    
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    
    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWMOUSE
    {
        public ushort usFlags;
        public ushort usButtonFlags;  // This is part of union with ulButtons
        public ushort usButtonData;
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUT
    {
        public RAWINPUTHEADER header;
        public RAWMOUSE mouse;
    }
    
    private const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEMOUSE = 0;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    
    #endregion
    
    #region Fields
    
    private List<MenuItemData> _menuItems = new();
    private int _selectedIndex = -1; // -1 = center/cancel
    private double _virtualX = 0;
    private double _virtualY = 0;
    private int _debugCounter = 0;
    private Point _menuCenter;
    private double _menuRadius = 150;
    private double _cancelRadius = 30;  // Smaller cancel zone
    private double _innerRadius = 50;
    private double _selectionThreshold = 15;  // Minimum distance to select (very small!)
    private bool _isClosing = false;
    private string? _result = null;
    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private Point _originalCursorPos;
    private bool _cursorHidden = false;
    
    // Sensitivity - higher = less mouse movement needed
    private double _sensitivity = 2.5;
    
    // Decay factor - how fast the position returns toward center when changing direction
    private double _decayFactor = 0.15;
    
    // Track last angle for smoother transitions
    private double _lastAngle = 0;
    private bool _hasSelection = false;
    
    // Mode: center of screen vs at mouse position
    private bool _atMousePosition = false;
    private double _itemSize = 70;  // Size of each item circle
    
    #endregion
    
    #region Initialization
    
    public MainWindow(string[]? args)
    {
        App.Log("MainWindow constructor called");
        
        InitializeComponent();
        
        App.Log("InitializeComponent completed");
        
        // Parse arguments or use demo items
        if (args != null && args.Length > 0)
        {
            ParseArguments(args);
        }
        else
        {
            // Check for stdin input
            if (Console.IsInputRedirected)
            {
                ReadFromStdin();
            }
            else
            {
                // Demo mode
                LoadDemoItems();
            }
        }
        
        App.Log($"Loaded {_menuItems.Count} menu items");
        
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }
    
    private void ParseArguments(string[] args)
    {
        // Format: --items "Item1|Item2|Item3" [--icons "icon1.png|icon2.png"] [--colors "#FF0000|#00FF00"]
        //         [--at-mouse] [--mini] [--radius 100] [--sensitivity 2.0]
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--items":
                case "-i":
                    if (i + 1 < args.Length)
                    {
                        var items = args[++i].Split('|');
                        foreach (var item in items)
                        {
                            _menuItems.Add(new MenuItemData { Label = item });
                        }
                    }
                    break;
                    
                case "--icons":
                    if (i + 1 < args.Length)
                    {
                        var icons = args[++i].Split('|');
                        for (int j = 0; j < Math.Min(icons.Length, _menuItems.Count); j++)
                        {
                            _menuItems[j].IconPath = icons[j];
                        }
                    }
                    break;
                    
                case "--colors":
                    if (i + 1 < args.Length)
                    {
                        var colors = args[++i].Split('|');
                        for (int j = 0; j < Math.Min(colors.Length, _menuItems.Count); j++)
                        {
                            _menuItems[j].Color = colors[j];
                        }
                    }
                    break;
                    
                case "--radius":
                    if (i + 1 < args.Length && double.TryParse(args[++i], out double r))
                    {
                        _menuRadius = r;
                    }
                    break;
                    
                case "--sensitivity":
                    if (i + 1 < args.Length && double.TryParse(args[++i], out double s))
                    {
                        _sensitivity = s;
                    }
                    break;
                    
                case "--at-mouse":
                case "-m":
                    // Show menu at mouse position instead of center
                    _atMousePosition = true;
                    break;
                    
                case "--mini":
                    // Smaller menu for contextual actions
                    _atMousePosition = true;
                    _menuRadius = 100;
                    _itemSize = 50;
                    _selectionThreshold = 10;
                    break;
            }
        }
        
        if (_menuItems.Count == 0)
        {
            LoadDemoItems();
        }
    }
    
    private void ReadFromStdin()
    {
        // Read JSON or simple format from stdin
        // Format: one item per line, or JSON array
        try
        {
            string? line;
            while ((line = Console.ReadLine()) != null && !string.IsNullOrWhiteSpace(line))
            {
                if (line.StartsWith("[") || line.StartsWith("{"))
                {
                    // JSON format - parse accordingly
                    // For simplicity, we'll use a basic parser
                    ParseJsonItems(line);
                    break;
                }
                else
                {
                    // Simple format: Label|IconPath|Color
                    var parts = line.Split('|');
                    _menuItems.Add(new MenuItemData
                    {
                        Label = parts[0],
                        IconPath = parts.Length > 1 ? parts[1] : null,
                        Color = parts.Length > 2 ? parts[2] : null
                    });
                }
            }
        }
        catch { }
        
        if (_menuItems.Count == 0)
        {
            LoadDemoItems();
        }
    }
    
    private void ParseJsonItems(string json)
    {
        // Basic JSON parsing for menu items
        // Expected: [{"label":"Item1","icon":"path","color":"#FFF"},...]
        try
        {
            json = json.Trim();
            if (json.StartsWith("[")) json = json[1..];
            if (json.EndsWith("]")) json = json[..^1];
            
            var items = json.Split(new[] { "},{" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                var cleaned = item.Replace("{", "").Replace("}", "");
                var menuItem = new MenuItemData();
                
                foreach (var pair in cleaned.Split(','))
                {
                    var kv = pair.Split(':');
                    if (kv.Length == 2)
                    {
                        var key = kv[0].Trim().Trim('"').ToLower();
                        var value = kv[1].Trim().Trim('"');
                        
                        switch (key)
                        {
                            case "label": menuItem.Label = value; break;
                            case "icon": menuItem.IconPath = value; break;
                            case "color": menuItem.Color = value; break;
                        }
                    }
                }
                
                if (!string.IsNullOrEmpty(menuItem.Label))
                {
                    _menuItems.Add(menuItem);
                }
            }
        }
        catch { }
    }
    
    private void LoadDemoItems()
    {
        _menuItems = new List<MenuItemData>
        {
            new() { Label = "Copy", Color = "#4CAF50" },
            new() { Label = "Paste", Color = "#2196F3" },
            new() { Label = "Cut", Color = "#FF9800" },
            new() { Label = "Delete", Color = "#F44336" },
            new() { Label = "Undo", Color = "#9C27B0" },
            new() { Label = "Redo", Color = "#00BCD4" },
            new() { Label = "Select All", Color = "#795548" },
            new() { Label = "Find", Color = "#607D8B" }
        };
    }
    
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        App.Log("MainWindow_Loaded - Starting...");
        
        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);
        
        // Get current cursor position and the monitor it's on
        GetCursorPos(out POINT cursorPos);
        _originalCursorPos = new Point(cursorPos.X, cursorPos.Y);
        App.Log($"Original cursor pos: {_originalCursorPos}");
        
        // Get the monitor where cursor is
        var monitor = MonitorFromPoint(cursorPos, MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref monitorInfo);
        
        // Get monitor bounds
        var monitorRect = monitorInfo.rcMonitor;
        int monitorWidth = monitorRect.Right - monitorRect.Left;
        int monitorHeight = monitorRect.Bottom - monitorRect.Top;
        
        App.Log($"Monitor bounds: L={monitorRect.Left}, T={monitorRect.Top}, W={monitorWidth}, H={monitorHeight}");
        
        // Position window to cover the entire monitor where the cursor is
        this.Left = monitorRect.Left;
        this.Top = monitorRect.Top;
        this.Width = monitorWidth;
        this.Height = monitorHeight;
        
        // Resize the RadialCanvas based on menu size
        RadialCanvas.Width = (_menuRadius + _itemSize) * 2 + 20;
        RadialCanvas.Height = (_menuRadius + _itemSize) * 2 + 20;
        
        // Determine menu center position
        double menuCenterX, menuCenterY;
        double screenCenterX, screenCenterY;
        
        if (_atMousePosition)
        {
            // Show at mouse position (in window coordinates)
            menuCenterX = cursorPos.X - monitorRect.Left;
            menuCenterY = cursorPos.Y - monitorRect.Top;
            screenCenterX = cursorPos.X;
            screenCenterY = cursorPos.Y;
            
            // Make sure menu stays within screen bounds
            double margin = _menuRadius + _itemSize + 10;
            menuCenterX = Math.Max(margin, Math.Min(monitorWidth - margin, menuCenterX));
            menuCenterY = Math.Max(margin, Math.Min(monitorHeight - margin, menuCenterY));
            
            App.Log($"Menu at mouse position: {menuCenterX}, {menuCenterY}");
        }
        else
        {
            // Show at center of monitor
            menuCenterX = monitorWidth / 2.0;
            menuCenterY = monitorHeight / 2.0;
            screenCenterX = monitorRect.Left + menuCenterX;
            screenCenterY = monitorRect.Top + menuCenterY;
            
            App.Log($"Menu at center: {menuCenterX}, {menuCenterY}");
        }
        
        _menuCenter = new Point(menuCenterX, menuCenterY);
        
        // Position the menu
        Canvas.SetLeft(MenuContainer, _menuCenter.X - RadialCanvas.Width / 2);
        Canvas.SetTop(MenuContainer, _menuCenter.Y - RadialCanvas.Height / 2);
        
        // Reset virtual position to center
        _virtualX = 0;
        _virtualY = 0;
        
        // Build the visual menu
        BuildMenuVisuals();
        App.Log($"Menu built with {_menuItems.Count} items");
        
        // Register for raw input
        RegisterRawInput();
        App.Log("Raw input registered");
        
        // Hide and lock cursor to menu center (screen coordinates)
        if (_atMousePosition)
        {
            screenCenterX = monitorRect.Left + menuCenterX;
            screenCenterY = monitorRect.Top + menuCenterY;
        }
        
        HideCursor();
        LockCursor(screenCenterX, screenCenterY);
        App.Log("Cursor hidden and locked");
        
        // Play open animation
        var openAnim = (Storyboard)Resources["OpenAnimation"];
        openAnim.Begin(this);
        App.Log("Open animation started - Menu should be visible now!");
    }
    
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        App.Log("MainWindow_Closing called");
        UnlockCursor();
        ShowCursorAgain();
    }
    
    #endregion
    
    #region Menu Visuals
    
    private void BuildMenuVisuals()
    {
        ItemsCanvas.Children.Clear();
        
        int count = _menuItems.Count;
        if (count == 0) return;
        
        double anglePerItem = 360.0 / count;
        double startAngle = -90; // Start from top
        
        // Update canvas size to fit menu
        double canvasSize = (_menuRadius + _itemSize) * 2 + 20;
        RadialCanvas.Width = canvasSize;
        RadialCanvas.Height = canvasSize;
        
        // Update cancel zone
        CancelZone.Width = _cancelRadius * 2;
        CancelZone.Height = _cancelRadius * 2;
        Canvas.SetLeft(CancelZone, canvasSize / 2 - _cancelRadius);
        Canvas.SetTop(CancelZone, canvasSize / 2 - _cancelRadius);
        
        // Update center label position
        Canvas.SetLeft(CenterLabel, canvasSize / 2 - 80);
        Canvas.SetTop(CenterLabel, canvasSize / 2 - 25);
        
        // Update direction line start
        DirectionLine.X1 = canvasSize / 2;
        DirectionLine.Y1 = canvasSize / 2;
        DirectionLine.X2 = canvasSize / 2;
        DirectionLine.Y2 = canvasSize / 2;
        
        for (int i = 0; i < count; i++)
        {
            var item = _menuItems[i];
            double angle = startAngle + (i * anglePerItem);
            double angleRad = angle * Math.PI / 180;
            
            // Position for the item (on the radius)
            double x = canvasSize / 2 + Math.Cos(angleRad) * _menuRadius;
            double y = canvasSize / 2 + Math.Sin(angleRad) * _menuRadius;
            
            // Create item visual
            var itemVisual = CreateItemVisual(item, i);
            
            // Position it
            Canvas.SetLeft(itemVisual, x - _itemSize / 2);
            Canvas.SetTop(itemVisual, y - _itemSize / 2);
            
            ItemsCanvas.Children.Add(itemVisual);
        }
    }
    
    private Border CreateItemVisual(MenuItemData item, int index)
    {
        var color = !string.IsNullOrEmpty(item.Color) 
            ? (Color)ColorConverter.ConvertFromString(item.Color)
            : (Color)ColorConverter.ConvertFromString("#2D2D2D");
        
        var border = new Border
        {
            Width = _itemSize,
            Height = _itemSize,
            CornerRadius = new CornerRadius(_itemSize / 2),
            Background = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(3),
            Tag = index,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 2,
                Opacity = 0.5,
                Color = Colors.Black
            }
        };
        
        var textBlock = new TextBlock
        {
            Text = item.Label,
            Foreground = Brushes.White,
            FontSize = _itemSize < 60 ? 9 : 11,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = _itemSize - 10
        };
        
        border.Child = textBlock;
        
        return border;
    }
    
    private void UpdateSelection()
    {
        // Calculate distance from center
        double distance = Math.Sqrt(_virtualX * _virtualX + _virtualY * _virtualY);
        double canvasCenter = RadialCanvas.Width / 2;
        
        int newSelection;
        
        if (distance < _selectionThreshold)
        {
            // In cancel zone - very small movement keeps you in center
            newSelection = -1;
            CenterText.Text = "Cancel";
            CenterText.Foreground = (Brush)FindResource("TextSecondaryBrush");
            SelectionIndicator.Visibility = Visibility.Collapsed;
            DirectionLine.Visibility = Visibility.Collapsed;
            _hasSelection = false;
        }
        else
        {
            // Calculate angle - selection is purely based on direction
            double angle = Math.Atan2(_virtualY, _virtualX) * 180 / Math.PI;
            angle = (angle + 90 + 360) % 360; // Normalize to start from top
            
            // Determine which item based on angle
            double anglePerItem = 360.0 / _menuItems.Count;
            newSelection = (int)((angle + anglePerItem / 2) % 360 / anglePerItem);
            
            if (newSelection >= 0 && newSelection < _menuItems.Count)
            {
                CenterText.Text = _menuItems[newSelection].Label;
                CenterText.Foreground = (Brush)FindResource("TextBrush");
                
                // Show selection indicator
                UpdateSelectionIndicator(newSelection);
                
                // Show direction line (subtle visual feedback)
                DirectionLine.Visibility = Visibility.Visible;
                double lineLength = Math.Min(distance, 60);
                double lineAngle = Math.Atan2(_virtualY, _virtualX);
                DirectionLine.X1 = canvasCenter;
                DirectionLine.Y1 = canvasCenter;
                DirectionLine.X2 = canvasCenter + Math.Cos(lineAngle) * lineLength;
                DirectionLine.Y2 = canvasCenter + Math.Sin(lineAngle) * lineLength;
                
                _hasSelection = true;
                _lastAngle = angle;
            }
        }
        
        // Update item highlights
        if (newSelection != _selectedIndex)
        {
            _selectedIndex = newSelection;
            UpdateItemHighlights();
        }
    }
    
    private void UpdateSelectionIndicator(int index)
    {
        int count = _menuItems.Count;
        double anglePerItem = 360.0 / count;
        double startAngle = -90 + (index * anglePerItem) - anglePerItem / 2;
        double endAngle = startAngle + anglePerItem;
        
        double canvasCenter = RadialCanvas.Width / 2;
        
        // Create pie slice path
        var geometry = CreatePieSlice(
            canvasCenter, 
            canvasCenter, 
            _innerRadius, 
            _menuRadius + _itemSize / 2 + 5, 
            startAngle, 
            endAngle);
        
        SelectionIndicator.Data = geometry;
        SelectionIndicator.Visibility = Visibility.Visible;
    }
    
    private PathGeometry CreatePieSlice(double centerX, double centerY, double innerRadius, double outerRadius, double startAngle, double endAngle)
    {
        double startAngleRad = startAngle * Math.PI / 180;
        double endAngleRad = endAngle * Math.PI / 180;
        
        Point outerStart = new(
            centerX + outerRadius * Math.Cos(startAngleRad),
            centerY + outerRadius * Math.Sin(startAngleRad));
        Point outerEnd = new(
            centerX + outerRadius * Math.Cos(endAngleRad),
            centerY + outerRadius * Math.Sin(endAngleRad));
        Point innerStart = new(
            centerX + innerRadius * Math.Cos(startAngleRad),
            centerY + innerRadius * Math.Sin(startAngleRad));
        Point innerEnd = new(
            centerX + innerRadius * Math.Cos(endAngleRad),
            centerY + innerRadius * Math.Sin(endAngleRad));
        
        bool isLargeArc = (endAngle - startAngle) > 180;
        
        var figure = new PathFigure { StartPoint = innerStart };
        figure.Segments.Add(new LineSegment(outerStart, true));
        figure.Segments.Add(new ArcSegment(outerEnd, new Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(innerStart, new Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true));
        figure.IsClosed = true;
        
        return new PathGeometry(new[] { figure });
    }
    
    private void UpdateItemHighlights()
    {
        foreach (var child in ItemsCanvas.Children)
        {
            if (child is Border border && border.Tag is int index)
            {
                if (index == _selectedIndex)
                {
                    border.BorderBrush = (Brush)FindResource("AccentBrush");
                    border.RenderTransform = new ScaleTransform(1.15, 1.15, _itemSize / 2, _itemSize / 2);
                }
                else
                {
                    border.BorderBrush = new SolidColorBrush(Colors.Transparent);
                    border.RenderTransform = null;
                }
            }
        }
        
        // Highlight cancel zone
        if (_selectedIndex == -1)
        {
            CancelZone.Stroke = (Brush)FindResource("AccentBrush");
            CancelZone.StrokeThickness = 3;
        }
        else
        {
            CancelZone.Stroke = (Brush)FindResource("BorderBrush");
            CancelZone.StrokeThickness = 2;
        }
    }
    
    #endregion
    
    #region Raw Input & Cursor Control
    
    private void RegisterRawInput()
    {
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = 0x01,
            usUsage = 0x02, // Mouse
            dwFlags = RIDEV_INPUTSINK,
            hwndTarget = _hwnd
        };
        
        RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }
    
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_INPUT)
        {
            ProcessRawInput(lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }
    
    private void ProcessRawInput(IntPtr lParam)
    {
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>());
        
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) == size)
            {
                var raw = Marshal.PtrToStructure<RAWINPUT>(buffer);
                
                if (raw.header.dwType == RIM_TYPEMOUSE)
                {
                    int deltaX = raw.mouse.lLastX;
                    int deltaY = raw.mouse.lLastY;
                    
                    if (deltaX != 0 || deltaY != 0)
                    {
                        // Apply decay - position slowly returns toward center
                        // This makes it easier to change direction
                        _virtualX *= (1.0 - _decayFactor);
                        _virtualY *= (1.0 - _decayFactor);
                        
                        // Add new movement with high sensitivity
                        _virtualX += deltaX * _sensitivity;
                        _virtualY += deltaY * _sensitivity;
                        
                        // Soft clamp - allow going beyond but with resistance
                        double maxDistance = _menuRadius * 0.6;
                        double distance = Math.Sqrt(_virtualX * _virtualX + _virtualY * _virtualY);
                        if (distance > maxDistance)
                        {
                            double excess = distance - maxDistance;
                            double dampening = 1.0 / (1.0 + excess * 0.05);
                            _virtualX *= (maxDistance + excess * dampening) / distance;
                            _virtualY *= (maxDistance + excess * dampening) / distance;
                        }
                        
                        // Update UI
                        Dispatcher.BeginInvoke(UpdateSelection);
                    }
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
    
    private void HideCursor()
    {
        if (!_cursorHidden)
        {
            ShowCursor(false);
            _cursorHidden = true;
        }
    }
    
    private void ShowCursorAgain()
    {
        if (_cursorHidden)
        {
            ShowCursor(true);
            _cursorHidden = false;
        }
    }
    
    private void LockCursor(double x, double y)
    {
        // Lock cursor to a single point
        int ix = (int)x;
        int iy = (int)y;
        
        var rect = new RECT { Left = ix, Top = iy, Right = ix + 1, Bottom = iy + 1 };
        ClipCursor(ref rect);
        SetCursorPos(ix, iy);
    }
    
    private void UnlockCursor()
    {
        ClipCursor(IntPtr.Zero);
        
        // Restore original position
        SetCursorPos((int)_originalCursorPos.X, (int)_originalCursorPos.Y);
    }
    
    #endregion
    
    #region Input Handling
    
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        App.Log($"KeyDown: {e.Key}");
        if (e.Key == Key.Escape)
        {
            CloseWithResult(null);
        }
    }
    
    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        App.Log($"Window_MouseDown: {e.ChangedButton} at {e.GetPosition(this)}");
        
        if (e.ChangedButton == MouseButton.Left)
        {
            // Left click confirms selection
            ConfirmSelection();
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            // Right click cancels
            CloseWithResult(null);
        }
    }
    
    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        App.Log($"Window_MouseUp: {e.ChangedButton}");
        // Can also confirm on release if preferred
        // ConfirmSelection();
    }
    
    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        App.Log($"Backdrop_MouseDown: {e.ChangedButton}");
        // Click on backdrop cancels
        CloseWithResult(null);
    }
    
    private void ConfirmSelection()
    {
        if (_selectedIndex >= 0 && _selectedIndex < _menuItems.Count)
        {
            CloseWithResult(_menuItems[_selectedIndex].Label);
        }
        else
        {
            CloseWithResult(null);
        }
    }
    
    #endregion
    
    #region Close & Result
    
    private void CloseWithResult(string? result)
    {
        App.Log($"CloseWithResult called with: {result ?? "null"}");
        App.Log($"Stack trace: {Environment.StackTrace}");
        
        if (_isClosing) return;
        _isClosing = true;
        
        _result = result;
        
        // Play close animation
        var closeAnim = (Storyboard)Resources["CloseAnimation"];
        closeAnim.Begin(this);
    }
    
    private void CloseAnimation_Completed(object? sender, EventArgs e)
    {
        // Output result to stdout
        if (_result != null)
        {
            Console.WriteLine(_result);
        }
        else
        {
            Console.WriteLine("CANCELLED");
        }
        
        // Cleanup and close
        UnlockCursor();
        ShowCursorAgain();
        
        Application.Current.Shutdown(_result != null ? 0 : 1);
    }
    
    #endregion
}

public class MenuItemData
{
    public string Label { get; set; } = "";
    public string? IconPath { get; set; }
    public string? Color { get; set; }
}
