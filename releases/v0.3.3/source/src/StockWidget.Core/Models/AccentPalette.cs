namespace StockWidget.Core.Models;

/// <summary>强调色候选（纯数据，供 UI 派生整套强调相关刷）。<c>AccentRgb</c> 为 0xRRGGBB。</summary>
public sealed record AccentPalette(string Key, string Name, int AccentRgb)
{
    public static readonly IReadOnlyList<AccentPalette> All =
    [
        new("orange", "默认橙", 0xFFA640),
        new("blue", "蓝", 0x38BDF8),
        new("green", "绿", 0x22C55E),
        new("purple", "紫", 0xA78BFA),
        new("red", "红", 0xF43F5E),
        new("teal", "青", 0x2DD4BF),
    ];

    public static AccentPalette FromKey(string? key) =>
        All.FirstOrDefault(p => p.Key.Equals(key ?? "", StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>由 0xRRGGBB 派生带 alpha 的颜色值（0xAARRGGBB）。</summary>
    public static long Rgba(int rgb, int alpha) => ((long)(alpha & 0xFF) << 24) | (rgb & 0xFFFFFFL);

    /// <summary>强调主色（0xFF_主色）。</summary>
    public long Main => Rgba(AccentRgb, 0xFF);
    /// <summary>柔和强调（16% alpha）。</summary>
    public long Soft => Rgba(AccentRgb, 0x29);
    /// <summary>强调头/竖条（~80% alpha）。</summary>
    public long Strong => Rgba(AccentRgb, 0xCC);
}