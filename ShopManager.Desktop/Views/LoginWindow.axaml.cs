using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
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

    // Animation transform for the login panel slide-in (the logo uses XAML transitions)
    private readonly TranslateTransform _loginPanelTranslate = new() { X = 0, Y = 20 };

    public LoginWindow()
    {
        InitializeComponent();

        VersionText.Text = "نسخه ۱.۰.7";

        // Initial logo transform; the XAML transition animates it to scale(1)/rotate(0)
        SplashLogoBorder.RenderTransform = TransformOperations.Parse("scale(0.3) rotate(-180deg)");
        LoginPanel.RenderTransform = _loginPanelTranslate;

        // ─── شروع توالی انیمیشن اسپلش هنگام بارگذاری پنجره ───
        // فوکوس روی نام کاربری در پایان توالی اسپلش انجام می‌شود (نه اینجا)
        Loaded += async (s, e) => await RunSplashSequenceAsync();
    }

    /// <summary>
    /// توالی انیمیشن اسپلش: بزرگ‌شدن لوگو، ظاهر شدن عنوان/زیرعنوان/نوار پیشرفت،
    /// سپس محو شدن اسپلش و نمایش فرم ورود.
    /// </summary>
    private async Task RunSplashSequenceAsync()
    {
        try
        {
            // Step 1: logo — the XAML transition animates from scale(0.3)/rotate(-180) to scale(1)/rotate(0)
            SplashLogoBorder.Opacity = 1;
            SplashLogoBorder.RenderTransform = TransformOperations.Parse("scale(1) rotate(0deg)");
            await Task.Delay(500); // matches the 0.5s transition duration

            // گام ۲: عنوان
            SplashTitleText.Opacity = 1;
            await AnimateAsync(400, (p) => { SplashTitleText.Opacity = p; });

            // گام ۳: زیرعنوان
            SplashSubtitleText.Opacity = 1;
            await AnimateAsync(400, (p) => { SplashSubtitleText.Opacity = p; });

            // گام ۴: متن لودینگ
            SplashLoadingText.Opacity = 1;
            await AnimateAsync(300, (p) => { SplashLoadingText.Opacity = p; });

            // گام ۵: پر شدن نوار پیشرفت
            await AnimateAsync(1500, (p) => { SplashProgressBar.Width = 200 * p; });

            // گام ۶: محو شدن اسپلش و نمایش فرم ورود
            await AnimateAsync(400, (p) => { SplashPanel.Opacity = 1 - p; });
            SplashPanel.IsVisible = false;
            LoginPanel.IsVisible = true;

            // گام ۷: نمایش فرم ورود (محو + سرخوردن از پایین)
            await AnimateAsync(500, (p) =>
            {
                LoginPanel.Opacity = p;
                _loginPanelTranslate.Y = 20 - (20 * p);
            });

            UsernameTextBox.Focus();
        }
        catch (Exception ex)
        {
            // Never let the splash animation block startup
            ErrorHandler.LogError(ex, "RunSplashSequenceAsync");
            SplashPanel.IsVisible = false;
            LoginPanel.IsVisible = true;
            LoginPanel.Opacity = 1;
            _loginPanelTranslate.Y = 0;
        }
    }

    /// <summary>
    /// انیمیشن سبک مبتنی بر Task.Delay (بدون استفاده از Avalonia.Animation).
    /// </summary>
    /// <param name="durationMs">مدت کل انیمیشن به میلی‌ثانیه</param>
    /// <param name="onFrame">کال‌بک هر فریم که مقدار پیشرفت نرم‌شده (۰ تا ۱) را دریافت می‌کند</param>
    private async Task AnimateAsync(int durationMs, Action<double> onFrame)
    {
        const int frameTime = 16;
        int totalFrames = Math.Max(1, durationMs / frameTime);
        for (int i = 1; i <= totalFrames; i++)
        {
            double progress = (double)i / totalFrames;
            // نرم‌سازی cubic ease-out برای حرکت طبیعی‌تر
            double eased = 1 - Math.Pow(1 - progress, 3);
            onFrame(eased);
            await Task.Delay(frameTime);
        }
    }

    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnLoginClick(null, new RoutedEventArgs());
        }
    }

    private async void OnLoginClick(object? sender, RoutedEventArgs e)
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

        var (success, message) = AuthService.Login(username, password);

        if (success)
        {
            _failedAttempts = 0;
            ShowMessage(message, isError: false);

            // ─── چک تغییر رمز ضروری (فقط از پرچم دیتابیس) ───
            var currentUser = AuthService.CurrentUser;
            bool forceChangePassword = currentUser?.MustChangePassword == true;

            // حالت «admin با رمز پیش‌فرض» توسط AuthServiceInitializer.CheckLegacyAdminPassword()
            // در استارتاپ تشخیص داده و پرچمش روشن می‌شود — نیازی به مقایسه‌ی رمز خام اینجا نیست

            if (forceChangePassword && currentUser != null)
            {
                var changeWindow = new ChangePasswordWindow(currentUser);
                var result = await changeWindow.ShowDialog<bool>(this);

                if (!result)
                {
                    // کاربر انصراف داد — بستن برنامه
                    AuthService.Logout("انصراف از تغییر رمز");

                    // Shutdown() باعث اجرای ShutdownRequested در App.axaml.cs می‌شود
                    // (بکاپ نهایی) — برخلاف Environment.Exit که آن را دور می‌زند
                    if (Application.Current?.ApplicationLifetime
                        is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.Shutdown();
                    }
                    else
                    {
                        Environment.Exit(0);
                    }
                    return;
                }
            }

            // باز کردن پنجره اصلی
            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }
        else
        {
            _failedAttempts++;
            ShowMessage(message, isError: true);

            PasswordTextBox.Text = "";
            PasswordTextBox.Focus();

            if (_failedAttempts >= 5)
            {
                StartLock(60);
                _failedAttempts = 0;
            }
        }
    }

    /// <summary>
    /// نمایش پیام به کاربر — فقط متن رنگی، بدون پس‌زمینه.
    /// </summary>
    private void ShowMessage(string message, bool isError)
    {
        MessageBorder.IsVisible = true;
        MessageBorder.Background = Brushes.Transparent;
        MessageText.Text = message;
        MessageText.Foreground = new SolidColorBrush(Color.Parse(isError ? "#DC2626" : "#059669"));
    }

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
