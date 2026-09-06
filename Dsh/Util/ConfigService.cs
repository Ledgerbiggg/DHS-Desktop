using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dsh.Models;

namespace Dsh.Util;

/// <summary>本地 JSON 配置读写服务：管理 %AppData%\Dsh\ 下的配置文件与数据目录</summary>
public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        // 允许中文直出，便于用户直接编辑配置文件
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>配置根目录（%AppData%\Dsh）</summary>
    public string RootDir { get; }

    /// <summary>全局设置文件路径</summary>
    public string SettingsPath { get; }

    /// <summary>WebView2 数据目录（登录态持久化）</summary>
    public string WebViewDataDir { get; }

    /// <summary>日志目录</summary>
    public string LogsDir { get; }

    /// <summary>以默认目录（%AppData%\Dsh）初始化</summary>
    public ConfigService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dsh"))
    {
    }

    /// <summary>指定根目录初始化，并确保目录结构存在</summary>
    public ConfigService(string rootDir)
    {
        RootDir = rootDir;
        SettingsPath = Path.Combine(RootDir, "settings.json");
        WebViewDataDir = Path.Combine(RootDir, "WebView2Data");
        LogsDir = Path.Combine(RootDir, "logs");
        EnsureDirs();
    }

    /// <summary>确保配置相关目录存在</summary>
    public void EnsureDirs()
    {
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(WebViewDataDir);
        Directory.CreateDirectory(LogsDir);
    }

    /// <summary>读取全局设置；文件缺失返回默认设置</summary>
    public AppSettings LoadSettings() => Load(SettingsPath, new AppSettings());

    /// <summary>保存全局设置到 settings.json</summary>
    public void SaveSettings(AppSettings settings)
    {
        Save(SettingsPath, settings);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>全局设置变更事件（主窗口据此重新注册热键）</summary>
    public event EventHandler? SettingsSaved;

    /// <summary>通用读取：文件不存在时返回默认值</summary>
    private static T Load<T>(string path, T fallback) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return fallback;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"读取配置失败: {path}", ex);
            return fallback;
        }
    }

    /// <summary>通用保存：先写临时文件再替换，避免写入中断损坏配置</summary>
    private static void Save<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, true);
    }
}
