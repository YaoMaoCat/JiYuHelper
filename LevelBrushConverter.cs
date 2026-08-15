using JiYuHelper.Core;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace JiYuHelper;

/// <summary>
/// 将 LogLevel 转换为日志文本前景色:
///   Info    -> 中性灰
///   Success -> 绿
///   Attack  -> 橙 (攻击事件高亮)
///   Warning -> 黄
///   Error   -> 红
/// </summary>
public sealed class LevelBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InfoBrush =
        new(Color.FromArgb(0xFF, 0x9A, 0xA0, 0xA6));
    private static readonly SolidColorBrush SuccessBrush =
        new(Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush AttackBrush =
        new(Color.FromArgb(0xFF, 0xFF, 0x98, 0x00));
    private static readonly SolidColorBrush WarningBrush =
        new(Color.FromArgb(0xFF, 0xFF, 0xC1, 0x07));
    private static readonly SolidColorBrush ErrorBrush =
        new(Color.FromArgb(0xFF, 0xF4, 0x43, 0x36));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value switch
        {
            LogLevel.Success => SuccessBrush,
            LogLevel.Attack => AttackBrush,
            LogLevel.Warning => WarningBrush,
            LogLevel.Error => ErrorBrush,
            _ => InfoBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
