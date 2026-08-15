using JiYuHelper.Core;
using JiYuHelper.Models;
using JiYuHelper.Views;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.UI;

namespace JiYuHelper;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, object> _pages = new();
    private readonly AppSettings _settings;

    public MainWindow()
    {
        InitializeComponent();

        // 加载持久化设置
        _settings = SettingsStore.Load();
        Title = _settings.WindowTitle;

        // 侧边栏拖拽手柄: pane 展开时显示, 跟随 OpenPaneLength 定位
        MainNav.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (s, dp) =>
        {
            PaneGrip.Visibility = MainNav.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
            UpdateGripPosition();
        });
        MainNav.Loaded += (_, _) =>
        {
            PaneGrip.Visibility = MainNav.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
            UpdateGripPosition();
        };

        // 启动时直接应用用户设置的主题 (无动画, 避免"从系统主题闪切"的不自然感)
        ThemeManager.Current = SettingsStore.ParseTheme(_settings.Theme);
        SwitchThemeCore(ThemeManager.Current);

        // 初始化界面模式 (页面构造时各自 ApplyToPage)
        UiModeManager.Apply(SettingsStore.ParseUiMode(_settings.UiMode));

        // 默认导航到发现页
        MainNav.SelectedItem = (NavigationViewItem)MainNav.MenuItems[0];
        NavigateTo("discover");

        Logger.Info("JiYuHelper 已启动");
        Logger.Info("目标: 极域课堂管理系统教师端 v6.0 (CMPC)");
        Logger.Warning("仅供授权环境下的安全研究使用");

        // 首次启动弹出免责声明 (需等 XamlRoot 就绪, 故在内容 Loaded 后弹出)
        if (!_settings.DisclaimerAccepted)
            ContentFrame.Loaded += OnContentLoadedForDisclaimer;
    }

    private void OnContentLoadedForDisclaimer(object sender, RoutedEventArgs e)
    {
        ContentFrame.Loaded -= OnContentLoadedForDisclaimer; // 只弹一次
        _ = ShowDisclaimerAsync();
    }

    /// <summary>启动免责声明 (接受后写入配置, 不再弹出)</summary>
    private async System.Threading.Tasks.Task ShowDisclaimerAsync()
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = ContentFrame.XamlRoot,
                RequestedTheme = ThemeManager.ToElementTheme(),
                Title = "免责声明",
                CloseButtonText = "拒绝",
                PrimaryButtonText = "我接受",
                DefaultButton = ContentDialogButton.Primary,
                Content = new ScrollViewer
                {
                    MaxHeight = 420,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 22,
                        Text =
                            "本软件 (JiYuHelper) 仅供网络安全研究与教育用途。\n\n" +
                            "使用本软件前，请仔细阅读以下条款：\n\n" +
                            "1. 本软件仅可用于您拥有合法授权的网络环境、设备或系统上进行安全评估、漏洞研究与教学演示。\n\n" +
                            "2. 严禁将本软件用于任何未经授权的攻击、入侵、破坏、干扰他人网络或设备的行为，包括但不限于：\n" +
                            "   - 攻击学校、企业、政府或其他组织的教学网络；\n" +
                            "   - 干扰或破坏他人的正常教学活动；\n" +
                            "   - 窃取、篡改、破坏任何未授权数据；\n" +
                            "   - 其他违反中华人民共和国法律法规的行为。\n\n" +
                            "3. 使用本软件产生的任何直接或间接后果（包括但不限于法律责任、经济损失、设备损坏、学业影响等）均由使用者本人承担，作者不承担任何责任。\n\n" +
                            "4. 您必须确保您已获得系统所有者的明确书面授权后方可进行测试，并应在测试完成后及时停止所有操作。\n\n" +
                            "5. 本软件仅用于提升网络安全意识与防护能力。如果您发现相关系统存在漏洞，请及时通知系统管理者或相关厂商进行修复，切勿恶意利用。\n\n" +
                            "点击「我接受」即表示您已阅读并同意以上全部条款。"
                    }
                }
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _settings.DisclaimerAccepted = true;
                SettingsStore.Save(_settings);
                Logger.Success("免责声明已接受");

                // 首次运行: 免责声明后引导选择界面模式 (新手/开发者)
                if (string.IsNullOrEmpty(_settings.UiMode))
                    await ShowModePickerAsync();
            }
            else
            {
                // 拒绝即退出程序
                Logger.Warning("免责声明未接受, 程序即将退出");
                Close();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("免责声明弹出失败: " + ex.Message);
        }
    }

    /// <summary>首次运行引导: 选择界面模式 (新手模式隐藏技术说明 / 开发者模式显示全部)</summary>
    private async System.Threading.Tasks.Task ShowModePickerAsync()
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = ContentFrame.XamlRoot,
                RequestedTheme = ThemeManager.ToElementTheme(),
                Title = "选择界面模式",
                PrimaryButtonText = "新手模式",
                SecondaryButtonText = "开发者模式",
                DefaultButton = ContentDialogButton.Primary,
                Content = new ScrollViewer
                {
                    MaxHeight = 260,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 22,
                        Text =
                            "选择适合你的界面模式，之后可在「设置」页随时切换。\n\n" +
                            "【新手模式】\n" +
                            "界面简洁，隐藏技术细节说明，适合快速上手使用主要功能。\n\n" +
                            "【开发者模式】\n" +
                            "显示全部技术说明（协议细节、Hook 原理、参数含义等），适合安全研究与深度使用。"
                    }
                }
            };

            var result = await dialog.ShowAsync();
            var mode = result switch
            {
                ContentDialogResult.Primary => UiModeOption.Novice,
                _ => UiModeOption.Developer,
            };

            _settings.UiMode = mode.ToString();
            SettingsStore.Save(_settings);
            ApplyUiMode(mode);
            Logger.Info($"界面模式: {(mode == UiModeOption.Novice ? "新手" : "开发者")}");
        }
        catch (Exception ex)
        {
            Logger.Error("界面模式选择失败: " + ex.Message);
        }
    }

    /// <summary>应用界面模式: 设置全局状态并广播到所有缓存页面刷新 (由启动引导/设置页调用)</summary>
    public void ApplyUiMode(UiModeOption option)
    {
        UiModeManager.Apply(option);
        foreach (var page in _pages.Values)
        {
            if (page is Microsoft.UI.Xaml.Controls.Page p)
                UiModeManager.ApplyToPage(p);
        }
    }

    /// <summary>设置窗口标题 (由设置页调用)</summary>
    public void SetWindowTitle(string title)
    {
        Title = title;
    }

    /// <summary>获取窗口句柄 (供文件选择器等 WinRT 控件初始化用)</summary>
    public IntPtr GetWindowHandle()
    {
        return WinRT.Interop.WindowNative.GetWindowHandle(this);
    }

    // ---------- 侧边栏宽度拖拽 ----------

    private const double PaneMinWidth = 160;
    private const double PaneMaxWidth = 400;
    private bool _gripDragging;

    /// <summary>手柄位置跟随 pane 宽度 (重叠在右边缘)</summary>
    private void UpdateGripPosition()
    {
        PaneGrip.Margin = new Thickness(MainNav.OpenPaneLength - 3, 0, 0, 0);
    }

    private void OnNavSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateGripPosition();
    }

    private void OnGripPressed(object sender, PointerRoutedEventArgs e)
    {
        _gripDragging = true;
        PaneGrip.CapturePointer(e.Pointer);
    }

    private void OnGripMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_gripDragging) return;

        var pt = e.GetCurrentPoint(MainNav);
        double w = Math.Clamp(pt.Position.X, PaneMinWidth, PaneMaxWidth);
        MainNav.OpenPaneLength = w;
        UpdateGripPosition();
    }

    private void OnGripReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_gripDragging) return;
        _gripDragging = false;
        try { PaneGrip.ReleasePointerCapture(e.Pointer); } catch { }
    }

    /// <summary>应用主题 (由设置页调用), 同步切换, 无过渡动画</summary>
    public void ApplyTheme(ThemeOption option)
    {
        SwitchThemeCore(option);
    }

    private ResourceDictionary? _blueThemeDict;

    // 窗口底色 (不依赖 Application.RequestedTheme, 后者运行时不可修改)
    private static readonly Brush LightWindowBrush =
        new SolidColorBrush(Color.FromArgb(255, 243, 243, 243));
    private static readonly Brush DarkWindowBrush =
        new SolidColorBrush(Color.FromArgb(255, 32, 32, 32));

    private void SwitchThemeCore(ThemeOption option)
    {
        // 同步全局主题状态 (供对话框同步)
        ThemeManager.Current = option;

        // 先移除蓝色主题资源字典 (若存在), 触发控件重新求值
        if (_blueThemeDict != null && MainNav.Resources.MergedDictionaries.Contains(_blueThemeDict))
            MainNav.Resources.MergedDictionaries.Remove(_blueThemeDict);

        // 主题应用到窗口根元素 (内容树整体跟随);
        // 窗口底色由根 Grid 显式绘制 (Light/Dark 纯色盖住 Mica, System 露出 Mica)
        switch (option)
        {
            case ThemeOption.System:
                RootGrid.RequestedTheme = ElementTheme.Default;
                RootGrid.Background = null;
                break;
            case ThemeOption.Light:
                RootGrid.RequestedTheme = ElementTheme.Light;
                RootGrid.Background = LightWindowBrush;
                break;
            case ThemeOption.Dark:
                RootGrid.RequestedTheme = ElementTheme.Dark;
                RootGrid.Background = DarkWindowBrush;
                break;
            case ThemeOption.Blue:
                // 蓝色主题: 强制浅色 + 蓝色资源覆盖 + 渐变底色
                RootGrid.RequestedTheme = ElementTheme.Light;
                ApplyBlueTheme();
                break;
        }
    }

    /// <summary>
    /// 蓝色主题: 通过 MergedDictionaries 增删带 ThemeDictionaries 的资源字典实现。
    /// 注意: ThemeResource 引用只解析 ThemeDictionaries, 普通字典里的同名 key 无效,
    ///       因此覆盖值必须放入 ThemeDictionaries (Light/Dark 各一份)。
    /// </summary>
    private void ApplyBlueTheme()
    {
        if (_blueThemeDict == null)
            _blueThemeDict = BuildBlueThemeDictionary();

        if (!MainNav.Resources.MergedDictionaries.Contains(_blueThemeDict))
            MainNav.Resources.MergedDictionaries.Add(_blueThemeDict);

        RootGrid.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(255, 232, 243, 252), Offset = 0 },
                new GradientStop { Color = Color.FromArgb(255, 214, 234, 250), Offset = 1 },
            }
        };
    }

    private static ResourceDictionary BuildBlueThemeDictionary()
    {
        var blue = Color.FromArgb(255, 13, 110, 189);
        var blueLight = Color.FromArgb(255, 46, 140, 210);
        var paneBlue = new SolidColorBrush(Color.FromArgb(255, 214, 232, 248));
        var cardBlue = new SolidColorBrush(Color.FromArgb(255, 246, 251, 255));

        // 浅色主题字典 (蓝色主题强制 Light, 正常解析到这份)
        var light = new ResourceDictionary
        {
            ["SystemAccentColor"] = blue,
            ["AccentFillColorDefaultBrush"] = new SolidColorBrush(blue),
            ["AccentFillColorSecondaryBrush"] = new SolidColorBrush(blueLight),
            ["AccentTextFillColorPrimaryBrush"] = new SolidColorBrush(Colors.White),
            ["AccentButtonBackground"] = new SolidColorBrush(blue),
            ["AccentButtonBackgroundPointerOver"] = new SolidColorBrush(blueLight),
            ["AccentButtonBackgroundPressed"] = new SolidColorBrush(blue),
            ["NavigationViewDefaultPaneBackground"] = paneBlue,
            ["NavigationViewExpandedPaneBackground"] = paneBlue,
            ["LayerFillColorDefaultBrush"] = paneBlue,
            ["CardBackgroundFillColorDefaultBrush"] = cardBlue,
            ["LayerOnAcrylicFillColorDefaultBrush"] = cardBlue,
            ["AcrylicInAppFillColorDefaultBrush"] = cardBlue,
        };

        // 深色主题字典 (蓝色主题下系统为深色时, 部分浮层仍可能解析 Dark)
        var dark = new ResourceDictionary
        {
            ["SystemAccentColor"] = blueLight,
            ["AccentFillColorDefaultBrush"] = new SolidColorBrush(blueLight),
            ["AccentFillColorSecondaryBrush"] = new SolidColorBrush(blue),
            ["AccentTextFillColorPrimaryBrush"] = new SolidColorBrush(Colors.White),
            ["AccentButtonBackground"] = new SolidColorBrush(blueLight),
            ["AccentButtonBackgroundPointerOver"] = new SolidColorBrush(blue),
            ["AccentButtonBackgroundPressed"] = new SolidColorBrush(blueLight),
            ["NavigationViewDefaultPaneBackground"] = new SolidColorBrush(Color.FromArgb(255, 30, 40, 55)),
            ["NavigationViewExpandedPaneBackground"] = new SolidColorBrush(Color.FromArgb(255, 30, 40, 55)),
            ["LayerFillColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(255, 30, 40, 55)),
            ["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(255, 38, 50, 68)),
            ["LayerOnAcrylicFillColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(255, 38, 50, 68)),
            ["AcrylicInAppFillColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(255, 38, 50, 68)),
        };

        var dict = new ResourceDictionary();
        dict.ThemeDictionaries["Light"] = light;
        dict.ThemeDictionaries["Dark"] = dark;
        return dict;
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            NavigateTo(tag);
    }

    private void NavigateTo(string tag)
    {
        switch (tag)
        {
            case "discover":
                EnsurePage(tag, () => new DiscoverPage());
                break;
            case "control":
                EnsurePage(tag, () => new HookPage());
                break;
            case "attack":
                EnsurePage(tag, () => new AttackPage());
                SyncAttackTarget();
                break;
            case "vuln":
                EnsurePage(tag, () => new VulnerabilityPage());
                SyncVulnTarget();
                break;
            case "log":
                EnsurePage(tag, () => new LogPage());
                break;
            case "help":
                EnsurePage(tag, () => new HelpPage());
                break;
            case "settings":
                EnsurePage(tag, () => new SettingsPage(this));
                break;
        }

        if (_pages.TryGetValue(tag, out var page) && page is Page p)
            ContentFrame.Content = p;
    }

    /// <summary>
    /// 从发现页选中项同步攻击页目标 (无选中则清空)
    /// </summary>
    private void SyncAttackTarget()
    {
        if (_pages.TryGetValue("discover", out var dpObj) && dpObj is DiscoverPage dp &&
            _pages.TryGetValue("attack", out var apObj) && apObj is AttackPage ap)
        {
            string? ip = dp.GetSelectedTeacherIP();
            if (!string.IsNullOrEmpty(ip))
                ap.SetTargetFromList(ip);
            else
                ap.ClearTarget();
        }
    }

    /// <summary>
    /// 从发现页选中项同步漏洞页目标 (无选中则清空)
    /// </summary>
    private void SyncVulnTarget()
    {
        if (_pages.TryGetValue("discover", out var dpObj) && dpObj is DiscoverPage dp &&
            _pages.TryGetValue("vuln", out var vpObj) && vpObj is VulnerabilityPage vp)
        {
            string? ip = dp.GetSelectedTeacherIP();
            if (!string.IsNullOrEmpty(ip))
                vp.SetTargetFromList(ip);
            else
                vp.ClearTarget();
        }
    }

    private void EnsurePage(string tag, Func<object> factory)
    {
        if (_pages.ContainsKey(tag)) return;

        var page = factory();
        if (page is AttackPage ap)
        {
            ap.NavigateToDiscover += _ =>
            {
                SelectNavItem("discover");
                SyncAttackTarget();
            };
        }
        if (page is VulnerabilityPage vp)
        {
            vp.NavigateToDiscover += _ =>
            {
                SelectNavItem("discover");
                SyncVulnTarget();
            };
        }
        _pages[tag] = page;
    }

    private void SelectNavItem(string tag)
    {
        foreach (var item in MainNav.MenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag?.ToString() == tag)
            {
                MainNav.SelectedItem = nvi;
                break;
            }
        }

        foreach (var item in MainNav.FooterMenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag?.ToString() == tag)
            {
                MainNav.SelectedItem = nvi;
                break;
            }
        }

        if (tag == "attack")
            SyncAttackTarget();
        if (tag == "vuln")
            SyncVulnTarget();
    }
}
