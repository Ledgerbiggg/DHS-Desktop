using System.Threading.Tasks;
using Wpf.Ui.Controls;

namespace Dsh.Util;

/// <summary>基于 WPF-UI 的 MessageBox 封装：在激活的 FluentWindow 上弹出美观对话框</summary>
public static class MessageBoxHelper
{
    public static Task Info(string message, string title = "Dsh") => Show(title, message);

    public static Task Warn(string message, string title = "Dsh") => Show(title, message);

    public static Task Error(string message, string title = "Dsh") => Show(title, message);

    /// <summary>确认对话框：返回 true 表示用户点击了「确认」按钮</summary>
    public static async Task<bool> Confirm(string message, string title = "Dsh")
    {
        var msg = new MessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "确认",
            CloseButtonText = "取消",
        };
        var result = await msg.ShowDialogAsync();
        return result == MessageBoxResult.Primary;
    }

    private static Task Show(string title, string message)
    {
        var msg = new MessageBox
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
        };
        return msg.ShowDialogAsync();
    }
}
