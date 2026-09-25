using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ShopManager.Desktop.Services;
using ShopManager.Desktop.ViewModels;
using System;
using System.Threading.Tasks;

using Color = Avalonia.Media.Color;

namespace ShopManager.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly UpdateService _updateService = new();
    private DispatcherTimer? _updateCheckTimer;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        ApplyPermissions();
        UpdateUserInfo();

        SessionTracker.UserDeactivated += OnUserDeactivated;
        SessionTracker.Start();

        StartClockTimer();
        StartUpdateCheck();
    }

    // ═══════════════════════════════════════════
    // آپدیت خودکار
    // ═══════════════════════════════════════════

    private void StartUpdateCheck()
    {
        // ═══ بررسی اولیه بعد از ۵ ثانیه ═══
        var initialTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        initialTimer.Tick += async (s, e) =>
        {
            initialTimer.Stop();
            await CheckForUpdatesAsync();
        };
        initialTimer.Start();

        // ═══ بررسی مجدد هر ۶ ساعت ═══
        _updateCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromHours(6)
        };
        _updateCheckTimer.Tick += async (s, e) => await CheckForUpdatesAsync();
        _updateCheckTimer.Start();

        _updateService.StateChanged += OnUpdateStateChanged;
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var updateInfo = await _updateService.CheckForUpdatesAsync();
            if (updateInfo != null)
            {
                UpdateButton.Content = $"🔄 بروزرسانی {updateInfo.TargetFullRelease.Version}";
                UpdateButton.Background = new SolidColorBrush(Color.Parse("#F59E0B"));
                UpdateButton.IsVisible = true;
            }
        }
        catch { }
    }

    private void OnUpdateStateChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_updateService.IsUpdateReady)
            {
                UpdateButton.Content = "✅ نصب بروزرسانی";
                UpdateButton.Background = new SolidColorBrush(Color.Parse("#10B981"));
            }
            else if (_updateService.DownloadProgress > 0)
            {
                UpdateButton.Content = $"⬇️ دانلود {_updateService.DownloadProgress}٪";
            }
        });
    }

    private async void OnUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (_updateService.IsUpdateReady)
        {
            // ─── اعمال آپدیت ───
            _updateService.ApplyUpdatesAndRestart();
            return;
        }

        // ─── دانلود آپدیت ───
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "⬇️ در حال دانلود...";

        var success = await _updateService.DownloadUpdatesAsync();

        if (success)
        {
            UpdateButton.Content = "✅ نصب بروزرسانی";
            UpdateButton.Background = new SolidColorBrush(Color.Parse("#10B981"));
            UpdateButton.IsEnabled = true;
        }
        else
        {
            UpdateButton.Content = "❌ خطا در دانلود";
            UpdateButton.Background = new SolidColorBrush(Color.Parse("#EF4444"));
            UpdateButton.IsEnabled = true;
        }
    }

    // ═══════════════════════════════════════════
    // دسترسی‌ها
    // ═══════════════════════════════════════════

    private void ApplyPermissions()
    {
        if (!AuthService.IsLoggedIn)
        {
            HideAllButtons();
            return;
        }

        DashboardButton.IsVisible = AuthService.HasAccess("Dashboard");
        ItemsButton.IsVisible = AuthService.HasAccess("Items");
        PurchaseButton.IsVisible = AuthService.HasAccess("Purchase");
        TransferButton.IsVisible = AuthService.HasAccess("Transfer");
        POSButton.IsVisible = AuthService.HasAccess("POS");
        CustomersButton.IsVisible = AuthService.HasAccess("Customers");
        CashboxButton.IsVisible = AuthService.HasAccess("Cashbox");
        SettingsButton.IsVisible = AuthService.HasAccess("Settings");
        UserManagementButton.IsVisible = AuthService.HasAccess("UserManagement");
    }

    private void HideAllButtons()
    {
        DashboardButton.IsVisible = false;
        ItemsButton.IsVisible = false;
        PurchaseButton.IsVisible = false;
        TransferButton.IsVisible = false;
        POSButton.IsVisible = false;
        CustomersButton.IsVisible = false;
        CashboxButton.IsVisible = false;
        SettingsButton.IsVisible = false;
        UserManagementButton.IsVisible = false;
    }

    private void UpdateUserInfo()
    {
        try
        {
            UserInfoText.Text = AuthService.GetCurrentUserDisplay();
        }
        catch { }
    }

    // ═══════════════════════════════════════════
    // ساعت
    // ═══════════════════════════════════════════

    private void StartClockTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };

        timer.Tick += (s, e) =>
        {
            UpdateClock();
            UpdateUserInfo();
        };

        UpdateClock();
        timer.Start();
    }

    private void UpdateClock()
    {
        try
        {
            var now = DateTime.Now;
            var shamsi = ShopManager.Domain.Helpers.JalaliDate.ToShamsi(now);
            var time = now.ToString("HH:mm");

            ClockText.Text = $"{ShopManager.Domain.Helpers.PersianNumber.ToPersianDigits(shamsi)} — {ShopManager.Domain.Helpers.PersianNumber.ToPersianDigits(time)}";
        }
        catch { }
    }

    // ═══════════════════════════════════════════
    // غیرفعال شدن کاربر
    // ═══════════════════════════════════════════

    private void OnUserDeactivated()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                SessionTracker.UserDeactivated -= OnUserDeactivated;
                SessionTracker.Stop();

                var loginWindow = new LoginWindow();
                loginWindow.Show();

                Close();
            }
            catch { }
        });
    }

    // ═══════════════════════════════════════════
    // رویدادهای دکمه‌ها
    // ═══════════════════════════════════════════

    private void OnDashboardClick(object? sender, RoutedEventArgs e)
    {
        if (!AuthService.HasAccess("Dashboard")) return;
        new DashboardWindow().Show();
    }

    private void OnItemsClick(object? sender, RoutedEventArgs e)
    {
        if (!AuthService.HasAccess("Items")) return;
        new ItemsWindow().Show();
    }

    private void OnPurchaseClick(object? sender, RoutedEventArgs e)
    {
        if (!AuthService.HasAccess("Purchase")) return;
        new PurchaseWindow().Show();
    }

    private void OnTransferClick(object? sender, RoutedEventArgs e)
    {
        if (!AuthService.HasAccess("Transfer")) return;
        new TransferWindow().Show();
    }

    private void OnPOSClick(object? sender, RoutedEventArgs e)
    {
        if (!AuthService.HasAccess("POS")) return;
        new POSWindow().Show();
    }

    private void OnCustomersClick(object? sender, RoutedEventArgs e)
    {
        if (!AuthService.HasAccess("Customers")) return;
        new CustomersWindow().Show();
    }

    private void OnCashboxClick(object? sender, RoutedEventArgs e)
    {
        if (!AuthService.HasAccess("Cashbox")) return;
        new CashboxWindow().Show();
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (!AuthService.HasAccess("Settings")) return;
        new SettingsWindow().Show();
    }

    private void OnUserManagementClick(object? sender, RoutedEventArgs e)
    {
        if (!AuthService.HasAccess("UserManagement")) return;
        new UsersWindow().Show();
    }

    private void OnLogoutClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            AuthService.Logout("خروج دستی");
            SessionTracker.Stop();
            SessionTracker.UserDeactivated -= OnUserDeactivated;

            var loginWindow = new LoginWindow();
            loginWindow.Show();

            Close();
        }
        catch { }
    }
}