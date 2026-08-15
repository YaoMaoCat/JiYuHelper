using JiYuHelper.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI;
using WinRT.Interop;
using WinPickers = Windows.Storage.Pickers;
using WapPickers = Microsoft.Windows.Storage.Pickers;

namespace JiYuHelper.Views;

public sealed partial class SettingsPage : Page
{
    private readonly MainWindow _owner;
    private readonly AppSettings _settings;
    private readonly HookSettings _hook;
    private readonly JyHookClient _reloadClient;
    private readonly DispatcherTimer _screenTimer;
    private bool _suppressSelectionEvent;
    private bool _suppressScreenToggle;
    private bool _suppressModeRadio;
    private bool _suppressPortChanged;
    private string _dllDir = "";
    private DateTime _lastScreenPngWrite = DateTime.MinValue;
    private long _lastScreenPngSize = -1;

    private sealed class ThemeCard
    {
        public string Name { get; set; } = "";
        public Color PreviewBg { get; set; }
        public Color PreviewAccent { get; set; }
        public ThemeOption Option { get; set; }
    }

    public SettingsPage(MainWindow owner)
    {
        InitializeComponent();
        _owner = owner;
        _settings = SettingsStore.Load();
        _hook = _settings.Hook;
        _dllDir = AppContext.BaseDirectory;

        TitleBox.Text = _settings.WindowTitle;

        // 端口设置: 初始化 (0 = 默认)
        _suppressPortChanged = true;
        MulticastPortBox.Value = _settings.MulticastPort;
        ControlPortBox.Value = _settings.ControlPort;
        SessionPortBox.Value = _settings.SessionPort;
        _suppressPortChanged = false;

        // 界面模式: 同步单选状态 (构造期赋值会触发 Checked, 用标志抑制)
        _suppressModeRadio = true;
        if (SettingsStore.ParseUiMode(_settings.UiMode) == UiModeOption.Novice)
            ModeNoviceRadio.IsChecked = true;
        else
            ModeDeveloperRadio.IsChecked = true;
        _suppressModeRadio = false;

        // 假屏图: 自动监测开关初始状态 + 管道客户端 (多客户端管道, 独立实例安全)
        _suppressScreenToggle = true;
        AutoReloadScreenSwitch.IsOn = _hook.AutoReloadScreen;
        _suppressScreenToggle = false;

        _reloadClient = new JyHookClient(JyHookClient.MainPipeName, ProcessManager.StudentMainExe,
            () => ProcessManager.FindProcesses(ProcessManager.StudentMainExe).Any(p => p.IsInjected));
        _reloadClient.Start();

        // 假屏图自动监测轮询 (2s), 页面常驻于缓存, 不随切页停止
        _screenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _screenTimer.Tick += (_, _) => CheckScreenPngChanged();
        _screenTimer.Start();

        var cards = new List<ThemeCard>
        {
            new() { Name = "跟随系统", Option = ThemeOption.System,
                    PreviewBg = Color.FromArgb(255, 80, 80, 80), PreviewAccent = Color.FromArgb(255, 0, 120, 212) },
            new() { Name = "蓝色 (个性)", Option = ThemeOption.Blue,
                    PreviewBg = Color.FromArgb(255, 214, 232, 248), PreviewAccent = Color.FromArgb(255, 13, 110, 189) },
            new() { Name = "白色 (明亮)", Option = ThemeOption.Light,
                    PreviewBg = Color.FromArgb(255, 255, 255, 255), PreviewAccent = Color.FromArgb(255, 0, 120, 212) },
            new() { Name = "深色", Option = ThemeOption.Dark,
                    PreviewBg = Color.FromArgb(255, 32, 32, 32), PreviewAccent = Color.FromArgb(255, 96, 169, 235) },
        };

        ThemeGrid.ItemsSource = cards;

        // 同步当前主题到选中项
        SyncThemeSelection(SettingsStore.ParseTheme(_settings.Theme));
    }

