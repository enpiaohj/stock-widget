using StockWidget.Core.Services;
using Xunit;

namespace StockWidget.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("ctrl+q")]
    [InlineData("alt+z")]
    [InlineData("ctrl+alt+s")]
    [InlineData("ctrl+shift+f5")]
    public void TryParse_ValidCombos(string combo)
    {
        Assert.True(HotkeyParser.TryParse(combo, out var mod, out var vk));
        Assert.NotEqual(0u, mod);
        Assert.NotEqual(0u, vk);
    }

    [Theory]
    [InlineData("")]
    [InlineData("q")]          // 无修饰键（与旧版校验一致：必须含 +）
    [InlineData("ctrl")]       // 只有修饰键
    [InlineData("ctrl+")]      // 主键为空
    [InlineData("ctrl+f13")]   // 不支持的主键
    public void TryParse_InvalidCombos(string combo)
    {
        Assert.False(HotkeyParser.TryParse(combo, out _, out _));
    }

    [Fact]
    public void TryParse_ModifierFlags()
    {
        HotkeyParser.TryParse("ctrl+q", out var mod, out var vk);
        Assert.Equal(HotkeyParser.ModControl, mod);
        Assert.Equal('Q', vk);

        HotkeyParser.TryParse("alt+z", out mod, out vk);
        Assert.Equal(HotkeyParser.ModAlt, mod);
        Assert.Equal('Z', vk);

        HotkeyParser.TryParse("ctrl+shift+f5", out mod, out vk);
        Assert.Equal(HotkeyParser.ModControl | HotkeyParser.ModShift, mod);
        Assert.Equal(0x74u, vk);
    }

    [Fact]
    public void NormalizeDisplay_OrdersModifiers()
    {
        Assert.Equal("ctrl+q", HotkeyParser.NormalizeDisplay("Q+CTRL"));
        Assert.Equal("alt+z", HotkeyParser.NormalizeDisplay("Z + Alt"));
    }
}
