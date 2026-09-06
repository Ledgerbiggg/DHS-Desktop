using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Dsh.Models;
using Dsh.Util;
using Dsh.ViewModels;
using Microsoft.Web.WebView2.Core;
using Wpf.Ui.Controls;

namespace Dsh.Views;

/// <summary>主窗口：WebView2 加载 DeepSeek 本地服务，全局热键呼出/隐藏，托盘常驻</summary>
public partial class MainWindow : FluentWindow
{
    /// <summary>单实例唤出消息</summary>
    private const int WmShowInstance = 0x0401;

    private readonly MainViewModel _vm;
    private readonly HotkeyManager _hotkeyManager;
    private readonly TrayService _trayService;
    private readonly ConfigService _config;
    private readonly DshHostService _dshHost;
    private readonly ThemeService _themeService;
    private AppSettings _settings;
    private HwndSource? _hwndSource;
    // 服务已就绪但 WebView2 仍在初始化时暂存的目标地址
    private string? _pendingUrl;

    public MainWindow(MainViewModel vm, HotkeyManager hotkeyManager,
        TrayService trayService, ConfigService config, DshHostService dshHost,
        ThemeService themeService)
    {
        InitializeComponent();
        // 容错设置窗口图标
        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/deepseek-dark_48x48.ico", UriKind.Absolute);
            Icon = new System.Windows.Media.Imaging.BitmapImage(iconUri);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("设置窗口图标失败（已忽略）", ex);
        }

        _vm = vm;
        _hotkeyManager = hotkeyManager;
        _trayService = trayService;
        _config = config;
        _dshHost = dshHost;
        _themeService = themeService;
        _settings = _config.LoadSettings();
        DataContext = vm;

        // 服务地址就绪（首次启动、失败重试、重启）后导航到带 token 的地址
        vm.NavigateRequested += (_, url) => Navigate(url);
        _trayService.ShowRequested += (_, _) => ToggleWindow();
        _trayService.OpenRequested += (_, _) => ShowWindow();
        _trayService.ExitRequested += (_, _) => ExitApp();

        // 设置保存后重新注册热键
        _config.SettingsSaved += (_, _) =>
        {
            _settings = _config.LoadSettings();
            RegisterHotkey();
        };

