using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using ShopManager.Desktop.Models;

namespace ShopManager.Desktop.Services;

/// <summary>
/// سرویس تم برنامه (روشن/تیره) و رنگ اصلی (Accent).
/// پیاده‌سازی کاملاً بومی روی FluentTheme — هیچ پکیج تم خارجی استفاده نشده است.
/// </summary>
/// <remarks>
/// ═══ معماری ═══
/// پالت رنگ روشن و تیره در <c>App.axaml</c> داخل <c>ResourceDictionary.ThemeDictionaries</c>
/// تعریف شده است؛ بنابراین با تعیین <see cref="Application.RequestedThemeVariant"/>
/// کل برنامه (پس‌زمینه، کارت‌ها، جدول‌ها، متن‌ها و حاشیه‌ها) به‌صورت خودکار رنگ‌آمیزی مجدد می‌شود.
///
/// این سرویس فقط سه مسئولیت دارد:
/// ۱) تعیین تم پایه (Light/Dark)
/// ۲) تنظیم فونت برنامه
/// ۳) بازنویسی رنگ Accent
///
/// نکته‌ی مهم: رنگ Accent انتخابی کاربر است و باید روی «هر دو» تم اعمال شود، پس به‌صورت
/// ورودی مستقیم در <see cref="Application.Resources"/> نوشته می‌شود؛ چون ورودی‌های مستقیم
/// بر ThemeDictionaries اولویت دارند، این مقدار روی مقدار پیش‌فرض هر تم غالب می‌شود.
/// </remarks>
public static class ThemeService
{
    /// <summary>رنگ Accent پیش‌فرض در تم روشن (آبی نفتی)</summary>
    public const string AccentLightHex = "#1E40AF";

    /// <summary>رنگ Accent پیش‌فرض در تم تاریک (آبی روشن‌تر برای کنتراست روی زمینه‌ی تیره)</summary>
    public const string AccentDarkHex = "#3B82F6";

    /// <summary>تم فعلی تاریک است؟ — برای اعمال زنده‌ی Accent بدون خواندن مجدد تنظیمات</summary>
    private static bool _isDark;

    /// <summary>نام رنگ Accent انتخاب‌شده‌ی فعلی — هنگام تغییر تم برای بازمحاسبه‌ی سایه‌ها لازم است</summary>
    private static string _accentName = "Blue";

    /// <summary>آیا تم فعلی تاریک است؟</summary>
    public static bool IsDark => _isDark;

    /// <summary>
    /// پالت رنگ‌های Accent قابل انتخاب در تب «ظاهر».
    /// هر عضو یک سه‌گانه است: (نام کلید، نام نمایشی فارسی، کد رنگ hex)
    /// </summary>
    public static readonly (string Name, string Display, string Hex)[] Palette = new[]
    {
        ("Blue",     "آبی",       "#1E40AF"),
        ("Sky",      "آبی آسمانی","#0EA5E9"),
        ("Cyan",     "فیروزه‌ای",  "#06B6D4"),
        ("Teal",     "سبز آبی",   "#14B8A6"),
        ("Green",    "سبز",       "#047857"),
        ("Lime",     "سبز روشن",  "#84CC16"),
        ("Yellow",   "زرد",       "#B45309"),
        ("Orange",   "نارنجی",    "#F59E0B"),
        ("Red",      "قرمز",      "#B91C1C"),
        ("Pink",     "صورتی",     "#EC4899"),
        ("Purple",   "بنفش",      "#7C3AED"),
        ("Slate",    "خاکستری",   "#64748B"),
    };

    /// <summary>
    /// کد رنگ hex مربوط به یک نام Accent را برمی‌گرداند.
    /// </summary>
    /// <param name="name">نام کلید رنگ؛ مثلاً Blue یا Purple</param>
    /// <returns>کد رنگ به شکل #RRGGBB؛ اگر نام ناشناخته باشد رنگ پیش‌فرض روشن برگردانده می‌شود.</returns>
    public static string GetHex(string name)
    {
        foreach (var item in Palette)
        {
            if (item.Name == name) return item.Hex;
        }
        return AccentLightHex;
    }

