using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ShopManager.Desktop.Services;
using ShopManager.Domain.Helpers;

namespace ShopManager.Desktop.Views;

public partial class LoginWindow : Window
{
    private int _failedAttempts = 0;
    private bool _isLocked = false;
    private DispatcherTimer? _lockTimer;
    private int _lockSecondsRemaining = 0;

    public LoginWindow()
    {
        InitializeComponent();

        // نمایش نسخه
        VersionText.Text = "نسخه ۱.۰.۰";

        // فوکوس روی فیلد نام کاربری
        Loaded += (s, e) =>
        {
            UsernameTextBox.Focus();
        };

        // پیش‌فرض نام کاربری admin رو پر کن (برای راحتی)
        try
        {
            using var db = DatabaseService.CreateContext();
            var adminExists = db.Users.Any(u => u.Username == "admin");
            if (adminExists)
            {
                UsernameTextBox.Text = "admin";
            }
        }
        catch { }
    }

    /// <summary>ورود با Enter در فیلد رمز</summary>
    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnLoginClick(null, new RoutedEventArgs());
        }
    }

    /// <summary>کلیک روی دکمه ورود</summary>
    private void OnLoginClick(object? sender, RoutedEventArgs e)
    {
        if (_isLocked)
        {
            ShowMessage($"به دلیل تلاش‌های ناموفق، {PersianNumber.ToPersian(_lockSecondsRemaining)} ثانیه صبر کنید", isError: true);
            return;
        }

        var username = UsernameTextBox.Text?.Trim() ?? "";
        var password = PasswordTextBox.Text ?? "";

        if (string.IsNullOrWhiteSpace(username))
        {
            ShowMessage("نام کاربری را وارد کنید", isError: true);
            UsernameTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            ShowMessage("رمز عبور را وارد کنید", isError: true);
            PasswordTextBox.Focus();
            return;
        }

        // ─── تلاش برای ورود ───
        var (success, message) = AuthService.Login(username, password);

        if (success)
        {
            _failedAttempts = 0;
            ShowMessage(message, isError: false);

            // ─── باز کردن MainWindow ───
            var mainWindow = new MainWindow();
            mainWindow.Show();

            // بستن LoginWindow
            Close();
        }
        else
        {
            _failedAttempts++;
            ShowMessage(message, isError: true);

            // پاک کردن رمز
            PasswordTextBox.Text = "";
            PasswordTextBox.Focus();

            // اگه ۵ بار اشتباه شد، ۶۰ ثانیه قفل کن
            if (_failedAttempts >= 5)
            {
                StartLock(60);
                _failedAttempts = 0;
            }
        }
    }

    /// <summary>نمایش پیام موفق/خطا</summary>
    private void ShowMessage(string message, bool isError)
    {
        MessageBorder.IsVisible = true;
        MessageText.Text = message;

        if (isError)
        {
            MessageBorder.Background = new SolidColorBrush(Color.Parse("#FEF2F2"));
            MessageText.Foreground = new SolidColorBrush(Color.Parse("#DC2626"));
        }
        else
        {
            MessageBorder.Background = new SolidColorBrush(Color.Parse("#ECFDF5"));
            MessageText.Foreground = new SolidColorBrush(Color.Parse("#059669"));
        }
    }

    /// <summary>قفل کردن فرم بعد از تلاش‌های ناموفق</summary>
    private void StartLock(int seconds)
    {
        _isLocked = true;
        _lockSecondsRemaining = seconds;

        LoginButton.IsEnabled = false;
        UsernameTextBox.IsEnabled = false;
        PasswordTextBox.IsEnabled = false;

        _lockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _lockTimer.Tick += (s, e) =>
        {
            _lockSecondsRemaining--;

            if (_lockSecondsRemaining <= 0)
            {
                EndLock();
            }
            else
            {
                ShowMessage($"به دلیل تلاش‌های ناموفق، {PersianNumber.ToPersian(_lockSecondsRemaining)} ثانیه صبر کنید", isError: true);
            }
        };

        _lockTimer.Start();
        ShowMessage($"به دلیل تلاش‌های ناموفق، {PersianNumber.ToPersian(_lockSecondsRemaining)} ثانیه صبر کنید", isError: true);
    }

    /// <summary>پایان قفل</summary>
    private void EndLock()
    {
        _isLocked = false;
        _lockSecondsRemaining = 0;

        _lockTimer?.Stop();
        _lockTimer = null;

        LoginButton.IsEnabled = true;
        UsernameTextBox.IsEnabled = true;
        PasswordTextBox.IsEnabled = true;

        MessageBorder.IsVisible = false;
        PasswordTextBox.Focus();
    }
}