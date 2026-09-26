using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ShopManager.Desktop.Model;
using ShopManager.Desktop.Models;
using ShopManager.Desktop.Services;
using ShopManager.Domain.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Color = Avalonia.Media.Color;
using FontFamily = Avalonia.Media.FontFamily;
using TextAlignment = Avalonia.Media.TextAlignment;

namespace ShopManager.Desktop.Views;

public partial class SettingsWindow : Window
{
    private StoreSettings _storeSettings = new();
    private AppPreferences _workingPrefs = new();

    private string _selectedAccent = "Blue";
    private bool _isLoading = true;
    private string _logoPath = "";

    /// <summary>در حال هم‌سان‌سازی کنترل‌های تم است تا تغییرات تکراری/بازگشتی رخ ندهد</summary>
    private bool _syncingAppearance;

    public SettingsWindow()
    {
        InitializeComponent();

        _storeSettings = StoreSettingsService.Current;
        _workingPrefs = PreferencesService.Current;
        _selectedAccent = _workingPrefs.AccentColor;
        _logoPath = _storeSettings.LogoPath;

        BuildPalette();

        LoadStoreTab();
        LoadFinanceTab();
        LoadInvoiceTab();
        LoadBackupTab();
        LoadAppearanceTab();

        ThemeLightRadio.IsCheckedChanged += (s, e) => OnThemeRadioChanged();
        ThemeDarkRadio.IsCheckedChanged += (s, e) => OnThemeRadioChanged();
        FontComboBox.SelectionChanged += (s, e) => OnAppearanceChanged();
        FontSizeComboBox.SelectionChanged += (s, e) => OnAppearanceChanged();

        // پیش‌نمایش زنده‌ی تم و رنگ اصلی از طریق ComboBoxها
        AppThemeComboBox.SelectionChanged += (s, e) => OnAppThemeComboChanged();
        AppAccentComboBox.SelectionChanged += (s, e) => OnAppAccentComboChanged();

        _isLoading = false;
    }

    // ═══ تب‌ها ═══
    private void OnTabStoreClick(object? sender, RoutedEventArgs e) => SwitchTab("store");
    private void OnTabFinanceClick(object? sender, RoutedEventArgs e) => SwitchTab("finance");
    private void OnTabInvoiceClick(object? sender, RoutedEventArgs e) => SwitchTab("invoice");
    private void OnTabBackupClick(object? sender, RoutedEventArgs e) => SwitchTab("backup");
    private void OnTabAppearanceClick(object? sender, RoutedEventArgs e) => SwitchTab("appearance");

    private void SwitchTab(string tab)
    {
        StorePanel.IsVisible = tab == "store";
        FinancePanel.IsVisible = tab == "finance";
        InvoicePanel.IsVisible = tab == "invoice";
        BackupPanel.IsVisible = tab == "backup";
        AppearancePanel.IsVisible = tab == "appearance";

        SetTabStyle(TabStoreBtn, tab == "store");
        SetTabStyle(TabFinanceBtn, tab == "finance");
        SetTabStyle(TabInvoiceBtn, tab == "invoice");
        SetTabStyle(TabBackupBtn, tab == "backup");
        SetTabStyle(TabAppearanceBtn, tab == "appearance");

        if (tab == "backup") RefreshBackupStats();
    }

