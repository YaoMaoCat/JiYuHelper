using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JiYuHelper.Core;

// ============================================================================
// JyHookClient.cs -- Hook 通信层
//
//   1. 命名管道客户端: 连接注入 DLL 创建的管道服务端
//        bypass_main.dll   -> \\.\pipe\JYHookHelper
//        bypass_master.dll -> \\.\pipe\JYMasterHelper
//      DLL 侧协议: 每行一个事件 "KIND|message\n" (UTF-8),
//      KIND = LOADED | HOOK | BLOCKED | HEARTBEAT | ERROR | INFO
//   2. 自动连接循环: 与页面生命周期无关的后台循环, 满足条件时每 3s 重试
//      连接 (DLL 管道服务端可能晚于注入就绪; 断线后自动重连)。
//   3. MinHook 本地 API 的 P/Invoke 声明 (供未来进程内自 hook 场景使用;
//      跨进程 hook 由注入的 C++ DLL 完成, 不在本类职责内)
// ============================================================================

/// <summary>DLL 通过命名管道回传的事件类型</summary>
public enum HookEventKind
{
    /// <summary>DLL 已加载, 初始化完成 (携带已启用功能列表)</summary>
    Loaded,
    /// <summary>Hook 安装完成/失败</summary>
    HookInstalled,
    /// <summary>拦截到教师端指令 (远程控制/结束进程等)</summary>
    CommandBlocked,
    /// <summary>心跳/状态变化</summary>
    Heartbeat,
    /// <summary>错误</summary>
    Error,
    /// <summary>普通信息</summary>
    Info,
}

/// <summary>一条 Hook 事件 (管道回传)</summary>
public class HookEvent
{
    /// <summary>来源进程名 (StudentMain.exe / MasterHelper.exe)</summary>
    public string SourceProcess { get; set; } = "";

    /// <summary>事件类型</summary>
    public HookEventKind Kind { get; set; }

    /// <summary>事件时间</summary>
    public DateTime Time { get; set; } = DateTime.Now;

    /// <summary>事件描述 (人类可读)</summary>
    public string Message { get; set; } = "";

    /// <summary>日志页显示文本</summary>
    public string Display => $"[{Time:HH:mm:ss.fff}] {SourceProcess}: {Message}";

    /// <summary>事件类型 -> 日志级别 (供写 Logger 使用)</summary>
    public LogLevel ToLogLevel() => Kind switch
    {
        HookEventKind.Loaded => LogLevel.Success,
        HookEventKind.CommandBlocked => LogLevel.Attack,
        HookEventKind.Error => LogLevel.Error,
        _ => LogLevel.Info,
    };
}

/// <summary>
/// 命名管道客户端: 后台自动连接循环 + 事件读取。
/// 由 shouldConnect 委托决定是否应保持连接 (目标进程已注入)。
/// </summary>
public sealed class JyHookClient : IDisposable
{
    /// <summary>bypass_main.dll 管道名</summary>
    public const string MainPipeName = @"\\.\pipe\JYHookHelper";

    /// <summary>bypass_master.dll 管道名</summary>
    public const string MasterPipeName = @"\\.\pipe\JYMasterHelper";

    private const int ConnectIntervalMs = 3000;
    private const int ConnectTimeoutMs = 3000;

    private readonly string _pipeName;
    private readonly string _sourceProcess;
    private readonly Func<bool> _shouldConnect;
    private NamedPipeClientStream? _stream;
    private CancellationTokenSource? _cts;
    private volatile bool _started;
    private volatile bool _disposed;

    /// <summary>收到 DLL 事件时触发 (后台线程, 由调用方决定是否回 UI)</summary>
    public event Action<HookEvent>? EventReceived;

    /// <summary>连接/断开状态变化 (后台线程)</summary>
    public event Action<bool>? ConnectionChanged;

    /// <summary>连接成功时触发 (调用方可在此同步功能掩码)</summary>
    public event Action? Connected;

    /// <summary>当前是否已连接</summary>
    public bool IsConnected { get; private set; }

    /// <param name="pipeName">管道全名 (MainPipeName / MasterPipeName)</param>
    /// <param name="sourceProcess">事件来源进程显示名</param>
    /// <param name="shouldConnect">是否应保持连接 (目标进程已注入)</param>
    public JyHookClient(string pipeName, string sourceProcess, Func<bool> shouldConnect)
    {
        _pipeName = pipeName;
        _sourceProcess = sourceProcess;
        _shouldConnect = shouldConnect;
    }

    /// <summary>启动后台自动连接循环 (调用一次即可, 内部按条件重试/重连)</summary>
    public void Start()
    {
        _disposed = false;
        _ = Task.Run(AutoConnectLoopAsync, CancellationToken.None);
    }

    private async Task AutoConnectLoopAsync()
    {
        int failCount = 0;
        while (!_disposed)
        {
            if (!IsConnected && !_started && _shouldConnect())
            {
                try
                {
                    await ConnectOnceAsync();
                    failCount = 0;
                    Connected?.Invoke();
                }
                catch
                {
                    // DLL 管道服务端未就绪/连接被拒, 下一个周期重试
                    if (++failCount % 10 == 1)
                        Logger.Info($"尝试连接 {_sourceProcess} 管道失败 (已重试 {failCount} 次)");
                }
            }
            else if (IsConnected)
            {
                // 保活/探测: 发送 PING, 若管道已死则写失败触发重连
                SendCommand("PING");
            }
            await Task.Delay(ConnectIntervalMs);
        }
    }

