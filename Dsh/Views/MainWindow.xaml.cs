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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // 挂载窗口消息钩子：处理全局热键与单实例唤出
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            _hwndSource?.AddHook(WndProc);

            _trayService.Show();

            // WebView2 运行时初始化与 dsh 服务启动并行，缩短首屏等待；
            // 服务就绪后经 NavigateRequested 回调导航，若此刻 WebView2 尚未就绪则暂存地址
            var webViewReady = WebView.EnsureCoreWebView2Async();
            _ = _vm.StartServiceAsync();

            await webViewReady;
            if (_pendingUrl is not null)
                Navigate(_pendingUrl);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("OnLoaded 加载异常", ex);
        }

        // 热键注册整体容错，绝不阻塞界面
        try { RegisterHotkey(); }
        catch (Exception ex) { LoggerHelper.Error("RegisterHotkey 异常", ex); }

        // 启动时静默检查更新（不阻塞 UI）
        _ = _vm.CheckUpdateAtStartupAsync();
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

    /// <summary>窗口句柄创建后：若配置了启动到托盘，直接隐藏</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // 句柄就绪后才能挂上系统主题监听（跟随系统模式），启动时补上这一环
        _themeService.Apply(_settings.Theme, this);

        if (_settings.StartHidden)
        {
            Visibility = Visibility.Hidden;
            ShowInTaskbar = false;
        }
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
