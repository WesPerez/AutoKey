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
        public string ConfigHotkey { get; set; } = "";
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public int AntiPatternLevel { get; set; } = 2;
    }

    public class AppStateDto
    {
        public string SelectedConfig { get; set; } = "默认";
        public string CycleConfigHotkey { get; set; } = "Ctrl+Z";
    }

    public class ConfigHotkey
    {
        public string ConfigName { get; set; } = "";
        public string Hotkey { get; set; } = "";
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
        private string _cycleConfigHotkey = "Ctrl+Z";
        private string _selectedConfigHotkey = "";
        private bool _suppressHotkeyAutoSave;
        private bool _suppressAppStateAutoSave;

        private CancellationTokenSource? _globalCts;
        private CancellationTokenSource[]? _keyCtsArray;
        private bool _stopRequested;
        private int _runVersion;
        private int _antiPatternLevel = 2;
        private const int LoopRecoveryDelayMs = 100;

        private sealed class KeyRunConfig
        {
            public int Index { get; init; }
            public KeyConfig Source { get; init; } = null!;
            public int KeyCode { get; init; }
            public string KeyName { get; init; } = "";
            public int Delay { get; init; }
            public int RandomDelay { get; init; }
            public bool IsEnabled { get; init; }
        }

        public ObservableCollection<KeyConfig> Keys { get; } = new();
        public ObservableCollection<string> ConfigNames { get; } = new() { "默认" };
        public ObservableCollection<ConfigHotkey> ConfigHotkeys { get; } = new();
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

        public string CycleConfigHotkey
        {
            get => _cycleConfigHotkey;
            set
            {
                _cycleConfigHotkey = NormalizeHotkeyText(value, "Ctrl+Z");
                OnPropertyChanged();
                if (!_suppressAppStateAutoSave)
                    SaveAppState();
            }
        }

        public string SelectedConfigHotkey
        {
            get => _selectedConfigHotkey;
            set
            {
                _selectedConfigHotkey = NormalizeHotkeyText(value, "");
                OnPropertyChanged();
                UpsertConfigHotkey(SelectedConfig, _selectedConfigHotkey);
                if (!_suppressHotkeyAutoSave && !string.IsNullOrWhiteSpace(SelectedConfig))
                    SaveConfig();
            }
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
            Humanizer.Reset();
            IsRunning = true;
            StatusText = "运行中...";
            _globalCts = new CancellationTokenSource();
            var runKeys = CreateRunSnapshot();

            if (IndependentLoop)
            {
                _keyCtsArray = new CancellationTokenSource[Keys.Count];
                for (int i = 0; i < runKeys.Count; i++)
                {
                    if (runKeys[i].IsEnabled && runKeys[i].KeyCode > 0)
                    {
                        _keyCtsArray[i] = new CancellationTokenSource();
                        runKeys[i].Source.IsRunning = true;
                        _ = RunKeyLoopAsync(runKeys[i].Source, runKeys[i], runVersion, _keyCtsArray[i].Token);
                    }
                }
            }
            else
            {
                _ = RunSequentialLoopAsync(runKeys, runVersion, _globalCts.Token);
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

        private List<KeyRunConfig> CreateRunSnapshot()
        {
            return Keys.Select((key, index) => new KeyRunConfig
            {
                Index = index,
                Source = key,
                IsEnabled = key.IsEnabled,
                KeyCode = key.KeyCode,
                KeyName = key.KeyName,
                Delay = key.Delay,
                RandomDelay = key.RandomDelay
            }).ToList();
        }

        private async Task RunKeyLoopAsync(KeyConfig key, KeyRunConfig runKey, int runVersion, CancellationToken token)
        {
            int loopCount = GetLoopCount();
            int currentLoop = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await SendKeyAsync(runKey, token).ConfigureAwait(false);
                        int delay = CalcDelay(runKey);
                        await Task.Delay(delay, token).ConfigureAwait(false);
                        currentLoop++;
                        if (loopCount > 0 && currentLoop >= loopCount) break;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        App.LogError($"RunKeyLoopAsync[{runKey.KeyName}]", ex);
                        SetStatusTextSafe($"按键 [{runKey.KeyName}] 循环异常，已自动恢复: {ex.Message}");

                        if (loopCount > 0)
                            break;

                        await Task.Delay(LoopRecoveryDelayMs, token).ConfigureAwait(false);
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                App.LogError($"RunKeyLoopAsync[{runKey.KeyName}]", ex);
                SetStatusTextSafe($"运行出错: {ex.Message}");
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

        private async Task RunSequentialLoopAsync(IReadOnlyList<KeyRunConfig> runKeys, int runVersion, CancellationToken token)
        {
            int loopCount = GetLoopCount();
            int currentLoop = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    for (int i = 0; i < runKeys.Count; i++)
                    {
                        var runKey = runKeys[i];
                        var key = runKey.Source;
                        if (token.IsCancellationRequested) break;
                        if (!runKey.IsEnabled || runKey.KeyCode <= 0) continue;
                        SetKeyRunningSafe(key, true);
                        try
                        {
                            await SendKeyAsync(runKey, token).ConfigureAwait(false);
                            int delay = CalcDelay(runKey);
                            await Task.Delay(delay, token).ConfigureAwait(false);
                        }
                        finally
                        {
                            SetKeyRunningSafe(key, false);
                        }
                    }
                    currentLoop++;
                    if (loopCount > 0 && currentLoop >= loopCount) break;
                }
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
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

        private int CalcDelay(KeyRunConfig key)
        {
            long combinedRange = (long)key.RandomDelay + GlobalRandomDelay;
            int safeRange = (int)Math.Clamp(combinedRange, 0, int.MaxValue / 4);
            int delay = Humanizer.NextDelay(key.Delay, safeRange, key.Index);
            return Math.Clamp(delay, 20, int.MaxValue - 1);
        }

        private async Task SendKeyAsync(KeyRunConfig key, CancellationToken token)
        {
            IntPtr targetWindow = IntPtr.Zero;
            try
            {
                int prePressDelay = Humanizer.NextPrePressDelay(key.Index);
                if (prePressDelay > 0)
                    await Task.Delay(prePressDelay, token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();
                targetWindow = _boundWindowHandle;
                if (targetWindow != IntPtr.Zero)
                    await NativeInterop.SendKeyToWindowAsync(targetWindow, key.KeyCode, token).ConfigureAwait(false);
                else
                    await NativeInterop.SendKeyForegroundAsync(key.KeyCode, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.LogError($"SendKeyAsync[{key.KeyName}]", ex);
                if (targetWindow != IntPtr.Zero && !NativeInterop.IsWindow(targetWindow))
                    ClearBoundWindowSafe();
                SetStatusTextSafe($"发送按键出错: {ex.Message}");
            }
        }

        private void SetStatusTextSafe(string text)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                    StatusText = text;
                else
                    dispatcher.Invoke(() => StatusText = text);
            }
            catch (Exception ex)
            {
                App.LogError("SetStatusTextSafe", ex);
            }
        }

        private static void SetKeyRunningSafe(KeyConfig key, bool isRunning)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                    key.IsRunning = isRunning;
                else
                    dispatcher.Invoke(() => key.IsRunning = isRunning);
            }
            catch (Exception ex)
            {
                App.LogError("SetKeyRunningSafe", ex);
            }
        }

        private void ClearBoundWindowSafe()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    BoundWindowHandle = IntPtr.Zero;
                    BoundWindowTitle = "未绑定";
                }
                else
                {
                    dispatcher.Invoke(() =>
                    {
                        BoundWindowHandle = IntPtr.Zero;
                        BoundWindowTitle = "未绑定";
                    });
                }
            }
            catch (Exception ex)
            {
                App.LogError("ClearBoundWindowSafe", ex);
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

        private static void WriteAllTextAtomic(string filePath, string contents)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tempPath, contents);
                if (File.Exists(filePath))
                    File.Replace(tempPath, filePath, null);
                else
                    File.Move(tempPath, filePath);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }
            }
        }

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
                    ConfigHotkey = SelectedConfigHotkey,
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
                WriteAllTextAtomic(filePath, json);
                StatusText = $"配置 [{name}] 已保存";
            }
            catch (Exception ex)
            {
                App.LogError("SaveConfig", ex);
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
                _suppressHotkeyAutoSave = true;
                SelectedConfigHotkey = dto.ConfigHotkey;
                _suppressHotkeyAutoSave = false;
                WindowWidth = dto.WindowWidth;
                WindowHeight = dto.WindowHeight;
                AntiPatternLevel = dto.AntiPatternLevel;

                StatusText = $"配置 [{name}] 已加载";
            }
            catch (Exception ex)
            {
                App.LogError("LoadConfig", ex);
                StatusText = $"加载配置出错: {ex.Message}";
            }
        }

        public void RefreshConfigList()
        {
            try
            {
                string dir = GetConfigDirectory();
                ConfigNames.Clear();
                ConfigHotkeys.Clear();
                ConfigNames.Add("默认");
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                {
                    string n = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrWhiteSpace(n))
                        continue;
                    if (n != "默认" && !ConfigNames.Contains(n))
                        ConfigNames.Add(n);

                    string? hotkey = TryReadConfigHotkey(file);
                    if (!string.IsNullOrWhiteSpace(hotkey))
                    {
                        ConfigHotkeys.Add(new ConfigHotkey
                        {
                            ConfigName = n,
                            Hotkey = hotkey
                        });
                    }
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
                var dto = new AppStateDto
                {
                    SelectedConfig = SanitizeConfigName(SelectedConfig),
                    CycleConfigHotkey = CycleConfigHotkey
                };
                string json = JsonSerializer.Serialize(dto, JsonOptions);
                WriteAllTextAtomic(GetAppStatePath(), json);
            }
            catch (Exception ex)
            {
                App.LogError("SaveAppState", ex);
            }
        }

        public void LoadAppState()
        {
            try
            {
                string filePath = GetAppStatePath();
                if (!File.Exists(filePath))
                {
                    _suppressAppStateAutoSave = true;
                    SelectedConfig = "默认";
                    CycleConfigHotkey = "Ctrl+Z";
                    _suppressAppStateAutoSave = false;
                    return;
                }

                string json = File.ReadAllText(filePath);
                var dto = JsonSerializer.Deserialize<AppStateDto>(json, JsonOptions);
                _suppressAppStateAutoSave = true;
                SelectedConfig = SanitizeConfigName(dto?.SelectedConfig ?? "默认");
                CycleConfigHotkey = dto?.CycleConfigHotkey ?? "Ctrl+Z";
                _suppressAppStateAutoSave = false;
            }
            catch
            {
                _suppressAppStateAutoSave = true;
                SelectedConfig = "默认";
                CycleConfigHotkey = "Ctrl+Z";
                _suppressAppStateAutoSave = false;
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

        public void LoadNextConfig()
        {
            RefreshConfigList();
            if (ConfigNames.Count == 0)
                return;

            int currentIndex = ConfigNames.IndexOf(SelectedConfig);
            int nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % ConfigNames.Count;
            LoadConfigByName(ConfigNames[nextIndex]);
        }

        public void LoadConfigByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            StopAll();
            SelectedConfig = name;
            LoadConfig();
            SaveAppState();
        }

        private static string NormalizeHotkeyText(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            return HotkeyGesture.TryParse(value, out var keys) && HotkeyGesture.IsRegisterableHotkey(keys)
                ? HotkeyGesture.FromVirtualKeys(keys)
                : fallback;
        }

        private static string? TryReadConfigHotkey(string filePath)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(filePath));
                if (document.RootElement.TryGetProperty(nameof(ConfigDto.ConfigHotkey), out var hotkey) &&
                    hotkey.ValueKind == JsonValueKind.String)
                {
                    string? value = hotkey.GetString();
                    if (!string.IsNullOrWhiteSpace(value) &&
                        HotkeyGesture.TryParse(value, out var keys) &&
                        HotkeyGesture.IsRegisterableHotkey(keys))
                    {
                        return HotkeyGesture.FromVirtualKeys(keys);
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private void UpsertConfigHotkey(string configName, string hotkey)
        {
            configName = SanitizeConfigName(configName);
            if (string.IsNullOrWhiteSpace(configName))
                return;

            var existing = ConfigHotkeys.FirstOrDefault(x =>
                string.Equals(x.ConfigName, configName, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(hotkey))
            {
                if (existing != null)
                    ConfigHotkeys.Remove(existing);
                return;
            }

            if (existing == null)
            {
                ConfigHotkeys.Add(new ConfigHotkey { ConfigName = configName, Hotkey = hotkey });
            }
            else
            {
                existing.Hotkey = hotkey;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
