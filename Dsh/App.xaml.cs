using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Dsh.Util;
using Dsh.ViewModels;
using Dsh.Views;
using Prism.Ioc;
using Prism.Unity;

namespace Dsh;

/// <summary>应用入口：Prism 依赖注入、单实例保护、全局异常兜底</summary>
public partial class App : PrismApplication
{
    /// <summary>单实例唤出消息（与主窗口 WndProc 约定一致）</summary>
    private const int WmShowInstance = 0x0401;

    private Mutex? _mutex;
    private bool _ownsMutex;

    /// <summary>是否为整个应用退出（区别于主窗口关闭后隐藏到托盘常驻）</summary>
    internal static bool IsExiting { get; private set; }

    /// <summary>静态持有 dsh 托管服务：进程退出兜底时 DI 容器可能已不可用</summary>
    private static DshHostService? _host;

    /// <summary>
    /// 请求整个应用退出：必须先置 <see cref="IsExiting"/>，否则主窗口 OnClosing 会按
    /// "关闭到托盘"拦截——WPF 中 Closing 被 Cancel 会连带取消 Application.Shutdown，
    /// 结果是进程不退出、dsh 后台服务与 3080 端口全部残留。
    /// </summary>
    internal static void RequestShutdown()
    {
        IsExiting = true;
        Current.Shutdown();
    }

    /// <summary>终止托管的 dsh web（幂等，所有退出路径共用）</summary>
    private static void StopHostSafe()
    {
        try { _host?.Stop(); }
        catch (Exception ex) { LoggerHelper.Error("停止本地服务失败", ex); }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局异常兜底：记录日志 + 弹窗提示，完整堆栈写入日志目录
        DispatcherUnhandledException += (_, args) =>
        {
            DumpCrash(args.Exception, "UI 线程未处理异常");
            LoggerHelper.Error("UI 线程未处理异常", args.Exception);
            ShowCrashDialog(args.Exception, "UI 线程未处理异常");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            DumpCrash(args.ExceptionObject as Exception, "AppDomain 未处理异常");
            LoggerHelper.Error("AppDomain 未处理异常", args.ExceptionObject as Exception);
            ShowCrashDialog(args.ExceptionObject as Exception, "AppDomain 未处理异常");
        };

        // 兜底：注销/关机、以及不走窗口关闭流程的退出（Environment.Exit、CLR 收尾）也要终止 dsh web
        SessionEnding += (_, _) => StopHostSafe();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopHostSafe();

        // 单实例：二次启动时通知已运行实例呼出窗口，自身退出
        _mutex = new Mutex(true, "Dsh_SingleInstance", out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            NotifyMainWindow();
            Shutdown();
            return;
        }
        // 主实例启动前先清理上一实例被强杀/崩溃残留的 dsh web 进程，
        // 避免 3080 端口被旧进程长期占用；本实例尚未拉起服务，不会误伤自身
        DshHostService.KillStaleDshProcesses();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 兜底：任何退出路径都终止托管的 dsh web，避免子进程残留占用 3080 端口
        StopHostSafe();

        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex?.Dispose();
        }
        base.OnExit(e);
    }

    /// <summary>弹窗防递归标志</summary>
    private static bool _isShowingCrashDialog;

    /// <summary>弹出未处理异常提示框</summary>
    private static void ShowCrashDialog(Exception? ex, string tag)
    {
        if (_isShowingCrashDialog)
            return;
        _isShowingCrashDialog = true;
        try
        {
            var detail = ex is null ? "（无异常对象）" : $"{ex.GetType().Name}: {ex.Message}";
            var msg = $"{tag}：\n\n{detail}\n\n详细信息已写入日志：{LoggerHelper.LogDir}";
            _ = MessageBoxHelper.Error(msg, "Dsh 异常提示")
                .ContinueWith(_ => _isShowingCrashDialog = false,
                    TaskScheduler.FromCurrentSynchronizationContext());
        }
        catch
        {
            _isShowingCrashDialog = false;
        }
    }

    /// <summary>把未处理异常完整堆栈写入日志目录 crash.log</summary>
    private static void DumpCrash(Exception? ex, string tag)
    {
        try
        {
            Directory.CreateDirectory(LoggerHelper.LogDir);
            var path = Path.Combine(LoggerHelper.LogDir, "crash.log");
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}");
            sb.AppendLine(ex?.ToString() ?? "（无异常对象）");
            sb.AppendLine(new string('-', 60));
            File.AppendAllText(path, sb.ToString());
        }
        catch { }
    }

    /// <summary>依赖注入注册：基础设施 + 服务 + 窗口/ViewModel</summary>
    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        var config = new ConfigService();
        LoggerHelper.SetLogDir(config.LogsDir);
        LoggerHelper.Info($"应用启动，配置目录: {config.RootDir}");
        containerRegistry.RegisterInstance(config);

        containerRegistry.RegisterSingleton<HotkeyManager>();
        containerRegistry.RegisterSingleton<TrayService>();
        containerRegistry.RegisterSingleton<UpdateService>();
        containerRegistry.RegisterSingleton<DshHostService>();
        containerRegistry.RegisterSingleton<ThemeService>();

        // 静态持有一份引用：进程退出兜底时容器可能已不可用
        try { _host = Container.Resolve<DshHostService>(); }
        catch (Exception ex) { LoggerHelper.Error("解析 DshHostService 失败", ex); }

        containerRegistry.RegisterSingleton<MainViewModel>();
        containerRegistry.RegisterSingleton<SettingsViewModel>();
        containerRegistry.RegisterSingleton<MainWindow>();
        // SettingsWindow 用瞬态注册：WPF Window 关闭后不能再次 Show
        containerRegistry.Register<SettingsWindow>();
    }

    /// <summary>创建主窗口前确保默认 settings.json 存在</summary>
    protected override Window? CreateShell()
    {
        var config = Container.Resolve<ConfigService>();
        var settings = config.LoadSettings();
        // 首次运行、或旧配置尚未包含新增的 Theme 字段时，写回一次让配置文件字段完整
        var raw = File.Exists(config.SettingsPath) ? File.ReadAllText(config.SettingsPath) : "";
        if (!raw.Contains("\"Theme\""))
            config.SaveSettings(settings);
        // 创建窗口前先套用主题，避免深色用户看到窗口先闪一下默认浅色
        Container.Resolve<ThemeService>().Apply(settings.Theme);
        return Container.Resolve<MainWindow>();
    }

    /// <summary>向已运行实例发送唤出消息</summary>
    private static void NotifyMainWindow()
    {
        var hwnd = FindWindow(null, "DeepSeek");
        if (hwnd != IntPtr.Zero)
            PostMessage(hwnd, WmShowInstance, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
