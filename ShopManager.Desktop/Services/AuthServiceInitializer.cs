using System;
using System.Linq;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;

namespace ShopManager.Desktop.Services;

public static class AuthServiceInitializer
{
    public const string DefaultAdminUsername = "admin";
    public const string DefaultAdminPassword = "admin";

    public static bool EnsureDefaultAdmin()
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            if (!db.Users.Any())
            {
                var salt = PasswordHasher.GenerateSalt();
                var hash = PasswordHasher.HashPassword(DefaultAdminPassword, salt);

                var admin = new User
                {
                    Username = DefaultAdminUsername,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    FullName = "مدیر سیستم",
                    Role = UserRole.Admin,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,

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
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($">>> Auth init error: {ex.Message}");
            return false;
        }
    }
}