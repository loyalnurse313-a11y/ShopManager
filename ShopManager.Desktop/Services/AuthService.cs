using System;
using System.Linq;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;

namespace ShopManager.Desktop.Services;

public static class AuthService
{
    private static User? _currentUser;
    public static User? CurrentUser => _currentUser;
    public static bool IsLoggedIn => _currentUser != null;
    public static bool IsAdmin => _currentUser?.Role == UserRole.Admin;

    private static LoginHistory? _currentLoginRecord;
    public static int? CurrentLoginHistoryId => _currentLoginRecord?.Id;

    public static event Action? UserLoggedIn;
    public static event Action? UserLoggedOut;

    /// <summary>آیا کاربر فعلی می‌تونه اعداد مالی رو ببینه؟</summary>
    public static bool CanViewFinance => _currentUser?.Role == UserRole.Admin
                                         || (_currentUser?.CanViewFinance ?? false);

    public static (bool Success, string Message) Login(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            return (false, "نام کاربری را وارد کنید");
        if (string.IsNullOrWhiteSpace(password))
            return (false, "رمز عبور را وارد کنید");

        try
        {
            using var db = DatabaseService.CreateContext();

            username = username.Trim().ToLower();

            var user = db.Users.FirstOrDefault(u => u.Username.ToLower() == username);

            if (user == null)
            {
                RecordFailedLogin(username, "کاربر پیدا نشد");
                return (false, "نام کاربری یا رمز عبور اشتباه است");
            }

            if (!user.IsActive)
            {
                RecordFailedLogin(username, "کاربر غیرفعال");
                return (false, "این حساب کاربری غیرفعال شده است");
            }

            if (!PasswordHasher.VerifyPassword(password, user.PasswordSalt, user.PasswordHash))
            {
                RecordFailedLogin(username, "رمز اشتباه");
                return (false, "نام کاربری یا رمز عبور اشتباه است");
            }

            user.LastLoginAt = DateTime.UtcNow;
            db.Users.Update(user);

            var loginRecord = new LoginHistory
            {
                UserId = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                LoginAt = DateTime.UtcNow,
                Status = "موفق"
            };
            db.LoginHistories.Add(loginRecord);
            db.SaveChanges();

            _currentUser = user;
            _currentLoginRecord = loginRecord;

            UserLoggedIn?.Invoke();

            return (true, $"خوش آمدید {user.FullName}");
        }
        catch (Exception ex)
        {
            return (false, $"خطا: {ex.Message}");
        }
    }

    public static void Logout(string note = "Logout")
    {
        try
        {
            if (_currentLoginRecord == null || _currentUser == null)
                return;

            using var db = DatabaseService.CreateContext();

            var record = db.LoginHistories.FirstOrDefault(h => h.Id == _currentLoginRecord.Id);
            if (record != null)
            {
                record.LogoutAt = DateTime.UtcNow;
                record.DurationSeconds = (int)(DateTime.UtcNow - record.LoginAt).TotalSeconds;
                record.Note = note;

                if (_currentUser != null)
                {
                    var user = db.Users.FirstOrDefault(u => u.Id == _currentUser.Id);
                    if (user != null)
                        user.LastLogoutAt = DateTime.UtcNow;
                }

                db.SaveChanges();
            }

            _currentUser = null;
            _currentLoginRecord = null;

            UserLoggedOut?.Invoke();
        }
        catch
        {
            _currentUser = null;
            _currentLoginRecord = null;
        }
    }

    private static void RecordFailedLogin(string username, string reason)
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            db.LoginHistories.Add(new LoginHistory
            {
                UserId = null,
                Username = username,
                FullName = "—",
                LoginAt = DateTime.UtcNow,
                LogoutAt = DateTime.UtcNow,
                DurationSeconds = 0,
                Status = "ناموفق",
                Note = reason
            });

            db.SaveChanges();
        }
        catch { }
    }

    public static bool HasAccess(string section)
    {
        if (_currentUser == null) return false;
        return _currentUser.HasAccess(section);
    }

    public static string GetCurrentUserDisplay()
    {
        if (_currentUser == null) return "—";
        return $"{_currentUser.FullName} ({_currentUser.Username})";
    }
}