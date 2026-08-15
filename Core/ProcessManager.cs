using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace JiYuHelper.Core;

// ============================================================================
// ProcessManager.cs -- 目标进程管理 (x64 单版本)
//
// 位数策略 (App 恒为 x64):
//   - 64 位目标: 标准注入 CreateRemoteThread(LoadLibraryW)
//   - 32 位目标: wow64 跨位数注入 NtCreateThreadEx + 32 位 kernel32!LoadLibraryW
// ============================================================================

/// <summary>一个目标进程的信息</summary>
public class TargetProcessInfo
{
    /// <summary>进程名 (StudentMain.exe / MasterHelper.exe)</summary>
    public string Name { get; set; } = "";

    /// <summary>进程 ID, 0 表示未运行</summary>
    public int Pid { get; set; }

    /// <summary>该进程内是否已注入我们的 DLL</summary>
    public bool IsInjected { get; set; }

    /// <summary>状态描述 (供 UI 显示)</summary>
    public string Display => !Running ? $"{Name}: 未运行" : $"{Name}: PID {Pid}{(IsInjected ? " (已注入)" : "")}";

    public bool Running => Pid > 0;
}

public static class ProcessManager
{
    /// <summary>极域学生端主进程 (被控端, 32 位)</summary>
    public const string StudentMainExe = "StudentMain.exe";

    /// <summary>极域辅助进程 (SYSTEM 权限, 32 位)</summary>
    public const string MasterHelperExe = "MasterHelper.exe";

    /// <summary>注入到主进程的 DLL (x86)</summary>
    public const string BypassMainDll = "jyhelper_main.dll";

    /// <summary>注入到 MasterHelper 的 DLL (x86)</summary>
    public const string BypassMasterDll = "jyhelper_master.dll";

    // ---------- 进程枚举 ----------

