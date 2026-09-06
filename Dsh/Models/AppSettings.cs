namespace Dsh.Models;

/// <summary>全局设置（对应 settings.json）</summary>
public class AppSettings
{
    /// <summary>全局热键：呼出/隐藏主窗口。默认 Alt+D</summary>
    public HotkeyBinding ToggleHotkey { get; set; } = new()
    {
        Modifier = "Alt",
        Key = "D"
    };

    /// <summary>窗口尺寸与位置记忆</summary>
    public WindowSettings Window { get; set; } = new();

    /// <summary>是否开机自动启动（注册表 Run 项）</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>启动时是否隐藏到托盘</summary>
    public bool StartHidden { get; set; } = false;
}

/// <summary>单条热键绑定</summary>
public class HotkeyBinding
{
    /// <summary>修饰键组合，如 "Ctrl+Alt"</summary>
    public string Modifier { get; set; } = "";

    /// <summary>触发按键，如 "Space"、"A"、"F1"</summary>
    public string Key { get; set; } = "";
}

/// <summary>窗口尺寸与位置记忆</summary>
public class WindowSettings
{
    /// <summary>窗口宽</summary>
    public double Width { get; set; } = 900;

    /// <summary>窗口高</summary>
    public double Height { get; set; } = 680;

    /// <summary>是否记住上次窗口位置</summary>
    public bool RememberPosition { get; set; } = true;

    /// <summary>上次窗口 Left</summary>
    public double? Left { get; set; }

    /// <summary>上次窗口 Top</summary>
    public double? Top { get; set; }
}
