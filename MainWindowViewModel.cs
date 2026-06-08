using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AutoKey
{
    /// <summary>
    /// Plain DTO for config serialization (avoids ObservableCollection issues).
    /// </summary>
    public class ConfigDto
    {
        public List<KeyConfigDto> Keys { get; set; } = new();
        public bool IndependentLoop { get; set; } = true;
        public int GlobalRandomDelay { get; set; } = 100;
        public string LoopMode { get; set; } = "循环到手动停止";
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
    }

    public class KeyConfigDto
    {
        public bool IsEnabled { get; set; }
        public int KeyCode { get; set; }
        public string KeyName { get; set; } = "未设置";
        public int Delay { get; set; } = 1000;
        public int RandomDelay { get; set; } = 100;
    }

    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private bool _isRunning;
        private bool _independentLoop = true;
        private int _globalRandomDelay = 100;
        private string _loopMode = "循环到手动停止";
        private string _boundWindowTitle = "未绑定";
        private IntPtr _boundWindowHandle = IntPtr.Zero;
        private string _statusText = "就绪";
        private string _selectedConfig = "默认";

        private CancellationTokenSource? _globalCts;
        private CancellationTokenSource[]? _keyCtsArray;
        private readonly Random _random = new();

        public ObservableCollection<KeyConfig> Keys { get; } = new();
        public ObservableCollection<string> ConfigNames { get; } = new() { "默认" };
        public ObservableCollection<string> LoopModes { get; } = new()
        {
            "循环到手动停止", "循环1次", "循环2次", "循环3次",
            "循环5次", "循环10次", "循环50次", "循环100次"
        };

        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(StartButtonText)); }
        }

        public bool IndependentLoop
        {
            get => _independentLoop;
            set { _independentLoop = value; OnPropertyChanged(); }
        }

        public int GlobalRandomDelay
        {
            get => _globalRandomDelay;
            set { _globalRandomDelay = Math.Max(0, value); OnPropertyChanged(); }
        }

        public string LoopMode
        {
            get => _loopMode;
            set { _loopMode = value; OnPropertyChanged(); }
        }

        public string BoundWindowTitle
        {
            get => _boundWindowTitle;
            set { _boundWindowTitle = value; OnPropertyChanged(); }
        }

        public IntPtr BoundWindowHandle
        {
            get => _boundWindowHandle;
            set { _boundWindowHandle = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsWindowBound)); }
        }

        public bool IsWindowBound => _boundWindowHandle != IntPtr.Zero;

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public string SelectedConfig
        {
            get => _selectedConfig;
            set { _selectedConfig = value; OnPropertyChanged(); }
        }

        public string StartButtonText => _isRunning ? "停止" : "开始";

        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }

        public MainWindowViewModel()
        {
            for (int i = 0; i < 12; i++)
            {
                Keys.Add(new KeyConfig { IsEnabled = i < 4, Index = i });
            }
        }

        #region Select All / Invert

        public void SelectAll(bool enabled)
        {
            foreach (var key in Keys) key.IsEnabled = enabled;
        }

        public void InvertSelection()
        {
            foreach (var key in Keys) key.IsEnabled = !key.IsEnabled;
        }

        #endregion

        #region Start / Stop

        public void StartAll()
        {
            if (IsRunning) return;
            IsRunning = true;
            StatusText = "运行中...";
            _globalCts = new CancellationTokenSource();

            if (IndependentLoop)
            {
                _keyCtsArray = new CancellationTokenSource[Keys.Count];
                for (int i = 0; i < Keys.Count; i++)
                {
                    if (Keys[i].IsEnabled && Keys[i].KeyCode > 0)
                    {
                        _keyCtsArray[i] = new CancellationTokenSource();
                        Keys[i].IsRunning = true;
                        _ = RunKeyLoopAsync(i, _keyCtsArray[i].Token);
                    }
                }
            }
            else
            {
                _ = RunSequentialLoopAsync(_globalCts.Token);
            }
        }

        public void StopAll()
        {
            if (!IsRunning) return;
            _globalCts?.Cancel();
            if (_keyCtsArray != null)
                foreach (var cts in _keyCtsArray) cts?.Cancel();
            foreach (var key in Keys) key.IsRunning = false;
            IsRunning = false;
            StatusText = "已停止";
        }

        private async Task RunKeyLoopAsync(int keyIndex, CancellationToken token)
        {
            var key = Keys[keyIndex];
            int loopCount = GetLoopCount();
            int currentLoop = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    SendKey(key);
                    int delay = CalcDelay(key);
                    await Task.Delay(delay, token);
                    currentLoop++;
                    if (loopCount > 0 && currentLoop >= loopCount) break;
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                Application.Current?.Dispatcher.Invoke(() => key.IsRunning = false);
            }
        }

        private async Task RunSequentialLoopAsync(CancellationToken token)
        {
            int loopCount = GetLoopCount();
            int currentLoop = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    foreach (var key in Keys)
                    {
                        if (token.IsCancellationRequested) break;
                        if (!key.IsEnabled || key.KeyCode <= 0) continue;
                        key.IsRunning = true;
                        SendKey(key);
                        int delay = CalcDelay(key);
                        await Task.Delay(delay, token);
                        key.IsRunning = false;
                    }
                    currentLoop++;
                    if (loopCount > 0 && currentLoop >= loopCount) break;
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                Application.Current?.Dispatcher.Invoke(() =>
                { foreach (var key in Keys) key.IsRunning = false; });
            }
        }

        private int CalcDelay(KeyConfig key)
        {
            int delay = key.Delay;
            if (key.RandomDelay > 0)
                delay += _random.Next(-key.RandomDelay, key.RandomDelay + 1);
            if (GlobalRandomDelay > 0)
                delay += _random.Next(-GlobalRandomDelay, GlobalRandomDelay + 1);
            return Math.Max(50, delay);
        }

        private void SendKey(KeyConfig key)
        {
            try
            {
                if (IsWindowBound && _boundWindowHandle != IntPtr.Zero)
                    NativeInterop.SendKeyToWindow(_boundWindowHandle, key.KeyCode);
                else
                    NativeInterop.SendKeyForeground(key.KeyCode);
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                    StatusText = $"发送按键出错: {ex.Message}");
            }
        }

        private int GetLoopCount()
        {
            if (LoopMode == "循环到手动停止") return -1;
            var parts = LoopMode.Replace("循环", "").Replace("次", "");
            if (int.TryParse(parts, out int count)) return count;
            return -1;
        }

        #endregion

        #region Window Binding

        public void BindWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                BoundWindowHandle = IntPtr.Zero;
                BoundWindowTitle = "未绑定";
                StatusText = "已解除窗口绑定";
            }
            else
            {
                BoundWindowHandle = hWnd;
                BoundWindowTitle = NativeInterop.GetWindowTitle(hWnd);
                if (string.IsNullOrWhiteSpace(BoundWindowTitle))
                    BoundWindowTitle = $"窗口 0x{hWnd.ToInt64():X}";
                StatusText = $"已绑定: {BoundWindowTitle}";
            }
        }

        #endregion

        #region Configuration Management

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private string GetConfigDirectory()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AutoKey", "configs");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public void SaveConfig()
        {
            try
            {
                // Convert to plain DTO for reliable serialization
                var dto = new ConfigDto
                {
                    IndependentLoop = IndependentLoop,
                    GlobalRandomDelay = GlobalRandomDelay,
                    LoopMode = LoopMode,
                    WindowWidth = WindowWidth,
                    WindowHeight = WindowHeight
                };
                foreach (var k in Keys)
                {
                    dto.Keys.Add(new KeyConfigDto
                    {
                        IsEnabled = k.IsEnabled,
                        KeyCode = k.KeyCode,
                        KeyName = k.KeyName,
                        Delay = k.Delay,
                        RandomDelay = k.RandomDelay
                    });
                }

                string name = string.IsNullOrWhiteSpace(SelectedConfig) ? "默认" : SelectedConfig.Trim();
                string filePath = Path.Combine(GetConfigDirectory(), $"{name}.json");
                string json = JsonSerializer.Serialize(dto, JsonOptions);
                File.WriteAllText(filePath, json);
                StatusText = $"配置 [{name}] 已保存";
            }
            catch (Exception ex)
            {
                StatusText = $"保存配置出错: {ex.Message}";
            }
        }

        public void LoadConfig()
        {
            try
            {
                string name = string.IsNullOrWhiteSpace(SelectedConfig) ? "默认" : SelectedConfig.Trim();
                string filePath = Path.Combine(GetConfigDirectory(), $"{name}.json");
                if (!File.Exists(filePath))
                {
                    StatusText = $"配置 [{name}] 不存在，使用默认";
                    return;
                }

                string json = File.ReadAllText(filePath);
                var dto = JsonSerializer.Deserialize<ConfigDto>(json, JsonOptions);
                if (dto == null)
                {
                    StatusText = "配置文件格式错误";
                    return;
                }

                Keys.Clear();
                if (dto.Keys != null)
                {
                    for (int i = 0; i < dto.Keys.Count; i++)
                    {
                        var d = dto.Keys[i];
                        Keys.Add(new KeyConfig
                        {
                            IsEnabled = d.IsEnabled,
                            KeyCode = d.KeyCode,
                            KeyName = d.KeyName,
                            Delay = d.Delay,
                            RandomDelay = d.RandomDelay,
                            Index = i
                        });
                    }
                }
                while (Keys.Count < 12)
                    Keys.Add(new KeyConfig { Index = Keys.Count });

                IndependentLoop = dto.IndependentLoop;
                GlobalRandomDelay = dto.GlobalRandomDelay;
                LoopMode = dto.LoopMode;
                WindowWidth = dto.WindowWidth;
                WindowHeight = dto.WindowHeight;

                StatusText = $"配置 [{name}] 已加载";
            }
            catch (Exception ex)
            {
                StatusText = $"加载配置出错: {ex.Message}";
            }
        }

        public void RefreshConfigList()
        {
            try
            {
                string dir = GetConfigDirectory();
                ConfigNames.Clear();
                ConfigNames.Add("默认");
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                {
                    string n = Path.GetFileNameWithoutExtension(file);
                    if (n != "默认" && !ConfigNames.Contains(n))
                        ConfigNames.Add(n);
                }
            }
            catch { }
        }

        public void DeleteConfig()
        {
            try
            {
                string name = string.IsNullOrWhiteSpace(SelectedConfig) ? "默认" : SelectedConfig.Trim();
                if (name == "默认")
                {
                    StatusText = "默认配置不能删除";
                    return;
                }

                string filePath = Path.Combine(GetConfigDirectory(), $"{name}.json");
                if (!File.Exists(filePath))
                {
                    StatusText = $"配置 [{name}] 不存在";
                    return;
                }

                File.Delete(filePath);
                SelectedConfig = "默认";
                StatusText = $"配置 [{name}] 已删除";
            }
            catch (Exception ex)
            {
                StatusText = $"删除配置出错: {ex.Message}";
            }
        }

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
