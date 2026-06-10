using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoKey.Controls
{
    public class HotkeyCaptureBox : TextBox
    {
        public static readonly DependencyProperty HotkeyTextProperty =
            DependencyProperty.Register(
                nameof(HotkeyText),
                typeof(string),
                typeof(HotkeyCaptureBox),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHotkeyTextChanged));

        private readonly HashSet<int> _pressedKeys = new();
        private string _capturedText = "";
        private bool _capturing;

        public string HotkeyText
        {
            get => (string)GetValue(HotkeyTextProperty);
            set => SetValue(HotkeyTextProperty, value);
        }

        public HotkeyCaptureBox()
        {
            IsReadOnly = true;
            IsReadOnlyCaretVisible = false;
            Cursor = Cursors.Hand;
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Center;
            MinWidth = 100;
            Height = 28;

            GotFocus += HotkeyCaptureBox_GotFocus;
            LostFocus += HotkeyCaptureBox_LostFocus;
            PreviewMouseDown += HotkeyCaptureBox_PreviewMouseDown;
            PreviewKeyDown += HotkeyCaptureBox_PreviewKeyDown;
            PreviewKeyUp += HotkeyCaptureBox_PreviewKeyUp;
            Text = string.IsNullOrWhiteSpace(HotkeyText) ? "未设置" : HotkeyText;
        }

        private static void OnHotkeyTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HotkeyCaptureBox box && !box._capturing)
                box.Text = string.IsNullOrWhiteSpace(e.NewValue?.ToString()) ? "未设置" : e.NewValue.ToString();
        }

        private void HotkeyCaptureBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsFocused)
            {
                Focus();
                e.Handled = true;
            }
        }

        private void HotkeyCaptureBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _capturing = true;
            _pressedKeys.Clear();
            _capturedText = "";
            Text = "请按快捷键...";
            Background = System.Windows.Media.Brushes.LightYellow;
        }

        private void HotkeyCaptureBox_LostFocus(object sender, RoutedEventArgs e)
        {
            FinishCapture();
        }

        private void HotkeyCaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                HotkeyText = "";
                FinishCapture();
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                return;
            }

            int vk = HotkeyGesture.NormalizeVirtualKey(KeyInterop.VirtualKeyFromKey(key));
            if (vk <= 0)
                return;

            _pressedKeys.Add(vk);
            _capturedText = HotkeyGesture.FromVirtualKeys(_pressedKeys);
            Text = string.IsNullOrWhiteSpace(_capturedText) ? "请按快捷键..." : _capturedText;
        }

        private void HotkeyCaptureBox_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            if (!string.IsNullOrWhiteSpace(_capturedText))
            {
                HotkeyText = _capturedText;
                FinishCapture();
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        }

        private void FinishCapture()
        {
            _capturing = false;
            _pressedKeys.Clear();
            _capturedText = "";
            Text = string.IsNullOrWhiteSpace(HotkeyText) ? "未设置" : HotkeyText;
            Background = System.Windows.Media.Brushes.White;
        }
    }
}
