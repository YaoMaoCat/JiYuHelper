using JiYuHelper.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Text;
using Windows.ApplicationModel.DataTransfer;

namespace JiYuHelper.Views;

public sealed partial class LogPage : Page
{
    private readonly ObservableCollection<LogEntry> _entries = new();
    private readonly List<LogEntry> _pending = new();   // 待 UI 批量刷新的日志
    private bool _flushScheduled;
    private bool _autoScroll = true;

    public LogPage()
    {
        InitializeComponent();
        LogItems.ItemsSource = _entries;

        // 默认勾选自动滚动 (启动即生效)
        AutoScrollCheck.IsChecked = true;

        // 先回放历史日志, 再订阅实时事件 (解决日志页晚创建导致日志丢失)
        foreach (var entry in Logger.GetHistory())
            _entries.Add(entry);

        Logger.EntryAdded += OnEntryAdded;

        // 每次页面显示时 (含重新切回日志页) 若自动滚动则回到底部
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_autoScroll && LogScrollViewer != null)
        {
            // 延迟到布局完成后再滚动, 否则 ScrollableHeight 可能还是旧值
            DispatcherQueue.TryEnqueue(() =>
                LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null));
        }
    }

    /// <summary>
    /// 日志事件合并: 先入待处理队列, 再由 DispatcherQueue 每帧批量刷新一次。
    /// (DLL 注入/补发时可能一次到达数百条, 逐条 TryEnqueue 会导致 UI 刷新过慢,
    ///  看起来"每次只更新 1 条旧日志")
    /// </summary>
    private void OnEntryAdded(LogEntry entry)
    {
        lock (_pending)
        {
            _pending.Add(entry);
            if (!_flushScheduled)
            {
                _flushScheduled = true;
                DispatcherQueue.TryEnqueue(FlushPending);
            }
        }
    }

    private void FlushPending()
    {
        List<LogEntry> batch;
        lock (_pending)
        {
            _flushScheduled = false;
            if (_pending.Count == 0) return;
            batch = new List<LogEntry>(_pending);
            _pending.Clear();
        }

        foreach (var entry in batch)
        {
            _entries.Add(entry);

            // 防止内存无限增长
            while (_entries.Count > 2000)
                _entries.RemoveAt(0);
        }

        if (_autoScroll && batch.Count > 0)
            LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
    }

    /// <summary>复制全部日志到剪贴板 (便于反馈问题)</summary>
    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_entries.Count == 0)
        {
            Logger.Warning("没有可复制的日志");
            return;
        }

        try
        {
            var sb = new StringBuilder();
            foreach (var entry in _entries)
                sb.AppendLine(entry.Display);

            var data = new DataPackage();
            data.SetText(sb.ToString());
            Clipboard.SetContent(data);
            Logger.Success($"已复制 {_entries.Count} 条日志到剪贴板");
        }
        catch (Exception ex)
        {
            Logger.Error($"复制日志失败: {ex.Message}");
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _entries.Clear();
    }

    private void OnAutoScrollChanged(object sender, RoutedEventArgs e)
    {
        if (LogScrollViewer == null) return; // XAML 初始化期间 Checked 会提前触发
        _autoScroll = AutoScrollCheck.IsChecked == true;

        // 勾选后立即滚到底部
        if (_autoScroll)
            LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
    }
}
