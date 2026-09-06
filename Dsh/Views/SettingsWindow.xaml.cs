using System.Windows.Input;
using Dsh.ViewModels;
using Wpf.Ui.Controls;



namespace Dsh.Views;

/// <summary>设置窗口：开机自启 + 快捷键录制</summary>
public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(SettingsViewModel viewModel)
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