        ApplySavedWindowState();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    /// <summary>运行时基础服务是否已启动（防止 Loaded 与静默启动路径重复执行）</summary>
    private bool _runtimeStarted;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 静默启动时运行时已在后台启动（StartRuntime 幂等短路），这里补 WebView2 初始化；
        // WebView2 与 dsh 服务启动并行，服务先就绪则地址暂存 _pendingUrl，待其就绪后补导航
        StartRuntime();
        _ = InitWebViewAsync();
    }

    /// <summary>
    /// 静默启动入口（App.InitializeShell 在 StartHidden 开启时调用）：
    /// 窗口从头到尾不显示、不渲染。关键点：
    /// ① 必须在 EnsureHandle 之前压 Visibility=Hidden——WPF 创建 HWND 时若
    ///    Visibility 为默认 Visible，CreateWindowEx 会带 WS_VISIBLE 样式，
    ///    窗口创建那一瞬间就显示了（EnsureHandle 只是不调 Show，挡不住这个）；
    /// ② ShowInTaskbar 同样必须在句柄创建前关掉，否则任务栏图标闪现；
    /// ③ 句柄创建后 SetWindowText 补标题——窗口未布局时 XAML 的 Title 绑定
    ///    不求值，HWND 标题为空会让 App.NotifyMainWindow 的 FindWindow 找不到
    ///    窗口，导致二次启动唤出失效。
    /// EnsureHandle 会触发 SourceInitialized（消息钩子/主题监听/热键句柄就绪），
    /// 但不触发 Loaded——WebView2 与首次导航推迟到窗口被呼出时再初始化。
    /// </summary>
    internal void StartHiddenToTray()
    {
        // 顺序不可颠倒：先压可见性，再创建句柄
        Visibility = Visibility.Hidden;
        ShowInTaskbar = false;
        // 仅创建 HWND，不显示窗口、不触发 Loaded
        var handle = new WindowInteropHelper(this).EnsureHandle();
        SetWindowText(handle, App.MainWindowCaption);
        StartRuntime();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);

    /// <summary>启动运行时基础服务：消息钩子、托盘图标、dsh 后台服务、全局热键、更新检查。
    /// 显示启动（Loaded）与静默启动共用，幂等</summary>
    private void StartRuntime()
    {
        if (_runtimeStarted) return;
        _runtimeStarted = true;

        try
        {
            // 挂载窗口消息钩子：处理全局热键与单实例唤出
            //（静默启动时窗口尚未布局，FromVisual 可能取不到，回退按句柄查找）
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource
                          ?? HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _hwndSource?.AddHook(WndProc);

            _trayService.Show();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("启动基础服务异常", ex);
        }

        // dsh 服务启动不依赖窗口可见，尽早拉起
        _ = _vm.StartServiceAsync();

        // 热键注册整体容错，绝不阻塞界面
        try { RegisterHotkey(); }
        catch (Exception ex) { LoggerHelper.Error("RegisterHotkey 异常", ex); }

        // 启动时静默检查更新（不阻塞 UI）
        _ = _vm.CheckUpdateAtStartupAsync();
    }

    /// <summary>初始化 WebView2 并补上暂存的导航地址</summary>
    private async Task InitWebViewAsync()
    {
        try
        {
            await WebView.EnsureCoreWebView2Async();
            if (_pendingUrl is not null)
                Navigate(_pendingUrl);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("WebView2 初始化异常", ex);
        }
    }

    /// <summary>导航到指定地址；WebView2 尚未就绪时暂存，待初始化完成后补上</summary>
    private void Navigate(string url)
    {
        if (WebView.CoreWebView2 is null)
        {
            _pendingUrl = url;
            return;
        }
        _pendingUrl = null;
        try
        {
            WebView.CoreWebView2.Navigate(url);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"导航失败: {url}", ex);
        }
    }

    /// <summary>窗口句柄创建后挂上系统主题监听（跟随系统模式）</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // 句柄就绪后才能挂上系统主题监听（跟随系统模式），启动时补上这一环。
        // 静默启动不再在此隐藏窗口——那条"先显示再隐藏"的路径会闪屏，
        // 已改为 App.InitializeShell 静默路径根本不调用 Show（见 StartHiddenToTray）
        _themeService.Apply(_settings.Theme, this);
    }

    /// <summary>窗口消息处理：WM_HOTKEY + 单实例唤出</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkeyManager.HandleMessage(msg, wParam))
        {
            handled = true;
        }
        else if (msg == WmShowInstance)
        {
            handled = true;
            ToggleWindow();
        }
        return IntPtr.Zero;
    }

    /// <summary>注册全局热键（呼出/隐藏窗口）</summary>
    private void RegisterHotkey()
    {
        try
        {
            _hotkeyManager.UnregisterAll();
            _hotkeyManager.HotkeyPressed -= OnHotkeyPressed;
            _hotkeyManager.HotkeyPressed += OnHotkeyPressed;

            var hwnd = new WindowInteropHelper(this).Handle;
            var binding = _settings.ToggleHotkey;
            if (string.IsNullOrEmpty(binding.Key)) return;

            if (!_hotkeyManager.Register(hwnd, "ToggleWindow",
                    binding.Modifier, binding.Key, 0x1000))
            {
                LoggerHelper.Info($"热键注册失败: {binding.Modifier}+{binding.Key} — {_hotkeyManager.LastError}");
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("RegisterHotkey 整体异常", ex);
        }
    }

    /// <summary>热键触发：呼出/隐藏窗口</summary>
    private void OnHotkeyPressed(object? sender, string actionId)
    {
        if (actionId == "ToggleWindow")
            ToggleWindow();
    }

    /// <summary>呼出/隐藏窗口切换</summary>
    private bool _isToggling;

    private void ToggleWindow()
    {
        if (_isToggling) return;
        _isToggling = true;
        Dispatcher.BeginInvoke(new Action(() => _isToggling = false),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            ShowWindow();
            return;
        }
        if (!IsActive)
        {
            Activate();
            return;
        }
        Hide();
    }

    /// <summary>显示并激活窗口</summary>
    private void ShowWindow()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Show();
        ShowInTaskbar = true;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    /// <summary>
    /// 关闭按钮 → 隐藏到托盘常驻；应用级退出（托盘"退出"/升级安装）放行真正关闭。
    /// 注意：若在此无条件 e.Cancel = true，WPF 会连带取消 Application.Shutdown，
    /// 导致进程与 dsh 后台服务都退不掉（升级时还会占用 exe 文件）。
    /// </summary>
    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (App.IsExiting) return;

        e.Cancel = true;
        Hide();
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        SaveWindowState();
        _hwndSource?.RemoveHook(WndProc);
        _hotkeyManager.Dispose();
        // 真正退出时终止托管的 dsh web 服务，避免 3080 端口残留
        _dshHost.Stop();
    }

    /// <summary>恢复上次窗口位置与大小</summary>
    private void ApplySavedWindowState()
    {
        var win = _settings.Window;
        if (win.RememberPosition && win.Left is not null && win.Top is not null)
        {
            Left = win.Left.Value;
            Top = win.Top.Value;
        }
        Width = win.Width;
        Height = win.Height;
    }

    /// <summary>记录窗口位置与大小到 settings.json</summary>
    private void SaveWindowState()
    {
        var win = _settings.Window;
        if (WindowState == WindowState.Normal)
        {
            win.Left = Left;
            win.Top = Top;
        }
        win.Width = Width;
        win.Height = Height;
        _config.SaveSettings(_settings);
    }

    /// <summary>托盘"退出"：真正结束进程（dsh 后台服务随窗口关闭终止）</summary>
    private void ExitApp()
    {
        SaveWindowState();
        _trayService.Hide();
        // 先停后台服务再退出：即便后续窗口关闭流程出意外，也不会残留 dsh 进程
        _dshHost.Stop();
        App.RequestShutdown();
    }
}
