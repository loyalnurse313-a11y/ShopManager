using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ShopManager.Desktop.Services;
using ShopManager.Domain.Entities;

namespace ShopManager.Desktop.Views;

/// <summary>
/// پنجره اجبار تغییر رمز عبور در اولین ورود (پرامپت ۱.۳)
/// تا زمانی که رمز عوض نشه، کاربر اجازه ورود به برنامه رو نداره
/// </summary>
public partial class ChangePasswordWindow : Window
{
    private User? _user;

    /// <summary>سازنده پنجره تغییر رمز</summary>
    public ChangePasswordWindow(User user)
    {
        InitializeComponent();
        _user = user;
    }

    /// <summary>ذخیره رمز جدید بعد از اعتبارسنجی</summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var newPassword = NewPasswordBox.Text ?? "";
        var confirmPassword = ConfirmPasswordBox.Text ?? "";

        // ─── اعتبارسنجی رمز جدید ───
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            ShowError("رمز جدید را وارد کنید");
            NewPasswordBox.Focus();
            return;
        }

        if (newPassword.Length < 8)
        {
            ShowError("رمز جدید باید حداقل ۸ کاراکتر باشد");
            NewPasswordBox.Focus();
            return;
        }

        // حداقل یک حرف
        if (!newPassword.Any(char.IsLetter))
        {
            ShowError("رمز جدید باید حداقل شامل یک حرف باشد");
            NewPasswordBox.Focus();
            return;
        }

        // حداقل یک عدد
        if (!newPassword.Any(char.IsDigit))
        {
            ShowError("رمز جدید باید حداقل شامل یک عدد باشد");
            NewPasswordBox.Focus();
            return;
        }

        if (newPassword != confirmPassword)
        {
            ShowError("رمز جدید و تکرار آن یکسان نیستند");
            ConfirmPasswordBox.Focus();
            return;
        }

        if (_user == null)
        {
            ShowError("خطا: کاربر یافت نشد");
            return;
        }

        try
        {
            using var db = DatabaseService.CreateContext();

            // ─── کاربر رو از دیتابیس بگیر ───
            var user = db.Users.FirstOrDefault(u => u.Id == _user.Id);
            if (user == null)
            {
                ShowError("خطا: کاربر در دیتابیس یافت نشد");
                return;
            }

            // ─── ساخت salt و هش جدید با PBKDF2 ───
            var newSalt = PasswordHasher.GenerateSalt();
            var newHash = PasswordHasher.HashPassword(newPassword, newSalt);

            user.PasswordHash = newHash;
            user.PasswordSalt = newSalt;
            user.MustChangePassword = false;

            db.SaveChanges();

            // ─── موفق — برگرداندن true به پنجره ورود ───
            Close(true);
        }
        catch (Exception ex)
        {
            ShowError($"خطا در ذخیره رمز: {ex.Message}");
        }
    }

    /// <summary>انصراف یا بستن پنجره — برگرداندن false</summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    /// <summary>نمایش پیام خطا در کادر قرمز</summary>
    private void ShowError(string message)
    {
        MessageBorder.IsVisible = true;
        MessageText.Text = message;
        MessageBorder.Background = new SolidColorBrush(Color.Parse("#FEF2F2"));
        MessageText.Foreground = new SolidColorBrush(Color.Parse("#DC2626"));
    }
}
