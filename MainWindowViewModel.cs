using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
        public int AntiPatternLevel { get; set; } = 2;
    }

    public class AppStateDto
    {
        public string SelectedConfig { get; set; } = "默认";
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
        private bool _stopRequested;
        private int _runVersion;
        private int _antiPatternLevel = 2;

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
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? "循环到手动停止" : value;
                _loopMode = LoopModes.Contains(next) ? next : "循环到手动停止";
                OnPropertyChanged();
            }
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
            set { _selectedConfig = SanitizeConfigName(value ?? "默认"); OnPropertyChanged(); }
        }

        public string StartButtonText => _isRunning ? "停止" : "开始";

        public int AntiPatternLevel
        {
            get => _antiPatternLevel;
            set
            {
                _antiPatternLevel = Math.Clamp(value, 0, 2);
                Humanizer.AntiPatternLevel = _antiPatternLevel;
                OnPropertyChanged();
            }
        }

        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }

        public MainWindowViewModel()
        {
            for (int i = 0; i < 12; i++)
            {
                Keys.Add(new KeyConfig { IsEnabled = i < 4 });
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
            _stopRequested = false;
            int runVersion = ++_runVersion;
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
                        _ = RunKeyLoopAsync(i, runVersion, _keyCtsArray[i].Token);
                    }
                }
            }
            else
            {
                _ = RunSequentialLoopAsync(runVersion, _globalCts.Token);
            }
        }

        public void StopAll()
        {
            if (!IsRunning) return;
            _stopRequested = true;
            _runVersion++;
            _globalCts?.Cancel();
            if (_keyCtsArray != null)
                foreach (var cts in _keyCtsArray) cts?.Cancel();
            foreach (var key in Keys) key.IsRunning = false;
            IsRunning = false;
            StatusText = "已停止";
        }

        private async Task RunKeyLoopAsync(int keyIndex, int runVersion, CancellationToken token)
        {
            var key = Keys[keyIndex];
            int loopCount = GetLoopCount();
            int currentLoop = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await SendKeyAsync(key);
                    int delay = CalcDelay(key);
                    await Task.Delay(delay, token);
                    currentLoop++;
                    if (loopCount > 0 && currentLoop >= loopCount) break;
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                    StatusText = $"运行出错: {ex.Message}");
            }
            finally
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (runVersion != _runVersion)
                        return;

                    key.IsRunning = false;
                    CompleteRunIfNoKeysRunning(runVersion);
                });
            }
        }

        private async Task RunSequentialLoopAsync(int runVersion, CancellationToken token)
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
                        try
                        {
                            await SendKeyAsync(key);
                            int delay = CalcDelay(key);
                            await Task.Delay(delay, token);
                        }
                        finally
                        {
                            key.IsRunning = false;
                        }
                    }
                    currentLoop++;
                    if (loopCount > 0 && currentLoop >= loopCount) break;
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                    StatusText = $"运行出错: {ex.Message}");
            }
            finally
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (runVersion != _runVersion)
                        return;

                    foreach (var key in Keys) key.IsRunning = false;
                    if (IsRunning && !_stopRequested)
                    {
                        IsRunning = false;
                        StatusText = "已完成";
                    }
                });
            }
        }

        private void CompleteRunIfNoKeysRunning(int runVersion)
        {
            if (runVersion != _runVersion || !IsRunning || _stopRequested || Keys.Any(k => k.IsRunning))
                return;

            IsRunning = false;
            StatusText = "已完成";
        }

        private int CalcDelay(KeyConfig key)
        {
            int combinedRange = key.RandomDelay + GlobalRandomDelay;
            return Humanizer.NextDelay(key.Delay, combinedRange);
        }

        private async Task SendKeyAsync(KeyConfig key)
        {
            try
            {
                if (IsWindowBound && _boundWindowHandle != IntPtr.Zero)
                    await NativeInterop.SendKeyToWindowAsync(_boundWindowHandle, key.KeyCode);
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

        private string GetAppDirectory()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AutoKey");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private string GetAppStatePath()
            => Path.Combine(GetAppDirectory(), "app-state.json");

        private static string SanitizeConfigName(string name)
        {
            string sanitized = name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                sanitized = sanitized.Replace(c, '_');
            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = "默认";
            return sanitized;
        }

        public void SaveConfig()
        {
            try
            {
                var dto = new ConfigDto
                {
                    IndependentLoop = IndependentLoop,
                    GlobalRandomDelay = GlobalRandomDelay,
                    LoopMode = LoopMode,
                    WindowWidth = WindowWidth,
                    WindowHeight = WindowHeight,
                    AntiPatternLevel = AntiPatternLevel
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

                string name = SanitizeConfigName(SelectedConfig);
                SelectedConfig = name;
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
                StopAll();

                string name = SanitizeConfigName(SelectedConfig);
                string filePath = Path.Combine(GetConfigDirectory(), $"{name}.json");
                if (!File.Exists(filePath))
                {
                    if (name != "默认")
                    {
                        SelectedConfig = "默认";
                        LoadConfig();
                        StatusText = $"配置 [{name}] 不存在，已切换到默认";
                    }
                    else
                    {
                        StatusText = "默认配置不存在，使用当前设置";
                    }
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
                            RandomDelay = d.RandomDelay
                        });
                    }
                }
                while (Keys.Count < 12)
                    Keys.Add(new KeyConfig());

                IndependentLoop = dto.IndependentLoop;
                GlobalRandomDelay = dto.GlobalRandomDelay;
                LoopMode = dto.LoopMode;
                WindowWidth = dto.WindowWidth;
                WindowHeight = dto.WindowHeight;
                AntiPatternLevel = dto.AntiPatternLevel;

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
                    if (string.IsNullOrWhiteSpace(n))
                        continue;
                    if (n != "默认" && !ConfigNames.Contains(n))
                        ConfigNames.Add(n);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"刷新配置列表出错: {ex.Message}";
            }
        }

        public void SaveAppState()
        {
            try
            {
                var dto = new AppStateDto { SelectedConfig = SanitizeConfigName(SelectedConfig) };
                string json = JsonSerializer.Serialize(dto, JsonOptions);
                File.WriteAllText(GetAppStatePath(), json);
            }
            catch { /* Best effort; config saving still reports its own errors. */ }
        }

        public void LoadAppState()
        {
            try
            {
                string filePath = GetAppStatePath();
                if (!File.Exists(filePath))
                {
                    SelectedConfig = "默认";
                    return;
                }

                string json = File.ReadAllText(filePath);
                var dto = JsonSerializer.Deserialize<AppStateDto>(json, JsonOptions);
                SelectedConfig = SanitizeConfigName(dto?.SelectedConfig ?? "默认");
            }
            catch
            {
                SelectedConfig = "默认";
            }
        }

        public void DeleteConfig()
        {
            try
            {
                StopAll();

                string name = SanitizeConfigName(SelectedConfig);
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