    private void SetTabStyle(Button btn, bool active)
    {
        if (active) { btn.Background = new SolidColorBrush(Color.Parse("#4F46E5")); btn.Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")); }
        else { btn.Background = new SolidColorBrush(Color.Parse("#00000000")); btn.Foreground = new SolidColorBrush(Color.Parse("#475569")); }
    }

    // ═══ تب فروشگاه ═══
    private void LoadStoreTab()
    {
        StoreNameBox.Text = _storeSettings.StoreName;
        StoreAddressBox.Text = _storeSettings.Address;
        StorePhoneBox.Text = _storeSettings.Phone;
        FooterTextBox.Text = _storeSettings.FooterText;
        UpdateLogoPreview();
    }

    private void UpdateLogoPreview()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_logoPath) && File.Exists(_logoPath))
            {
                LogoPreview.Source = new Bitmap(_logoPath);
                LogoPathText.Text = Path.GetFileName(_logoPath);
            }
            else { LogoPreview.Source = null; LogoPathText.Text = "لوگویی انتخاب نشده"; }
        }
        catch { LogoPreview.Source = null; LogoPathText.Text = "خطا در بارگذاری لوگو"; }
    }

    private async void OnPickLogoClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "انتخاب لوگو",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("تصویر") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.ico" } } }
            });

            if (files.Count == 0) return;
            var sourcePath = files[0].Path.LocalPath;
            if (string.IsNullOrWhiteSpace(sourcePath)) return;

            var destFolder = Path.Combine(DatabaseService.DataFolder, "logo");
            Directory.CreateDirectory(destFolder);
            var ext = Path.GetExtension(sourcePath);
            var destPath = Path.Combine(destFolder, $"logo{ext}");
            try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
            File.Copy(sourcePath, destPath, overwrite: true);
            _logoPath = destPath;
            UpdateLogoPreview();
        }
        catch (Exception ex) { ErrorHandler.LogError(ex, "Settings"); StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444")); StatusText.Text = ErrorHandler.GetUserMessage(ex); }
    }

    private void OnRemoveLogoClick(object? sender, RoutedEventArgs e)
    {
        try { if (!string.IsNullOrWhiteSpace(_logoPath) && File.Exists(_logoPath)) { try { File.Delete(_logoPath); } catch { } } } catch { }
        _logoPath = "";
        UpdateLogoPreview();
    }

    // ═══ تب مالی ═══
    private void LoadFinanceTab()
    {
        try
        {
            using var db = DatabaseService.CreateContext();
            var setting = db.Settings.FirstOrDefault();
            InitialCapitalBox.Text = (setting?.InitialCapital ?? 0).ToString("0");
        }
        catch { InitialCapitalBox.Text = "0"; }

        DefaultMarkupBox.Text = _storeSettings.DefaultMarkupPct.ToString("0");
        POSTerminalsBox.Text = string.Join("\n", _storeSettings.POSTerminals);
    }

    // ═══ تب فاکتور ═══
    private void LoadInvoiceTab()
    {
        switch (_storeSettings.PaperSize)
        {
            case "A5": PaperA5Radio.IsChecked = true; break;
            case "80mm": Paper80mmRadio.IsChecked = true; break;
            case "58mm": Paper58mmRadio.IsChecked = true; break;
            default: PaperA4Radio.IsChecked = true; break;
        }
        AutoPrintCheckBox.IsChecked = _storeSettings.AutoPrintAfterSale;
        ShowLogoCheckBox.IsChecked = _storeSettings.ShowLogoOnInvoice;
    }

    private string GetSelectedPaperSize()
    {
        if (PaperA5Radio.IsChecked == true) return "A5";
        if (Paper80mmRadio.IsChecked == true) return "80mm";
        if (Paper58mmRadio.IsChecked == true) return "58mm";
        return "A4";
    }

    private void OnPreviewInvoiceClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var html = SaleInvoiceHtmlBuilder.BuildPreviewHtml(_storeSettings, GetSelectedPaperSize(), ShowLogoCheckBox.IsChecked == true);
            var tempPath = Path.Combine(Path.GetTempPath(), $"invoice-preview-{DateTime.Now:yyyyMMddHHmmss}.html");
            File.WriteAllText(tempPath, html, Encoding.UTF8);
            Process.Start(new ProcessStartInfo { FileName = tempPath, UseShellExecute = true });
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#4F46E5"));
            StatusText.Text = "✓ پیش‌نمایش باز شد";
        }
        catch (Exception ex) { ErrorHandler.LogError(ex, "Settings"); StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444")); StatusText.Text = ErrorHandler.GetUserMessage(ex); }
    }

    // ═══ تب بکاپ ═══
    private void LoadBackupTab()
    {
        BackupAutoCheckBox.IsChecked = _storeSettings.BackupAutoEnabled;
        BackupIntervalCombo.SelectedIndex = _storeSettings.BackupIntervalMinutes switch { 5 => 0, 15 => 1, 30 => 2, 60 => 3, 120 => 4, _ => 1 };
        BackupKeepCombo.SelectedIndex = _storeSettings.BackupKeepCount switch { 10 => 0, 20 => 1, 30 => 2, 50 => 3, 100 => 4, _ => 2 };
        RefreshBackupStats();
    }

    private void RefreshBackupStats()
    {
        try
        {
            DbSizeText.Text = BackupService.FormatSize(BackupService.GetDatabaseSize());
            var backups = BackupService.GetBackups();
            BackupCountText.Text = PersianNumber.ToPersian(backups.Count);

            if (backups.Count > 0)
            {
                var last = backups.First();
                LastBackupText.Text = $"آخرین بکاپ: {PersianNumber.ToPersianDigits(JalaliDate.ToShamsi(last.CreatedAt))} ساعت {PersianNumber.ToPersianDigits(last.CreatedAt.ToString("HH:mm"))} — {last.SizeDisplay}";
            }
            else LastBackupText.Text = "هنوز بکاپی گرفته نشده";
        }
        catch { }
    }

    private void OnManualBackupClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = BackupService.CreateForcedBackup();
            RefreshBackupStats();
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
            StatusText.Text = $"✓ بکاپ گرفته شد: {Path.GetFileName(path)}";
        }
        catch (Exception ex) { ErrorHandler.LogError(ex, "Settings"); StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444")); StatusText.Text = ErrorHandler.GetUserMessage(ex); }
    }

    private void OnOpenBackupFolderClick(object? sender, RoutedEventArgs e) => BackupService.OpenBackupFolder();

    private async void OnShowBackupListClick(object? sender, RoutedEventArgs e)
    {
        var backups = BackupService.GetBackups();
        if (backups.Count == 0) { StatusText.Foreground = new SolidColorBrush(Color.Parse("#D97706")); StatusText.Text = "هنوز بکاپی وجود ندارد"; return; }
        await ShowBackupListDialog(backups);
    }

    private async Task ShowBackupListDialog(List<BackupInfo> backups)
    {
        var dialog = new Window
        {
            Title = "لیست بکاپ‌ها",
            Width = 700,
            Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Vazirmatn,IRANSans,Segoe UI"),
            Background = new SolidColorBrush(Color.Parse("#F1F5F9"))
        };

        var mainPanel = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20) };
        var header = new Border { Background = new SolidColorBrush(Color.Parse("#EEF2FF")), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12) };
        header.Child = new TextBlock { Text = $"📜 {PersianNumber.ToPersian(backups.Count)} بکاپ موجود", FontSize = 16, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#4F46E5")) };
        Grid.SetRow(header, 0); mainPanel.Children.Add(header);

        var listPanel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 12) };

        foreach (var backup in backups)
        {
            var row = new Border { Background = new SolidColorBrush(Color.Parse("#FFFFFF")), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12), BorderBrush = new SolidColorBrush(Color.Parse("#E2E8F0")), BorderThickness = new Thickness(1) };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,140,120,Auto,Auto") };

            var nameText = new TextBlock { Text = backup.FileName, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#0F172A")), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameText, 0);
            var dateText = new TextBlock { Text = $"{PersianNumber.ToPersianDigits(JalaliDate.ToShamsi(backup.CreatedAt))} {PersianNumber.ToPersianDigits(backup.CreatedAt.ToString("HH:mm"))}", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#475569")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(dateText, 1);
            var sizeText = new TextBlock { Text = backup.SizeDisplay, FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#64748B")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(sizeText, 2);

            var restoreBtn = new Button { Content = "🔄 بازیابی", FontSize = 12, FontWeight = FontWeight.SemiBold, Background = new SolidColorBrush(Color.Parse("#FEF3C7")), Foreground = new SolidColorBrush(Color.Parse("#D97706")), Padding = new Thickness(12, 6), CornerRadius = new CornerRadius(6), Margin = new Thickness(4, 0, 4, 0) };
            var capturedBackup = backup;
            restoreBtn.Click += async (s, e) =>
            {
                var ok = await ShowConfirmDialog("بازیابی", $"از بکاپ «{capturedBackup.FileName}» بازیابی می‌شه. برنامه بسته می‌شه. ادامه؟");
                if (ok) { try { BackupService.RestoreBackup(capturedBackup.FilePath); Environment.Exit(0); } catch (Exception ex) { ErrorHandler.LogError(ex, "Settings"); StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444")); StatusText.Text = ErrorHandler.GetUserMessage(ex); } }
            };
            Grid.SetColumn(restoreBtn, 3);

            var deleteBtn = new Button { Content = "🗑️", FontSize = 12, Background = new SolidColorBrush(Color.Parse("#FEF2F2")), Foreground = new SolidColorBrush(Color.Parse("#DC2626")), Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(6) };
            deleteBtn.Click += async (s, e) =>
            {
                var ok = await ShowConfirmDialog("حذف بکاپ", $"حذف «{capturedBackup.FileName}»؟");
                if (ok) { BackupService.DeleteBackup(capturedBackup.FilePath); dialog.Close(); RefreshBackupStats(); }
            };
            Grid.SetColumn(deleteBtn, 4);

            grid.Children.Add(nameText); grid.Children.Add(dateText); grid.Children.Add(sizeText); grid.Children.Add(restoreBtn); grid.Children.Add(deleteBtn);
            row.Child = grid; listPanel.Children.Add(row);
        }

        var scroll = new ScrollViewer { Content = listPanel };
        Grid.SetRow(scroll, 1); mainPanel.Children.Add(scroll);

        var closeBtn = new Button { Content = "بستن", FontSize = 14, FontWeight = FontWeight.SemiBold, Background = new SolidColorBrush(Color.Parse("#F1F5F9")), Foreground = new SolidColorBrush(Color.Parse("#475569")), BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")), BorderThickness = new Thickness(1), Padding = new Thickness(30, 10), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center };
        closeBtn.Click += (s, e) => dialog.Close();
        Grid.SetRow(closeBtn, 2); mainPanel.Children.Add(closeBtn);
        dialog.Content = mainPanel;
        await dialog.ShowDialog(this);
    }

    private async Task<bool> ShowConfirmDialog(string title, string message)
    {
        var dialog = new Window { Title = title, Width = 460, Height = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner, FlowDirection = FlowDirection.RightToLeft, FontFamily = new FontFamily("Vazirmatn,IRANSans,Segoe UI"), Background = new SolidColorBrush(Color.Parse("#F1F5F9")), CanResize = false };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = "⚠️ " + title, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#DC2626")) });
        panel.Children.Add(new TextBlock { Text = message, FontSize = 13, Foreground = new SolidColorBrush(Color.Parse("#334155")), TextWrapping = TextWrapping.Wrap });
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        bool result = false;
        var yesBtn = new Button { Content = "بله", FontSize = 14, FontWeight = FontWeight.Bold, Background = new SolidColorBrush(Color.Parse("#DC2626")), Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")), Padding = new Thickness(26, 10), CornerRadius = new CornerRadius(8) };
        yesBtn.Click += (s, e) => { result = true; dialog.Close(); };
        var noBtn = new Button { Content = "انصراف", FontSize = 14, Background = new SolidColorBrush(Color.Parse("#F1F5F9")), Foreground = new SolidColorBrush(Color.Parse("#475569")), BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")), BorderThickness = new Thickness(1), Padding = new Thickness(26, 10), CornerRadius = new CornerRadius(8) };
        noBtn.Click += (s, e) => dialog.Close();
        btnPanel.Children.Add(yesBtn); btnPanel.Children.Add(noBtn); panel.Children.Add(btnPanel);
        dialog.Content = panel;
        return await dialog.ShowDialog<bool>(this);
    }

    // ═══ تب ظاهر ═══
    private void LoadAppearanceTab()
    {
        if (_workingPrefs.Theme == "Dark") ThemeDarkRadio.IsChecked = true; else ThemeLightRadio.IsChecked = true;
        FontComboBox.SelectedIndex = _workingPrefs.FontFamily switch { "IRANSans" => 1, "Tahoma" => 2, "Segoe UI" => 3, _ => 0 };
        FontSizeComboBox.SelectedIndex = _workingPrefs.FontSize switch { 12 => 0, 14 => 1, 16 => 2, 18 => 3, _ => 1 };

        // ─── مقداردهی اولیه ComboBoxها از تنظیمات ذخیره‌شده ───
        // مقدار پایدار هر گزینه در Tag نگه داشته شده است (Light/Dark و نام رنگ‌ها)
        AppThemeComboBox.SelectedIndex = _workingPrefs.Theme == "Dark" ? 1 : 0;
        SetAccentComboSelection(_selectedAccent);
    }

    private void BuildPalette()
    {
        PalettePanel.Children.Clear();
        foreach (var item in ThemeService.Palette)
        {
            var color = Color.Parse(item.Hex);
            var swatch = new Border
            {
                Width = 60,
                Height = 60,
                CornerRadius = new CornerRadius(30),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(6),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Tag = item.Name
            };
            if (item.Name == _selectedAccent) { swatch.BorderBrush = new SolidColorBrush(Color.Parse("#0F172A")); swatch.BorderThickness = new Thickness(4); }
            swatch.PointerPressed += OnSwatchClicked;
            PalettePanel.Children.Add(swatch);
        }
    }

    private void OnSwatchClicked(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is string colorName)
        {
            _selectedAccent = colorName;
            _workingPrefs.AccentColor = colorName;

            HighlightSwatch(colorName);
            SetAccentComboSelection(colorName);
            OnAppearanceChanged();
        }
    }

    /// <summary>حاشیه‌ی سواچ رنگ انتخاب‌شده را در پنل پالت برجسته می‌کند.</summary>
    /// <param name="colorName">نام کلید رنگ انتخاب‌شده</param>
    private void HighlightSwatch(string colorName)
    {
        foreach (var child in PalettePanel.Children)
        {
            if (child is Border b && b.Tag is string name)
            {
                if (name == colorName) { b.BorderBrush = new SolidColorBrush(Color.Parse("#0F172A")); b.BorderThickness = new Thickness(4); }
                else { b.BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")); b.BorderThickness = new Thickness(2); }
            }
        }
    }

    /// <summary>
    /// ComboBox رنگ اصلی را با رنگ انتخاب‌شده هم‌سان می‌کند.
    /// اگر رنگ انتخابی در میان گزینه‌های ComboBox نباشد، هیچ گزینه‌ای انتخاب نمی‌شود (‎-1)
    /// تا مقدار اشتباهی به کاربر نمایش داده نشود.
    /// </summary>
    /// <param name="colorName">نام کلید رنگ انتخاب‌شده</param>
    private void SetAccentComboSelection(string colorName)
    {
        _syncingAppearance = true;
        try
        {
            var index = -1;
            for (var i = 0; i < AppAccentComboBox.ItemCount; i++)
            {
                if (AppAccentComboBox.Items[i] is ComboBoxItem item && item.Tag is string key && key == colorName)
                {
                    index = i;
                    break;
                }
            }
            AppAccentComboBox.SelectedIndex = index;
        }
        finally { _syncingAppearance = false; }
    }

    /// <summary>با کلیک روی دکمه‌های رادیویی تم، ComboBox تم را هم‌سان می‌کند.</summary>
    private void OnThemeRadioChanged()
    {
        if (_syncingAppearance) return;

        _syncingAppearance = true;
        try { AppThemeComboBox.SelectedIndex = ThemeDarkRadio.IsChecked == true ? 1 : 0; }
        finally { _syncingAppearance = false; }

        OnAppearanceChanged();
    }

    /// <summary>تغییر تم از طریق ComboBox — تم به‌صورت زنده اعمال می‌شود.</summary>
    private void OnAppThemeComboChanged()
    {
        if (_isLoading || _syncingAppearance) return;

        _syncingAppearance = true;
        try
        {
            if (AppThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string key && key == "Dark") ThemeDarkRadio.IsChecked = true;
            else ThemeLightRadio.IsChecked = true;
        }
        finally { _syncingAppearance = false; }

        OnAppearanceChanged();
    }

    /// <summary>تغییر رنگ اصلی از طریق ComboBox — رنگ به‌صورت زنده اعمال می‌شود.</summary>
    private void OnAppAccentComboChanged()
    {
        if (_isLoading || _syncingAppearance) return;
        if (AppAccentComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string key) return;

        _selectedAccent = key;
        _workingPrefs.AccentColor = key;
        HighlightSwatch(key);
        OnAppearanceChanged();
    }

    /// <summary>نام کلید تم انتخاب‌شده — مقدار پایدار از Tag خوانده می‌شود، نه از متن فارسی.</summary>
    /// <returns>"Dark" یا "Light"</returns>
    private string GetSelectedThemeKey()
    {
        if (AppThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string key && !string.IsNullOrWhiteSpace(key))
            return key;
        return ThemeDarkRadio.IsChecked == true ? "Dark" : "Light";
    }

    /// <summary>نام کلید رنگ اصلی انتخاب‌شده — مقدار پایدار از Tag خوانده می‌شود.</summary>
    /// <returns>نام رنگ؛ در صورت نبود انتخاب، آخرین رنگ ثبت‌شده برگردانده می‌شود.</returns>
    private string GetSelectedAccentKey()
    {
        if (AppAccentComboBox.SelectedItem is ComboBoxItem item && item.Tag is string key && !string.IsNullOrWhiteSpace(key))
            return key;
        return _selectedAccent;
    }

    private void OnAppearanceChanged()
    {
        if (_isLoading) return;
        try
        {
            var tempPrefs = new AppPreferences { Theme = ThemeDarkRadio.IsChecked == true ? "Dark" : "Light", FontFamily = GetSelectedFont(), FontSize = GetSelectedFontSize(), AccentColor = _selectedAccent };
            _workingPrefs.Theme = tempPrefs.Theme; _workingPrefs.FontFamily = tempPrefs.FontFamily; _workingPrefs.FontSize = tempPrefs.FontSize; _workingPrefs.AccentColor = tempPrefs.AccentColor;
            ThemeService.Apply(tempPrefs);
        }
        catch { }
    }

    private string GetSelectedFont() => FontComboBox.SelectedIndex switch { 1 => "IRANSans", 2 => "Tahoma", 3 => "Segoe UI", _ => "Vazirmatn" };
    private double GetSelectedFontSize() => FontSizeComboBox.SelectedIndex switch { 0 => 12, 2 => 16, 3 => 18, _ => 14 };

    // ═══ ذخیره ═══
    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            _storeSettings.StoreName = StoreNameBox.Text?.Trim() ?? "فروشگاه";
            _storeSettings.Address = StoreAddressBox.Text?.Trim() ?? "";
            _storeSettings.Phone = StorePhoneBox.Text?.Trim() ?? "";
            _storeSettings.FooterText = FooterTextBox.Text?.Trim() ?? "";
            _storeSettings.LogoPath = _logoPath;

            var capitalText = PersianNumber.ToEnglishDigits(InitialCapitalBox.Text ?? "").Trim().Replace(",", "").Replace("٬", "");
            if (decimal.TryParse(capitalText, out var capital) && capital >= 0)
            {
                using var db = DatabaseService.CreateContext();
                var setting = db.Settings.FirstOrDefault();
                if (setting == null) { setting = new Domain.Entities.Setting { InitialCapital = capital }; db.Settings.Add(setting); }
                else setting.InitialCapital = capital;
                db.SaveChanges();
            }

            var markupText = PersianNumber.ToEnglishDigits(DefaultMarkupBox.Text ?? "").Trim();
            if (decimal.TryParse(markupText, out var markup) && markup >= 0) _storeSettings.DefaultMarkupPct = markup;

            _storeSettings.POSTerminals = (POSTerminalsBox.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
            _storeSettings.PaperSize = GetSelectedPaperSize();
            _storeSettings.AutoPrintAfterSale = AutoPrintCheckBox.IsChecked == true;
            _storeSettings.ShowLogoOnInvoice = ShowLogoCheckBox.IsChecked == true;
            _storeSettings.BackupAutoEnabled = BackupAutoCheckBox.IsChecked == true;
            _storeSettings.BackupIntervalMinutes = BackupIntervalCombo.SelectedIndex switch { 0 => 5, 1 => 15, 2 => 30, 3 => 60, 4 => 120, _ => 15 };
            _storeSettings.BackupKeepCount = BackupKeepCombo.SelectedIndex switch { 0 => 10, 1 => 20, 2 => 30, 3 => 50, 4 => 100, _ => 30 };
            StoreSettingsService.Save(_storeSettings);

            _workingPrefs.Theme = GetSelectedThemeKey();
            _workingPrefs.FontFamily = GetSelectedFont();
            _workingPrefs.FontSize = GetSelectedFontSize();
            _workingPrefs.AccentColor = GetSelectedAccentKey();
            PreferencesService.Save(_workingPrefs);
            ThemeService.Apply(_workingPrefs);

            BackupService.RestartAutoBackupTimer();

            StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
            StatusText.Text = "✓ تنظیمات ذخیره شد";
        }
        catch (Exception ex) { ErrorHandler.LogError(ex, "Settings"); StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444")); StatusText.Text = ErrorHandler.GetUserMessage(ex); }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}