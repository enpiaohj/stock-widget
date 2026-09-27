using System.Security.Cryptography;
using System.Text;

namespace StockWidget.Core.Services.Ai;

/// <summary>
/// API Key 保护：Windows DPAPI（DataProtectionScope.CurrentUser）加密，
/// 仅存密文；密文只能被同一 Windows 用户解密。空输入返回空串，非法密文解密失败返回空串。
/// </summary>
public static class AiCredentialProtector
{
    public static string Protect(string plainKey)
    {
        if (string.IsNullOrWhiteSpace(plainKey)) return "";
        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainKey), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            return "";
        }
    }

    public static string Unprotect(string encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return "";
        }
    }
}
