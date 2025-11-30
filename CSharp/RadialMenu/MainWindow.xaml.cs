using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ChokoLPT.Shared.Helpers;
using ChokoLPT.Shared.Services;
using RadialMenu.Models;

namespace RadialMenu;

public partial class MainWindow : Window
{
    
    #region Fields
    
    private List<RadialMenuItem> _menuItems = new();
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

    // Spec-related config
    private string? _title;
    private IntPtr _callbackHwnd = IntPtr.Zero;
    private double? _initialX;
    private double? _initialY;
    private bool _preferStdinJson = false;

    // Result item (id/label/icon/color)
    private RadialMenuItem? _resultItem;
    
    #endregion
    
    #region Initialization
    
    public MainWindow(string[]? args)
    {
        App.Log("MainWindow constructor called");
        
        InitializeComponent();
        
        App.Log("InitializeComponent completed");
        
        var effectiveArgs = args ?? Array.Empty<string>();
        ParseArguments(effectiveArgs);

        // Por padrão, abrir no mouse (a não ser que coordenadas explícitas tenham sido passadas).
        // _initialX/_initialY têm prioridade sobre _atMousePosition.
        if (!_initialX.HasValue && !_initialY.HasValue && !_atMousePosition)
        {
            _atMousePosition = true;
        }

        // Prefer stdin JSON when explicitly requested
        if (_preferStdinJson && Console.IsInputRedirected)
        {
            App.Log("Reading config from stdin JSON due to --stdin flag");
            ReadConfigFromStdin();
        }
        else if (_menuItems.Count == 0 && Console.IsInputRedirected)
        {
            App.Log("No items from args, attempting to read config from stdin JSON");
            ReadConfigFromStdin();
        }
        
        if (_menuItems.Count == 0)
        {
            App.Log("No items provided, loading demo items");
            LoadDemoItems();
        }
        
        App.Log($"Loaded {_menuItems.Count} menu items");
        
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }
    
    private void ParseArguments(string[] args)
    {
        // Spec formats:
        // --items "id:label:icon,id2:label2:icon2"
        // --title "Clipboard"
        // --stdin (read JSON config from stdin)
        // --hwnd 12345 (callback HWND for WM_COPYDATA)
        // --x 100 --y 200
        // Legacy options preserved: --at-mouse, --mini, --radius, --sensitivity
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--items":
                case "-i":
                    if (i + 1 < args.Length)
                    {
                        ParseItemsFromCli(args[++i]);
                    }
                    break;

                case "--title":
                    if (i + 1 < args.Length)
                    {
                        _title = args[++i];
                    }
                    break;

                case "--stdin":
                    _preferStdinJson = true;
                    break;

                case "--hwnd":
                    if (i + 1 < args.Length && long.TryParse(args[++i], out var hwndVal))
                    {
                        _callbackHwnd = new IntPtr(hwndVal);
                    }
                    break;

                case "--x":
                    if (i + 1 < args.Length && double.TryParse(args[++i], out var x))
                    {
                        _initialX = x;
                    }
                    break;

                case "--y":
                    if (i + 1 < args.Length && double.TryParse(args[++i], out var y))
                    {
                        _initialY = y;
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
    }

    private void ParseItemsFromCli(string itemsArg)
    {
        // New format: id:label:icon,id2:label2:icon2
        // Legacy fallback: "Label1|Label2|Label3"
        if (string.IsNullOrWhiteSpace(itemsArg))
            return;

        // First split by commas into item specs
        var specs = itemsArg.Split(',', StringSplitOptions.RemoveEmptyEntries);

        // If there's only one spec and it contains '|' but no ':', treat as legacy
        if (specs.Length == 1 && specs[0].Contains('|') && !specs[0].Contains(':'))
        {
            var labels = specs[0].Split('|', StringSplitOptions.RemoveEmptyEntries);
            foreach (var labelRaw in labels)
            {
                var label = labelRaw.Trim();
                if (label.Length == 0) continue;

                _menuItems.Add(new RadialMenuItem
                {
                    Id = label,
                    Label = label
                });
            }
            return;
        }

        foreach (var specRaw in specs)
        {
            var spec = specRaw.Trim();
            if (spec.Length == 0) continue;

            var parts = spec.Split(':');
            string id = parts.Length > 0 ? parts[0] : string.Empty;
            string label = parts.Length > 1 ? parts[1] : id;
            string? icon = parts.Length > 2 ? parts[2] : null;

            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(label))
                continue;

            if (string.IsNullOrWhiteSpace(id))
                id = label;
            if (string.IsNullOrWhiteSpace(label))
                label = id;

            _menuItems.Add(new RadialMenuItem
            {
                Id = id,
                Label = label,
                Icon = icon
            });
        }
    }
    
