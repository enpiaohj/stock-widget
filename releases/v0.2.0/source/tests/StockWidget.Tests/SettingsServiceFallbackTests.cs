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
}