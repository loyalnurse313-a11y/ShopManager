using System;
using System.Security.Cryptography;
using System.Text;

namespace ShopManager.Desktop.Services;

/// <summary>
/// هش و بررسی رمز عبور — PBKDF2 با پشتیبانی از کاربران قدیمی (SHA256)
/// رمزها هیچ‌وقت به صورت خام ذخیره نمی‌شن
/// </summary>
public static class PasswordHasher
{
    /// <summary>تعداد تکرارهای PBKDF2</summary>
    private const int Iterations = 100000;

    /// <summary>اندازه salt به بایت</summary>
    private const int SaltSize = 16;

    /// <summary>اندازه خروجی هش به بایت</summary>
    private const int HashSize = 32;

    /// <summary>پیشوند نسخه PBKDF2 در هش ذخیره‌شده</summary>
    private const string Prefix = "v2:";

    /// <summary>ساخت Salt تصادفی امن (۱۶ بایت) با RandomNumberGenerator</summary>
    public static string GenerateSalt()
    {
        // استفاده از CSPRNG برای تولید salt غیرقابل پیش‌بینی
        using var rng = RandomNumberGenerator.Create();
        var saltBytes = new byte[SaltSize];
        rng.GetBytes(saltBytes);
        return Convert.ToBase64String(saltBytes);
    }

    /// <summary>هش کردن رمز با PBKDF2 (SHA256، ۱۰۰۰۰۰ تکرار، خروجی ۳۲ بایت)</summary>
    public static string HashPassword(string password, string salt)
    {
        // ورودی نامعتبر → رشته خالی (مطابق رفتار قبلی)
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(salt)) return string.Empty;

        // salt به صورت base64 ذخیره می‌شود؛ اینجا به بایت تبدیل می‌کنیم
        var saltBytes = Convert.FromBase64String(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        // پیشوند نسخه برای تشخیص فرمت جدید از قدیمی (SHA256)
        return Prefix + Convert.ToBase64String(hash);
    }

    /// <summary>بررسی رمز — پشتیبانی هم‌زمان از PBKDF2 و SHA256 قدیمی</summary>
    public static bool VerifyPassword(string password, string salt, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(salt) || string.IsNullOrEmpty(storedHash))
            return false;

        // ─── فرمت جدید (PBKDF2) ───
        if (storedHash.StartsWith(Prefix))
        {
            var saltBytes = Convert.FromBase64String(salt);
            var expected = Convert.FromBase64String(storedHash.Substring(Prefix.Length));
            // طول خروجی را همانند هش ذخیره‌شده در نظر می‌گیریم تا مقایسه درست باشد
            var computed = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                saltBytes,
                Iterations,
                HashAlgorithmName.SHA256,
                expected.Length);
            return CryptographicOperations.FixedTimeEquals(computed, expected);
        }

        // ─── فرمت قدیمی SHA256: combined = password + salt ───
        using var sha = SHA256.Create();
        var combined = password + salt;
        var legacyBytes = Encoding.UTF8.GetBytes(combined);
        var legacyHash = sha.ComputeHash(legacyBytes);
        var legacyComputed = Convert.ToBase64String(legacyHash);
        // مقایسه به‌صورت constant-time برای جلوگیری از حمله زمان‌سنجی
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(legacyComputed),
            Encoding.UTF8.GetBytes(storedHash));
    }

    /// <summary>آیا هش ذخیره‌شده نیاز به ارتقا به PBKDF2 دارد؟</summary>
    public static bool NeedsUpgrade(string storedHash)
    {
        // هش خالی یا بدون پیشوند v2 → نیاز به ارتقا
        if (string.IsNullOrEmpty(storedHash)) return true;
        return !storedHash.StartsWith(Prefix);
    }
}
