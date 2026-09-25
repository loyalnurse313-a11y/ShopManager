using System.Globalization;

namespace ShopManager.Domain.Helpers;

/// <summary>
/// کمک‌کننده تبدیل تاریخ شمسی ↔ میلادی
/// از PersianCalendar داخلی .NET استفاده می‌کنه
/// </summary>
public static class JalaliDate
{
    private static readonly PersianCalendar _pc = new PersianCalendar();

    public static string ToShamsi(DateTime gregorian)
    {
        int year = _pc.GetYear(gregorian);
        int month = _pc.GetMonth(gregorian);
        int day = _pc.GetDayOfMonth(gregorian);
        return $"{year:0000}/{month:00}/{day:00}";
    }

    public static DateTime ToGregorian(string shamsi)
    {
        if (string.IsNullOrWhiteSpace(shamsi))
            throw new ArgumentException("تاریخ شمسی خالیه", nameof(shamsi));

        var parts = shamsi.Split('/', '-');
        if (parts.Length != 3)
            throw new ArgumentException($"فرمت تاریخ اشتباهه: {shamsi}");

        int year = int.Parse(parts[0]);
        int month = int.Parse(parts[1]);
        int day = int.Parse(parts[2]);

        return _pc.ToDateTime(year, month, day, 0, 0, 0, 0);
    }

    public static string TodayShamsi() => ToShamsi(DateTime.Today);

    public static DateTime TodayGregorian() => DateTime.Today;

    public static string ToPersianLong(string shamsi)
    {
        var parts = shamsi.Split('/');
        if (parts.Length != 3) return shamsi;

        string[] monthNames = {
            "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
            "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
        };

        int year = int.Parse(parts[0]);
        int month = int.Parse(parts[1]);
        int day = int.Parse(parts[2]);

        if (month < 1 || month > 12) return shamsi;

        return $"{PersianNumber.ToPersian(day)} {monthNames[month - 1]} {PersianNumber.ToPersian(year)}";
    }
}