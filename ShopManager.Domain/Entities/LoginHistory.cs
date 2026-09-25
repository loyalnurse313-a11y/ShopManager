using System;

namespace ShopManager.Domain.Entities;

/// <summary>
/// سابقه ورود و خروج کاربران
/// برای audit و پیگیری مدت حضور
/// </summary>
public class LoginHistory
{
    public int Id { get; set; }

    /// <summary>کد کاربر (اگه پاک بشه، null می‌شه ولی سابقه می‌مونه)</summary>
    public int? UserId { get; set; }

    /// <summary>نام کاربری — برای نگه‌داشتن سابقه حتی بعد از حذف کاربر</summary>
    public string Username { get; set; } = "";

    /// <summary>نام کامل کاربر</summary>
    public string FullName { get; set; } = "";

    /// <summary>زمان ورود (UTC)</summary>
    public DateTime LoginAt { get; set; } = DateTime.UtcNow;

    /// <summary>زمان خروج (UTC) — اگه null، یعنی هنوز توی برنامه هست</summary>
    public DateTime? LogoutAt { get; set; }

    /// <summary>مدت حضور به ثانیه</summary>
    public int DurationSeconds { get; set; } = 0;

    /// <summary>وضعیت: "موفق" یا "ناموفق"</summary>
    public string Status { get; set; } = "موفق";

    /// <summary>توضیحات (مثلاً «بستن با ضربدر»، «Logout»، «قطع برق»)</summary>
    public string? Note { get; set; }

    // ═══════════ Helper ═══════════

    /// <summary>مدت حضور به فرمت خوانا</summary>
    public string DurationDisplay
    {
        get
        {
            if (DurationSeconds <= 0) return "—";

            var ts = TimeSpan.FromSeconds(DurationSeconds);

            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours} ساعت و {ts.Minutes} دقیقه";

            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes} دقیقه و {ts.Seconds} ثانیه";

            return $"{ts.Seconds} ثانیه";
        }
    }
}