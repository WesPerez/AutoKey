using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using AutoKey.Controls;
using Forms = System.Windows.Forms;
using Microsoft.Win32;

namespace AutoKey
{
    public class StringToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string colorStr && !string.IsNullOrEmpty(colorStr))
            {
                try { return (Color)ColorConverter.ConvertFromString(colorStr); }
                catch { }
            }
            return Colors.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    public partial class MainWindow : Window
    {
        private MainWindowViewModel ViewModel { get; } = new();
        private IntPtr _windowHandle;
        private HwndSource? _hwndSource;
        private System.Drawing.Icon? _stoppedTrayIcon;
        private System.Drawing.Icon? _runningTrayIcon;
        private string _lastValidConfigName = "默认";
        private bool _refreshingConfigCombo;

        private const int HOTKEY_BIND_WINDOW = 2;

        // Right-click drag state
        private bool _rightDragging;
        private NativeInterop.POINT _rightDragStartPoint;

        // Low-level mouse hook for global right-click detection
        private IntPtr _mouseHookId = IntPtr.Zero;
        private NativeInterop.LowLevelMouseProc? _mouseProc;

        // Low-level keyboard hook for Left Alt hotkey
        private IntPtr _keyboardHookId = IntPtr.Zero;
        private NativeInterop.LowLevelKeyboardProc? _keyboardProc;
        private bool _leftAltDown;
        private bool _leftAltSolo;

        // Tray icon for minimize-to-tray
        private Forms.NotifyIcon? _trayIcon;
        private bool _isReallyClosing;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = ViewModel;
            KeyList.ItemsSource = ViewModel.Keys;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _windowHandle = new WindowInteropHelper(this).EnsureHandle();
                _hwndSource = HwndSource.FromHwnd(_windowHandle);
                _hwndSource?.AddHook(WndProc);

                // Publish window handle for single-instance activation
                App.PublishWindowHandle(_windowHandle);

                NativeInterop.RegisterHotKey(_windowHandle, HOTKEY_BIND_WINDOW,
                    NativeInterop.MOD_CONTROL | NativeInterop.MOD_ALT, 0x20);

                // Install global mouse hook for right-click drag binding
                InstallMouseHook();

                // Install global keyboard hook for Left Alt hotkey
                InstallKeyboardHook();

                // Create system tray icon
                SetupTrayIcon();

                // Initialize taskbar overlay (needed for pinned taskbar icon)
                TaskbarItemInfo = new TaskbarItemInfo();

                ViewModel.LoadAppState();
                ViewModel.RefreshConfigList();
                ViewModel.LoadConfig();
                KeyList.ItemsSource = ViewModel.Keys;
                UpdateWindowIcon();
                RefreshConfigComboText();

                // Restore window size
                if (ViewModel.WindowWidth >= MinWidth) Width = ViewModel.WindowWidth;
                if (ViewModel.WindowHeight >= MinHeight) Height = ViewModel.WindowHeight;

                ViewModel.StatusText = "就绪 - 按左Alt开始/停止";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化出错: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (!_isReallyClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            SyncConfigName();
            ViewModel.SelectedConfig = _lastValidConfigName;
            ViewModel.StopAll();

            // Save window size before auto-saving config
            ViewModel.WindowWidth = Width;
            ViewModel.WindowHeight = Height;

            // Auto-save config on close
            TryAutoSave(ViewModel.SaveConfig);
            TryAutoSave(ViewModel.SaveAppState);

            if (_windowHandle != IntPtr.Zero)
            {
                NativeInterop.UnregisterHotKey(_windowHandle, HOTKEY_BIND_WINDOW);
            }

            UninstallMouseHook();
            UninstallKeyboardHook();
            _hwndSource?.RemoveHook(WndProc);
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

            _trayIcon?.Dispose();
        }

        #region Tray Icon

        private const string AutoStartRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AutoStartValueName = "AutoKey";

        private static bool IsAutoStartEnabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegKey, false);
                return key?.GetValue(AutoStartValueName) != null;
            }
        }

        private static void SetAutoStart(bool enable)
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegKey, true);
            if (key == null) return;
            if (enable)
            {
                var exePath = Environment.GetCommandLineArgs()[0];
                key.SetValue(AutoStartValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AutoStartValueName, false);
            }
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "")
                      ?? System.Drawing.SystemIcons.Application,
                Text = "后台按键工具",
                Visible = true
            };

            _trayIcon.DoubleClick += (s, e) =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            };

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示", null, (s, e) =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });

            var autoStartItem = new Forms.ToolStripMenuItem("开机自启");
            autoStartItem.Checked = IsAutoStartEnabled;
            autoStartItem.Click += (s, e) =>
            {
                bool newState = !autoStartItem.Checked;
                SetAutoStart(newState);
                autoStartItem.Checked = newState;
            };
            menu.Items.Add(autoStartItem);

            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) =>
            {
                _isReallyClosing = true;
                _trayIcon.Visible = false;
                Close();
            });

            _trayIcon.ContextMenuStrip = menu;
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Dispatcher.BeginInvoke(new Action(Hide));
            }
        }

        #endregion

        private static void TryAutoSave(Action saveAction)
        {
            try { saveAction(); }
            catch { /* Avoid blocking shutdown on a best-effort auto-save. */ }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsRunning))
                Dispatcher.BeginInvoke(new Action(UpdateWindowIcon));
        }

        private void UpdateWindowIcon()
        {
            _stoppedTrayIcon ??= CreateTrayStatusIcon(false);
            _runningTrayIcon ??= CreateTrayStatusIcon(true);

            var icon = ViewModel.IsRunning ? _runningTrayIcon : _stoppedTrayIcon;

            // Update tray icon
            if (_trayIcon != null)
                _trayIcon.Icon = icon;

            // Force taskbar icon update via WM_SETICON
            if (_windowHandle != IntPtr.Zero)
            {
                NativeInterop.SendMessage(_windowHandle, NativeInterop.WM_SETICON, (IntPtr)0 /*ICON_SMALL*/, icon.Handle);
                NativeInterop.SendMessage(_windowHandle, NativeInterop.WM_SETICON, (IntPtr)1 /*ICON_BIG*/, icon.Handle);
            }

            // Taskbar overlay: the only reliable indicator when pinned to taskbar
            if (TaskbarItemInfo != null)
            {
                var overlayColor = ViewModel.IsRunning ? Colors.LimeGreen : Color.FromRgb(211, 47, 47);
                TaskbarItemInfo.Overlay = CreateOverlayIcon(overlayColor);
            }
        }

        private static System.Drawing.Icon CreateTrayStatusIcon(bool isRunning)
        {
            const int size = 32;
            using var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);

                // Dark background with rounded corners
                using var bgBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(34, 34, 34));
                var rect = new System.Drawing.Rectangle(2, 2, 28, 28);
                int d = 12; // diameter for corner arcs
                using var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.X + rect.Width - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.X + rect.Width - d, rect.Y + rect.Height - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Y + rect.Height - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(bgBrush, path);

                // Colored circle
                var accent = isRunning
                    ? System.Drawing.Color.LimeGreen
                    : System.Drawing.Color.FromArgb(211, 47, 47);
                using var circleBrush = new System.Drawing.SolidBrush(accent);
                g.FillEllipse(circleBrush, 8, 8, 16, 16);

                // White border
                using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2);
                g.DrawEllipse(pen, 8, 8, 16, 16);
            }

            IntPtr hIcon = bmp.GetHicon();
            return System.Drawing.Icon.FromHandle(hIcon);
        }

        private static ImageSource CreateOverlayIcon(Color accent)
        {
            const int size = 16;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawEllipse(new SolidColorBrush(accent), null, new Point(8, 8), 7, 7);
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeInterop.WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                if (hotkeyId == HOTKEY_BIND_WINDOW)
                {
                    BindWindowUnderCursor();
                    handled = true;
                }
            }
            else if (msg == (int)NativeInterop.WM_AUTOKEY_RESTORE)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    NativeInterop.SetForegroundWindow(_windowHandle);
                }));
                handled = true;
            }
            return IntPtr.Zero;
        }

        #region Global Mouse Hook (for right-click drag window binding)

        private void InstallMouseHook()
        {
            _mouseProc = MouseHookCallback;
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _mouseHookId = NativeInterop.SetWindowsHookEx(
                NativeInterop.WH_MOUSE_LL, _mouseProc,
                NativeInterop.GetModuleHandle(curModule.ModuleName), 0);
        }

        private void UninstallMouseHook()
        {
            if (_mouseHookId != IntPtr.Zero)
            {
                NativeInterop.UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();

                if (msg == NativeInterop.WM_RBUTTONDOWN)
                {
                    // Right button pressed - start drag tracking
                    _rightDragging = true;
                    NativeInterop.GetCursorPos(out _rightDragStartPoint);
                }
                else if (msg == NativeInterop.WM_RBUTTONUP && _rightDragging)
                {
                    _rightDragging = false;
                    // Right button released - bind to window under cursor
                    if (NativeInterop.GetCursorPos(out NativeInterop.POINT pt))
                    {
                        int dx = pt.X - _rightDragStartPoint.X;
                        int dy = pt.Y - _rightDragStartPoint.Y;
                        if ((dx * dx) + (dy * dy) < 64)
                            return NativeInterop.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);

                        IntPtr hWnd = NativeInterop.WindowFromPoint(pt);
                        if (hWnd != IntPtr.Zero && hWnd != _windowHandle)
                        {
                            // Get top-level parent window
                            IntPtr topLevel = NativeInterop.GetAncestor(hWnd, 2); // GA_ROOT = 2
                            if (topLevel != IntPtr.Zero && topLevel != _windowHandle)
                                hWnd = topLevel;

                            Dispatcher.BeginInvoke(new Action(() => ViewModel.BindWindow(hWnd)));
                        }
                    }
                }
            }
            return NativeInterop.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        #endregion

        #region Global Keyboard Hook (for Left Alt hotkey)

        private void InstallKeyboardHook()
        {
            _keyboardProc = KeyboardHookCallback;
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _keyboardHookId = NativeInterop.SetWindowsHookEx(
                NativeInterop.WH_KEYBOARD_LL, _keyboardProc,
                NativeInterop.GetModuleHandle(curModule.ModuleName), 0);
        }

        private void UninstallKeyboardHook()
        {
            if (_keyboardHookId != IntPtr.Zero)
            {
                NativeInterop.UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
            }
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                var kbData = Marshal.PtrToStructure<NativeInterop.KBDLLHOOKSTRUCT>(lParam);

                // VK_LMENU = 0xA4, scan code 56 (0x38) for left, right has LLKHF_EXTENDED flag
                bool isLeftAlt = kbData.vkCode == 0xA4 && (kbData.flags & 0x10) == 0;

                if (isLeftAlt)
                {
                    if (msg == NativeInterop.WM_KEYDOWN || msg == NativeInterop.WM_SYSKEYDOWN)
                    {
                        if (!_leftAltDown)
                        {
                            _leftAltDown = true;
                            _leftAltSolo = true;
                        }
                    }
                    else if (msg == NativeInterop.WM_KEYUP || msg == NativeInterop.WM_SYSKEYUP)
                    {
                        if (_leftAltDown && _leftAltSolo)
                        {
                            _leftAltDown = false;
                            _leftAltSolo = false;
                            Dispatcher.BeginInvoke(new Action(() => ToggleStartStop()));
                        }
                        _leftAltDown = false;
                    }
                }
                else if (_leftAltDown && (msg == NativeInterop.WM_KEYDOWN || msg == NativeInterop.WM_SYSKEYDOWN))
                {
                    // Another key pressed while Left Alt held - cancel solo
                    _leftAltSolo = false;
                }
            }
            return NativeInterop.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        #endregion

        #region Button Handlers

        private void BtnStart_Click(object sender, RoutedEventArgs e) => ToggleStartStop();

        private void ToggleStartStop()
        {
            if (ViewModel.IsRunning)
            {
                ViewModel.StopAll();
            }
            else
            {
                bool hasValidKey = false;
                foreach (var key in ViewModel.Keys)
                {
                    if (key.IsEnabled && key.KeyCode > 0) { hasValidKey = true; break; }
                }
                if (!hasValidKey)
                {
                    MessageBox.Show("请至少设置一个按键并启用！", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                ViewModel.StartAll();
            }
        }

        private void BtnBindAll_Click(object sender, RoutedEventArgs e)
        {
            IntPtr foreground = NativeInterop.GetForegroundWindow();
            if (foreground != IntPtr.Zero && foreground != _windowHandle)
            {
                ViewModel.BindWindow(foreground);
            }
            else
            {
                var windows = NativeInterop.EnumerateWindows();
                if (windows.Count == 0)
                {
                    MessageBox.Show("没有找到可绑定的窗口。", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var dlg = new WindowSelectionDialog(windows) { Owner = this };
                if (dlg.ShowDialog() == true)
                    ViewModel.BindWindow(dlg.SelectedHWnd);
            }
        }

        private void BtnUnbind_Click(object sender, RoutedEventArgs e) => ViewModel.BindWindow(IntPtr.Zero);

        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            // Sync ComboBox text to ViewModel
            SyncConfigName();
            ViewModel.SaveConfig();
            ViewModel.RefreshConfigList();
            RefreshConfigComboText();
        }

        private void BtnLoadConfig_Click(object sender, RoutedEventArgs e)
        {
            SyncConfigName();
            ViewModel.LoadConfig();
            KeyList.ItemsSource = ViewModel.Keys;
            RefreshConfigComboText();
        }

        private void BtnDeleteConfig_Click(object sender, RoutedEventArgs e)
        {
            SyncConfigName();
            ViewModel.DeleteConfig();
            ViewModel.RefreshConfigList();
            RefreshConfigComboText();
        }

        private void CmbConfig_LostFocus(object sender, RoutedEventArgs e) => SyncConfigName();

        private void CmbConfig_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshingConfigCombo)
                return;

            if (cmbConfig.SelectedItem is string name && !string.IsNullOrWhiteSpace(name))
            {
                ViewModel.SelectedConfig = name;
                _lastValidConfigName = name;
                RefreshConfigComboText();
                ViewModel.SaveAppState();
            }
        }

        private void SyncConfigName()
        {
            string text = (cmbConfig.Text ?? cmbConfig.SelectedItem as string ?? "").Trim();
            if (string.IsNullOrEmpty(text))
                text = _lastValidConfigName;

            if (!string.IsNullOrEmpty(text))
            {
                ViewModel.SelectedConfig = text;
                _lastValidConfigName = text;
                ViewModel.SaveAppState();
            }
        }

        private void RefreshConfigComboText()
        {
            _refreshingConfigCombo = true;
            try
            {
                _lastValidConfigName = string.IsNullOrWhiteSpace(ViewModel.SelectedConfig)
                    ? "默认"
                    : ViewModel.SelectedConfig;
                cmbConfig.SelectedItem = ViewModel.ConfigNames.Contains(_lastValidConfigName)
                    ? _lastValidConfigName
                    : null;
                cmbConfig.Text = _lastValidConfigName;
            }
            finally
            {
                _refreshingConfigCombo = false;
            }
        }

        #endregion

        #region Select All / Invert

        private void ChkSelectAll_Changed(object sender, RoutedEventArgs e)
        {
            if (chkSelectAll.IsChecked == true) ViewModel.SelectAll(true);
            else ViewModel.SelectAll(false);
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            chkSelectAll.IsChecked = true;
            ViewModel.SelectAll(true);
        }

        private void BtnInvertSelection_Click(object sender, RoutedEventArgs e) => ViewModel.InvertSelection();

        #endregion

        #region Key Capture

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void NumericOnly_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }

            string text = e.DataObject.GetData(DataFormats.Text)?.ToString() ?? "";
            if (!int.TryParse(text, out _))
                e.CancelCommand();
        }

        private void NumericTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && !int.TryParse(textBox.Text, out _))
                textBox.Text = "0";
        }

        private void KeyCaptureBox_KeyCaptured(object sender, RoutedEventArgs e)
        {
            if (sender is KeyCaptureBox cb && cb.Tag is KeyConfig kc)
            {
                kc.KeyCode = cb.CapturedKeyCode;
                kc.KeyName = cb.CapturedKeyName;
            }
        }

        #endregion

        #region Window Binding

        private void BindWindowUnderCursor()
        {
            if (NativeInterop.GetCursorPos(out NativeInterop.POINT pt))
            {
                IntPtr hWnd = NativeInterop.WindowFromPoint(pt);
                if (hWnd != IntPtr.Zero && hWnd != _windowHandle)
                {
                    IntPtr topLevel = NativeInterop.GetAncestor(hWnd, 2);
                    if (topLevel != IntPtr.Zero && topLevel != _windowHandle)
                        hWnd = topLevel;
                    ViewModel.BindWindow(hWnd);
                }
            }
        }

        #endregion
    }

    #region Window Selection Dialog

    public class WindowSelectionDialog : Window
    {
        public IntPtr SelectedHWnd { get; private set; }
        private readonly System.Collections.Generic.List<WindowInfo> _windows;

        public WindowSelectionDialog(System.Collections.Generic.List<WindowInfo> windows)
        {
            _windows = windows;
            Title = "选择要绑定的窗口";
            Width = 500; Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var listBox = new ListBox { Margin = new Thickness(10), FontSize = 13 };
            foreach (var win in windows)
                listBox.Items.Add($"{win.Title} [0x{win.HWnd.ToInt64():X}]");
            if (listBox.Items.Count > 0) listBox.SelectedIndex = 0;
            Grid.SetRow(listBox, 0);
            grid.Children.Add(listBox);

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(10)
            };

            var btnOk = new Button { Content = "确定", Width = 80, Height = 30, Margin = new Thickness(5) };
            btnOk.Click += (s, e) =>
            {
                if (listBox.SelectedIndex >= 0 && listBox.SelectedIndex < _windows.Count)
                { SelectedHWnd = _windows[listBox.SelectedIndex].HWnd; DialogResult = true; Close(); }
            };

            var btnCancel = new Button { Content = "取消", Width = 80, Height = 30, Margin = new Thickness(5) };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };

            panel.Children.Add(btnOk);
            panel.Children.Add(btnCancel);
            Grid.SetRow(panel, 1);
            grid.Children.Add(panel);
            Content = grid;
        }
    }

    #endregion
}