    /// <summary>按名称枚举目标进程</summary>
    public static IReadOnlyList<TargetProcessInfo> FindProcesses(string exeName)
    {
        var result = new List<TargetProcessInfo>();
        string dllName = exeName.Equals(StudentMainExe, StringComparison.OrdinalIgnoreCase)
            ? BypassMainDll
            : BypassMasterDll;

        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == INVALID_HANDLE_VALUE) return result;

        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snap, ref entry)) return result;

            do
            {
                if (string.Equals(entry.szExeFile, exeName, StringComparison.OrdinalIgnoreCase))
                {
                    int pid = (int)entry.th32ProcessID;
                    result.Add(new TargetProcessInfo
                    {
                        Name = entry.szExeFile,
                        Pid = pid,
                        IsInjected = IsModuleInjected(pid, dllName),
                    });
                }
            }
            while (Process32NextW(snap, ref entry));
        }
        finally
        {
            CloseHandle(snap);
        }
        return result;
    }

    /// <summary>目标进程内是否已加载指定 DLL (模块名匹配)</summary>
    public static bool IsModuleInjected(int pid, string dllName)
    {
        var hp = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
        if (hp == IntPtr.Zero) return false;
        try
        {
            return FindModuleBase(hp, dllName, LIST_MODULES_ALL) != IntPtr.Zero;
        }
        finally
        {
            CloseHandle(hp);
        }
    }

    // ---------- 提权 ----------

    /// <summary>当前进程是否以管理员身份运行</summary>
    public static bool IsRunningAsAdmin()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var token))
            return false;
        try
        {
            if (!GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation,
                    out var elevation, (uint)Marshal.SizeOf<TOKEN_ELEVATION>(), out _))
                return false;
            return elevation.TokenIsElevated != 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    /// <summary>以管理员身份重新启动当前进程 (调用方应在成功后退出原实例)</summary>
    public static bool RelaunchAsAdmin()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath)) return false;

            var sei = new SHELLEXECUTEINFOW
            {
                cbSize = (uint)Marshal.SizeOf<SHELLEXECUTEINFOW>(),
                fMask = SEE_MASK_NOCLOSEPROCESS,
                lpVerb = "runas",
                lpFile = exePath,
                nShow = SW_SHOWNORMAL,
            };
            return ShellExecuteExW(ref sei);
        }
        catch (Exception ex)
        {
            Logger.Error($"提权重启失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 启用 SeDebugPrivilege: 打开 SYSTEM 进程句柄所需 (管理员令牌中默认禁用)。
    /// </summary>
    private static void EnableDebugPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
        {
            Logger.Error($"OpenProcessToken 失败 (错误 {Marshal.GetLastWin32Error()})");
            return;
        }
        try
        {
            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES
                {
                    Attributes = SE_PRIVILEGE_ENABLED,
                },
            };
            if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out tp.Privileges.Luid))
            {
                Logger.Error($"LookupPrivilegeValue(SeDebugPrivilege) 失败 (错误 {Marshal.GetLastWin32Error()})");
                return;
            }
            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
            {
                Logger.Error($"AdjustTokenPrivileges(SeDebugPrivilege) 失败 (错误 {Marshal.GetLastWin32Error()})");
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    // ---------- 注入 / 卸载 (按目标位数分流) ----------

    /// <summary>远程注入 DLL: 64 位目标标准注入, 32 位目标 wow64 跨位数注入</summary>
    public static bool InjectDll(int pid, string dllFullPath)
    {
        EnableDebugPrivilege();
        return IsTarget64(pid)
            ? InjectDllDirect(pid, dllFullPath)
            : Wow64InjectDll(pid, dllFullPath);
    }

    /// <summary>远程卸载 DLL (按目标位数分流)。调用方需先 BypassStop + BypassUnhook。</summary>
    public static bool UninjectDll(int pid, string dllName)
    {
        EnableDebugPrivilege();
        return IsTarget64(pid)
            ? UninjectDllDirect(pid, dllName)
            : Wow64UninjectDll(pid, dllName);
    }

    /// <summary>目标进程是否为 64 位 (App 恒为 64 位: IsWow64Process=false 即 64 位)</summary>
    private static bool IsTarget64(int pid)
    {
        var hp = OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
        if (hp == IntPtr.Zero) return false;
        try
        {
            IsWow64Process(hp, out bool wow64);
            return !wow64;
        }
        finally
        {
            CloseHandle(hp);
        }
    }

    // ---------- 64 位目标: 标准注入 ----------

    private static bool InjectDllDirect(int pid, string dllFullPath)
    {
        var hp = OpenProcess(
            PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
            false, pid);
        if (hp == IntPtr.Zero)
        {
            Logger.Error($"注入失败: 打开进程 PID={pid} 失败 (错误 {Marshal.GetLastWin32Error()}, 管理员={IsRunningAsAdmin()})");
            return false;
        }

        try
        {
            var pathBytes = Encoding.Unicode.GetBytes(dllFullPath + "\0");

            var mem = VirtualAllocEx(hp, IntPtr.Zero, (uint)pathBytes.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (mem == IntPtr.Zero)
            {
                Logger.Error($"注入失败: VirtualAllocEx 失败 (错误 {Marshal.GetLastWin32Error()})");
                return false;
            }

            try
            {
                if (!WriteProcessMemory(hp, mem, pathBytes, (uint)pathBytes.Length, out _))
                {
                    Logger.Error($"注入失败: WriteProcessMemory 失败 (错误 {Marshal.GetLastWin32Error()})");
                    return false;
                }

                var loadLib = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
                if (loadLib == IntPtr.Zero)
                {
                    Logger.Error("注入失败: 找不到 LoadLibraryW");
                    return false;
                }

                var thread = CreateRemoteThread(hp, IntPtr.Zero, 0, loadLib, mem, 0, out _);
                if (thread == IntPtr.Zero)
                {
                    Logger.Error($"注入失败: CreateRemoteThread 失败 (错误 {Marshal.GetLastWin32Error()})");
                    return false;
                }

                try
                {
                    return WaitForThreadLoad(hp, thread, dllFullPath);
                }
                finally
                {
                    CloseHandle(thread);
                }
            }
            finally
            {
                VirtualFreeEx(hp, mem, 0, MEM_RELEASE);
            }
        }
        finally
        {
            CloseHandle(hp);
        }
    }

    private static bool UninjectDllDirect(int pid, string dllName)
    {
        var hp = OpenProcess(
            PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
            false, pid);
        if (hp == IntPtr.Zero) return false;

        try
        {
            IntPtr target = FindModuleBase(hp, dllName, LIST_MODULES_ALL);
            if (target == IntPtr.Zero)
            {
                Logger.Info($"卸载: 进程 PID={pid} 中未找到 {dllName}");
                return false;
            }

            var freeLib = GetProcAddress(GetModuleHandle("kernel32.dll"), "FreeLibrary");
            if (freeLib == IntPtr.Zero) return false;

            // 引用计数可能 > 1, 循环 FreeLibrary 直到返回非 0
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var thread = CreateRemoteThread(hp, IntPtr.Zero, 0, freeLib, target, 0, out _);
                if (thread == IntPtr.Zero) return false;

                bool ok;
                try
                {
                    ok = WaitForSingleObject(thread, 3000) == 0;
                    uint exitCode = 0;
                    if (ok) GetExitCodeThread(thread, out exitCode);
                    ok = ok && exitCode != 0;
                }
                finally
                {
                    CloseHandle(thread);
                }

                if (ok) return true;
            }

            Logger.Warning($"FreeLibrary 多次失败: {dllName} 仍被占用 (极域重启后可删除)");
            return false;
        }
        finally
        {
            CloseHandle(hp);
        }
    }

    /// <summary>等待加载线程完成并校验 LoadLibraryW 返回值</summary>
    private static bool WaitForThreadLoad(IntPtr hProcess, IntPtr thread, string dllPath)
    {
        if (WaitForSingleObject(thread, 5000) != 0)
        {
            Logger.Error($"注入超时: {dllPath}");
            return false;
        }
        GetExitCodeThread(thread, out uint exitCode);
        if (exitCode == 0)
        {
            Logger.Error($"注入失败: LoadLibraryW 返回 0 ({dllPath}) — 目标进程可能启用了防注入策略");
            return false;
        }
        return true;
    }

    // ---------- 32 位目标: wow64 跨位数注入 ----------

    private static bool Wow64InjectDll(int pid, string dllFullPath)
    {
        var hp = OpenProcess(
            PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
            false, pid);
        if (hp == IntPtr.Zero)
        {
            Logger.Error($"注入失败: 打开进程 PID={pid} 失败 (错误 {Marshal.GetLastWin32Error()})");
            return false;
        }

        try
        {
            IntPtr kernel32 = FindModuleBase(hp, "kernel32.dll", LIST_MODULES_32BIT);
            if (kernel32 == IntPtr.Zero)
            {
                Logger.Error("注入失败: 未找到目标进程的 32 位 kernel32");
                return false;
            }

            IntPtr loadLib = FindExportInModule(hp, kernel32, "LoadLibraryW");
            if (loadLib == IntPtr.Zero)
            {
                Logger.Error("注入失败: 无法解析 32 位 LoadLibraryW");
                return false;
            }

            var pathBytes = Encoding.Unicode.GetBytes(dllFullPath + "\0");
            var mem = VirtualAllocEx(hp, IntPtr.Zero, (uint)pathBytes.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (mem == IntPtr.Zero)
            {
                Logger.Error($"注入失败: VirtualAllocEx 失败 (错误 {Marshal.GetLastWin32Error()})");
                return false;
            }

            try
            {
                if (!WriteProcessMemory(hp, mem, pathBytes, (uint)pathBytes.Length, out _))
                {
                    Logger.Error($"注入失败: WriteProcessMemory 失败 (错误 {Marshal.GetLastWin32Error()})");
                    return false;
                }

                var thread = CreateWow64RemoteThread(hp, loadLib, mem);
                if (thread == IntPtr.Zero)
                {
                    Logger.Error($"注入失败: NtCreateThreadEx 失败");
                    return false;
                }

                try
                {
                    return WaitForThreadLoad(hp, thread, dllFullPath);
                }
                finally
                {
                    CloseHandle(thread);
                }
            }
            finally
            {
                VirtualFreeEx(hp, mem, 0, MEM_RELEASE);
            }
        }
        finally
        {
            CloseHandle(hp);
        }
    }

    private static bool Wow64UninjectDll(int pid, string dllName)
    {
        var hp = OpenProcess(
            PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
            false, pid);
        if (hp == IntPtr.Zero) return false;

        try
        {
            IntPtr target = FindModuleBase(hp, dllName, LIST_MODULES_32BIT);
            if (target == IntPtr.Zero)
            {
                Logger.Info($"卸载: 进程 PID={pid} 中未找到 {dllName}");
                return false;
            }

            IntPtr kernel32 = FindModuleBase(hp, "kernel32.dll", LIST_MODULES_32BIT);
            IntPtr freeLib = kernel32 == IntPtr.Zero ? IntPtr.Zero : FindExportInModule(hp, kernel32, "FreeLibrary");
            if (freeLib == IntPtr.Zero)
            {
                Logger.Error("卸载: 无法解析 32 位 FreeLibrary");
                return false;
            }

            // 引用计数可能 > 1, 循环 FreeLibrary 直到返回非 0
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var thread = CreateWow64RemoteThread(hp, freeLib, target);
                if (thread == IntPtr.Zero) return false;

                bool ok;
                try
                {
                    ok = WaitForSingleObject(thread, 3000) == 0;
                    uint exitCode = 0;
                    if (ok) GetExitCodeThread(thread, out exitCode);
                    ok = ok && exitCode != 0;
                }
                finally
                {
                    CloseHandle(thread);
                }

                if (ok) return true;
            }

            Logger.Warning($"FreeLibrary 多次失败: {dllName} 仍被占用 (极域重启后可删除)");
            return false;
        }
        finally
        {
            CloseHandle(hp);
        }
    }

    /// <summary>创建 32 位目标进程内的远程线程 (NtCreateThreadEx), 失败返回 0 并输出 NTSTATUS</summary>
    private static IntPtr CreateWow64RemoteThread(IntPtr hProcess, IntPtr startAddress, IntPtr parameter)
    {
        uint status = NtCreateThreadEx(out var thread, THREAD_ALL_ACCESS, IntPtr.Zero, hProcess,
                startAddress, parameter, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (status != 0)
        {
            Logger.Error($"NtCreateThreadEx 失败 (NTSTATUS=0x{status:X8})");
            return IntPtr.Zero;
        }
        return thread;
    }

    /// <summary>
    /// 远程调用 DLL 导出的无参函数 (如 BypassStart/BypassStop/BypassUnhook), 32/64 位通用。
    /// </summary>
    public static bool CallDllExport(int pid, string dllName, string exportName)
        => CallDllExport(pid, dllName, exportName, out _);

    /// <summary>同上, 额外返回远程线程退出码 (BypassUnhook: 0=MH_OK 可卸载, 非0=恢复失败)</summary>
    public static bool CallDllExport(int pid, string dllName, string exportName, out uint exitCode)
    {
        exitCode = 0;
        EnableDebugPrivilege();
        bool is64 = IsTarget64(pid);

        var hp = OpenProcess(
            PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
            false, pid);
        if (hp == IntPtr.Zero) return false;

        try
        {
            IntPtr target = FindModuleBase(hp, dllName, is64 ? LIST_MODULES_ALL : LIST_MODULES_32BIT);
            if (target == IntPtr.Zero)
            {
                Logger.Info($"{dllName} 未加载于 PID={pid}");
                return false;
            }

            IntPtr fn = FindExportInModule(hp, target, exportName);
            if (fn == IntPtr.Zero)
            {
                Logger.Warning($"{dllName} 无导出 {exportName} (旧版本 DLL? 请重启极域后再注入)");
                return false;
            }

            var thread = is64
                ? CreateRemoteThread(hp, IntPtr.Zero, 0, fn, IntPtr.Zero, 0, out _)
                : CreateWow64RemoteThread(hp, fn, IntPtr.Zero);
            if (thread == IntPtr.Zero) return false;

            try
            {
                if (WaitForSingleObject(thread, 8000) != 0) return false;
                GetExitCodeThread(thread, out exitCode);
                return true;
            }
            finally
            {
                CloseHandle(thread);
            }
        }
        finally
        {
            CloseHandle(hp);
        }
    }

    // ---------- 模块 / 导出解析 ----------

    /// <summary>在目标进程中按模块名查找模块基址 (可按位数过滤)</summary>
    private static IntPtr FindModuleBase(IntPtr hProcess, string moduleName, uint filter)
    {
        var modules = new IntPtr[512];
        if (!EnumProcessModulesEx(hProcess, modules, (uint)(modules.Length * IntPtr.Size), out var needed, filter))
            return IntPtr.Zero;

        int count = (int)(needed / (uint)IntPtr.Size);
        var name = new StringBuilder(260);
        for (int i = 0; i < count && i < modules.Length; i++)
        {
            if (GetModuleBaseNameW(hProcess, modules[i], name, (uint)name.Capacity) == 0) continue;
            if (string.Equals(name.ToString(), moduleName, StringComparison.OrdinalIgnoreCase))
                return modules[i];
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// 解析目标进程内某模块的导出表, 返回导出函数的绝对地址。
    /// 用于获取 32 位 kernel32 的 LoadLibraryW/FreeLibrary (跨位数注入起点)。
    /// </summary>
    private static IntPtr FindExportInModule(IntPtr hProcess, IntPtr moduleBase, string exportName)
    {
        var dos = new byte[0x40];
        if (!ReadProcessMemory(hProcess, moduleBase, dos, (uint)dos.Length, out _)) return IntPtr.Zero;
        int peOff = BitConverter.ToInt32(dos, 0x3C);
        if (peOff <= 0) return IntPtr.Zero;

        var pe = new byte[0x100];
        if (!ReadProcessMemory(hProcess, moduleBase + peOff, pe, (uint)pe.Length, out _)) return IntPtr.Zero;

        const int optOff = 0x18;
        if (BitConverter.ToUInt16(pe, optOff) != 0x10B) return IntPtr.Zero; // 必须是 32 位 PE

        int ddOffset = optOff + 0x60;
        int exportRva = BitConverter.ToInt32(pe, ddOffset);
        if (exportRva == 0) return IntPtr.Zero;

        var exp = new byte[40];
        if (!ReadProcessMemory(hProcess, moduleBase + exportRva, exp, (uint)exp.Length, out _)) return IntPtr.Zero;

        int numNames = BitConverter.ToInt32(exp, 24);
        int addrFunctions = BitConverter.ToInt32(exp, 28);
        int addrNames = BitConverter.ToInt32(exp, 32);
        int addrOrdinals = BitConverter.ToInt32(exp, 36);
        if (numNames <= 0 || numNames > 65536) return IntPtr.Zero;

        var nameRvaBytes = new byte[4];
        var oneByte = new byte[1];
        var nameBuf = new StringBuilder(64);

        for (int i = 0; i < numNames; i++)
        {
            if (!ReadProcessMemory(hProcess, moduleBase + addrNames + i * 4, nameRvaBytes, 4, out _)) break;
            int nameRva = BitConverter.ToInt32(nameRvaBytes, 0);

            nameBuf.Clear();
            for (int j = 0; j < 64; j++)
            {
                if (!ReadProcessMemory(hProcess, moduleBase + nameRva + j, oneByte, 1, out _)) break;
                if (oneByte[0] == 0) break;
                nameBuf.Append((char)oneByte[0]);
            }

            if (nameBuf.ToString() == exportName)
            {
                var ordBytes = new byte[2];
                if (!ReadProcessMemory(hProcess, moduleBase + addrOrdinals + i * 2, ordBytes, 2, out _)) break;
                int ordinal = BitConverter.ToUInt16(ordBytes, 0);

                var fnRvaBytes = new byte[4];
                if (!ReadProcessMemory(hProcess, moduleBase + addrFunctions + ordinal * 4, fnRvaBytes, 4, out _)) break;
                int fnRva = BitConverter.ToInt32(fnRvaBytes, 0);
                return moduleBase + fnRva;
            }
        }
        return IntPtr.Zero;
    }

    // ---------- P/Invoke ----------

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_CREATE_THREAD = 0x0002;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint LIST_MODULES_ALL = 0x03;
    private const uint LIST_MODULES_32BIT = 0x01;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
    private const uint THREAD_ALL_ACCESS = 0x001FFFFF;
    private const int SW_SHOWNORMAL = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_ELEVATION
    {
        public uint TokenIsElevated;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFOW
    {
        public uint cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    private enum TOKEN_INFORMATION_CLASS
    {
        TokenElevation = 20,
    }

    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize,
        IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

    [DllImport("ntdll.dll")]
    private static extern uint NtCreateThreadEx(
        out IntPtr threadHandle,
        uint desiredAccess,
        IntPtr objectAttributes,
        IntPtr processHandle,
        IntPtr startAddress,
        IntPtr parameter,
        uint createFlags,
        IntPtr zeroBits,
        IntPtr stackSize,
        IntPtr maximumStackSize,
        IntPtr attributeList);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EnumProcessModulesEx(IntPtr hProcess, IntPtr[] lphModule, uint cb, out uint lpcbNeeded, uint dwFilterFlag);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleBaseNameW(IntPtr hProcess, IntPtr hModule, StringBuilder lpBaseName, uint nSize);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, TOKEN_INFORMATION_CLASS tokenInformationClass,
        out TOKEN_ELEVATION tokenInformation, uint tokenInformationLength, out uint returnLength);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteExW(ref SHELLEXECUTEINFOW pExecInfo);
}
