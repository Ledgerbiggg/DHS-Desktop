using System.Windows;
using Dsh.Util;
using Dsh.Views;
using Prism.Commands;
using Prism.Ioc;
using Prism.Mvvm;

namespace Dsh.ViewModels;

/// <summary>主窗口 ViewModel：窗口标题、设置命令、版本号、启动检查更新</summary>
public class MainViewModel : BindableBase
{
    private readonly ConfigService _config;
    private readonly UpdateService _updateService;

    /// <summary>窗口标题</summary>
    public string Title => "DeepSeek";

    /// <summary>版本号</summary>
    public string Version => GetVersionString();

    /// <summary>DeepSeek 本地服务地址</summary>
    public string Url => "http://127.0.0.1:3080/";

    /// <summary>打开设置窗口命令</summary>
    public DelegateCommand OpenSettingsCommand { get; }

    /// <summary>刷新网页命令</summary>
    public DelegateCommand ReloadCommand { get; }

    /// <summary>点击新版本标识后执行升级命令</summary>
    public DelegateCommand UpdateCommand { get; }

    /// <summary>请求刷新网页（主窗口监听执行）</summary>
    public event EventHandler? ReloadRequested;

    // —— 自动升级 ——
    private bool _hasUpdate;
    /// <summary>是否检测到新版本（绑定标题栏标识可见性）</summary>
    public bool HasUpdate
    {
        get => _hasUpdate;
        set => SetProperty(ref _hasUpdate, value);
    }

    private string _updateBadgeText = "";
    /// <summary>标题栏新版本标识文本</summary>
    public string UpdateBadgeText
    {
        get => _updateBadgeText;
        set => SetProperty(ref _updateBadgeText, value);
    }

    private bool _isUpdating;
    /// <summary>是否正在下载安装包（防止重复触发）</summary>
    public bool IsUpdating
    {
        get => _isUpdating;
        set => SetProperty(ref _isUpdating, value);
    }

    /// <summary>设置窗口是否已打开</summary>
    public bool IsSettingsWindowOpen => _settingsWindow is { IsVisible: true };

    private SettingsWindow? _settingsWindow;

    public MainViewModel(ConfigService config, UpdateService updateService)
    {
        _config = config;
        _updateService = updateService;
        OpenSettingsCommand = new DelegateCommand(OpenSettings);
        ReloadCommand = new DelegateCommand(() => ReloadRequested?.Invoke(this, EventArgs.Empty));
        UpdateCommand = new DelegateCommand(UpdateAsync, () => !IsUpdating)
            .ObservesProperty(() => IsUpdating);
    }

    /// <summary>启动时静默检查更新：拉远程 version.json 比较版本</summary>
    public async Task CheckUpdateAtStartupAsync()
    {
        try
        {
            var info = await _updateService.FetchLatestAsync();
            if (info is null) return;

            var localVersion = GetVersionString();
            if (UpdateService.IsNewer(info.Version, localVersion))
            {
                HasUpdate = true;
                UpdateBadgeText = $"🆕 新版本 v{info.Version}";
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("启动检查更新失败", ex);
        }
    }

    /// <summary>用户点击新版本标识：弹窗确认 → 下载安装包 → 启动安装 → 退出</summary>
    private async void UpdateAsync()
    {
        if (IsUpdating) return;
        IsUpdating = true;
        UpdateBadgeText = "下载中…";

        try
        {
            var info = await _updateService.FetchLatestAsync();
            if (info is null)
            {
                _ = MessageBoxHelper.Warn("获取版本信息失败，请稍后重试。");
                UpdateBadgeText = "🆕 点击重试";
                return;
            }

            // 弹窗确认
            var msg = $"发现新版本 v{info.Version}\n\n更新说明：\n{info.Notes}\n\n是否立即下载并安装？";
            if (!await MessageBoxHelper.Confirm(msg, "发现新版本"))
                return;

            // 下载安装包
            UpdateBadgeText = "下载中 0%";
            var progress = new Progress<int>(p => UpdateBadgeText = $"下载中 {p}%");
            var path = await _updateService.DownloadInstallerAsync(info.Version, progress);
            if (path is null)
            {
                _ = MessageBoxHelper.Warn("下载安装包失败，请前往 GitHub 手动下载。");
                UpdateBadgeText = "🆕 下载失败，点击重试";
                return;
            }

            // 弹窗确认安装
            if (!await MessageBoxHelper.Confirm("下载完成，是否立即安装？\n（安装程序将以管理员权限运行）", "下载完成"))
                return;

            // 启动安装向导（UAC 由系统接管），退出主程序释放文件占用
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("UpdateAsync 失败", ex);
            _ = MessageBoxHelper.Error("升级失败：" + ex.Message);
            UpdateBadgeText = "🆕 升级失败，点击重试";
        }
        finally
        {
            IsUpdating = false;
        }
    }

    /// <summary>打开/关闭设置窗口</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Close();
            return;
        }
        try
        {
            var window = (SettingsWindow)Prism.Ioc.ContainerLocator.Container.Resolve(typeof(SettingsWindow));
            window.Owner = Application.Current.MainWindow;
            _settingsWindow = window;
            window.Closed += (_, _) => _settingsWindow = null;
            window.Show();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("打开设置窗口失败", ex);
            _ = MessageBoxHelper.Error("打开设置失败：" + ex.Message);
        }
    }

    /// <summary>统一读取版本号：优先 version.json，回退程序集版本</summary>
    internal static string GetVersionString()
    {
        try
        {
            var vj = System.IO.Path.Combine(AppContext.BaseDirectory, "version.json");
            if (System.IO.File.Exists(vj))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(vj));
                if (doc.RootElement.TryGetProperty("version", out var ve))
                {
                    var s = ve.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(s)) return Normalize(s);
                }
            }
        }
        catch { }

        try
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (v is not null) return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch { }

        return "0.0.0";
    }

    /// <summary>把任意版本串规范成 "主.次.修" 三段</summary>
    private static string Normalize(string raw)
    {
        var parts = raw.Split('.');
        var maj = parts.Length > 0 && int.TryParse(parts[0], out var a) ? a : 0;
        var min = parts.Length > 1 && int.TryParse(parts[1], out var b) ? b : 0;
        var bld = parts.Length > 2 && int.TryParse(parts[2], out var c) ? c : 0;
        return $"{maj}.{min}.{bld}";
    }
}
