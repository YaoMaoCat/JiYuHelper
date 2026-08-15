using JiYuHelper.Models;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace JiYuHelper.Core;

public enum ScanMode
{
    /// <summary>UDP 组播 224.50.50.42:4988 监听 OONC/CANC</summary>
    Multicast,
    /// <summary>网段扫描: TCP 4806 + WORB 握手验证</summary>
    SubnetScan,
}

/// <summary>
/// 教师机发现器
/// - 组播模式: 监听 UDP 组播 224.50.50.42:4988 (OONC/CANC)
/// - 网段扫描: 枚举本机网段, TCP 连接 4806 + WORB 握手验证
/// </summary>
public class TeacherDiscoverer : IDisposable
{
    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private bool _running;

    public ObservableCollection<TeacherInfo> Teachers { get; } = new();

    /// <summary>UI 线程调度器, 用于跨线程安全更新集合</summary>
    public DispatcherQueue? UiDispatcher { get; set; }

    public bool IsRunning => _running;

    public event Action? StateChanged;
    public event Action<string>? ProgressChanged;

    /// <summary>当前扫描进度描述 (UI 显示用)</summary>
    public string ProgressText { get; private set; } = "";

    public async Task StartAsync(ScanMode mode, int timeoutSeconds = 15)
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        StateChanged?.Invoke();

