using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShopManager.Desktop.Services;

namespace ShopManager.Desktop.Views;

/// <summary>
/// پنجره‌ی راه‌اندازی اولیه — فقط وقتی هیچ کاربری در دیتابیس نیست نمایش داده می‌شود.
/// جایگزین ادمین پیش‌فرض admin/admin (رمز هاردکد حذف شده است).
/// </summary>
public partial class FirstRunSetupWindow : Window
{
    /// <summary>گارد جلوگیری از دوبار کلیک / درخواست موازی</summary>
    private bool _isSaving;

    /// <summary>آیا حساب مدیر با موفقیت ساخته شد؟</summary>
    public bool Created { get; private set; }

    public FirstRunSetupWindow()
    {
        InitializeComponent();
    }

    /// <summary>ساخت حساب مدیر اولیه</summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        // ─── گارد دوبار کلیک سریع ───
        if (_isSaving) return;
        _isSaving = true;
        SaveButton.IsEnabled = false;

        try
        {
            var fullName = FullNameTextBox.Text?.Trim() ?? "";
            var username = UsernameTextBox.Text?.Trim() ?? "";
            var password = PasswordTextBox.Text ?? "";
            var confirmPassword = ConfirmPasswordTextBox.Text ?? "";

            // ─── اعتبارسنجی (fail-fast + Focus) ───
            if (fullName.Length == 0)
            {
                ShowError("نام کامل را وارد کنید", FullNameTextBox);
                return;
            }
            if (fullName.Length < 3)
            {
                ShowError("نام کامل باید حداقل ۳ کاراکتر باشد", FullNameTextBox);
                return;
            }
            if (fullName.Length > 200)
            {
                ShowError("نام کامل حداکثر ۲۰۰ کاراکتر", FullNameTextBox);
                return;
            }

            if (username.Length == 0)
            {
                ShowError("نام کاربری را وارد کنید", UsernameTextBox);
                return;
            }
            if (username.Length < 3)
            {
                ShowError("نام کاربری باید حداقل ۳ کاراکتر باشد", UsernameTextBox);
                return;
            }
            if (username.Length > 50)
            {
                ShowError("نام کاربری حداکثر ۵۰ کاراکتر", UsernameTextBox);
                return;
            }
            if (!IsUsernameAllowed(username))
            {
                ShowError("نام کاربری فقط می‌تواند شامل حرف، عدد، نقطه، خط تیره و زیرخط باشد", UsernameTextBox);
                return;
            }

            if (password.Length < 8)
            {
                ShowError("رمز عبور باید حداقل ۸ کاراکتر باشد", PasswordTextBox);
                return;
            }
            if (!password.Any(char.IsLetter))
            {
                ShowError("رمز عبور باید حداقل شامل یک حرف باشد", PasswordTextBox);
                return;
            }
            if (!password.Any(char.IsDigit))
            {
                ShowError("رمز عبور باید حداقل شامل یک عدد باشد", PasswordTextBox);
                return;
            }
            if (password != confirmPassword)
            {
                ShowError("رمز عبور و تکرار آن یکسان نیستند", ConfirmPasswordTextBox);
                return;
            }

            // ─── ساخت حساب (منطق در سرویس است، نه در View) ───
            var (success, error) = AuthServiceInitializer.CreateInitialAdmin(username, fullName, password);
            if (!success)
            {
                ShowError(error ?? "خطا در ساخت حساب", UsernameTextBox);
                return;
            }

            Created = true;

            // ─── الگوی پروژه: پنجره‌ی بعدی را اول Show می‌کنیم و بعد این را می‌بندیم
            //     تا هرگز «صفر پنجره» نشود (ShutdownMode پیش‌فرض = OnLastWindowClose) ───
            new LoginWindow().Show();
            Close(true);
        }
        finally
        {
            _isSaving = false;
            SaveButton.IsEnabled = true;
        }
    }

    /// <summary>مجاز‌بودن نام کاربری: ^[a-zA-Z0-9._-]+$ (فقط ASCII)</summary>
    private static bool IsUsernameAllowed(string value) =>
        value.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '_' || c == '-');

    /// <summary>نمایش پیام خطا در نوار قرمز + فوکوس روی فیلد مشکل‌دار</summary>
    private void ShowError(string message, Control? focusTarget = null)
    {
        MessageBorder.IsVisible = true;
        MessageText.Text = message;
        focusTarget?.Focus();
    }
}
