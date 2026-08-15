using JiYuHelper.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;

namespace JiYuHelper.Views;

/// <summary>
/// 「控制」页: 进程监控 / 注入卸载 / Hook 功能开关 / 管道事件接入全局日志。
/// 开关状态持久化到 AppSettings.Hook, 注入前由 HookConfigWriter 写入 hook.cfg。
/// </summary>
public sealed partial class HookPage : Page
{
    private readonly AppSettings _settings;
    private readonly HookSettings _hook;
    private readonly DispatcherTimer _pollTimer;
    private readonly JyHookClient _mainClient;
    private readonly JyHookClient _masterClient;

    private string _dllDir = "";
    private int _lastStudentPid;
    private int _lastMasterPid;
    private bool _stoppedMain;      // DLL 已加载但被 BypassStop 停用
    private bool _stoppedMaster;
    private DateTime _lastAutoReinjectAttempt = DateTime.MinValue;
    private bool _suppressToggle;

    /// <summary>目标进程是否已注入 (供管道自动连接判断)</summary>
    private static bool IsInjected(string exeName)
        => ProcessManager.FindProcesses(exeName).Any(p => p.IsInjected);

    public HookPage()
    {
        InitializeComponent();

        // 界面模式: 新手模式隐藏技术说明 + 通俗名称
        UiModeManager.Attach(this);

        _settings = SettingsStore.Load();
        _hook = _settings.Hook;

        // DLL 与 App 同目录分发
        _dllDir = AppContext.BaseDirectory;

        LoadSwitches();

        // 管道客户端: 后台自动连接循环 (与页面生命周期无关), 连接成功即同步功能掩码
        // 停用(_stopped)后不再尝试连接
        _mainClient = new JyHookClient(JyHookClient.MainPipeName, ProcessManager.StudentMainExe,
            () => IsInjected(ProcessManager.StudentMainExe) && !_stoppedMain);
        _masterClient = new JyHookClient(JyHookClient.MasterPipeName, ProcessManager.MasterHelperExe,
            () => IsInjected(ProcessManager.MasterHelperExe) && !_stoppedMaster);

        _mainClient.EventReceived += OnHookEvent;
        _masterClient.EventReceived += OnHookEvent;
        _mainClient.Connected += () =>
        {
            Logger.Success("已连接 StudentMain.exe 管道 (JYHookHelper)");
            DispatcherQueue.TryEnqueue(PushHotUpdate);
        };
        _masterClient.Connected += () =>
        {
            Logger.Success("已连接 MasterHelper.exe 管道 (JYMasterHelper)");
            DispatcherQueue.TryEnqueue(PushHotUpdate);
        };

        _mainClient.Start();
        _masterClient.Start();

        // 进程状态轮询 (2s): 状态卡片 + 自动重注入
        // 页面缓存常驻, 定时器不随 Unloaded 停止, 保证切页后仍监控
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();

        RefreshElevationHint();
    }

    // ---------- 开关绑定 ----------

    /// <summary>从持久化设置初始化各开关状态 (显式赋值, 编译期检查)</summary>
    private void LoadSwitches()
    {
        _suppressToggle = true;
        SwitchRemoteInput.IsOn = _hook.EnableRemoteInput;
        SwitchInputLock.IsOn = _hook.EnableInputLock;
        SwitchProcGuard.IsOn = _hook.EnableProcGuard;
        SwitchProcHookGuard.IsOn = _hook.EnableProcHookGuard;
        SwitchFilterGuard.IsOn = _hook.EnableFilterGuard;
        SwitchNetSim.IsOn = _hook.EnableNetSimBlock;
        SwitchTopmostStrip.IsOn = _hook.EnableTopmostStrip;
        SwitchFocusLock.IsOn = _hook.EnableFocusLock;
        SwitchAppList.IsOn = _hook.EnableAppList;
        SwitchProcList.IsOn = _hook.EnableProcList;
        SwitchScreenFake.IsOn = _hook.EnableScreenFake;
        SwitchScreenCap.IsOn = _hook.EnableScreenCap;
        SwitchBlackMonitor.IsOn = _hook.EnableBlackMonitor;
        SwitchKeyboardBypass.IsOn = _hook.EnableKeyboardBypass;
        AutoReinjectSwitch.IsOn = _hook.EnableAutoReinject;
        _suppressToggle = false;
    }

