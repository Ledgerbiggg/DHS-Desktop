using System.Text;

namespace Dsh.Util;

/// <summary>简单文件日志：按天滚动写入 logs\yyyy-MM-dd.log</summary>
public static class LoggerHelper
{
    private static readonly object Lock = new();
    private static string _logDir = Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>当前日志目录</summary>
    public static string LogDir => _logDir;

    /// <summary>设置日志目录（应用启动时由 ConfigService.LogsDir 传入）</summary>
    public static void SetLogDir(string dir) => _logDir = dir;

    /// <summary>记录信息级日志</summary>
    public static void Info(string message) => Write("INFO", message);

    /// <summary>记录错误级日志（含异常堆栈）</summary>
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}\n{ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(_logDir);
                var file = Path.Combine(_logDir, $"{DateTime.Now:yyyy-MM-dd}.log");
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(file, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不阻塞主流程
        }
    }
}
