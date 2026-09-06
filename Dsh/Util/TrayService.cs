using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Dsh.Util;

/// <summary>系统托盘服务（基于 System.Windows.Forms.NotifyIcon）：
/// 左键单击图标打开/呼出主界面，右键菜单显示/隐藏与退出</summary>
public class TrayService : IDisposable
{
    private NotifyIcon? _trayIcon;
    private bool _disposed;

    /// <summary>托盘"显示/隐藏"请求（右键菜单触发，行为为切换）</summary>
    public event EventHandler? ShowRequested;

    /// <summary>托盘"打开/呼出"请求（左键单击图标触发，行为仅为显示）</summary>
    public event EventHandler? OpenRequested;

    /// <summary>托盘"退出"请求（菜单触发）</summary>
    public event EventHandler? ExitRequested;

    /// <summary>创建并显示托盘图标</summary>
    public void Show()
    {
        if (_trayIcon is not null)
            return;

        _trayIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "DeepSeek",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                OpenRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>隐藏并销毁托盘图标</summary>
    public void Hide()
    {
        if (_trayIcon is null)
            return;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    /// <summary>加载托盘图标：优先从 exe 内嵌资源（WPF Resource）读取，
    /// 单文件发布/安装后不依赖外部文件；失败再回退输出目录文件与系统图标。
    /// 背景：csproj 中同一 ico 被 Resource 与 Content 双重声明，单文件 publish
    /// 时 Content 复制被 Resource 声明吞掉，产物缺少 Assets 文件，导致安装后
    /// 托盘显示系统默认图标</summary>
    private static Icon LoadIcon()
    {
        try
        {
            var sri = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/deepseek-dark_48x48.ico"));
            if (sri != null)
            {
                using var stream = sri.Stream;
                return new Icon(stream);
            }
        }
        catch
        {
            // 嵌入资源异常时走文件回退，不在此处中断托盘创建
        }

        // 开发期 Debug 目录的 Content 副本（仅作回退，安装环境下通常不存在）
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "deepseek-dark_48x48.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }

    /// <summary>构建托盘右键菜单</summary>
    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var toggle = new ToolStripMenuItem("显示 / 隐藏");
        toggle.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(toggle);
        menu.Items.Add(exit);
        return menu;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Hide();
    }
}