    private void ReadConfigFromStdin()
    {
        try
        {
            string all = Console.In.ReadToEnd();
            if (string.IsNullOrWhiteSpace(all))
                return;

            ParseConfigJson(all);
        }
        catch (Exception ex)
        {
            App.Log($"Error reading config from stdin: {ex}");
        }
    }

    private void ParseConfigJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                // title
                if (root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                {
                    _title = titleProp.GetString();
                }

                // hwnd_callback
                if (root.TryGetProperty("hwnd_callback", out var hwndProp))
                {
                    long hwndVal;
                    switch (hwndProp.ValueKind)
                    {
                        case JsonValueKind.Number when hwndProp.TryGetInt64(out hwndVal):
                            _callbackHwnd = new IntPtr(hwndVal);
                            break;
                        case JsonValueKind.String:
                            var s = hwndProp.GetString();
                            if (!string.IsNullOrWhiteSpace(s) && long.TryParse(s, out hwndVal))
                            {
                                _callbackHwnd = new IntPtr(hwndVal);
                            }
                            break;
                    }
                }

                // items array
                if (root.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
                {
                    ParseItemsArray(itemsProp);
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                // Backwards-compatible: JSON is just an array of items
                ParseItemsArray(root);
            }
        }
        catch (Exception ex)
        {
            App.Log($"Error parsing JSON config: {ex}");
        }
    }

    private void ParseItemsArray(JsonElement itemsElement)
    {
        foreach (var elem in itemsElement.EnumerateArray())
        {
            if (elem.ValueKind != JsonValueKind.Object)
                continue;

            var item = new RadialMenuItem();

            if (elem.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                item.Id = idProp.GetString() ?? string.Empty;

            if (elem.TryGetProperty("label", out var labelProp) && labelProp.ValueKind == JsonValueKind.String)
                item.Label = labelProp.GetString() ?? string.Empty;

            if (elem.TryGetProperty("icon", out var iconProp) && iconProp.ValueKind == JsonValueKind.String)
                item.Icon = iconProp.GetString();

            if (elem.TryGetProperty("color", out var colorProp) && colorProp.ValueKind == JsonValueKind.String)
                item.Color = colorProp.GetString();

            if (string.IsNullOrWhiteSpace(item.Label) && string.IsNullOrWhiteSpace(item.Id))
                continue;

            if (string.IsNullOrWhiteSpace(item.Id))
                item.Id = item.Label;
            if (string.IsNullOrWhiteSpace(item.Label))
                item.Label = item.Id;

            _menuItems.Add(item);
        }
    }
    
    private void LoadDemoItems()
    {
        _menuItems = new List<RadialMenuItem>
        {
            new() { Id = "copy", Label = "Copy", Color = "#4CAF50" },
            new() { Id = "paste", Label = "Paste", Color = "#2196F3" },
            new() { Id = "cut", Label = "Cut", Color = "#FF9800" },
            new() { Id = "delete", Label = "Delete", Color = "#F44336" },
            new() { Id = "undo", Label = "Undo", Color = "#9C27B0" },
            new() { Id = "redo", Label = "Redo", Color = "#00BCD4" },
            new() { Id = "select_all", Label = "Select All", Color = "#795548" },
            new() { Id = "find", Label = "Find", Color = "#607D8B" }
        };
    }
    
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        App.Log("MainWindow_Loaded - Starting...");
        
        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);
        
        // Get current cursor position and the monitor it's on
        Win32.GetCursorPos(out Win32.POINT cursorPos);
        _originalCursorPos = new Point(cursorPos.X, cursorPos.Y);
        App.Log($"Original cursor pos: {_originalCursorPos}");
        
        // Decide reference point for monitor/menu center: either explicit X/Y or cursor
        var refPoint = cursorPos;
        if (_initialX.HasValue && _initialY.HasValue)
        {
            refPoint = new Win32.POINT { X = (int)_initialX.Value, Y = (int)_initialY.Value };
        }
        
