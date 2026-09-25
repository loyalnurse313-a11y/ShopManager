using System.Globalization;
using System.Text;

namespace ShopManager.Domain.Helpers;

/// <summary>
/// کمک‌کننده نمایش اعداد فارسی و قالب‌بندی تومان
/// </summary>
public static class PersianNumber
{
    /// <summary>
    /// تبدیل عدد اعشاری به رشته فارسی با جداکننده هزارگان
    /// مثال: 1234567.5 → "۱٬۲۳۴٬۵۶۷.۵"
    /// </summary>
    public static string ToPersian(decimal value)
    {
        var formatted = value.ToString("#,##0.##", CultureInfo.InvariantCulture);
        return ToPersianDigits(formatted);
    }

    /// <summary>
    /// تبدیل عدد صحیح به فارسی
    /// مثال: 1234 → "۱٬۲۳۴"
    /// </summary>
    public static string ToPersian(int value)
    {
        return ToPersianDigits(value.ToString("#,##0", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// تبدیل ارقام انگلیسی به فارسی
    /// مثال: "1234" → "۱۲۳۴"
    /// </summary>
    public static string ToPersianDigits(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            sb.Append(c switch
            {
                '0' => '۰',
                '1' => '۱',
                '2' => '۲',
                '3' => '۳',
                '4' => '۴',
                '5' => '۵',
                '6' => '۶',
                '7' => '۷',
                '8' => '۸',
                '9' => '۹',
                _ => c
            });
        }
        return sb.ToString();
    }

    /// <summary>
    /// تبدیل ارقام فارسی به انگلیسی (برای ورودی کاربر)
    /// مثال: "۱۲۳۴" → "1234"
    /// </summary>
    public static string ToEnglishDigits(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            sb.Append(c switch
            {
                '۰' => '0',
                '۱' => '1',
                '۲' => '2',
                '۳' => '3',
                '۴' => '4',
                '۵' => '5',
                '۶' => '6',
                '۷' => '7',
                '۸' => '8',
                '۹' => '9',
                _ => c
            });
        }
        return sb.ToString();
    }

    /// <summary>
    /// نمایش مبلغ تومان
    /// مثال: 50000 → "۵۰٬۰۰۰ تومان"
    /// </summary>
    public static string ToToman(decimal amount)
    {
        return $"{ToPersian(amount)} تومان";
    }
}