    /// <summary>单次连接尝试 (失败抛异常, 由自动循环重试)</summary>
    private async Task ConnectOnceAsync()
    {
        var stream = new NamedPipeClientStream(".", PipeNameFromFull(_pipeName),
            PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await stream.ConnectAsync(ConnectTimeoutMs);
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        _stream = stream;
        _started = true;
        IsConnected = true;
        ConnectionChanged?.Invoke(true);

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(stream, _cts.Token), CancellationToken.None);
    }

    /// <summary>后台读取循环: 逐行解析 "KIND|message" 并触发事件</summary>
    private async Task ReadLoopAsync(NamedPipeClientStream stream, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(stream);
            while (!ct.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(ct);
                if (line == null) break; // 服务端关闭
                if (line.Length == 0) continue;

                var ev = ParseLine(line);
                if (ev != null)
                    EventReceived?.Invoke(ev);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }   // 管道断开
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            Logger.Error($"管道 {_pipeName} 读取异常: {ex.Message}");
        }
        finally
        {
            // 断线: 重置状态, 允许自动循环重新连接
            _started = false;
            IsConnected = false;
            ConnectionChanged?.Invoke(false);
        }
    }

    /// <summary>解析一行 "KIND|message" 为 HookEvent</summary>
    private HookEvent? ParseLine(string line)
    {
        string kind = "INFO";
        string message = line;

        int sep = line.IndexOf('|');
        if (sep > 0)
        {
            kind = line[..sep];
            message = line[(sep + 1)..];
        }

        return new HookEvent
        {
            SourceProcess = _sourceProcess,
            Kind = ParseKind(kind),
            Message = message,
        };
    }

    private static HookEventKind ParseKind(string kind) => kind.ToUpperInvariant() switch
    {
        "LOADED" => HookEventKind.Loaded,
        "HOOK" => HookEventKind.HookInstalled,
        "BLOCKED" => HookEventKind.CommandBlocked,
        "HEARTBEAT" => HookEventKind.Heartbeat,
        "ERROR" => HookEventKind.Error,
        _ => HookEventKind.Info,
    };

    private static string PipeNameFromFull(string fullName)
    {
        // "\\.\pipe\JYHookHelper" -> "JYHookHelper"
        int idx = fullName.LastIndexOf('\\');
        return idx >= 0 ? fullName[(idx + 1)..] : fullName;
    }

    /// <summary>
    /// 发送命令到 DLL (如 "UPDATE|0x1F3" 热更新功能掩码)。未连接时记录日志
    /// (说明热更新未生效, 等待自动重连或重新注入)。
    /// </summary>
    public void SendCommand(string command)
    {
        var s = _stream;
        if (s == null || !IsConnected)
        {
            Logger.Warning($"管道未连接 ({_sourceProcess}), 命令未送达: {command}");
            return;
        }
        try
        {
            var bytes = Encoding.UTF8.GetBytes(command + "\n");
            s.Write(bytes, 0, bytes.Length);
            s.Flush();
        }
        catch (Exception ex)
        {
            Logger.Error($"管道命令发送失败 ({_pipeName}): {ex.Message}, 触发重连");
            Disconnect();
        }
    }

    /// <summary>断开当前连接 (停止读取; 自动循环按条件决定是否重连)</summary>
    public void Disconnect()
    {
        _started = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        var s = _stream;
        _stream = null;
        s?.Dispose();
        IsConnected = false;
    }

    public void Dispose()
    {
        _disposed = true;
        Disconnect();
    }
}

// ----------------------------------------------------------------------------
// MinHook 本地 API P/Invoke 声明
//
// 注意:
//   - MinHook 为 C API, 调用约定 Cdecl; 仅能 hook 当前进程;
//   - 依赖 libMinHook.dll (按 App 位数), 需随 App 分发。
// ----------------------------------------------------------------------------

/// <summary>MinHook 本地 API</summary>
public static partial class MinHookNative
{
    private const string Lib = "libMinHook.dll";

    public const int MH_OK = 0;

    /// <summary>初始化 MinHook 引擎 (进程内仅调用一次)</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int MH_Initialize();

    /// <summary>
    /// 创建 hook: 将 pTarget 替换为 pDetour, 原函数地址写入 ppOriginal
    /// </summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int MH_CreateHook(IntPtr pTarget, IntPtr pDetour, out IntPtr ppOriginal);

    /// <summary>启用指定 hook</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int MH_EnableHook(IntPtr pTarget);

    /// <summary>禁用指定 hook</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int MH_DisableHook(IntPtr pTarget);

    /// <summary>卸载 MinHook 引擎</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int MH_Uninitialize();

    /// <summary>将 MH_STATUS 码转为描述文本</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr MH_StatusToString(int status);

    /// <summary>转换 MH_StatusToString 返回值 (IntPtr -> string)</summary>
    public static string StatusText(int status)
    {
        try
        {
            var p = MH_StatusToString(status);
            return p == IntPtr.Zero ? $"MH_Unknown({status})" : Marshal.PtrToStringAnsi(p) ?? $"MH_Unknown({status})";
        }
        catch (DllNotFoundException)
        {
            return $"libMinHook.dll 未找到 (status={status})";
        }
    }
}