        // Get the monitor where cursor (or explicit point) is
        var monitor = Win32.MonitorFromPoint(refPoint, Win32.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new Win32.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(monitor, ref monitorInfo);
        
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
        
        if (_initialX.HasValue && _initialY.HasValue)
        {
            // Explicit X/Y in screen coordinates
            menuCenterX = _initialX.Value - monitorRect.Left;
            menuCenterY = _initialY.Value - monitorRect.Top;
            screenCenterX = _initialX.Value;
            screenCenterY = _initialY.Value;
        }
        else if (_atMousePosition)
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

        // No warmup: lock cursor and play open animation normally.
        // Warmup (--background) já aconteceu em App.OnStartup com Opacity=0/Hide,
        // então aqui não devemos roubar mouse/foco.
        if (!App.IsBackgroundStartup)
        {
            HideCursor();
            LockCursor(screenCenterX, screenCenterY);
            App.Log("Cursor hidden and locked");
            
            // Play open animation
            var openAnim = (Storyboard)Resources["OpenAnimation"];
            openAnim.Begin(this);
            App.Log("Open animation started - Menu should be visible now!");
        }
        else
        {
            App.Log("Background startup detected in MainWindow_Loaded - skipping cursor lock & open animation.");
        }
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
    
    private Border CreateItemVisual(RadialMenuItem item, int index)
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
        var device = new Win32.RAWINPUTDEVICE
        {
            usUsagePage = 0x01,
            usUsage = 0x02, // Mouse
            dwFlags = Win32.RIDEV_INPUTSINK,
            hwndTarget = _hwnd
        };
        
        Win32.RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<Win32.RAWINPUTDEVICE>());
    }
    
    private void WndProcHelper(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_INPUT)
        {
            ProcessRawInput(lParam);
            handled = true;
        }
        else if (msg == 0x004A) // WM_COPYDATA
        {
            HandleCopyData(lParam, ref handled);
        }
    }
    
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        WndProcHelper(msg, wParam, lParam, ref handled);
        return IntPtr.Zero;
    }
    
    private void ProcessRawInput(IntPtr lParam)
    {
        uint size = 0;
        Win32.GetRawInputData(lParam, Win32.RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<Win32.RAWINPUTHEADER>());
        
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (Win32.GetRawInputData(lParam, Win32.RID_INPUT, buffer, ref size, (uint)Marshal.SizeOf<Win32.RAWINPUTHEADER>()) == size)
            {
                var raw = Marshal.PtrToStructure<Win32.RAWINPUT>(buffer);
                
                if (raw.header.dwType == Win32.RIM_TYPEMOUSE)
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

    /// <summary>
    /// Handles WM_COPYDATA messages with JSON payload to update menu items
    /// without spawning a new process (warm resident instance).
    /// </summary>
    private void HandleCopyData(IntPtr lParam, ref bool handled)
    {
        try
        {
            var cds = Marshal.PtrToStructure<Win32.COPYDATASTRUCT>(lParam);
            if (cds.lpData == IntPtr.Zero || cds.cbData <= 0)
                return;

            // Payload enviado via MessageService / AHK é UTF-8 (cbData em bytes).
            var bytes = new byte[cds.cbData];
            Marshal.Copy(cds.lpData, bytes, 0, cds.cbData);
            string json = Encoding.UTF8.GetString(bytes);
            if (string.IsNullOrWhiteSpace(json))
                return;

            App.Log($"HandleCopyData: received JSON length={json.Length}");
            Dispatcher.BeginInvoke(() => UpdateFromJson(json));
            handled = true;
        }
        catch (Exception ex)
        {
            App.Log($"Error in HandleCopyData: {ex}");
        }
    }

    /// <summary>
    /// Updates the current menu from a JSON payload (same format as stdin/CLI),
    /// repositions at current mouse (with edge clamping) and shows the menu.
    /// </summary>
    private void UpdateFromJson(string json)
    {
        try
        {
            // Reset current state and parse new config
            _menuItems.Clear();
            _callbackHwnd = IntPtr.Zero;
            _title = null;

            ParseConfigJson(json);

            if (_menuItems.Count == 0)
            {
                App.Log("UpdateFromJson: no items after parsing JSON, aborting.");
                return;
            }

            // Get current cursor and monitor
            Win32.GetCursorPos(out Win32.POINT cursorPos);
            var monitor = Win32.MonitorFromPoint(cursorPos, Win32.MONITOR_DEFAULTTONEAREST);
            var monitorInfo = new Win32.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
            Win32.GetMonitorInfo(monitor, ref monitorInfo);

            var monitorRect = monitorInfo.rcMonitor;
            int monitorWidth = monitorRect.Right - monitorRect.Left;
            int monitorHeight = monitorRect.Bottom - monitorRect.Top;

            // Cover the whole monitor
            Left = monitorRect.Left;
            Top = monitorRect.Top;
            Width = monitorWidth;
            Height = monitorHeight;

            // Position menu at mouse with edge clamping (same logic as first load)
            double menuCenterX = cursorPos.X - monitorRect.Left;
            double menuCenterY = cursorPos.Y - monitorRect.Top;

            double margin = _menuRadius + _itemSize + 10;
            menuCenterX = Math.Max(margin, Math.Min(monitorWidth - margin, menuCenterX));
            menuCenterY = Math.Max(margin, Math.Min(monitorHeight - margin, menuCenterY));

            _menuCenter = new Point(menuCenterX, menuCenterY);

            // Ensure canvas size is consistent with menu size
            RadialCanvas.Width = (_menuRadius + _itemSize) * 2 + 20;
            RadialCanvas.Height = (_menuRadius + _itemSize) * 2 + 20;

            Canvas.SetLeft(MenuContainer, _menuCenter.X - RadialCanvas.Width / 2);
            Canvas.SetTop(MenuContainer, _menuCenter.Y - RadialCanvas.Height / 2);

            // Reset virtual position
            _virtualX = 0;
            _virtualY = 0;

            // Build visuals with new items
            BuildMenuVisuals();
            App.Log($"UpdateFromJson: menu rebuilt with {_menuItems.Count} items");

            // Lock/hide cursor when actually showing (warmup já passou)
            double screenCenterX = monitorRect.Left + menuCenterX;
            double screenCenterY = monitorRect.Top + menuCenterY;

            HideCursor();
            LockCursor(screenCenterX, screenCenterY);

            // Show menu (reuse open animation)
            Show();
            Activate();
            var openAnim = (Storyboard)Resources["OpenAnimation"];
            openAnim.Begin(this);
        }
        catch (Exception ex)
        {
            App.Log($"Error in UpdateFromJson: {ex}");
        }
    }
    
    private void HideCursor()
    {
        if (!_cursorHidden)
        {
            Win32.ShowCursor(false);
            _cursorHidden = true;
        }
    }
    
    private void ShowCursorAgain()
    {
        if (_cursorHidden)
        {
            Win32.ShowCursor(true);
            _cursorHidden = false;
        }
    }
    
    private void LockCursor(double x, double y)
    {
        // Lock cursor to a single point
        int ix = (int)x;
        int iy = (int)y;
        
        var rect = new Win32.RECT { Left = ix, Top = iy, Right = ix + 1, Bottom = iy + 1 };
        Win32.ClipCursor(ref rect);
        Win32.SetCursorPos(ix, iy);
    }
    
    private void UnlockCursor()
    {
        Win32.ClipCursor(IntPtr.Zero);
        
        // Restore original position
        Win32.SetCursorPos((int)_originalCursorPos.X, (int)_originalCursorPos.Y);
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
            CloseWithResult(_menuItems[_selectedIndex]);
        }
        else
        {
            CloseWithResult(null);
        }
    }
    
    #endregion
    
    #region Close & Result
    
    private void CloseWithResult(RadialMenuItem? item)
    {
        var debugId = item?.Id ?? item?.Label ?? "null";
        App.Log($"CloseWithResult called with item: {debugId}");
        App.Log($"Stack trace: {Environment.StackTrace}");
        
        if (_isClosing) return;
        _isClosing = true;
        
        _resultItem = item;
        
        // Play close animation
        var closeAnim = (Storyboard)Resources["CloseAnimation"];
        closeAnim.Begin(this);
    }
    
    private void CloseAnimation_Completed(object? sender, EventArgs e)
    {
        bool cancelled = _resultItem is null;

        // Prepare id for output
        string? id = null;
        if (_resultItem is not null)
        {
            id = _resultItem.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                id = _resultItem.Label;
            }
        }

        // Send result via WM_COPYDATA to callback HWND if provided
        if (_callbackHwnd != IntPtr.Zero)
        {
            try
            {
                string json;
                if (!cancelled && id is not null)
                {
                    var payload = new { selected = id };
                    json = JsonSerializer.Serialize(payload);
                }
                else
                {
                    var payload = new { selected = (string?)null, cancelled = true };
                    json = JsonSerializer.Serialize(payload);
                }

                // dwData marca a mensagem como vindo do RadialMenu (deve bater com _callbackTag no AHK)
                MessageService.SendJsonCopyData(_callbackHwnd, json, new IntPtr(0x524D5253)); // 'RMRS'
            }
            catch (Exception ex)
            {
                App.Log($"Error sending WM_COPYDATA callback: {ex}");
            }
        }

        // Stdout fallback per spec (returns id or CANCELLED)
        if (!cancelled && id is not null)
        {
            Console.WriteLine(id);
        }
        else
        {
            Console.WriteLine("CANCELLED");
        }
        
        // Cleanup cursor
        UnlockCursor();
        ShowCursorAgain();

        // Em modo normal (sem warmup), encerramos o processo como antes.
        // Em modo warmup (--background), apenas escondemos a janela para reaproveitar
        // a instância residente (singleton).
        if (!App.IsBackgroundStartup)
        {
            Application.Current.Shutdown(!cancelled ? 0 : 1);
        }
        else
        {
            // Reset flag para permitir reuso em próximos gestos
            _isClosing = false;
            Hide();
        }
    }
    
    #endregion
}

