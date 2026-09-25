using ShopManager.Domain.Helpers;

namespace ShopManager.Desktop.Services;

/// <summary>
/// سرویس ماسک اعداد مالی
/// 
/// اگه کاربر دسترسی مالی نداشته باشه، همه اعداد مالی
/// به *** تبدیل می‌شن.
/// 
/// POS خودش از این سرویس استفاده نمی‌کنه، چون فروشنده
/// باید اعداد فروش خودش رو ببینه.
/// </summary>
public static class MoneyMask
{
    /// <summary>آیا اعداد مالی قفل هستن؟</summary>
    public static bool IsLocked => !AuthService.CanViewFinance;

    /// <summary>متن *** جایگزین</summary>
    private const string MaskedText = "***";

    /// <summary>نمایش مبلغ به تومان با ماسک</summary>
    public static string Toman(decimal amount)
    {
        if (IsLocked) return MaskedText;
        return PersianNumber.ToToman(amount);
    }

    /// <summary>نمایش عدد با ماسک</summary>
    public static string Persian(decimal amount)
    {
        if (IsLocked) return MaskedText;
        return PersianNumber.ToPersian(amount);
    }

    /// <summary>نمایش عدد صحیح با ماسک</summary>
    public static string Persian(int amount)
    {
        if (IsLocked) return MaskedText;
        return PersianNumber.ToPersian(amount);
    }

    /// <summary>اگه قفل باشه، این متن رو نشون بده</summary>
    public static string LockedMessage(string text = "🔒 دسترسی مالی محدود شده")
    {
        return IsLocked ? text : "";
    }
}