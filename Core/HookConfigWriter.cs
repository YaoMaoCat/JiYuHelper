using System;
using System.IO;
using System.Text;

namespace JiYuHelper.Core;

/// <summary>
/// hook.cfg 写入器: 将 HookSettings 的 14 个开关按分组映射为 DLL 读取的 7 个配置键。
/// 写出的文件与 bypass_main.dll / bypass_master.dll 同目录。
/// </summary>
public static class HookConfigWriter
{
    /// <summary>配置文件中的布尔真值</summary>
    private const string TrueVal = "1";
    private const string FalseVal = "0";

    // 功能位 (与 Native src/core/hotupdate.hpp 的 FEATURE_* 一致)
    private const ulong F_KEYBOARD = 1UL << 0;
    private const ulong F_TOPMOST = 1UL << 1;
    private const ulong F_FOCUS = 1UL << 2;
    private const ulong F_APPLIST = 1UL << 3;
    private const ulong F_PROCLIST = 1UL << 4;
    private const ulong F_PROCGUARD = 1UL << 5;
    private const ulong F_PROCHOOK = 1UL << 6;
    private const ulong F_SCREENFAKE = 1UL << 7;
    private const ulong F_SCREENCAP = 1UL << 8;
    private const ulong F_BLACKMON = 1UL << 9;
    private const ulong F_REMOTE = 1UL << 10;
    private const ulong F_INPUTLOCK = 1UL << 11;
    private const ulong F_FILTER = 1UL << 12;
    private const ulong F_NETSIM = 1UL << 13;

    /// <summary>
    /// 生成热更新功能位掩码 (与 DLL 的 FEATURE_* 位定义一致, 通过管道 UPDATE|0x.. 下发)
    /// </summary>
    public static ulong BuildFeatureMask(HookSettings hook)
    {
        ulong mask = 0;
        if (hook.EnableKeyboardBypass) mask |= F_KEYBOARD;
        if (hook.EnableTopmostStrip) mask |= F_TOPMOST;
        if (hook.EnableFocusLock) mask |= F_FOCUS;
        if (hook.EnableAppList) mask |= F_APPLIST;
        if (hook.EnableProcList) mask |= F_PROCLIST;
        if (hook.EnableProcGuard) mask |= F_PROCGUARD;
        if (hook.EnableProcHookGuard) mask |= F_PROCHOOK;
        if (hook.EnableScreenFake) mask |= F_SCREENFAKE;
        if (hook.EnableScreenCap) mask |= F_SCREENCAP;
        if (hook.EnableBlackMonitor) mask |= F_BLACKMON;
        if (hook.EnableRemoteInput) mask |= F_REMOTE;
        if (hook.EnableInputLock) mask |= F_INPUTLOCK;
        if (hook.EnableFilterGuard) mask |= F_FILTER;
        if (hook.EnableNetSimBlock) mask |= F_NETSIM;
        return mask;
    }

    /// <summary>生成 UPDATE 管道命令 (十六进制掩码)</summary>
    public static string BuildUpdateCommand(HookSettings hook)
        => $"UPDATE|0x{BuildFeatureMask(hook):X}";

    /// <summary>
    /// 生成 hook.cfg 内容 (与 Native config/config.cpp 的解析器对应)
    /// </summary>
    public static string Build(HookSettings hook)
    {
        var sb = new StringBuilder();
        sb.AppendLine("; JiYuHelper hook configuration");
        sb.AppendLine("; key=value, bool: 0/1");
        sb.AppendLine();

        sb.AppendLine($"enableKeyboardBypass={(hook.EnableKeyboardBypass ? TrueVal : FalseVal)}");
        sb.AppendLine($"enableTopmostBlock={(hook.EnableTopmostStrip || hook.EnableFocusLock ? TrueVal : FalseVal)}");
        sb.AppendLine($"enableAppListBlock={(hook.EnableAppList ? TrueVal : FalseVal)}");
        sb.AppendLine($"enableProcListBlock={(hook.EnableProcList || hook.EnableProcGuard || hook.EnableProcHookGuard ? TrueVal : FalseVal)}");
        sb.AppendLine($"enableScreenFake={(hook.EnableScreenFake || hook.EnableScreenCap ? TrueVal : FalseVal)}");
        sb.AppendLine($"enableRemoteBlock={(hook.EnableRemoteInput || hook.EnableInputLock || hook.EnableFilterGuard ? TrueVal : FalseVal)}");
        sb.AppendLine($"enableBlackMonitor={(hook.EnableBlackMonitor ? TrueVal : FalseVal)}");
        sb.AppendLine($"enableNetSimBlock={(hook.EnableNetSimBlock ? TrueVal : FalseVal)}");
        return sb.ToString();
    }

    /// <summary>
    /// 写入 hook.cfg 到指定目录 (UTF-8, 无 BOM)
    /// </summary>
    public static void Write(string directory, HookSettings hook)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var content = Build(hook);
            File.WriteAllText(Path.Combine(directory, "hook.cfg"), content, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Logger.Error($"写入 hook.cfg 失败: {ex.Message}");
        }
    }
}
