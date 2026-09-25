using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using ShopManager.Desktop.Models;

namespace ShopManager.Desktop.Services;

/// <summary>
/// سرویس اعمال تم روی کل برنامه
/// ۱۲ رنگ آماده + تم روشن/تاریک
/// </summary>
public static class ThemeService
{
    /// <summary>۱۲ رنگ آماده برای انتخاب کاربر</summary>
    public static readonly (string Name, string Display, string Hex)[] Palette = new[]
    {
        ("Blue",     "آبی",       "#4F46E5"),
        ("Sky",      "آبی آسمانی","#0EA5E9"),
        ("Cyan",     "فیروزه‌ای",  "#06B6D4"),
        ("Teal",     "سبز آبی",   "#14B8A6"),
        ("Green",    "سبز",       "#10B981"),
        ("Lime",     "سبز روشن",  "#84CC16"),
        ("Yellow",   "زرد",       "#EAB308"),
        ("Orange",   "نارنجی",    "#F59E0B"),
        ("Red",      "قرمز",      "#EF4444"),
        ("Pink",     "صورتی",     "#EC4899"),
        ("Purple",   "بنفش",      "#8B5CF6"),
        ("Slate",    "خاکستری",   "#64748B"),
    };

    /// <summary>رنگ hex بر اساس نام</summary>
    public static string GetHex(string name)
    {
        foreach (var item in Palette)
        {
            if (item.Name == name) return item.Hex;
        }
        return "#4F46E5";
    }

    /// <summary>اعمال تنظیمات روی کل برنامه</summary>
    public static void Apply(AppPreferences prefs)
    {
        if (Application.Current == null) return;

        var res = Application.Current.Resources;

        // ─── تم ───
        Application.Current.RequestedThemeVariant =
            prefs.Theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        // ─── فونت ───
        try
        {
            res["AppFontFamily"] = new FontFamily($"{prefs.FontFamily}, IRANSans, Segoe UI");
        }
        catch { }

        res["AppFontSize"] = prefs.FontSize;

        // ─── Accent ───
        var accentHex = GetHex(prefs.AccentColor);
        res["AppAccent"] = new SolidColorBrush(Color.Parse(accentHex));

        // ─── رنگ‌های متناسب با تم ───
        if (prefs.Theme == "Dark")
        {
            // ─── دارک مود واقعی ───
            res["AppBackground"] = new SolidColorBrush(Color.Parse("#0F172A"));
            res["AppCardBackground"] = new SolidColorBrush(Color.Parse("#1E293B"));
            res["AppHeaderBackground"] = new SolidColorBrush(Color.Parse("#020617"));
            res["AppMenuBackground"] = new SolidColorBrush(Color.Parse("#1E293B"));
            res["AppMenuText"] = new SolidColorBrush(Color.Parse("#E2E8F0"));
            res["AppTextPrimary"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
            res["AppTextSecondary"] = new SolidColorBrush(Color.Parse("#94A3B8"));
            res["AppBorder"] = new SolidColorBrush(Color.Parse("#334155"));
            res["AppButtonText"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
            res["AppTableHeader"] = new SolidColorBrush(Color.Parse("#1E293B"));
            res["AppTableHeaderText"] = new SolidColorBrush(Color.Parse("#E2E8F0"));
            res["AppTableRow"] = new SolidColorBrush(Color.Parse("#1E293B"));
            res["AppTableAltRow"] = new SolidColorBrush(Color.Parse("#0F172A"));
            res["AppTableText"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
            res["AppTableBorder"] = new SolidColorBrush(Color.Parse("#334155"));
        }
        else
        {
            // ─── لایت مود ───
            res["AppBackground"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
            res["AppCardBackground"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
            res["AppHeaderBackground"] = new SolidColorBrush(Color.Parse("#2C3E50"));
            res["AppMenuBackground"] = new SolidColorBrush(Color.Parse("#34495E"));
            res["AppMenuText"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
            res["AppTextPrimary"] = new SolidColorBrush(Color.Parse("#0F172A"));
            res["AppTextSecondary"] = new SolidColorBrush(Color.Parse("#475569"));
            res["AppBorder"] = new SolidColorBrush(Color.Parse("#E2E8F0"));
            res["AppButtonText"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
            res["AppTableHeader"] = new SolidColorBrush(Color.Parse("#EEF2FF"));
            res["AppTableHeaderText"] = new SolidColorBrush(Color.Parse("#4F46E5"));
            res["AppTableRow"] = new SolidColorBrush(Color.Parse("#FFFFFF"));
            res["AppTableAltRow"] = new SolidColorBrush(Color.Parse("#F8FAFC"));
            res["AppTableText"] = new SolidColorBrush(Color.Parse("#0F172A"));
            res["AppTableBorder"] = new SolidColorBrush(Color.Parse("#F1F5F9"));
        }

        // ─── رنگ‌های ثابت ───
        res["AppSuccess"] = new SolidColorBrush(Color.Parse("#10B981"));
        res["AppDanger"] = new SolidColorBrush(Color.Parse("#EF4444"));
    }
}