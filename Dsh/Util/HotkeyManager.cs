using System.Runtime.InteropServices;

namespace Dsh.Util;

/// <summary>全局热键管理器（Win32 RegisterHotKey 封装）
/// 注册成功后通过 HandleMessage 分发事件，外部订阅 HotkeyPressed 接收</summary>
public sealed class HotkeyManager : IDisposable
{
    /// <summary>热键触发事件，参数为该热键的 Id（区分多个热键）</summary>
    public event EventHandler<string>? HotkeyPressed;

    private const int WmHotkey = 0x0312;
    private const int ModAlt = 0x0001;
    private const int ModControl = 0x0002;
    private const int ModShift = 0x0004;
    private const int ModWin = 0x0008;
    private const int ModNoRepeat = 0x4000;

    private IntPtr _hwnd;
    private readonly Dictionary<int, string> _idToAction = [];
    private bool _disposed;

    /// <summary>已注册的热键数量</summary>
    public int Count => _idToAction.Count;

    /// <summary>上次注册失败原因</summary>
    public string? LastError { get; private set; }

    /// <summary>注册一个全局热键；id 必须唯一</summary>
    public bool Register(IntPtr hwnd, string actionId, string modifierText, string keyText, int id)
    {
        _hwnd = hwnd;

        if (!TryParseModifiers(modifierText, out var modifiers, out var err))
        {
            LastError = err;
            return false;
        }
        if (!TryParseKey(keyText, out var vk, out err))
        {
            LastError = err;
            return false;
        }

        if (!RegisterHotKey(hwnd, id, modifiers | ModNoRepeat, vk))
        {
            LastError = $"热键注册失败（{modifierText}+{keyText}），可能被其他程序占用";
            return false;
        }

        _idToAction[id] = actionId;
        return true;
    }

    /// <summary>取消所有已注册的热键</summary>
    public void UnregisterAll()
    {
        foreach (var id in _idToAction.Keys)
        {
            UnregisterHotKey(_hwnd, id);
        }
        _idToAction.Clear();
    }

    /// <summary>处理窗口消息：收到 WM_HOTKEY 时按 id 分发事件</summary>
    public bool HandleMessage(int msg, IntPtr wParam)
    {
        if (msg != WmHotkey) return false;
        var id = wParam.ToInt32();
        if (_idToAction.TryGetValue(id, out var actionId))
        {
            HotkeyPressed?.Invoke(this, actionId);
            return true;
        }
        return false;
    }

    /// <summary>解析修饰键字符串（Ctrl / Alt / Shift / Win，+ 号组合）</summary>
    private static bool TryParseModifiers(string text, out uint modifiers, out string? error)
    {
        modifiers = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModWin;
                    break;
                default:
                    error = $"无法识别的修饰键: {part}";
                    return false;
            }
        }
        return true;
    }

    /// <summary>解析按键字符串为虚拟键码</summary>
    private static bool TryParseKey(string text, out uint vk, out string? error)
    {
        vk = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "按键不能为空";
            return false;
        }

        var key = text.Trim();
        switch (key.ToLowerInvariant())
        {
            case "space": vk = 0x20; return true;
            case "enter": vk = 0x0D; return true;
            case "tab": vk = 0x09; return true;
            case "esc":
            case "escape": vk = 0x1B; return true;
            case "back":
            case "backspace": vk = 0x08; return true;
            case "delete":
            case "del": vk = 0x2E; return true;
            case "home": vk = 0x24; return true;
            case "end": vk = 0x23; return true;
            case "pageup": vk = 0x21; return true;
            case "pagedown": vk = 0x22; return true;
            case "up": vk = 0x26; return true;
            case "down": vk = 0x28; return true;
            case "left": vk = 0x25; return true;
            case "right": vk = 0x27; return true;
        }

        if (key.Length == 1 && char.IsAsciiLetterUpper(key[0])) { vk = (uint)key[0]; return true; }
        if (key.Length == 1 && char.IsAsciiDigit(key[0])) { vk = (uint)key[0]; return true; }
        if (key.StartsWith('F') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + f - 1);
            return true;
        }

        error = $"无法识别的按键: {key}";
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
