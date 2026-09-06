using System.Threading.Tasks;
using Wpf.Ui.Controls;

namespace Dsh.Util;

/// <summary>基于 WPF-UI 的 MessageBox 封装：在激活的 FluentWindow 上弹出美观对话框</summary>
public static class MessageBoxHelper
{
    /// <summary>dsh（DeepSeek harness）本地安装教程地址，未安装时引导用户前往</summary>
    public const string DshInstallTutorialUrl =
        "https://www.runoob.com/deepseek-harness/deepseek-harness-install.html";

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

    /// <summary>
    /// 本地未安装 dsh 时的引导弹窗：三个选项对应
    /// 打开安装教程（OpenTutorial）/ 已安装重新进入（Reenter）/ 退出（Exit）。
    /// </summary>
    public static async Task<DshDialogResult> ShowDshNotInstalledAsync()
    {
        var msg = new MessageBox
        {
            Title = "未检测到 DeepSeek harness (dsh)",
            Content = "本地未安装 dsh（DeepSeek harness），无法启动本地服务。\n\n" +
                      "请先在本地安装 dsh，再重新进入程序。\n" +
                      "安装教程：\n" + DshInstallTutorialUrl,
            PrimaryButtonText = "打开安装教程",
            SecondaryButtonText = "我已安装，重新进入",
            CloseButtonText = "退出",
        };
        var result = await msg.ShowDialogAsync();
        return result switch
        {
            MessageBoxResult.Primary => DshDialogResult.OpenTutorial,
            MessageBoxResult.Secondary => DshDialogResult.Reenter,
            _ => DshDialogResult.Exit,
        };
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

/// <summary>未安装 dsh 引导弹窗的返回结果</summary>
public enum DshDialogResult
{
    /// <summary>用户选择打开安装教程</summary>
    OpenTutorial,
    /// <summary>用户称已安装，请求重新进入（再次检测并启动）</summary>
    Reenter,
    /// <summary>用户选择退出程序</summary>
    Exit,
}