    private void OnApplyTitleClick(object sender, RoutedEventArgs e)
    {
        string title = TitleBox.Text.Trim();
        if (string.IsNullOrEmpty(title))
        {
            Logger.Warning("窗口标题不能为空");
            return;
        }

        _settings.WindowTitle = title;
        SettingsStore.Save(_settings);
        _owner.SetWindowTitle(title);
        Logger.Success($"窗口标题已改为 \"{title}\"");
        ShowSaveHint("已保存");
    }

    private void OnResetTitleClick(object sender, RoutedEventArgs e)
    {
        _settings.WindowTitle = "JiYuHelper";
        SettingsStore.Save(_settings);
        _owner.SetWindowTitle("JiYuHelper");
        TitleBox.Text = "JiYuHelper";
        Logger.Success("窗口标题已恢复默认");
        ShowSaveHint("已恢复默认");
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvent) return;
        if (ThemeGrid.SelectedItem is not ThemeCard card) return;

        _settings.Theme = card.Option.ToString();
        SettingsStore.Save(_settings);
        _owner.ApplyTheme(card.Option);
        Logger.Success($"主题已切换: {card.Name}");
        ShowSaveHint("已保存");
        RefreshSelectionRing();
    }

    /// <summary>GridView 加载后刷新选中边框 (容器此时才生成)</summary>
    private void OnThemeGridLoaded(object sender, RoutedEventArgs e) => RefreshSelectionRing();

    /// <summary>刷新选中项边框: 蓝色主题 = 蓝色, 其他 = 系统强调色</summary>
    private void RefreshSelectionRing()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            bool isBlue = ThemeManager.Current == ThemeOption.Blue;
            var selColor = new SolidColorBrush(isBlue
                ? Color.FromArgb(255, 13, 110, 189)
                : Color.FromArgb(255, 0, 120, 212));

            foreach (var item in ThemeGrid.Items)
            {
                if (ThemeGrid.ContainerFromItem(item) is GridViewItem container)
                {
                    bool selected = ThemeGrid.SelectedItem == item;
                    container.BorderBrush = selected ? selColor : new SolidColorBrush(Colors.Transparent);
                }
            }
        });
    }

    /// <summary>
    /// 导出配置: 窗口标题 + 主题 + Hook 设置 -> JSON 文件
    /// (不包含免责声明状态、发现的教师机与攻击内容)
    /// </summary>
    private async void OnExportConfigClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await PickSaveFileAsync();
            if (file == null) return; // 用户取消

            var data = new AppSettings
            {
                WindowTitle = _settings.WindowTitle,
                Theme = _settings.Theme,
                Hook = _settings.Hook,
            };
            var json = JsonSerializer.Serialize(data, AppSettingsJsonContext.Default.AppSettings);
            await FileIO.WriteTextAsync(file, json);
            Logger.Success($"配置已导出: {file.Path}");
            ShowSaveHint("已导出");
        }
        catch (Exception ex)
        {
            Logger.Error($"配置导出失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 导入配置: 从 JSON 文件恢复窗口标题 + 主题 + Hook 设置并立即应用
    /// </summary>
    private async void OnImportConfigClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await PickOpenFileAsync();
            if (file == null) return; // 用户取消

            AppSettings? imported;
            try
            {
                string json = await FileIO.ReadTextAsync(file);
                imported = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
            }
            catch (Exception ex)
            {
                Logger.Error($"配置文件读取失败: {ex.Message}");
                ShowSaveHint("导入失败");
                return;
            }

            if (imported == null)
            {
                Logger.Warning($"配置文件无效: {file.Path}");
                ShowSaveHint("导入失败");
                return;
            }

            // 窗口标题
            if (!string.IsNullOrEmpty(imported.WindowTitle))
            {
                _settings.WindowTitle = imported.WindowTitle;
                TitleBox.Text = imported.WindowTitle;
                _owner.SetWindowTitle(imported.WindowTitle);
            }

            // 主题 (非法值回退跟随系统)
            ThemeOption theme = SettingsStore.ParseTheme(imported.Theme);
            _settings.Theme = theme.ToString();
            _owner.ApplyTheme(theme);
            SyncThemeSelection(theme);

            // Hook 设置 (缺失字段保持默认)
            _settings.Hook = imported.Hook ?? new HookSettings();

            SettingsStore.Save(_settings);
            Logger.Success($"配置已导入: {file.Path}");
            ShowSaveHint("已导入");
        }
        catch (Exception ex)
        {
            Logger.Error($"配置导入失败: {ex.Message}");
        }
    }

    // ---------- 文件选择器 (unpackaged 兼容) ----------

    /// <summary>当前窗口的 WindowId (新版 picker 构造参数)</summary>
    private Microsoft.UI.WindowId GetWindowId()
        => Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_owner.GetWindowHandle());

    /// <summary>
    /// 保存文件选择器: 优先 Windows App SDK 新版 picker (unpackaged 可用)。
    /// 仅在"新版 picker 抛异常(不可用)"时回退经典 WinRT picker;
    /// 用户主动取消 (返回 null) 不触发回退, 避免弹两次窗口。
    /// </summary>
    private async Task<IStorageFile?> PickSaveFileAsync()
    {
        bool newPickerFailed = false;
        try
        {
            var p = new WapPickers.FileSavePicker(GetWindowId())
            {
                SuggestedStartLocation = WapPickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = "JiYuHelper-config",
            };
            p.FileTypeChoices.Add("JSON 配置文件", new List<string> { ".json" });
            var result = await p.PickSaveFileAsync();
            if (result == null) return null;                    // 用户取消
            if (!string.IsNullOrEmpty(result.Path))
                return await StorageFile.GetFileFromPathAsync(result.Path);
            return null;                                        // 无结果也视为取消
        }
        catch (Exception ex)
        {
            newPickerFailed = true;
            Logger.Warning($"新版文件选择器不可用: {ex.Message}, 回退经典选择器");
        }

        if (!newPickerFailed) return null;

        var old = new WinPickers.FileSavePicker
        {
            SuggestedStartLocation = WinPickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "JiYuHelper-config",
        };
        old.FileTypeChoices.Add("JSON 配置文件", new List<string> { ".json" });
        InitializeWithWindow.Initialize(old, _owner.GetWindowHandle());
        return await old.PickSaveFileAsync();
    }

    /// <summary>打开文件选择器 (同上, 取消不回退)</summary>
    private async Task<IStorageFile?> PickOpenFileAsync()
    {
        bool newPickerFailed = false;
        try
        {
            var p = new WapPickers.FileOpenPicker(GetWindowId())
            {
                SuggestedStartLocation = WapPickers.PickerLocationId.DocumentsLibrary,
                ViewMode = WapPickers.PickerViewMode.List,
            };
            p.FileTypeFilter.Add(".json");
            var result = await p.PickSingleFileAsync();
            if (result == null) return null;                    // 用户取消
            if (!string.IsNullOrEmpty(result.Path))
                return await StorageFile.GetFileFromPathAsync(result.Path);
            return null;
        }
        catch (Exception ex)
        {
            newPickerFailed = true;
            Logger.Warning($"新版文件选择器不可用: {ex.Message}, 回退经典选择器");
        }

        if (!newPickerFailed) return null;

        var old = new WinPickers.FileOpenPicker
        {
            SuggestedStartLocation = WinPickers.PickerLocationId.DocumentsLibrary,
            ViewMode = WinPickers.PickerViewMode.List,
        };
        old.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(old, _owner.GetWindowHandle());
        return await old.PickSingleFileAsync();
    }

    /// <summary>同步主题选中项 (供构造与导入后调用)</summary>
    private void SyncThemeSelection(ThemeOption current)
    {
        foreach (var item in ThemeGrid.Items)
        {
            if (item is ThemeCard card && card.Option == current)
            {
                _suppressSelectionEvent = true;
                ThemeGrid.SelectedItem = card;
                _suppressSelectionEvent = false;
                break;
            }
        }
        RefreshSelectionRing();
    }

    private void ShowSaveHint(string text)
    {
        SaveHintText.Text = text;
        SaveHintText.Opacity = 1;
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            await System.Threading.Tasks.Task.Delay(2000);
            SaveHintText.Opacity = 0;
        });
    }

    private void OnUiModeRadioChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressModeRadio) return;

        var mode = ModeNoviceRadio.IsChecked == true ? UiModeOption.Novice : UiModeOption.Developer;
        _settings.UiMode = mode.ToString();
        SettingsStore.Save(_settings);
        _owner.ApplyUiMode(mode);
        Logger.Info($"界面模式: {(mode == UiModeOption.Novice ? "新手" : "开发者")}");
        ShowSaveHint(mode == UiModeOption.Novice ? "已切换到新手模式" : "已切换到开发者模式");
    }

    // ---------- 端口设置 ----------

    private void OnPortChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressPortChanged) return;

        _settings.MulticastPort = (int)MulticastPortBox.Value;
        _settings.ControlPort = (int)ControlPortBox.Value;
        _settings.SessionPort = (int)SessionPortBox.Value;
        SettingsStore.Save(_settings);
        Logger.Info($"端口设置已更新: 组播={_settings.MulticastPort} 控制={_settings.ControlPort} 会话={_settings.SessionPort} (0=默认)");
        ShowSaveHint("端口设置已保存");
    }

    private void OnResetPortsClick(object sender, RoutedEventArgs e)
    {
        _suppressPortChanged = true;
        MulticastPortBox.Value = 0;
        ControlPortBox.Value = 0;
        SessionPortBox.Value = 0;
        _suppressPortChanged = false;

        _settings.MulticastPort = 0;
        _settings.ControlPort = 0;
        _settings.SessionPort = 0;
        SettingsStore.Save(_settings);
        Logger.Info("端口设置已恢复默认");
        ShowSaveHint("已恢复默认端口");
    }

    // ---------- 假屏图管理 ----------

    /// <summary>上传假屏图: 选择图片 -> 复制为 screen.png -> 通知 DLL 重载</summary>
    private async void OnUploadScreenClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_owner.GetWindowHandle());
            var picker = new WapPickers.FileOpenPicker(windowId)
            {
                SuggestedStartLocation = WapPickers.PickerLocationId.PicturesLibrary,
                ViewMode = WapPickers.PickerViewMode.List,
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");

            var result = await picker.PickSingleFileAsync();
            if (result == null || string.IsNullOrEmpty(result.Path)) return;

            string src = result.Path;
            string dst = Path.Combine(_dllDir, "screen.png");
            File.Copy(src, dst, true);
            Logger.Success($"假屏图已替换: {src}");
            ShowSaveHint("假屏图已替换");

            // 通知 DLL 重载
            _reloadClient.SendCommand("SCREEN_RELOAD");
            if (!_reloadClient.IsConnected)
                Logger.Warning("管道未连接, 假屏将在下次注入/连接时生效");
        }
        catch (Exception ex)
        {
            Logger.Error($"上传假屏图失败: {ex.Message}");
        }
    }

    private void OnAutoReloadScreenToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressScreenToggle) return;
        _hook.AutoReloadScreen = AutoReloadScreenSwitch.IsOn;
        SettingsStore.Save(_settings);
        Logger.Info($"自动监测假屏图替换: {(AutoReloadScreenSwitch.IsOn ? "开" : "关")}");
    }

    /// <summary>检查 screen.png 是否被外部替换, 变化则通知 DLL 重载 (轮询 2s)</summary>
    private void CheckScreenPngChanged()
    {
        if (!_hook.AutoReloadScreen) return;

        try
        {
            string path = Path.Combine(_dllDir, "screen.png");
            if (!File.Exists(path)) return;

            var fi = new FileInfo(path);
            long size = fi.Length;
            DateTime wt = fi.LastWriteTimeUtc;

            if (_lastScreenPngSize >= 0 && (size != _lastScreenPngSize || wt != _lastScreenPngWrite))
            {
                Logger.Info("检测到 screen.png 变化, 通知 DLL 重载假屏");
                _reloadClient.SendCommand("SCREEN_RELOAD");
            }
            _lastScreenPngSize = size;
            _lastScreenPngWrite = wt;
        }
        catch { /* 文件被占用等瞬时错误忽略 */ }
    }
}