    /// <summary>任一功能开关变化: 写回设置 + 保存 + 更新 hook.cfg + 管道热更新</summary>
    private void OnFeatureToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle || sender is not ToggleSwitch ts || ts.Tag is not string field) return;

        var prop = typeof(HookSettings).GetProperty(field);
        prop?.SetValue(_hook, ts.IsOn);

        SaveHookSettings();
        HookConfigWriter.Write(_dllDir, _hook);

        // 已注入: 通过管道立即生效, 无需重新注入
        PushHotUpdate();
        Logger.Info($"功能开关已更新: {field}={ts.IsOn} (已热更新)");
    }

    /// <summary>向已注入的 DLL 推送当前功能掩码 (未注入/未连接时静默)</summary>
    private void PushHotUpdate()
    {
        string cmd = HookConfigWriter.BuildUpdateCommand(_hook);
        _mainClient.SendCommand(cmd);
        _masterClient.SendCommand(cmd);
    }

    private void OnAutoReinjectToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        _hook.EnableAutoReinject = AutoReinjectSwitch.IsOn;
        SaveHookSettings();
        Logger.Info($"自动重注入: {(AutoReinjectSwitch.IsOn ? "开" : "关")}");
    }

    private void SaveHookSettings()
    {
        SettingsStore.Save(_settings);
    }

    // ---------- 进程轮询 ----------

    private void OnPollTick(object? sender, object e)
    {
        var student = ProcessManager.FindProcesses(ProcessManager.StudentMainExe).FirstOrDefault();
        var master = ProcessManager.FindProcesses(ProcessManager.MasterHelperExe).FirstOrDefault();

        bool injectedChanged = false;
        bool anyInjected = false;

        // StudentMain
        if (student != null)
        {
            if (student.Pid != _lastStudentPid)
            {
                _lastStudentPid = student.Pid;
                if (_lastStudentPid != 0) injectedChanged = true;
                _stoppedMain = false; // 新进程实例, 停用标志重置
            }
            StudentMainStatusText.Text = student.IsInjected
                ? (_stoppedMain ? $"PID {student.Pid} (已停用)" : $"PID {student.Pid} (已注入)")
                : $"PID {student.Pid} (未注入)";
            SetDot(StudentMainDot, student.IsInjected ? (_stoppedMain ? DotGray : DotGreen) : DotYellow);
            anyInjected |= student.IsInjected;
        }
        else
        {
            StudentMainStatusText.Text = "未运行";
            SetDot(StudentMainDot, DotGray);
            _lastStudentPid = 0;
        }

        // MasterHelper
        if (master != null)
        {
            if (master.Pid != _lastMasterPid)
            {
                _lastMasterPid = master.Pid;
                if (_lastMasterPid != 0) injectedChanged = true;
                _stoppedMaster = false;
            }
            MasterHelperStatusText.Text = master.IsInjected
                ? (_stoppedMaster ? $"PID {master.Pid} (已停用)" : $"PID {master.Pid} (已注入)")
                : $"PID {master.Pid} (未注入)";
            SetDot(MasterHelperDot, master.IsInjected ? (_stoppedMaster ? DotGray : DotGreen) : DotYellow);
            anyInjected |= master.IsInjected;
        }
        else
        {
            MasterHelperStatusText.Text = "未运行";
            SetDot(MasterHelperDot, DotGray);
            _lastMasterPid = 0;
        }

        UninjectButton.IsEnabled = anyInjected;

        // 进程重启: 记录状态变化 (自动重注入与管道自动重连各自处理)
        if (injectedChanged)
        {
            Logger.Info("检测到极域进程重启");
        }

        TryAutoReinject(student, master);
    }

    private void TryAutoReinject(TargetProcessInfo? student, TargetProcessInfo? master)
    {
        if (!_hook.EnableAutoReinject) return;

        // 节流: 注入失败后 15s 内不重试
        if ((DateTime.Now - _lastAutoReinjectAttempt).TotalSeconds < 15) return;

        if (student != null && !student.IsInjected)
        {
            _lastAutoReinjectAttempt = DateTime.Now;
            Logger.Info("自动重注入: StudentMain.exe");
            DoInjectStudent(student.Pid);
        }
        if (master != null && !master.IsInjected)
        {
            _lastAutoReinjectAttempt = DateTime.Now;
            Logger.Info("自动重注入: MasterHelper.exe");
            DoInjectMaster(master.Pid);
        }
    }

    // ---------- 状态显示 ----------

    private static readonly SolidColorBrush DotGreen = new(Color.FromArgb(255, 90, 220, 140));
    private static readonly SolidColorBrush DotYellow = new(Color.FromArgb(255, 255, 200, 80));
    private static readonly SolidColorBrush DotGray = new(Color.FromArgb(255, 130, 130, 130));

    private static void SetDot(Microsoft.UI.Xaml.Shapes.Ellipse dot, SolidColorBrush brush) => dot.Fill = brush;

    private void RefreshElevationHint()
    {
        if (ProcessManager.IsRunningAsAdmin())
        {
            ElevationHintText.Text = "当前以管理员权限运行，可注入 SYSTEM 进程";
            ElevationHintText.Foreground = new SolidColorBrush(Color.FromArgb(255, 90, 220, 140));
        }
        else
        {
            ElevationHintText.Text = "当前未提权，注入 MasterHelper.exe 需要管理员权限（点「注入并启用」会自动提权重启）";
            ElevationHintText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 80));
        }
    }

    // ---------- 注入 / 卸载 ----------

    private async void OnInjectClick(object sender, RoutedEventArgs e)
    {
        // 未提权: 提权重启 (重启后用户再次点击注入)
        if (!ProcessManager.IsRunningAsAdmin())
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeManager.ToElementTheme(),
                Title = "需要管理员权限",
                CloseButtonText = "取消",
                PrimaryButtonText = "提权重启",
                DefaultButton = ContentDialogButton.Primary,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "注入 MasterHelper.exe (SYSTEM 进程) 需要管理员权限。\n\n点击「提权重启」将以管理员身份重新启动程序，重启后再次点击「注入并启用」。"
                }
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (ProcessManager.RelaunchAsAdmin())
                {
                    App.Current.Exit(); // 原实例退出
                }
                else
                {
                    Logger.Error("提权重启失败，请手动以管理员身份运行");
                }
            }
            return;
        }

        // 写配置 + 注入
        HookConfigWriter.Write(_dllDir, _hook);
        Logger.Info($"hook.cfg 已写入: {Path.Combine(_dllDir, "hook.cfg")}");

        var student = ProcessManager.FindProcesses(ProcessManager.StudentMainExe).FirstOrDefault();
        var master = ProcessManager.FindProcesses(ProcessManager.MasterHelperExe).FirstOrDefault();

        if (student == null && master == null)
        {
            Logger.Warning("未找到 StudentMain.exe / MasterHelper.exe，请先启动极域");
            return;
        }

        if (student != null) DoInjectStudent(student.Pid);
        if (master != null) DoInjectMaster(master.Pid);

        PushHotUpdate();
        RefreshElevationHint();
    }

    private void DoInjectStudent(int pid)
    {
        string dll = Path.Combine(_dllDir, ProcessManager.BypassMainDll);
        if (!File.Exists(dll))
        {
            Logger.Error($"未找到 {ProcessManager.BypassMainDll}，请将其复制到程序目录");
            return;
        }

        // 已加载 DLL (上次注入/停用残留): 调用 BypassStart 重新启用即可,
        // 不卸载模块 (FreeLibrary 会因残留线程导致极域崩溃)
        if (ProcessManager.IsModuleInjected(pid, ProcessManager.BypassMainDll))
        {
            Logger.Info("StudentMain.exe 已加载 DLL, 调用 BypassStart 启用 ...");
            if (ProcessManager.CallDllExport(pid, ProcessManager.BypassMainDll, "BypassStart"))
                Logger.Success("StudentMain.exe 已启用");
            _stoppedMain = false;
            return;
        }

        Logger.Info($"注入 {ProcessManager.BypassMainDll} -> StudentMain.exe (PID {pid}) ...");
        if (ProcessManager.InjectDll(pid, dll))
        {
            Logger.Success("StudentMain.exe 注入成功");
            _stoppedMain = false;
        }
    }

    private void DoInjectMaster(int pid)
    {
        string dll = Path.Combine(_dllDir, ProcessManager.BypassMasterDll);
        if (!File.Exists(dll))
        {
            Logger.Error($"未找到 {ProcessManager.BypassMasterDll}，请将其复制到程序目录");
            return;
        }

        if (ProcessManager.IsModuleInjected(pid, ProcessManager.BypassMasterDll))
        {
            Logger.Info("MasterHelper.exe 已加载 DLL, 调用 BypassStart 启用 ...");
            if (ProcessManager.CallDllExport(pid, ProcessManager.BypassMasterDll, "BypassStart"))
                Logger.Success("MasterHelper.exe 已启用");
            _stoppedMaster = false;
            return;
        }

        Logger.Info($"注入 {ProcessManager.BypassMasterDll} -> MasterHelper.exe (PID {pid}) ...");
        if (ProcessManager.InjectDll(pid, dll))
        {
            Logger.Success("MasterHelper.exe 注入成功");
            _stoppedMaster = false;
        }
    }

    /// <summary>
    /// 卸载流程: BypassStop(停线程+关管道) -> BypassUnhook(恢复 hooks, SEH 保护)
    /// -> 成功则 FreeLibrary 完全卸载 (DLL 文件可删除); 失败则保留模块(软停用)
    /// </summary>
    private async void OnUninjectClick(object sender, RoutedEventArgs e)
    {
        _hook.EnableAutoReinject = false;
        AutoReinjectSwitch.IsOn = false;
        SaveHookSettings();

        foreach (var p in ProcessManager.FindProcesses(ProcessManager.StudentMainExe))
        {
            if (!p.IsInjected) continue;

            Logger.Info($"卸载 {ProcessManager.BypassMainDll} (PID {p.Pid}) ...");
            ProcessManager.CallDllExport(p.Pid, ProcessManager.BypassMainDll, "BypassStop");
            if (ProcessManager.CallDllExport(p.Pid, ProcessManager.BypassMainDll, "BypassUnhook", out uint ec))
            {
                if (ec == 0 && ProcessManager.UninjectDll(p.Pid, ProcessManager.BypassMainDll))
                    Logger.Success("StudentMain.exe 已完全卸载 (DLL 可删除)");
                else
                    Logger.Warning($"hooks 恢复失败 (code={ec}), 模块保留为软停用, 极域重启后可删除 DLL");
            }
            else
            {
                Logger.Warning("BypassUnhook 不可用 (旧版 DLL), 回退发送 UPDATE|0x0 停用全部功能");
                _mainClient.SendCommand("UPDATE|0x0");
            }
        }
        foreach (var p in ProcessManager.FindProcesses(ProcessManager.MasterHelperExe))
        {
            if (!p.IsInjected) continue;

            Logger.Info($"卸载 {ProcessManager.BypassMasterDll} (PID {p.Pid}) ...");
            ProcessManager.CallDllExport(p.Pid, ProcessManager.BypassMasterDll, "BypassStop");
            if (ProcessManager.CallDllExport(p.Pid, ProcessManager.BypassMasterDll, "BypassUnhook", out uint ec))
            {
                if (ec == 0 && ProcessManager.UninjectDll(p.Pid, ProcessManager.BypassMasterDll))
                    Logger.Success("MasterHelper.exe 已完全卸载 (DLL 可删除)");
                else
                    Logger.Warning($"hooks 恢复失败 (code={ec}), 模块保留为软停用, 极域重启后可删除 DLL");
            }
            else
            {
                Logger.Warning("BypassUnhook 不可用 (旧版 DLL), 回退发送 UPDATE|0x0 停用全部功能");
                _masterClient.SendCommand("UPDATE|0x0");
            }
        }

        _stoppedMain = true;
        _stoppedMaster = true;
        _mainClient.Disconnect();
        _masterClient.Disconnect();
        await Task.Delay(300); // 等一轮轮询刷新状态
    }

    // ---------- 管道事件 ----------

    /// <summary>管道事件 -> 全局日志 (日志页统一显示; 心跳保活不刷屏)</summary>
    private void OnHookEvent(HookEvent ev)
    {
        if (ev.Kind == HookEventKind.Heartbeat) return;
        Logger.Log(ev.ToLogLevel(), ev.Display);
    }
}
