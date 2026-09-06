// 消除 WPF + WinForms 同时引用导致的命名冲突
// 项目需要 WinForms 仅用于 NotifyIcon（系统托盘），其余一律用 WPF 版本
global using Application = System.Windows.Application;
global using MessageBox = Wpf.Ui.Controls.MessageBox;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using System.IO;
global using System.Diagnostics;
