namespace JiYuHelper.Core;

public enum LogLevel
{
    Info,
    Success,
    Attack,
    Warning,
    Error
}

public class LogEntry
{
    public DateTime Time { get; set; } = DateTime.Now;
    public LogLevel Level { get; set; }
    public string Message { get; set; } = "";
    public string Display => $"[{Time:HH:mm:ss.fff}] {Message}";
}

public static class Logger
{
    private const int MaxHistory = 2000;

    private static readonly object Lock = new();
    private static readonly List<LogEntry> History = new();

    public static event Action<LogEntry>? EntryAdded;

    public static void Log(LogLevel level, string message)
    {
        var entry = new LogEntry { Level = level, Message = message };

        lock (Lock)
        {
            History.Add(entry);
            if (History.Count > MaxHistory)
                History.RemoveAt(0);
        }

        EntryAdded?.Invoke(entry);
    }

    /// <summary>
    /// 获取历史日志快照 (供 UI 创建后回放, 解决"先操作后打开日志页"无日志问题)
    /// </summary>
    public static LogEntry[] GetHistory()
    {
        lock (Lock)
            return History.ToArray();
    }

    public static void Info(string msg) => Log(LogLevel.Info, msg);
    public static void Success(string msg) => Log(LogLevel.Success, msg);
    public static void Attack(string msg) => Log(LogLevel.Attack, msg);
    public static void Warning(string msg) => Log(LogLevel.Warning, msg);
    public static void Error(string msg) => Log(LogLevel.Error, msg);
}
