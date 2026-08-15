using JiYuHelper.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace JiYuHelper.Views;

public sealed partial class AttackPage : Page
{
    private readonly AttackEngine _engine = new();
    private readonly System.Threading.Timer? _statsTimer;
    private readonly Stopwatch _attackSw = new();
    private double _duration = 60;
    private string? _currentTarget;

    public event Action<string>? NavigateToDiscover;

    public AttackPage()
    {
        InitializeComponent();
        UiModeManager.Attach(this);
        _engine.CrashDetected += OnCrashDetected;
        _engine.Stopped += OnEngineStopped;

        _statsTimer = new System.Threading.Timer(_ =>
        {
            DispatcherQueue.TryEnqueue(RefreshStats);
        }, null, 500, 500);
    }

    private void OnEngineStopped()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_engine.IsRunning) return;

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            AttackStateText.Text = "攻击已结束";
            AttackProgress.Value = 0;
        });
    }

    private AttackMode GetSelectedMode()
    {
        if (ModeFileSubmit.IsChecked == true) return AttackMode.FileSubmitCrash;
        if (ModeAnswerSheet.IsChecked == true) return AttackMode.AnswerSheetCrash;
        if (ModeUpfb.IsChecked == true) return AttackMode.UpfbUploadCrash;
        // 其余模式已从 UI 隐藏, 代码保留待后续恢复
        return AttackMode.FileSubmitCrash;
    }

    private void OnPickFromListClick(object sender, RoutedEventArgs e)
    {
        NavigateToDiscover?.Invoke("attack");
    }

    public void SetTargetFromList(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return;

        _currentTarget = ip;
        TargetIpText.Text = ip;
        TargetSourceText.Text = "来自「扫描」页列表";
    }

    public void ClearTarget()
    {
        _currentTarget = null;
        TargetIpText.Text = "未选择目标";
        TargetSourceText.Text = "请在「扫描」页扫描并选中教师机";
    }

    public void StartAttack(string? targetIP = null)
    {
        if (_engine.IsRunning) return;

        if (!string.IsNullOrEmpty(targetIP))
            SetTargetFromList(targetIP);

        string ip = _currentTarget ?? "";
        if (string.IsNullOrEmpty(ip))
        {
            Logger.Warning("未选择攻击目标");
            _ = ShowNoTargetDialogAsync();
            return;
        }

        _duration = DurationBox.Value;
        // 控制端口来自设置 (0=默认 4806)
        _engine.Port = SettingsStore.Load().ControlPort;
        _engine.Start(ip, GetSelectedMode(), (int)ThreadCountBox.Value, (int)_duration);

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        _attackSw.Restart();
        AttackStateText.Text = $"攻击中: {AttackEngine.ModeName(GetSelectedMode())} @ {ip}";
    }

    /// <summary>未选择目标时的提示框 (与免责声明同风格)</summary>
    private async System.Threading.Tasks.Task ShowNoTargetDialogAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ThemeManager.ToElementTheme(),
            Title = "请选择攻击目标",
            CloseButtonText = "取消",
            PrimaryButtonText = "去选择目标",
            DefaultButton = ContentDialogButton.Primary,
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "尚未选择目标教师机。\n\n请先在「扫描」页扫描教师机，并在列表中选中目标，然后返回本页开始攻击。"
            }
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            NavigateToDiscover?.Invoke("attack");
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => StartAttack();

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        _engine.Stop();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        AttackStateText.Text = "已停止";
        AttackProgress.Value = 0;
    }

    private void OnCrashDetected(AttackMode mode, string ip)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AttackStateText.Text = $"检测到崩溃: {AttackEngine.ModeName(mode)} @ {ip} (可停止攻击)";
        });
    }

    private void RefreshStats()
    {
        StatPackets.Text = _engine.Stats.PacketsSent.ToString("N0");
        StatBytes.Text = FormatBytes(_engine.Stats.BytesSent);
        StatCrashes.Text = _engine.Stats.Crashes.ToString("N0");
        StatThreads.Text = _engine.Stats.ActiveThreads.ToString();

        if (_engine.IsRunning && _duration > 0)
        {
            double pct = Math.Min(100, _attackSw.Elapsed.TotalSeconds / _duration * 100);
            AttackProgress.Value = pct;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:0.00} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes} B";
    }
}
