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
using AutoKey.Controls;

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
            => throw new NotImplementedException();
    }

    public partial class MainWindow : Window
    {
        private MainWindowViewModel ViewModel { get; } = new();
        private IntPtr _windowHandle;
        private HwndSource? _hwndSource;

        private const int HOTKEY_TOGGLE = 1;
        private const int HOTKEY_BIND_WINDOW = 2;

        // Right-click drag state
        private bool _rightDragging;
        private IntPtr _rightDragStartWindow;

        // Low-level mouse hook for global right-click detection
        private IntPtr _mouseHookId = IntPtr.Zero;
        private NativeInterop.LowLevelMouseProc? _mouseProc;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = ViewModel;
            KeyList.ItemsSource = ViewModel.Keys;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _windowHandle = new WindowInteropHelper(this).EnsureHandle();
                _hwndSource = HwndSource.FromHwnd(_windowHandle);
                _hwndSource?.AddHook(WndProc);

                NativeInterop.RegisterHotKey(_windowHandle, HOTKEY_TOGGLE, NativeInterop.MOD_NOREPEAT, 0xA4);
                NativeInterop.RegisterHotKey(_windowHandle, HOTKEY_BIND_WINDOW,
                    NativeInterop.MOD_CONTROL | NativeInterop.MOD_ALT, 0x20);

                // Install global mouse hook for right-click drag binding
                InstallMouseHook();

                ViewModel.SelectedConfig = "默认";
                ViewModel.LoadConfig();
                ViewModel.RefreshConfigList();
                KeyList.ItemsSource = ViewModel.Keys;

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
            ViewModel.StopAll();

            // Save window size before auto-saving config
            ViewModel.WindowWidth = Width;
            ViewModel.WindowHeight = Height;

            // Auto-save config on close
            try { ViewModel.SaveConfig(); } catch { }

            if (_windowHandle != IntPtr.Zero)
            {
                NativeInterop.UnregisterHotKey(_windowHandle, HOTKEY_TOGGLE);
                NativeInterop.UnregisterHotKey(_windowHandle, HOTKEY_BIND_WINDOW);
            }

            UninstallMouseHook();
            _hwndSource?.RemoveHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeInterop.WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                switch (hotkeyId)
                {
                    case HOTKEY_TOGGLE: ToggleStartStop(); handled = true; break;
                    case HOTKEY_BIND_WINDOW: BindWindowUnderCursor(); handled = true; break;
                }
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
                    if (NativeInterop.GetCursorPos(out NativeInterop.POINT pt))
                    {
                        _rightDragStartWindow = NativeInterop.WindowFromPoint(pt);
                    }
                }
                else if (msg == NativeInterop.WM_RBUTTONUP && _rightDragging)
                {
                    _rightDragging = false;
                    // Right button released - bind to window under cursor
                    if (NativeInterop.GetCursorPos(out NativeInterop.POINT pt))
                    {
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
        }

        private void BtnLoadConfig_Click(object sender, RoutedEventArgs e)
        {
            SyncConfigName();
            ViewModel.LoadConfig();
            KeyList.ItemsSource = ViewModel.Keys;
        }

        private void BtnDeleteConfig_Click(object sender, RoutedEventArgs e)
        {
            SyncConfigName();
            ViewModel.DeleteConfig();
            ViewModel.RefreshConfigList();
        }

        private void CmbConfig_LostFocus(object sender, RoutedEventArgs e) => SyncConfigName();

        private void SyncConfigName()
        {
            string text = cmbConfig.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(text))
                ViewModel.SelectedConfig = text;
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
