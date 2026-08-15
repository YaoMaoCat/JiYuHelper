using JiYuHelper.Core;
using JiYuHelper.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Linq;
using System.Net;
using Windows.System;

namespace JiYuHelper.Views;

public sealed partial class DiscoverPage : Page
{
    public TeacherDiscoverer Discoverer { get; } = new();

    public DiscoverPage()
    {
        InitializeComponent();
        UiModeManager.Attach(this);
        Discoverer.UiDispatcher = DispatcherQueue;
        Discoverer.StateChanged += OnDiscovererStateChanged;
        Discoverer.ProgressChanged += OnDiscovererProgressChanged;
    }

    private void OnDiscovererStateChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ScanButton.IsEnabled = !Discoverer.IsRunning;
            StopScanButton.IsEnabled = Discoverer.IsRunning;
            ModeMulticast.IsEnabled = !Discoverer.IsRunning;
            ModeSubnet.IsEnabled = !Discoverer.IsRunning;
            StatusText.Text = Discoverer.IsRunning
                ? "扫描中 ..."
                : $"发现 {Discoverer.Teachers.Count} 台教师机";
        });
    }

    private void OnDiscovererProgressChanged(string text)
    {
        DispatcherQueue.TryEnqueue(() => StatusText.Text = text);
    }

    private ScanMode GetSelectedMode()
    {
        return ModeSubnet.IsChecked == true ? ScanMode.SubnetScan : ScanMode.Multicast;
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (StatusText == null) return; // XAML 初始化期间 Checked 会提前触发
        StatusText.Text = GetSelectedMode() == ScanMode.SubnetScan
            ? "网段扫描: 枚举本机网段, TCP 4806 + WORB 验证 (推荐)"
            : "组播扫描: 监听 224.50.50.42:4988 (部分网络不可用)";
    }

    private async void OnScanClick(object sender, RoutedEventArgs e)
    {
        // 组播端口来自设置 (不同版本极域端口不同, 0=默认 4988)
        Discoverer.MulticastPort = SettingsStore.Load().MulticastPort;
        await Discoverer.StartAsync(GetSelectedMode(), 15);
    }

    private async void OnStopScanClick(object sender, RoutedEventArgs e)
    {
        await Discoverer.StopAsync();
    }

    private void OnManualIpKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
            AddManualIp();
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        AddManualIp();
    }

    private void AddManualIp()
    {
        string input = ManualIpBox.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        if (!IPAddress.TryParse(input, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            Logger.Warning($"无效 IP: {input}");
            return;
        }

        string ipStr = ip.ToString();
        if (Discoverer.Teachers.Any(t => t.IP == ipStr))
        {
            Logger.Info($"教师机 {ipStr} 已在列表中");
            return;
        }

        Discoverer.Teachers.Add(new TeacherInfo { IP = ipStr, Source = "手动添加", PacketType = "MANUAL" });
        Logger.Success($"已添加教师机 {ipStr}");
        ManualIpBox.Text = "";
        StatusText.Text = $"发现 {Discoverer.Teachers.Count} 台教师机";
    }

    public string? GetSelectedTeacherIP()
    {
        return (TeacherList.SelectedItem as TeacherInfo)?.IP;
    }
}
