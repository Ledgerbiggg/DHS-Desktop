using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dsh.Util;

/// <summary>应用图标统一加载入口（托盘 + 窗口共用）。
/// 背景：单文件发布(PublishSingleFile)下磁盘上没有 Assets 副本（并入 bundle），
/// 且 WPF 对 pack URI 的资源查找会退化到 ContentFile 路径并以 null 基路径抛
/// ArgumentNullException(path1)，导致安装后托盘显示系统默认图标。
/// 因此首选从 exe 自身内嵌的 Win32 图标提取（csproj 的 ApplicationIcon），
/// 该方式对 Debug / 安装 / 单文件环境全部适用；pack URI 与磁盘文件仅作回退。</summary>
public static class AppIcon
{
    /// <summary>窗口图标（WPF ImageSource）：
    /// pack URI 优先（非单文件环境可取到高质量多尺寸帧），
    /// 失败回退从 exe 提取的 Win32 图标转换</summary>
    public static ImageSource? GetWindowIcon()
    {
        try
        {
            var sri = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/deepseek-dark_48x48.ico"));
            if (sri != null)
            {
                using var stream = sri.Stream;
                var frame = BitmapDecoder
                    .Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
                    .Frames.MaxBy(f => f.PixelWidth);
                if (frame != null)
                {
                    frame.Freeze();
                    return frame;
                }
            }
        }
        catch
        {
            // 单文件/安装环境走 exe 提取回退
        }

        try
        {
            using var icon = ExtractFromExecutable();
            if (icon == null) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>托盘图标（WinForms Icon）：首选从 exe 内嵌 Win32 图标提取。
    /// 每次返回新实例——NotifyIcon.Dispose 会连带处置传入的 Icon，不能共享缓存</summary>
    public static Icon GetTrayIcon()
    {
        var fromExe = ExtractFromExecutable();
        if (fromExe != null) return fromExe;

        try
        {
            var sri = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/deepseek-dark_48x48.ico"));
            if (sri != null)
            {
                using var stream = sri.Stream;
                return new Icon(stream);
            }
        }
        catch
        {
            // 走磁盘文件回退
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "deepseek-dark_48x48.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }

    /// <summary>从当前 exe 提取内嵌 Win32 图标</summary>
    private static Icon? ExtractFromExecutable()
    {
        try
        {
            var exe = Environment.ProcessPath;
            return exe == null ? null : Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
            return null;
        }
    }
}
