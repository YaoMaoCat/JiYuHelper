using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;

namespace JiYuHelper.Core;

/// <summary>
/// 界面模式管理: 新手模式隐藏各页 Tag="tech" 的技术细节说明块。
///
/// XAML 约定: 需要按模式隐藏的技术描述 TextBlock 加 Tag="tech",
/// 页面构造/模式切换时调用 <see cref="ApplyToPage"/> 统一刷新。
/// 新手模式: 技术块折叠; 开发者模式: 全部显示 (默认)。
/// </summary>
public static class UiModeManager
{
    /// <summary>当前界面模式 (默认开发者)</summary>
    public static UiModeOption Current { get; private set; } = UiModeOption.Developer;

    public static bool IsNovice => Current == UiModeOption.Novice;
    public static bool IsDeveloper => Current == UiModeOption.Developer;

    /// <summary>模式切换时触发 (页面订阅后调用 ApplyToPage 刷新)</summary>
    public static event Action? ModeChanged;

    /// <summary>设置当前模式并广播 (由主窗口调用, 通知所有缓存页面刷新)</summary>
    public static void Apply(UiModeOption option)
    {
        if (Current == option) return;
        Current = option;
        ModeChanged?.Invoke();
    }

    /// <summary>
    /// 页面接入: 在 Loaded 时应用模式 (视觉树此时已连接)。
    /// 注意: VisualTreeHelper 的父子关系在页面加载/布局后才建立,
    /// InitializeComponent 刚完成时遍历会拿到 0 个子节点, 因此不在构造期遍历。
    /// </summary>
    public static void Attach(Page page)
    {
        page.Loaded += (_, _) => ApplyToPage(page);
    }

    /// <summary>按当前模式刷新页面内标记块:
    /// Tag="tech"       → 技术细节, 新手模式隐藏
    /// Tag="novice"     → 新手引导, 仅新手模式显示
    /// Tag="name-&lt;key&gt;" → 名称替换, 新手模式显示通俗名 (见 NoviceNames), 开发者模式恢复原名</summary>
    public static void ApplyToPage(Page page)
    {
        bool novice = IsNovice;
        foreach (var el in FindDescendants<FrameworkElement>(page))
        {
            if (el.Tag is string tag)
            {
                if (tag == "tech")
                    el.Visibility = novice ? Visibility.Collapsed : Visibility.Visible;
                else if (tag == "novice")
                    el.Visibility = novice ? Visibility.Visible : Visibility.Collapsed;
                else if (tag.StartsWith("name-", StringComparison.Ordinal))
                    ApplyNoviceName(el, tag[5..], novice);
            }
        }
    }

    /// <summary>名称替换: 新手模式把技术名词换成通俗名, 开发者模式恢复原名 (原名缓存于首次切换)。
    /// 支持 TextBlock.Text / ToggleSwitch.Header / RadioButton.Content (字符串内容)。</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, string>
        OriginalNames = new();

    private static void ApplyNoviceName(FrameworkElement el, string key, bool novice)
    {
        if (!TryGetDisplayString(el, out var current)) return;

        if (novice)
        {
            if (!OriginalNames.TryGetValue(el, out _))
                OriginalNames.Add(el, current);
            if (NoviceNames.TryGetValue(key, out var simple))
                SetDisplayString(el, simple);
        }
        else
        {
            if (OriginalNames.TryGetValue(el, out var original))
                SetDisplayString(el, original);
        }
    }

    private static bool TryGetDisplayString(FrameworkElement el, out string value)
    {
        switch (el)
        {
            case TextBlock tb:
                value = tb.Text;
                return true;
            case ToggleSwitch ts when ts.Header is string s:
                value = s;
                return true;
            case RadioButton rb when rb.Content is string s:
                value = s;
                return true;
            default:
                value = "";
                return false;
        }
    }

    private static void SetDisplayString(FrameworkElement el, string text)
    {
        switch (el)
        {
            case TextBlock tb:
                tb.Text = text;
                break;
            case ToggleSwitch ts:
                ts.Header = text;
                break;
            case RadioButton rb:
                rb.Content = text;
                break;
        }
    }

    /// <summary>通俗名称映射: key = XAML 里 Tag="name-&lt;key&gt;" 的键</summary>
    public static readonly Dictionary<string, string> NoviceNames = new()
    {
        // 控制页: 远程控制拦截
        ["remote-input"] = "禁止教师输入",
        ["input-lock"] = "教室端无法锁定你的输入",
        ["proc-guard"] = "保护本机进程",
        ["proc-hook-guard"] = "禁止结束本机程序",
        ["filter-guard"] = "禁用 USB/光驱放行",
        ["net-sim"] = "教师端无法禁用你的网络",
        // 控制页: 界面与进程
        ["topmost-strip"] = "去掉窗口置顶",
        ["focus-lock"] = "禁止抢焦点",
        ["app-list"] = "隐藏本机程序列表",
        ["proc-list"] = "隐藏本机进程列表",
        // 控制页: 屏幕监控
        ["screen-fake"] = "教师端看你的屏幕看到的是假画面（需要在设置中上传假屏图）",
        ["screen-cap"] = "教师端截取或者录制你的屏幕是假画面",
        ["black-monitor"] = "自动退出黑屏",
        // 控制页: 输入
        ["keyboard-bypass"] = "禁止记录键盘",
        // 控制页: 进程状态行
        ["proc-main"] = "StudentMain.exe（极域主进程）",
        ["proc-master"] = "MasterHelper.exe（极域功能进程）",
        // 控制页: 注入按钮
        ["btn-inject"] = "开始控制极域",
        ["btn-uninject"] = "停止",
        ["auto-reinject"] = "控制失败时自动重新控制",
        // 分组标题
        ["group-remote"] = "防止教师远程控制",
        ["group-ui"] = "窗口与列表隐藏",
        ["group-screen"] = "屏幕保护",
        ["group-input"] = "输入保护",
        // 扫描页: 扫描方式
        ["scan-multicast"] = "方式一：组播自动发现",
        ["scan-subnet"] = "方式二：扫描整个网段",
        // 设置页: 配置导入导出 / 假屏图
        ["export-desc"] = "导出当前设置为 JSON 文件，或从 JSON 文件恢复",
        ["screen-desc"] = "上传一张图片，「控制」页开启「伪造屏幕」后，教师端看到你的屏幕就是这张图",
    };

    /// <summary>深度优先遍历视觉树的子元素 (需在页面 Loaded 后调用)</summary>
    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var deeper in FindDescendants<T>(child))
                yield return deeper;
        }
    }
}