    /// <summary>
    /// اعمال کامل تنظیمات ظاهری روی کل برنامه — در شروع برنامه فراخوانی می‌شود.
    /// </summary>
    /// <param name="prefs">تنظیمات ظاهری ذخیره‌شده‌ی کاربر (تم، فونت، رنگ Accent)</param>
    public static void Apply(AppPreferences prefs)
    {
        if (Application.Current == null) return;

        _isDark = string.Equals(prefs.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
        _accentName = string.IsNullOrWhiteSpace(prefs.AccentColor) ? "Blue" : prefs.AccentColor;

        // ─── تم پایه: پالت از ThemeDictionaries در App.axaml خوانده می‌شود ───
        Application.Current.RequestedThemeVariant = _isDark ? ThemeVariant.Dark : ThemeVariant.Light;

        // ─── فونت ───
        ApplyFont(prefs.FontFamily, prefs.FontSize);

        // ─── رنگ اصلی ───
        ApplyAccent(_accentName, _isDark);
    }

    /// <summary>
    /// تغییر زنده‌ی تم برنامه (بدون ذخیره‌سازی) — برای پیش‌نمایش لحظه‌ای در تنظیمات.
    /// </summary>
    /// <param name="isDark">true برای تم تاریک، false برای تم روشن</param>
    public static void SetTheme(bool isDark)
    {
        if (Application.Current == null) return;

        _isDark = isDark;
        Application.Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;

        // Accent به‌صورت ورودی مستقیم نوشته شده است، پس باید برای تم جدید بازمحاسبه شود
        ApplyAccent(_accentName, isDark);
    }

    /// <summary>
    /// تغییر زنده‌ی رنگ اصلی برنامه (بدون ذخیره‌سازی) — برای پیش‌نمایش لحظه‌ای در تنظیمات.
    /// </summary>
    /// <param name="colorName">نام کلید رنگ؛ مثلاً Blue</param>
    public static void SetAccent(string colorName)
    {
        if (Application.Current == null) return;

        _accentName = string.IsNullOrWhiteSpace(colorName) ? "Blue" : colorName;
        ApplyAccent(_accentName, _isDark);
    }

    /// <summary>
    /// تنظیم فونت و اندازه‌ی فونت کل برنامه.
    /// </summary>
    /// <param name="family">نام فونت اصلی</param>
    /// <param name="size">اندازه‌ی فونت بر حسب پیکسل</param>
    public static void ApplyFont(string family, double size)
    {
        if (Application.Current == null) return;

        var res = Application.Current.Resources;
        var safeFamily = string.IsNullOrWhiteSpace(family) ? "Vazirmatn" : family;
        var safeSize = size <= 0 ? 14d : size;

        res["AppFontFamily"] = new FontFamily($"{safeFamily}, IRANSans, Segoe UI");
        res["AppFontSize"] = safeSize;
    }

    /// <summary>
    /// اعمال رنگ اصلی (Accent) و سایه‌ی Hover آن روی کل برنامه.
    /// </summary>
    /// <param name="colorName">نام کلید رنگ انتخاب‌شده</param>
    /// <param name="isDark">آیا تم فعلی تاریک است (در تم تاریک رنگ روشن‌تر می‌شود تا کنتراست حفظ شود)</param>
    public static void ApplyAccent(string colorName, bool isDark)
    {
        if (Application.Current == null) return;

        var baseColor = SafeParse(GetHex(colorName), AccentLightHex);

        // در تم تاریک رنگ کمی روشن‌تر می‌شود تا روی پس‌زمینه‌ی تیره خوانا باشد
        var accent = isDark ? AdjustBrightness(baseColor, 0.28) : baseColor;

        // سایه‌ی Hover: در تم روشن تیره‌تر، در تم تاریک روشن‌تر
        var hover = isDark ? AdjustBrightness(accent, 0.18) : AdjustBrightness(accent, -0.12);

        var res = Application.Current.Resources;
        res["AppAccent"] = new SolidColorBrush(accent);
        res["AppAccentHover"] = new SolidColorBrush(hover);
    }

    /// <summary>
    /// رنگ را روشن‌تر یا تیره‌تر می‌کند.
    /// </summary>
    /// <param name="color">رنگ پایه</param>
    /// <param name="factor">مقدار بین ‎-1 و 1؛ مقدار مثبت روشن‌تر و مقدار منفی تیره‌تر می‌کند.</param>
    /// <returns>رنگ تعدیل‌شده</returns>
    private static Color AdjustBrightness(Color color, double factor)
    {
        if (factor >= 0)
        {
            // نزدیک شدن به سفید
            return Color.FromRgb(
                (byte)Math.Min(255, color.R + (255 - color.R) * factor),
                (byte)Math.Min(255, color.G + (255 - color.G) * factor),
                (byte)Math.Min(255, color.B + (255 - color.B) * factor));
        }

        // نزدیک شدن به مشکی
        var scale = 1 + factor;
        return Color.FromRgb(
            (byte)Math.Max(0, color.R * scale),
            (byte)Math.Max(0, color.G * scale),
            (byte)Math.Max(0, color.B * scale));
    }

    /// <summary>
    /// تبدیل ایمن یک کد رنگ رشته‌ای به <see cref="Color"/>.
    /// </summary>
    /// <param name="hex">کد رنگ مورد انتظار</param>
    /// <param name="fallbackHex">کد رنگ جایگزین در صورت نامعتبر بودن ورودی</param>
    /// <returns>رنگ پارس‌شده یا رنگ جایگزین</returns>
    private static Color SafeParse(string hex, string fallbackHex)
    {
        try
        {
            return Color.Parse(hex);
        }
        catch
        {
            return Color.Parse(fallbackHex);
        }
    }
}
