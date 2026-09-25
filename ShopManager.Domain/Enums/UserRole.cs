namespace ShopManager.Domain.Enums;

/// <summary>
/// نقش کاربر در سیستم
/// </summary>
public enum UserRole
{
    /// <summary>ادمین — دسترسی کامل به همه چیز</summary>
    Admin = 0,

    /// <summary>کاربر عادی — فقط دسترسی‌هایی که ادمین تعیین کرده</summary>
    User = 1
}