using System;
using ShopManager.Domain.Enums;

namespace ShopManager.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public string FullName { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.User;
    public bool IsActive { get; set; } = true;

    // ═══ دسترسی‌های عادی ═══
    public bool CanDashboard { get; set; } = false;
    public bool CanItems { get; set; } = false;
    public bool CanPurchase { get; set; } = false;
    public bool CanTransfer { get; set; } = false;
    public bool CanPOS { get; set; } = false;
    public bool CanCustomers { get; set; } = false;
    public bool CanStats { get; set; } = false;
    public bool CanCashbox { get; set; } = false;
    public bool CanSettings { get; set; } = false;
    public bool CanUserManagement { get; set; } = false;
    public bool CanPrint { get; set; } = true;
    public bool CanExportExcel { get; set; } = false;

    // ═══ 🆕 دسترسی مالی ═══
    /// <summary>
    /// آیا این کاربر می‌تونه اعداد و ارقام مالی رو ببینه؟
    /// اگه false باشه، همه اعداد مالی به *** تبدیل می‌شن
    /// </summary>
    public bool CanViewFinance { get; set; } = false;

    // ═══ متادیتا ═══
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public DateTime? LastLogoutAt { get; set; }

    // ═══ Helper ═══
    public bool HasAccess(string section)
    {
        if (Role == UserRole.Admin) return true;

        return section switch
        {
            "Dashboard" => CanDashboard,
            "Items" => CanItems,
            "Purchase" => CanPurchase,
            "Transfer" => CanTransfer,
            "POS" => CanPOS,
            "Customers" => CanCustomers,
            "Stats" => CanStats,
            "Cashbox" => CanCashbox,
            "Settings" => CanSettings,
            "UserManagement" => CanUserManagement,
            "Print" => CanPrint,
            "ExportExcel" => CanExportExcel,
            "ViewFinance" => CanViewFinance,
            _ => false
        };
    }
}