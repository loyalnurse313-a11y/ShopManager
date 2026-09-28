using System;
using System.Linq;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;

namespace ShopManager.Desktop.Services;

public static class AuthServiceInitializer
{
    /// <summary>
    /// رمز پیش‌فرض نسخه‌ی v1.0.7 — فقط برای تشخیص کاربران قدیمی.
    /// هرگز برای ساخت کاربر جدید یا احراز هویت استفاده نمی‌شود.
    /// </summary>
    private const string LegacyDefaultPassword = "admin";

    /// <summary>
    /// آیا هیچ کاربری در دیتابیس وجود دارد؟ (مبنای تصمیم راه‌اندازی اولیه)
    /// در صورت خطای دیتابیس، true برمی‌گرداند تا مسیر عادی ورود طی شود
    /// و کاربر پیام خطای واقعی را از LoginWindow بگیرد.
    /// </summary>
    public static bool HasAnyUser()
    {
        try
        {
            using var db = DatabaseService.CreateContext();
            return db.Users.Any();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($">>> HasAnyUser error: {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// ساخت حساب مدیر اولیه از ورودی کاربر (راه‌اندازی اولیه).
    /// منطق ساخت در سرویس است، نه در View.
    /// </summary>
    /// <returns>Success = نتیجه، Error = پیام خطای فارسی برای نمایش</returns>
    public static (bool Success, string? Error) CreateInitialAdmin(
        string username, string fullName, string password)
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            // ─── Defense in Depth: جلوگیری از race بین دو نمونه‌ی هم‌زمان برنامه ───
            var normalized = username.Trim().ToLower();
            if (db.Users.Any(u => u.Username.ToLower() == normalized))
            {
                return (false, $"نام کاربری «{username}» قبلاً استفاده شده");
            }

            var salt = PasswordHasher.GenerateSalt();
            var hash = PasswordHasher.HashPassword(password, salt);

            var admin = new User
            {
                Username = username.Trim(),
                PasswordHash = hash,
                PasswordSalt = salt,
                FullName = fullName.Trim(),
                Role = UserRole.Admin,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,

                // کاربر خودش رمز را ساخته → نیازی به اجبار تغییر نیست
                MustChangePassword = false,

                CanDashboard = true,
                CanItems = true,
                CanPurchase = true,
                CanTransfer = true,
                CanPOS = true,
                CanCustomers = true,
                CanStats = true,
                CanCashbox = true,
                CanSettings = true,
                CanUserManagement = true,
                CanPrint = true,
                CanExportExcel = true,
                CanViewFinance = true
            };

            db.Users.Add(admin);
            db.SaveChanges();
            return (true, null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($">>> CreateInitialAdmin error: {ex.Message}");
            return (false, $"خطا در ساخت حساب: {ex.Message}");
        }
    }

    /// <summary>
    /// مهاجرت امنیتی: اگر رمز کاربری هنوز پیش‌فرض قدیمی («admin») باشد،
    /// پرچم اجبار تغییر رمز را روشن می‌کند. یک‌بار در استارتاپ صدا زده می‌شود.
    /// این جایگزین «Backdoor» حذف‌شده‌ی LoginWindow است.
    /// </summary>
    /// <returns>تعداد کاربرانی که پرچمشان روشن شد</returns>
    public static int CheckLegacyAdminPassword()
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            // فقط حساب «admin» که هنوز پرچم تغییر رمز ندارد کاندید است
            // (هم‌دامنه با Backdoor حذف‌شده — بدون هزینه‌ی PBKDF2 روی همه‌ی کاربران)
            var admin = db.Users.FirstOrDefault(u =>
                u.Username == "admin" && !u.MustChangePassword);

            if (admin == null) return 0;

            // VerifyPassword سنگین است (PBKDF2، ۱۰۰٬۰۰۰ تکرار) → حداکثر یک بار اجرا می‌شود
            if (!PasswordHasher.VerifyPassword(LegacyDefaultPassword, admin.PasswordSalt, admin.PasswordHash))
            {
                return 0;
            }

            admin.MustChangePassword = true;
            db.SaveChanges();
            return 1;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($">>> CheckLegacyAdminPassword error: {ex.Message}");
            return 0;
        }
    }
}