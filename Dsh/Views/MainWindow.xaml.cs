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
    private AppSettings _settings;
    private HwndSource? _hwndSource;
    private bool _closingToTray = true;

    public MainWindow(MainViewModel vm, HotkeyManager hotkeyManager,
        TrayService trayService, ConfigService config)
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
        _settings = _config.LoadSettings();
        DataContext = vm;

        vm.ReloadRequested += (_, _) => WebView.Reload();
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

            // 初始化 WebView2 并加载 DeepSeek 本地服务
            await WebView.EnsureCoreWebView2Async();
            WebView.CoreWebView2.NavigationCompleted += (_, _) => { };
            WebView.Source = new Uri(_vm.Url);
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

    /// <summary>窗口句柄创建后：若配置了启动到托盘，直接隐藏</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
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

    /// <summary>关闭按钮 → 隐藏到托盘常驻（真正退出走托盘"退出"）</summary>
    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closingToTray)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        SaveWindowState();
        _hwndSource?.RemoveHook(WndProc);
        _hotkeyManager.Dispose();
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

    /// <summary>托盘"退出"：真正结束进程</summary>
    private void ExitApp()
    {
        _closingToTray = false;
        SaveWindowState();
        _trayService.Hide();
        Application.Current.Shutdown();
    }
}
