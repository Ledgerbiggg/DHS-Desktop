using System.Diagnostics;
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
    private readonly DshHostService _dshHost;

    /// <summary>窗口标题</summary>
    public string Title => "DeepSeek";

    /// <summary>版本号</summary>
    public string Version => GetVersionString();

    private string _url = DshHostService.FallbackUrl;
    /// <summary>DeepSeek 本地服务地址（含一次性 token，由 DshHostService 启动时获取）</summary>
    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value);
    }

    private bool _isServiceStarting = true;
    /// <summary>本地服务是否正在启动（显示加载遮罩）</summary>
    public bool IsServiceStarting
    {
        get => _isServiceStarting;
        set => SetProperty(ref _isServiceStarting, value);
    }

    private string? _serviceError;
    /// <summary>本地服务启动失败原因；为 null 表示正常</summary>
    public string? ServiceError
    {
        get => _serviceError;
        set => SetProperty(ref _serviceError, value);
    }

    /// <summary>打开设置窗口命令</summary>
    public DelegateCommand OpenSettingsCommand { get; }

    /// <summary>重启本地服务命令：终止 dsh web 后重新拉起</summary>
    public DelegateCommand RestartCommand { get; }

    /// <summary>点击新版本标识后执行升级命令</summary>
    public DelegateCommand UpdateCommand { get; }

    /// <summary>本地服务地址就绪，请求主窗口导航到该地址（首次加载与失败重试、重启共用）</summary>
    public event EventHandler<string>? NavigateRequested;

    /// <summary>本地服务启动失败后重试</summary>
    public DelegateCommand RetryServiceCommand { get; }

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

    public MainViewModel(ConfigService config, UpdateService updateService, DshHostService dshHost)
    {
        _config = config;
        _updateService = updateService;
        _dshHost = dshHost;
        OpenSettingsCommand = new DelegateCommand(OpenSettings);
        RetryServiceCommand = new DelegateCommand(() => _ = StartServiceAsync());
        // 启动中禁止再次重启，避免并行拉起多个 dsh 进程争抢 3080 端口
        RestartCommand = new DelegateCommand(() => _ = RestartServiceAsync(), () => !IsServiceStarting)
            .ObservesProperty(() => IsServiceStarting);
        UpdateCommand = new DelegateCommand(UpdateAsync, () => !IsUpdating)
            .ObservesProperty(() => IsUpdating);
    }

    /// <summary>启动本地 dsh 服务，取到带 token 的地址后通知主窗口导航</summary>
    public async Task StartServiceAsync()
    {
        IsServiceStarting = true;
        ServiceError = null;
        try
        {
            // 本地未安装 dsh（DeepSeek harness）时主动引导安装，并提供重新进入入口，
            // 避免直接尝试启动后只拿到笼统的失败信息
            if (!await EnsureDshInstalledAsync())
                return;

            // 重试场景先清理可能残留的进程，否则 3080 端口被占会再次启动失败
            _dshHost.Stop();
            Url = await _dshHost.StartAsync();
            if (!_dshHost.HasToken)
            {
                ServiceError = "未能启动本地服务。请确认已安装 dsh（PowerShell 中执行 dsh web 可正常启动），"
                    + "并检查 3080 端口未被其他程序占用。";
            }
            NavigateRequested?.Invoke(this, Url);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("启动本地服务失败", ex);
            ServiceError = "启动本地服务失败：" + ex.Message;
        }
        finally
        {
            IsServiceStarting = false;
        }
    }

    /// <summary>
    /// 启动前检测本地是否已安装 dsh；未安装则弹窗引导安装教程，
    /// 用户安装后可点「重新进入」再次检测并继续，或选择退出程序。
    /// </summary>
    /// <returns>true 表示已具备启动条件可继续；false 表示用户退出</returns>
    private async Task<bool> EnsureDshInstalledAsync()
    {
        if (await _dshHost.IsDshInstalledAsync())
            return true;

        while (true)
        {
            var choice = await MessageBoxHelper.ShowDshNotInstalledAsync();
            if (choice == DshDialogResult.OpenTutorial)
            {
                // 打开教程网页，用户安装完成后点「重新进入」再检测
                OpenDshTutorial();
                continue;
            }
            if (choice == DshDialogResult.Reenter)
            {
                // 用户称已安装，再次验证；仍检测不到则继续提示
                if (await _dshHost.IsDshInstalledAsync())
                    return true;
                continue;
            }
            // 退出程序
            App.RequestShutdown();
            return false;
        }
    }

    /// <summary>用默认浏览器打开 dsh 本地安装教程</summary>
    private static void OpenDshTutorial()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = MessageBoxHelper.DshInstallTutorialUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("打开 dsh 安装教程失败", ex);
        }
    }

    /// <summary>
    /// 重启本地服务：确认后终止当前 dsh web 进程（指令取消）并重新运行，
    /// 成功后取到新的带 token 地址，由 NavigateRequested 驱动界面重新加载。
    /// </summary>
    private async Task RestartServiceAsync()
    {
        var confirmed = await MessageBoxHelper.Confirm(
            "将终止当前的 DeepSeek harness 服务并重新启动，页面会重新加载。\n\n是否继续？",
            "重启 DeepSeek harness");
        if (!confirmed) return;

        // StartServiceAsync 内部会先 Stop 清理旧进程（含其拉起的 node 子进程）再重新启动
        await StartServiceAsync();
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
            App.RequestShutdown();
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
