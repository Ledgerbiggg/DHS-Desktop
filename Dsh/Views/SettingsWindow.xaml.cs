using System.Windows.Input;
using Dsh.Util;
using Dsh.ViewModels;
using Wpf.Ui.Controls;

namespace Dsh.Views;

/// <summary>设置窗口：开机自启 + 快捷键录制</summary>
public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        // 容错设置窗口图标（单文件发布下 pack URI 失效，见 AppIcon）
        try
        {
            Icon = AppIcon.GetWindowIcon();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("设置窗口图标加载失败（已忽略）: " + ex.Message);
        }
        DataContext = viewModel;
    }

    /// <summary>录制状态下捕获组合键</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        if (!vm.IsRecording) return;

        e.Handled = true;
        vm.CaptureKey(Keyboard.Modifiers, e.Key);
    }
}
