using Microsoft.Extensions.DependencyInjection;
using StockWidget.Core.Services;

namespace StockWidget.Tests;

public class SettingsServiceFallbackTests : DatabaseTestBase
{
    [Fact]
    public void Default_CustomFields_IsRich()
    {
        var svc = Provider.GetRequiredService<ISettingsService>();
        Assert.NotEmpty(svc.Current.CustomFields);
        // 至少包含一个非必选字段（换手率/最高/最低等）
        Assert.Contains(svc.Current.CustomFields, k => k is "turnover" or "high" or "low" or "prev_close" or "amplitude");
    }

    [Fact]
    public void EmptySavedCustomFields_FallsBackToDefault()
    {
        var svc = Provider.GetRequiredService<ISettingsService>();
        var cfg = svc.Current.Clone();
        cfg.CustomFields.Clear();
        svc.Save(cfg);

        // 重载后 CustomFields 应被回退为默认可选集（非空）
        var reloaded = svc.Reload();
        Assert.NotEmpty(reloaded.CustomFields);
    }

    [Fact]
    public void Default_CategoryColors_IsEmpty()
    {
        var svc = Provider.GetRequiredService<ISettingsService>();
        Assert.Empty(svc.Current.CategoryColors);
    }

    [Fact]
    public void CategoryColors_RoundTripsThroughSaveAndReload()
    {
        var svc = Provider.GetRequiredService<ISettingsService>();
        var cfg = svc.Current.Clone();
        cfg.CategoryColors = new Dictionary<string, string>
        {
            ["index"] = "blue",
            ["stock"] = "red",
        };
        svc.Save(cfg);

        var reloaded = svc.Reload();
        Assert.Equal(2, reloaded.CategoryColors.Count);
        Assert.Equal("blue", reloaded.CategoryColors["index"]);
        Assert.Equal("red", reloaded.CategoryColors["stock"]);
    }

    [Fact]
    public void Clone_CategoryColors_IsIndependent()
    {
        var svc = Provider.GetRequiredService<ISettingsService>();
        var cfg = svc.Current.Clone();
        cfg.CategoryColors["index"] = "purple";

        // Clone 的字典不应与 Current 共享引用（取消设置时不得污染原值）
        Assert.DoesNotContain("index", svc.Current.CategoryColors);
    }

    // ---------- v0.3.0 AI 分析配置 ----------

    [Fact]
    public void Default_AiSettings_IsSane()
    {
        var svc = Provider.GetRequiredService<ISettingsService>();
        var ai = svc.Current.Ai;
        Assert.True(ai.Enabled);
        Assert.Equal("https://api.deepseek.com", ai.BaseUrl);
        Assert.Equal("deepseek-chat", ai.Model);
        Assert.Equal(60, ai.TimeoutSeconds);
        Assert.Equal("", ai.ApiKeyEncrypted);
    }

    [Fact]
    public void AiSettings_RoundTripsThroughSaveAndReload()
    {
        var svc = Provider.GetRequiredService<ISettingsService>();
        var cfg = svc.Current.Clone();
        cfg.Ai = new StockWidget.Core.Models.Ai.AiSettings
        {
            Enabled = false,
            BaseUrl = "https://custom.example.com",
            Model = "deepseek-reasoner",
            TimeoutSeconds = 90,
            ApiKeyEncrypted = "enc-data",
        };
        svc.Save(cfg);

        var reloaded = svc.Reload();
        Assert.False(reloaded.Ai.Enabled);
        Assert.Equal("https://custom.example.com", reloaded.Ai.BaseUrl);
        Assert.Equal("deepseek-reasoner", reloaded.Ai.Model);
        Assert.Equal(90, reloaded.Ai.TimeoutSeconds);
        Assert.Equal("enc-data", reloaded.Ai.ApiKeyEncrypted);
    }

    [Fact]
    public void Clone_AiSettings_IsIndependent()
    {
        var svc = Provider.GetRequiredService<ISettingsService>();
        var cfg = svc.Current.Clone();
        cfg.Ai.Model = "changed";

        Assert.NotEqual("changed", svc.Current.Ai.Model);
    }
}