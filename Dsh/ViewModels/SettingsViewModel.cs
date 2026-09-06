using System.Windows.Input;
using Dsh.Models;
using Dsh.Util;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Mvvm;

namespace Dsh.ViewModels;

/// <summary>设置窗口 ViewModel：开机自启 + 快捷键录制 + 检查更新</summary>
public class SettingsViewModel : BindableBase
{
    private readonly ConfigService _config;
    private readonly AppSettings _settings;
    private readonly UpdateService _updateService;

    // —— 开机自启 ——
    private bool _autoStart;
    /// <summary>是否开机自动启动（注册表 Run 项）</summary>
    public bool AutoStart
    {
        get => _autoStart;
        set => SetProperty(ref _autoStart, value);
    }

    // —— 快捷键 ——
    private string _hotkeyModifier = "Alt";
    /// <summary>快捷键修饰键</summary>
    public string HotkeyModifier
    {
        get => _hotkeyModifier;
        set
        {
            SetProperty(ref _hotkeyModifier, value);
            RaisePropertyChanged(nameof(HotkeyDisplay));
        }
    }

    private string _hotkeyKey = "D";
    /// <summary>快捷键按键</summary>
    public string HotkeyKey
    {
        get => _hotkeyKey;
        set
        {
            SetProperty(ref _hotkeyKey, value);
            RaisePropertyChanged(nameof(HotkeyDisplay));
        }
    }

    private bool _isRecording;
    /// <summary>是否正在录制快捷键</summary>
    public bool IsRecording
    {
        get => _isRecording;
        set => SetProperty(ref _isRecording, value);
    }

    /// <summary>快捷键可读显示，如 "Alt + D"</summary>
    public string HotkeyDisplay
    {
        get
        {
            var mod = HotkeyModifier.Trim();
            var key = HotkeyKey.Trim();
            if (string.IsNullOrEmpty(mod) && string.IsNullOrEmpty(key)) return "未设置";
            if (string.IsNullOrEmpty(mod)) return key;
            return $"{mod} + {key}";
        }
    }

    // —— 检查更新 ——
    private bool _isBusy;
    /// <summary>是否正在检查/下载更新</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            SetProperty(ref _isBusy, value);
            CheckCommand.RaiseCanExecuteChanged();
        }
    }

    private string _updateStatusText = "";
    /// <summary>更新状态文本（检查中/下载中/结果等）</summary>
    public string UpdateStatusText
    {
        get => _updateStatusText;
        set => SetProperty(ref _updateStatusText, value);
    }

    /// <summary>当前版本号</summary>
    public string Version => MainViewModel.GetVersionString();

    public DelegateCommand SaveCommand { get; }
    public DelegateCommand RecordCommand { get; }
    public DelegateCommand CheckCommand { get; }

    public SettingsViewModel(ConfigService config, UpdateService updateService)
    {
        _config = config;
        _updateService = updateService;
        _settings = config.LoadSettings();

        AutoStart = GetAutoStart();
        HotkeyModifier = _settings.ToggleHotkey.Modifier;
        HotkeyKey = _settings.ToggleHotkey.Key;

        SaveCommand = new DelegateCommand(Save);
        RecordCommand = new DelegateCommand(ToggleRecording);
        CheckCommand = new DelegateCommand(CheckAsync, () => !IsBusy)
            .ObservesProperty(() => IsBusy);
    }

    /// <summary>保存设置到 settings.json 并写入/删除注册表自启项</summary>
    private void Save()
    {
        _settings.ToggleHotkey.Modifier = HotkeyModifier;
        _settings.ToggleHotkey.Key = HotkeyKey;
        _settings.AutoStart = AutoStart;
        SetAutoStart(AutoStart);
        _config.SaveSettings(_settings);
        _ = MessageBoxHelper.Info("设置已保存，快捷键立即生效。");
    }

    /// <summary>切换录制状态</summary>
    private void ToggleRecording()
    {
        IsRecording = !IsRecording;
    }

    /// <summary>由 View 的 PreviewKeyDown 调用：捕获组合键</summary>
    public void CaptureKey(ModifierKeys modifiers, Key key)
    {
        if (key == Key.Escape)
        {
            IsRecording = false;
            return;
        }

        var nonModifier = GetNonModifierKey(key);
        if (nonModifier == null) return;

        if (modifiers == ModifierKeys.None)
            return;

        IsRecording = false;
        HotkeyModifier = ModifiersToString(modifiers);
        HotkeyKey = nonModifier;
    }

    /// <summary>手动检查更新</summary>
    private async void CheckAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        UpdateStatusText = "正在检查更新…";

        try
        {
            var info = await _updateService.FetchLatestAsync();
            if (info is null)
            {
                UpdateStatusText = "检查失败，请稍后重试。";
                return;
            }

            var localVersion = MainViewModel.GetVersionString();
            if (UpdateService.IsNewer(info.Version, localVersion))
            {
                var msg = $"发现新版本 v{info.Version}\n\n更新说明：\n{info.Notes}\n\n是否立即下载并安装？";
                if (!await MessageBoxHelper.Confirm(msg, "发现新版本"))
                {
                    UpdateStatusText = $"发现新版本 v{info.Version}，可稍后升级。";
                    return;
                }

                await DownloadAndRunInstallerAsync(info.Version);
            }
            else
            {
                UpdateStatusText = $"当前已是最新版本 v{localVersion}。";
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("检查更新失败", ex);
            UpdateStatusText = "检查失败，请稍后重试。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>下载安装包并启动安装程序</summary>
    private async Task DownloadAndRunInstallerAsync(string version)
    {
        UpdateStatusText = "下载中 0%";
        var progress = new Progress<int>(p => UpdateStatusText = $"下载中 {p}%");
        var path = await _updateService.DownloadInstallerAsync(version, progress);
        if (path is null)
        {
            UpdateStatusText = "下载失败，请前往 GitHub 手动下载。";
            return;
        }

        if (!await MessageBoxHelper.Confirm("下载完成，是否立即安装？\n（安装程序将以管理员权限运行）", "下载完成"))
        {
            UpdateStatusText = "安装包已下载，可稍后安装。";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
        Application.Current.Shutdown();
    }

    /// <summary>读取开机自启状态</summary>
    private static bool GetAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("Dsh") is string v && !string.IsNullOrEmpty(v);
    }

    /// <summary>开启/关闭开机自启：写入或删除注册表 Run 项</summary>
    private static void SetAutoStart(bool enable)
    {
        var exePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? "";
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enable)
        {
            if (!string.IsNullOrEmpty(exePath))
            {
                key.SetValue("Dsh", exePath);
                LoggerHelper.Info($"已写入开机自启: {exePath}");
            }
        }
        else
        {
            key.DeleteValue("Dsh", false);
            LoggerHelper.Info("已移除开机自启");
        }
    }

    private static string? GetNonModifierKey(Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.System)
            return null;

        return key switch
        {
            Key.Space => "Space",
            Key.OemTilde => "`",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.Tab => "Tab",
            Key.Enter => "Enter",
            Key.Back => "Back",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            _ when key >= Key.D0 && key <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            _ when key >= Key.NumPad0 && key <= Key.NumPad9 => ((int)key - (int)Key.NumPad0).ToString(),
            _ when key >= Key.A && key <= Key.Z => key.ToString(),
            _ when key >= Key.F1 && key <= Key.F24 => key.ToString(),
            _ => key.ToString(),
        };
    }

    private static string ModifiersToString(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        return string.Join("+", parts);
    }
}
