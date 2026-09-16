using StockWidget.Core.Services.Ai;

namespace StockWidget.Tests;

/// <summary>DPAPI（CurrentUser）API Key 加解密。</summary>
public class AiCredentialProtectorTests
{
    [Fact]
    public void ProtectUnprotect_RoundTrips()
    {
        var key = "sk-test-abcdef123456";
        var encrypted = AiCredentialProtector.Protect(key);

        Assert.NotEqual(key, encrypted);                 // 不落明文
        Assert.DoesNotContain("sk-test", encrypted);
        Assert.Equal(key, AiCredentialProtector.Unprotect(encrypted));
    }

    [Fact]
    public void Protect_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal("", AiCredentialProtector.Protect(""));
        Assert.Equal("", AiCredentialProtector.Protect("   "));
    }

    [Fact]
    public void Unprotect_EmptyOrInvalid_ReturnsEmpty()
    {
        Assert.Equal("", AiCredentialProtector.Unprotect(""));
        Assert.Equal("", AiCredentialProtector.Unprotect("not-base64!!"));
        Assert.Equal("", AiCredentialProtector.Unprotect("AAAA")); // 合法 Base64 但非 DPAPI 密文
    }

    [Fact]
    public void Protect_SameInput_DifferentCiphertext()
    {
        // DPAPI 每次加密熵不同：确认没有确定性降级
        Assert.NotEqual(AiCredentialProtector.Protect("sk-abc"), AiCredentialProtector.Protect("sk-abc"));
    }
}