        try
        {
            if (mode == ScanMode.Multicast)
                await RunMulticastAsync(timeoutSeconds, ct);
            else
                await RunSubnetScanAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error($"扫描失败: {ex.Message}");
        }
        finally
        {
            await StopAsync();
        }
    }

    // ---------- 组播模式 ----------

    private async Task RunMulticastAsync(int timeoutSeconds, CancellationToken ct)
    {
        SetProgress("正在绑定组播端口 ...");
        Logger.Info($"监听组播 {PacketBuilder.MulticastGroup}:{PacketBuilder.MulticastPort} ...");

        Socket? sock = null;
        try
        {
            // 原生 Socket + SO_REUSEADDR:
            // - 允许与本机极域学生端共享 4988 端口 (WinRT DatagramSocket 会绑定失败)
            // - 无需管理员权限 (组播接收不需要特权)
            sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            sock.Bind(new IPEndPoint(IPAddress.Any, PacketBuilder.MulticastPort));
            sock.ReceiveTimeout = 500;

            // 在每个 IPv4 网卡上加入组播组 (防止多网卡绑错接口)
            int joined = 0;
            var group = IPAddress.Parse(PacketBuilder.MulticastGroup);

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    try
                    {
                        sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                            new MulticastOption(group, ua.Address));
                        joined++;
                    }
                    catch { /* 该网卡不支持组播则跳过 */ }
                }
            }

            if (joined == 0)
            {
                // 兜底: 用 INADDR_ANY 加入
                sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                    new MulticastOption(group, IPAddress.Any));
                joined = 1;
            }

            _socket = sock;
            Logger.Info($"已加入组播组 ({joined} 个接口), 等待教师机广播 (超时 {timeoutSeconds}s)");
            SetProgress($"监听组播 {PacketBuilder.MulticastGroup}:{PacketBuilder.MulticastPort} (超时 {timeoutSeconds}s)");

            // 接收循环移到后台线程, 避免阻塞 UI (ReceiveFrom 是同步阻塞调用)
            var recvTask = Task.Run(() => ReceiveLoop(sock, timeoutSeconds, ct), ct);
            try
            {
                await Task.Delay(timeoutSeconds * 1000, ct);
            }
            catch (TaskCanceledException) { }

            // 等待接收线程退出 (StopAsync 会 Close socket 中断阻塞)
            try { await recvTask; } catch { }
        }
        catch (SocketException ex)
        {
            SetProgress("组播绑定/加入失败");
            Logger.Error($"组播失败: {ex.Message} (错误码 {ex.SocketErrorCode})");
            Logger.Info("提示: 组播在部分网络环境不可用, 可改用网段扫描模式");
            sock?.Dispose();
            _socket = null;
            await Task.Delay(1500, ct);
        }
        catch (Exception ex)
        {
            SetProgress("组播初始化失败");
            Logger.Error($"组播失败: {ex.Message}");
            sock?.Dispose();
            _socket = null;
            await Task.Delay(1500, ct);
        }
    }

    /// <summary>后台线程的组播接收循环 (同步 ReceiveFrom, 由 Close socket 中断)</summary>
    private void ReceiveLoop(Socket sock, int timeoutSeconds, CancellationToken ct)
    {
        var buf = new byte[4096];
        EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (!ct.IsCancellationRequested && sw.Elapsed.TotalSeconds < timeoutSeconds)
        {
            try
            {
                int n = sock.ReceiveFrom(buf, ref ep);
                if (n > 0)
                {
                    byte[] data = new byte[n];
                    Array.Copy(buf, data, n);
                    ParseMulticastPacket(data, ((IPEndPoint)ep).Address?.ToString() ?? "");
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                continue; // 正常超时, 继续循环
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; } // socket 被 Close
        }
    }

    /// <summary>解析组播包: OONC(来源IP=教师机) / CANC(偏移0x20=教师IP)</summary>
    private void ParseMulticastPacket(byte[] buffer, string srcIP)
    {
        try
        {
            if (buffer.Length < 4) return;

            string packetType = System.Text.Encoding.ASCII.GetString(buffer, 0, 4);
            string teacherIP = "";

            if (packetType == "OONC")
            {
                teacherIP = srcIP;
            }
            else if (packetType == "CANC" && buffer.Length >= 0x24)
            {
                teacherIP = $"{buffer[0x20]}.{buffer[0x21]}.{buffer[0x22]}.{buffer[0x23]}";
            }
            else
            {
                return;
            }

            if (string.IsNullOrEmpty(teacherIP) || teacherIP == "0.0.0.0") return;
            if (IPAddress.TryParse(teacherIP, out var ip) && ip.AddressFamily != AddressFamily.InterNetwork)
                return;

            // Source 用来源方式标签, 避免 OONC 时 IP(IP) 重复显示
            Logger.Success($"发现教师机 {teacherIP} ({packetType}包, 来自 {srcIP})");
            AddTeacher(teacherIP, "组播扫描", packetType);
        }
        catch (Exception ex)
        {
            Logger.Error($"解析组播包失败: {ex.Message}");
        }
    }

    // ---------- 网段扫描模式 ----------

    private async Task RunSubnetScanAsync(CancellationToken ct)
    {
        var targets = EnumerateSubnetTargets();
        if (targets.Count == 0)
        {
            SetProgress("未找到可用网段");
            Logger.Error("无法枚举本机 IPv4 网段");
            return;
        }

        Logger.Info($"网段扫描: 共 {targets.Count} 个候选 IP, TCP 4806 + WORB 握手验证");
        SetProgress($"扫描中 ... (0/{targets.Count})");

        int done = 0;
        int found = 0;
        int maxConcurrent = 64;
        using var sem = new SemaphoreSlim(maxConcurrent);

        var tasks = targets.Select(async ip =>
        {
            await sem.WaitAsync(ct);
            try
            {
                if (await ProbeTeacherAsync(ip, ct))
                {
                    int f = Interlocked.Increment(ref found);
                    Logger.Success($"网段扫描发现教师机: {ip} (WORB 握手响应)");
                    AddTeacher(ip.ToString(), "网段扫描", "TCP");
                }
            }
            finally
            {
                sem.Release();
                int d = Interlocked.Increment(ref done);
                if (d % 20 == 0 || d == targets.Count)
                    SetProgress($"扫描中 ... ({d}/{targets.Count}, 发现 {Interlocked.CompareExchange(ref found, 0, 0)})");
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }

        if (ct.IsCancellationRequested)
        {
            // 用户主动停止
            SetProgress($"扫描终止, 共发现 {found} 台教师机");
            Logger.Info($"扫描已终止, 共发现 {found} 台教师机");
        }
        else
        {
            SetProgress($"网段扫描完成: {targets.Count} 个 IP, 发现 {found} 台教师机");
            Logger.Info($"网段扫描完成, 共发现 {found} 台教师机");
        }
    }

    /// <summary>
    /// 探测单个 IP: TCP 连接 4806 成功 + 发送 WORB 握手包后连接未被立即拒绝
    /// </summary>
    private static async Task<bool> ProbeTeacherAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork)
            {
                NoDelay = true,
                SendTimeout = 800,
                ReceiveTimeout = 800,
            };

            var connectTask = client.ConnectAsync(new IPEndPoint(ip, PacketBuilder.ControlPort));
            var t = await Task.WhenAny(connectTask, Task.Delay(600, ct));
            if (t != connectTask) return false;
            await connectTask;

            if (!client.Connected) return false;

            // 发送 WORB 握手包, 验证协议特征
            byte[] worb = PacketBuilder.BuildWorbPacket();
            await client.GetStream().WriteAsync(worb, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>枚举本机所有 IPv4 网段内的主机地址 (排除本机/网关/广播)</summary>
    private static List<IPAddress> EnumerateSubnetTargets()
    {
        var result = new List<IPAddress>();
        var localIps = new HashSet<string>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = ua.Address;
                var mask = ua.IPv4Mask;
                localIps.Add(ip.ToString());

                if (mask == null || mask.GetAddressBytes().All(b => b == 0)) continue;

                uint ipVal = BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray(), 0);
                uint maskVal = BitConverter.ToUInt32(mask.GetAddressBytes().Reverse().ToArray(), 0);
                uint network = ipVal & maskVal;
                uint broadcast = network | ~maskVal;
                uint count = broadcast - network - 1;

                // 限制单网段扫描量, 防止 /8 大网段卡死
                if (count > 1024) count = 1024;

                for (uint i = 1; i <= count; i++)
                {
                    uint host = network + i;
                    if (host == ipVal) continue;
                    var addr = new IPAddress(BitConverter.GetBytes(host).Reverse().ToArray());
                    if (addr.ToString() == "0.0.0.0" || addr.ToString() == "255.255.255.255") continue;
                    result.Add(addr);
                }
            }
        }

        // 去重
        return result.Distinct().ToList();
    }

    // ---------- 公共 ----------

    public async Task StopAsync()
    {
        if (!_running && _socket == null) return;
        _running = false;
        _cts?.Cancel();

        if (_socket != null)
        {
            try
            {
                // 关闭 socket 会中断 ReceiveFrom 阻塞
                _socket.Close();
            }
            catch { }
            _socket = null;
        }

        StateChanged?.Invoke();
    }

    private void AddTeacher(string teacherIP, string source, string packetType)
    {
        var info = new TeacherInfo
        {
            IP = teacherIP,
            Source = source,
            PacketType = packetType,
            DiscoveredAt = DateTime.Now
        };

        // 去重 + 添加必须在同一线程执行, 避免多网络线程并发导致重复
        void AddIfNew()
        {
            lock (Teachers)
            {
                if (Teachers.Any(t => t.IP == teacherIP)) return;
                Teachers.Add(info);
            }
        }

        var dispatcher = UiDispatcher;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
            dispatcher.TryEnqueue(AddIfNew);
        else
            AddIfNew();
    }

    private void SetProgress(string text)
    {
        ProgressText = text;
        ProgressChanged?.Invoke(text);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _socket?.Close();
        _socket = null;
    }
}
