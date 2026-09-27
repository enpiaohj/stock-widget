using StockWidget.Core.Models;
using Xunit;

namespace StockWidget.Tests;

public class AccentPaletteTests
{
    [Fact]
    public void AllOptions_AreUniqueKeys_WithBrandDefaultFirst()
    {
        var all = AccentPalette.All;
        Assert.Equal(6, all.Count);
        Assert.All(all, p => Assert.False(string.IsNullOrWhiteSpace(p.Key)));
        Assert.All(all, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
        Assert.Equal("orange", all[0].Key); // 默认橙列首位
        Assert.Equal(all.Count, all.Select(p => p.Key).Distinct().Count()); // key 唯一
    }

    [Fact]
    public void FromKey_Unknown_FallsBackToDefault()
    {
        Assert.Equal("orange", AccentPalette.FromKey("nonexistent").Key);
        Assert.Equal("orange", AccentPalette.FromKey(null).Key);
        Assert.Equal("orange", AccentPalette.FromKey("").Key);
    }

    [Fact]
    public void Rgba_MasksAlphaAndRgb()
    {
        // 0xRRGGBB=FFA640, alpha=0x29 → 0x29FFA640
        Assert.Equal(0x29FFA640, AccentPalette.Rgba(0xFFA640, 0x29));
        // alpha=0xFF → 主色带 alpha（0xFFRRGGBB）
        Assert.Equal(0xFFFFA640, AccentPalette.Rgba(0xFFA640, 0xFF));
    }

    [Fact]
    public void AccentRgb_Range()
    {
        Assert.All(AccentPalette.All, p => Assert.InRange(p.AccentRgb, 0, 0xFFFFFF));
    }
}