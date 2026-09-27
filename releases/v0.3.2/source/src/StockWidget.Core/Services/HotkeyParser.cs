using System.Runtime.InteropServices;

namespace StockWidget.Core.Services;

// ---------------------------
// 全局热键（Win32 RegisterHotKey；窗口钩子在 App 侧接入）
// ---------------------------

/// <summary>热键组合解析："ctrl+alt+s" → (MOD_CONTROL|MOD_ALT, 'S' 的 VK)。</summary>
public static class HotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>解析结果；非法组合返回 false。</summary>
    public static bool TryParse(string combo, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(combo)) return false;

        var parts = combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false; // 必须含修饰键 + 主键（与旧版校验一致）

        foreach (var raw in parts)
        {
            var p = raw.ToLowerInvariant();
            switch (p)
            {
                case "ctrl" or "control": modifiers |= ModControl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win" or "windows": modifiers |= ModWin; break;
                default:
                    if (!TryParseKey(p, out virtualKey)) return false;
                    break;
            }
        }

        return modifiers != 0 && virtualKey != 0;
    }

    private static bool TryParseKey(string key, out uint vk)
    {
        vk = key switch
        {
            // 字母 / 数字
            _ when key.Length == 1 && char.IsAsciiLetter(key[0]) => char.ToUpperInvariant(key[0]),
            _ when key.Length == 1 && char.IsAsciiDigit(key[0]) => key[0],
            "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73,
            "f5" => 0x74, "f6" => 0x75, "f7" => 0x76, "f8" => 0x77,
            "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
            "space" => 0x20, "esc" or "escape" => 0x1B,
            "tab" => 0x09, "`" => 0xC0, "~" => 0xC0,
            _ => 0,
        };
        return vk != 0;
    }

    /// <summary>展示用规范化："ctrl+Q" → "ctrl+q"。</summary>
    public static string NormalizeDisplay(string combo) =>
        string.Join("+", combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.ToLowerInvariant())
            .OrderBy(p => p is "ctrl" or "control" ? 0 : p == "alt" ? 1 : p == "shift" ? 2 : p is "win" or "windows" ? 3 : 4)
            .ThenBy(p => p));
}

/// <summary>RegisterHotKey / UnregisterHotKey P/Invoke（在指定 HWND 上注册）。</summary>
public static class HotkeyNative
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const int WmHotkey = 0x0312;
}
