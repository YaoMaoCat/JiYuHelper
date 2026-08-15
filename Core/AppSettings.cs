using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JiYuHelper.Core;

/// <summary>
/// 设置 JSON 序列化上下文 (源生成)。
/// Release 发布启用裁剪 (PublishTrimmed) 后, 反射式序列化会被禁用,
/// 必须使用 JsonSerializerContext 源生成才能正常读写 settings.json。
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
public partial class AppSettingsJsonContext : JsonSerializerContext
{
}

public enum ThemeOption
{
    System = 0,
    Light = 1,
    Dark = 2,
    Blue = 3,
}

/// <summary>
/// 界面模式: 新手模式隐藏各页技术细节说明, 开发者模式显示全部。
/// 默认开发者模式 (保持现有行为)。
/// </summary>
public enum UiModeOption
{
    Novice = 0,
    Developer = 1,
}

/// <summary>
/// Hook 功能设置 — 与控制页 14 个开关一一对应, 默认全部开启 (开箱即用)。
/// 写 hook.cfg 时按分组映射到 DLL 的 7 个配置键 (见 HookConfigWriter)。
/// </summary>
public class HookSettings
{
    // ---- 远程控制拦截 (脱控) ----
    /// <summary>远程输入拦截 (hook/remote.cpp SendInput/BlockInput) → cfg: enableRemoteBlock</summary>
    public bool EnableRemoteInput { get; set; } = true;

    /// <summary>输入锁定放行 (hook/tdmaster.cpp LockLocalInput) → cfg: enableRemoteBlock</summary>
    public bool EnableInputLock { get; set; } = true;

    /// <summary>进程操作守护 (hook/procguard.cpp 进程/关机) → cfg: enableProcListBlock</summary>
    public bool EnableProcGuard { get; set; } = true;

    /// <summary>进程终止能力屏蔽 (master_hook/guard.cpp TDProcHookEnableTerminate) → cfg: enableProcListBlock</summary>
    public bool EnableProcHookGuard { get; set; } = true;

    /// <summary>设备过滤屏蔽 (hook/filterguard.cpp USB/CD/程序) → cfg: enableRemoteBlock</summary>
    public bool EnableFilterGuard { get; set; } = true;

    /// <summary>网络仿真屏蔽 (master_hook/guard.cpp BeginSimulate/StopSimulate) → cfg: enableNetSimBlock</summary>
    public bool EnableNetSimBlock { get; set; } = true;

    // ---- 界面与进程 (窗口化) ----
    /// <summary>置顶窗口剥离 (hook/topmost.cpp WS_EX_TOPMOST) → cfg: enableTopmostBlock</summary>
    public bool EnableTopmostStrip { get; set; } = true;

    /// <summary>焦点锁定拦截 (hook/focuslock.cpp) → cfg: enableTopmostBlock</summary>
    public bool EnableFocusLock { get; set; } = true;

    /// <summary>应用列表屏蔽 (hook/applist.cpp EnumWindows) → cfg: enableAppListBlock</summary>
    public bool EnableAppList { get; set; } = true;

    /// <summary>进程列表屏蔽 (hook/proclist.cpp) → cfg: enableProcListBlock</summary>
    public bool EnableProcList { get; set; } = true;

    // ---- 屏幕监控 ----
    /// <summary>屏幕假屏 (hook/screen.cpp JPEG 注入) → cfg: enableScreenFake</summary>
    public bool EnableScreenFake { get; set; } = true;

    /// <summary>屏幕捕获屏蔽 (hook/screencap.cpp BitBlt) → cfg: enableScreenFake</summary>
    public bool EnableScreenCap { get; set; } = true;

    /// <summary>自动监测 screen.png 变化并通知 DLL 重载假屏 (仅 App 侧)</summary>
    public bool AutoReloadScreen { get; set; } = true;

    /// <summary>黑屏监控 (monitor/monitor.cpp) → cfg: enableBlackMonitor</summary>
    public bool EnableBlackMonitor { get; set; } = true;

    // ---- 输入 ----
    /// <summary>键盘钩子绕过 (hook/keyboard.cpp) → cfg: enableKeyboardBypass</summary>
    public bool EnableKeyboardBypass { get; set; } = true;

    /// <summary>自动重注入 (进程重启后自动再次注入, 仅 App 侧使用)</summary>
    public bool EnableAutoReinject { get; set; } = true;

    /// <summary>是否启用了任何 Hook 功能</summary>
    public bool AnyEnabled =>
        EnableRemoteInput || EnableInputLock || EnableProcGuard || EnableProcHookGuard ||
        EnableFilterGuard || EnableNetSimBlock || EnableTopmostStrip || EnableFocusLock ||
        EnableAppList || EnableProcList || EnableScreenFake || EnableScreenCap ||
        EnableBlackMonitor || EnableKeyboardBypass;
}

public class AppSettings
{
    public string WindowTitle { get; set; } = "JiYuHelper";
    public string Theme { get; set; } = nameof(ThemeOption.System);

    /// <summary>界面模式 (新手/开发者), 空表示尚未选择 (首次启动引导)</summary>
    public string UiMode { get; set; } = "";

    /// <summary>组播公告监听端口 (0 = 默认 4988; 不同版本极域可能用 4705 等)</summary>
    public int MulticastPort { get; set; }

    /// <summary>教师端 TCP 控制端口 (0 = 默认 4806)</summary>
    public int ControlPort { get; set; }

    /// <summary>会话通道 UDP 端口 (0 = 默认 5512)</summary>
    public int SessionPort { get; set; }

    /// <summary>是否已接受启动免责声明 (接受后不再弹出)</summary>
    public bool DisclaimerAccepted { get; set; }

    /// <summary>Hook 功能设置 (由「控制」页读写)</summary>
    public HookSettings Hook { get; set; } = new();
}

/// <summary>
/// 设置持久化: %LocalAppData%\JiYuHelper\settings.json
/// 目录解析失败时回退 LOCALAPPDATA 环境变量, 再回退程序目录 (32 位环境下个别系统
/// 的 GetFolderPath 可能返回空, 导致文件写到不可预期的相对路径)。
/// </summary>
public static class SettingsStore
{
    private static readonly string Dir = ResolveSettingsDir();
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    /// <summary>设置文件完整路径 (供日志/诊断显示)</summary>
    public static string SettingsPath => FilePath;

    private static string ResolveSettingsDir()
    {
        try
        {
            string? baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Environment.GetEnvironmentVariable("LOCALAPPDATA");

            if (!string.IsNullOrEmpty(baseDir))
            {
                var dir = Path.Combine(baseDir, "JiYuHelper");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"设置目录解析失败: {ex.Message}");
        }

        // 回退: 程序目录 (exe 同目录)
        Logger.Warning("设置目录不可用, 回退到程序目录");
        return AppContext.BaseDirectory;
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
                if (s != null) return s;
            }
            Logger.Info($"设置文件不存在, 使用默认设置: {FilePath}");
        }
        catch (Exception ex)
        {
            Logger.Error($"设置加载失败: {ex.Message}");
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Error($"设置保存失败 ({FilePath}): {ex.Message}");
        }
    }

    public static ThemeOption ParseTheme(string name)
    {
        return Enum.TryParse<ThemeOption>(name, out var t) ? t : ThemeOption.System;
    }

    public static UiModeOption ParseUiMode(string? name)
    {
        return Enum.TryParse<UiModeOption>(name, out var m) ? m : UiModeOption.Developer;
    }
}
