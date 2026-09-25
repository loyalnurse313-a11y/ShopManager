using System;
using System.Security.Cryptography;
using System.Text;

namespace ShopManager.Desktop.Services;

/// <summary>
/// هش و بررسی رمز عبور با SHA256 + Salt
/// رمزها هیچ‌وقت به صورت خام ذخیره نمی‌شن
/// </summary>
public static class PasswordHasher
{
    /// <summary>ساخت Salt تصادفی (۱۶ بایت)</summary>
    public static string GenerateSalt()
    {
        var saltBytes = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(saltBytes);
        }
        return Convert.ToBase64String(saltBytes);
    }

    /// <summary>هش کردن رمز با Salt</summary>
    public static string HashPassword(string password, string salt)
    {
        if (string.IsNullOrEmpty(password)) return "";

        using var sha = SHA256.Create();
        var combined = password + salt;
        var bytes = Encoding.UTF8.GetBytes(combined);
        var hash = sha.ComputeHash(bytes);

        return Convert.ToBase64String(hash);
    }

    /// <summary>بررسی اینکه رمز وارد‌شده با هش ذخیره‌شده مطابقت داره یا نه</summary>
    public static bool VerifyPassword(string password, string salt, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            return false;

        var computedHash = HashPassword(password, salt);
        return computedHash == storedHash;
    }
}