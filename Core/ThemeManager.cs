namespace JiYuHelper.Core;

/// <summary>
/// 全局主题状态: 供弹窗/对话框同步应用主题
/// (ContentDialog 挂在 XamlRoot 上, 不继承 MainNav.RequestedTheme, 需显式设置)
/// </summary>
public static class ThemeManager
{
    public static ThemeOption Current { get; set; } = ThemeOption.System;

    /// <summary>将当前主题映射为 ElementTheme (供对话框 RequestedTheme 使用)</summary>
    public static Microsoft.UI.Xaml.ElementTheme ToElementTheme()
    {
        return Current switch
        {
            ThemeOption.Dark => Microsoft.UI.Xaml.ElementTheme.Dark,
            ThemeOption.Light or ThemeOption.Blue => Microsoft.UI.Xaml.ElementTheme.Light,
            _ => Microsoft.UI.Xaml.ElementTheme.Default,
        };
    }
}
