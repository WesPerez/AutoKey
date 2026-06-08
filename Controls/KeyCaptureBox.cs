using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoKey.Controls
{
    /// <summary>
    /// A TextBox-based control that captures keyboard key presses and displays the key name.
    /// Uses DependencyProperties for XAML binding support.
    /// </summary>
    public class KeyCaptureBox : TextBox
    {
        #region Dependency Properties

        public static readonly DependencyProperty CapturedKeyCodeProperty =
            DependencyProperty.Register("CapturedKeyCode", typeof(int), typeof(KeyCaptureBox),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty CapturedKeyNameProperty =
            DependencyProperty.Register("CapturedKeyName", typeof(string), typeof(KeyCaptureBox),
                new FrameworkPropertyMetadata("点击设置按键", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnCapturedKeyNameChanged));

        private static void OnCapturedKeyNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is KeyCaptureBox box && !box.IsFocused)
            {
                box.Text = e.NewValue?.ToString() ?? "点击设置按键";
            }
        }

        public int CapturedKeyCode
        {
            get => (int)GetValue(CapturedKeyCodeProperty);
            set => SetValue(CapturedKeyCodeProperty, value);
        }

        public string CapturedKeyName
        {
            get => (string)GetValue(CapturedKeyNameProperty);
            set => SetValue(CapturedKeyNameProperty, value);
        }

        #endregion

        public static readonly RoutedEvent KeyCapturedEvent =
            EventManager.RegisterRoutedEvent("KeyCaptured", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(KeyCaptureBox));

        public event RoutedEventHandler KeyCaptured
        {
            add { AddHandler(KeyCapturedEvent, value); }
            remove { RemoveHandler(KeyCapturedEvent, value); }
        }

        public KeyCaptureBox()
        {
            IsReadOnly = true;
            Text = CapturedKeyName;
            IsReadOnlyCaretVisible = false;
            Cursor = Cursors.Hand;
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Center;
            FontSize = 13;
            MinWidth = 100;
            Height = 30;

            GotFocus += KeyCaptureBox_GotFocus;
            LostFocus += KeyCaptureBox_LostFocus;
            PreviewKeyDown += KeyCaptureBox_PreviewKeyDown;
            PreviewMouseDown += KeyCaptureBox_PreviewMouseDown;
        }

        private void KeyCaptureBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsFocused)
            {
                Focus();
                e.Handled = true;
            }
        }

        private void KeyCaptureBox_GotFocus(object sender, RoutedEventArgs e)
        {
            Text = "请按下按键...";
            Background = System.Windows.Media.Brushes.LightYellow;
        }

        private void KeyCaptureBox_LostFocus(object sender, RoutedEventArgs e)
        {
            Text = CapturedKeyName;
            Background = System.Windows.Media.Brushes.White;
        }

        private void KeyCaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            Key key = e.Key;
            if (key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            if (key == Key.Escape)
            {
                CapturedKeyCode = 0;
                CapturedKeyName = "未设置";
                RaiseEvent(new RoutedEventArgs(KeyCapturedEvent, this));
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                return;
            }

            CapturedKeyCode = KeyInterop.VirtualKeyFromKey(key);
            CapturedKeyName = KeyConfig.GetKeyDisplayName(key);

            RaiseEvent(new RoutedEventArgs(KeyCapturedEvent, this));
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
    }
}
