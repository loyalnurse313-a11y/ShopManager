using System;
using System.Linq;
using Avalonia.Threading;
using ShopManager.Domain.Enums;

namespace ShopManager.Desktop.Services;

/// <summary>
/// ردیاب Session — هر ۳۰ ثانیه چک می‌کنه:
/// 1. آیا کاربر هنوز فعاله؟ (اگه غیرفعال بشه، از برنامه بیرون می‌ندازه)
/// 2. مدت حضور رو ذخیره می‌کنه
/// </summary>
public static class SessionTracker
{
    private static DispatcherTimer? _timer;
    private static int _tickCount = 0;

    /// <summary>وقتی کاربر غیرفعال شد، این ایونت صدا زده می‌شه</summary>
    public static event Action? UserDeactivated;

    /// <summary>شروع ردیابی</summary>
    public static void Start()
    {
        Stop(); // اگه قبلاً روشن بود، اول خاموش کن

        _tickCount = 0;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };

        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>توقف ردیابی</summary>
    public static void Stop()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        _tickCount++;

        try
        {
            if (!AuthService.IsLoggedIn) return;

            var currentUser = AuthService.CurrentUser;
            if (currentUser == null) return;

            using var db = DatabaseService.CreateContext();

            // ─── چک فعال بودن کاربر ───
            var user = db.Users.FirstOrDefault(u => u.Id == currentUser.Id);

            if (user == null)
            {
                // کاربر حذف شده
                AuthService.Logout("حساب کاربری حذف شد");
                UserDeactivated?.Invoke();
                Stop();
                return;
            }

            if (!user.IsActive)
            {
                // کاربر غیرفعال شده
                AuthService.Logout("حساب کاربری غیرفعال شد");
                UserDeactivated?.Invoke();
                Stop();
                return;
            }

            // ─── آپدیت مدت حضور در دیتابیس (هر ۲ دقیقه یک‌بار) ───
            if (_tickCount % 4 == 0)
            {
                var historyId = AuthService.CurrentLoginHistoryId;
                if (historyId.HasValue)
                {
                    var record = db.LoginHistories.FirstOrDefault(h => h.Id == historyId.Value);
                    if (record != null)
                    {
                        record.DurationSeconds = (int)(DateTime.UtcNow - record.LoginAt).TotalSeconds;
                        db.SaveChanges();
                    }
                }
            }
        }
        catch
        {
            // خطا رو نادیده بگیر — مهم نیست اگه یه بار نشد
        }
    }
}