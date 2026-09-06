using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Dsh.Util;

/// <summary>
/// 应用主题服务：管理「跟随系统 / 浅色 / 深色」三种模式。
/// 只有跟随系统模式才监听系统主题变化；显式选择浅色或深色时必须停止监听，
/// 否则系统主题一变就会把用户的选择覆盖掉。
/// </summary>
public class ThemeService
{
    /// <summary>跟随系统</summary>
    public const string System = "System";

    /// <summary>浅色</summary>
    public const string Light = "Light";

    /// <summary>深色</summary>
    public const string Dark = "Dark";

    private bool _isWatching;
    private Window? _watchedWindow;

    /// <summary>
    /// 应用主题。传入窗口才会启用系统主题监听——监听依赖窗口句柄，
    /// 应用启动早期（窗口尚未创建）只切换资源字典，等窗口就绪后再补上监听。
    /// </summary>
    public void Apply(string? mode, Window? window = null)
    {
        try
        {
            var m = Normalize(mode);
            if (m == System)
            {
                ApplicationThemeManager.ApplySystemTheme();
                StartWatch(window);
            }
            else
            {
                StopWatch();
                ApplicationThemeManager.Apply(
                    m == Dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
                    WindowBackdropType.Mica, true);
            }

            LoggerHelper.Info($"应用主题: {m}（监听系统变化={_isWatching}）");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"应用主题失败: {mode}", ex);
        }
    }

    /// <summary>规范模式取值：仅接受 Light / Dark，其余一律按跟随系统处理</summary>
    public static string Normalize(string? mode) => mode?.Trim() switch
    {
        Light => Light,
        Dark => Dark,
        _ => System,
    };

    /// <summary>开始监听系统主题变化（同一窗口不重复监听，否则会重复挂钩子）</summary>
    private void StartWatch(Window? window)
    {
        if (window is null || _isWatching) return;
        SystemThemeWatcher.Watch(window, WindowBackdropType.Mica, true);
        _watchedWindow = window;
        _isWatching = true;
    }

    private void StopWatch()
    {
        if (!_isWatching || _watchedWindow is null) return;
        SystemThemeWatcher.UnWatch(_watchedWindow);
        _watchedWindow = null;
        _isWatching = false;
    }
}
