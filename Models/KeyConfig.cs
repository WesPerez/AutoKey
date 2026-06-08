using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace AutoKey
{
    public class KeyConfig : INotifyPropertyChanged
    {
        private bool _isEnabled = true;
        private int _keyCode;
        private string _keyName = "未设置";
        private int _delay = 1000;
        private int _randomDelay = 100;
        private bool _isRunning;

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        [JsonPropertyName("keyCode")]
        public int KeyCode
        {
            get => _keyCode;
            set { _keyCode = value; OnPropertyChanged(); }
        }

        [JsonPropertyName("keyName")]
        public string KeyName
        {
            get => _keyName;
            set { _keyName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayKey)); }
        }

        [JsonPropertyName("delay")]
        public int Delay
        {
            get => _delay;
            set { _delay = Math.Max(50, value); OnPropertyChanged(); }
        }

        [JsonPropertyName("randomDelay")]
        public int RandomDelay
        {
            get => _randomDelay;
            set { _randomDelay = Math.Max(0, value); OnPropertyChanged(); }
        }

        [JsonIgnore]
        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusColor)); }
        }

        [JsonIgnore]
        public string DisplayKey => string.IsNullOrEmpty(_keyName) ? "未设置" : _keyName;

        [JsonIgnore]
        public string StatusColor => _isRunning ? "#4CAF50" : "#999999";

        [JsonIgnore]
        public int Index { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>
        /// Convert WPF Key enum to virtual key code and display name.
        /// </summary>
        public void SetFromWpfKey(Key key)
        {
            KeyCode = KeyInterop.VirtualKeyFromKey(key);
            KeyName = GetKeyDisplayName(key);
        }

        public static string GetKeyDisplayName(Key key)
        {
            return key switch
            {
                Key.Space => "空格键",
                Key.Up => "上光标键",
                Key.Down => "下光标键",
                Key.Left => "左光标键",
                Key.Right => "右光标键",
                Key.Return => "回车键",
                Key.Escape => "Esc键",
                Key.Tab => "Tab键",
                Key.Back => "退格键",
                Key.Delete => "Delete键",
                Key.Insert => "Insert键",
                Key.Home => "Home键",
                Key.End => "End键",
                Key.PageUp => "PageUp键",
                Key.PageDown => "PageDown键",
                Key.LeftShift or Key.RightShift => "Shift键",
                Key.LeftCtrl or Key.RightCtrl => "Ctrl键",
                Key.LeftAlt or Key.RightAlt => "Alt键",
                Key.CapsLock => "CapsLock键",
                Key.NumLock => "NumLock键",
                Key.Scroll => "ScrollLock键",
                Key.F1 => "F1键",
                Key.F2 => "F2键",
                Key.F3 => "F3键",
                Key.F4 => "F4键",
                Key.F5 => "F5键",
                Key.F6 => "F6键",
                Key.F7 => "F7键",
                Key.F8 => "F8键",
                Key.F9 => "F9键",
                Key.F10 => "F10键",
                Key.F11 => "F11键",
                Key.F12 => "F12键",
                Key.NumPad0 => "小键盘0",
                Key.NumPad1 => "小键盘1",
                Key.NumPad2 => "小键盘2",
                Key.NumPad3 => "小键盘3",
                Key.NumPad4 => "小键盘4",
                Key.NumPad5 => "小键盘5",
                Key.NumPad6 => "小键盘6",
                Key.NumPad7 => "小键盘7",
                Key.NumPad8 => "小键盘8",
                Key.NumPad9 => "小键盘9",
                Key.Multiply => "小键盘*",
                Key.Add => "小键盘+",
                Key.Subtract => "小键盘-",
                Key.Decimal => "小键盘.",
                Key.Divide => "小键盘/",
                _ => key.ToString() + "键"
            };
        }
    }
}
