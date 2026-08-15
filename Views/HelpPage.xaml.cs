using JiYuHelper.Core;
using Microsoft.UI.Xaml.Controls;

namespace JiYuHelper.Views;

public sealed partial class HelpPage : Page
{
    public HelpPage()
    {
        InitializeComponent();
        UiModeManager.Attach(this);
    }
}
