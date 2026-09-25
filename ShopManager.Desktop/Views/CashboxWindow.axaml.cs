using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ShopManager.Desktop.Services;
using ShopManager.Desktop.ViewModels;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;
using ShopManager.Domain.Services;

using Color = Avalonia.Media.Color;

namespace ShopManager.Desktop.Views;

public partial class CashboxWindow : Window
{
    private bool _isLocked = false;

    public CashboxWindow()
    {
        InitializeComponent();
        DateTextBox.Text = JalaliDate.TodayShamsi();
        _isLocked = MoneyMask.IsLocked;
        LoadData();
    }

    private void LoadData()
    {
        try
        {
            // اگه کاربر دسترسی مالی نداره، همه چی ماسک می‌شه
            using var db = DatabaseService.CreateContext();

            var settings = db.Settings.FirstOrDefault();
            decimal initialCapital = settings?.InitialCapital ?? 0;

            InitialCapitalText.Text = MoneyMask.Toman(initialCapital);

            var allSales = db.Sales.Where(s => s.EntryType == EntryType.Normal).ToList();
            var allPurchases = db.Purchases.Where(p => p.EntryType == EntryType.Normal).ToList();
            var allLedger = db.CashLedgers.ToList();

            var cashSales = allSales
                .Where(s => s.PaymentStatus == PaymentStatus.Cash)
                .Sum(s => s.Revenue);
            CashSalesText.Text = MoneyMask.Toman(cashSales);

            var cashPurchases = allPurchases
                .Where(p => p.PaymentStatus == PaymentStatus.Cash)
                .Sum(p => p.TotalCost);
            CashPurchasesText.Text = MoneyMask.Toman(cashPurchases);

            var ledgerIn = allLedger.Sum(l => l.AmountIn ?? 0);
            var ledgerOut = allLedger.Sum(l => l.AmountOut ?? 0);
            LedgerInText.Text = MoneyMask.Toman(ledgerIn);
            LedgerOutText.Text = MoneyMask.Toman(ledgerOut);

            var balance = CashboxCalculator.CalculateCashbox(
                initialCapital, allSales, allPurchases, allLedger);
            CashboxBalanceText.Text = MoneyMask.Toman(balance);

            var receivables = CashboxCalculator.CalculateReceivables(allSales);
            var payables = CashboxCalculator.CalculatePayables(allPurchases);
            ReceivablesText.Text = MoneyMask.Toman(receivables);
            PayablesText.Text = MoneyMask.Toman(payables);

            LoadLedgerRows(allLedger);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    private void LoadLedgerRows(List<CashLedger> ledger)
    {
        LedgerPanel.Children.Clear();

        if (ledger.Count == 0)
        {
            LedgerPanel.Children.Add(new TextBlock
            {
                Text = "هنوز تراکنشی ثبت نشده",
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 40)
            });
            LedgerCountText.Text = "تعداد: ۰";
            return;
        }

        var sorted = ledger.OrderByDescending(l => l.DateGregorian)
                           .ThenByDescending(l => l.Id)
                           .ToList();

        foreach (var l in sorted)
        {
            var isIncome = (l.AmountIn ?? 0) > 0;
            var amount = l.AmountIn ?? l.AmountOut ?? 0;

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("100,180,*,150,100,150")
            };

            var dateText = new TextBlock
            {
                Text = PersianNumber.ToPersianDigits(l.DateShamsi),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dateText, 0);

            var typeText = new TextBlock
            {
                Text = GetTypeDisplay(l.Type),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#334155")),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(typeText, 1);

            var cpText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(l.Counterparty) ? "—" : l.Counterparty,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(cpText, 2);

            var amountText = new TextBlock
            {
                Text = MoneyMask.Toman(amount),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse(isIncome ? "#10B981" : "#EF4444")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(amountText, 3);

            var dirText = new TextBlock
            {
                Text = isIncome ? "ورود" : "خروج",
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse(isIncome ? "#10B981" : "#EF4444")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dirText, 4);

            var noteText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(l.Note) ? "—" : l.Note,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(noteText, 5);

            grid.Children.Add(dateText);
            grid.Children.Add(typeText);
            grid.Children.Add(cpText);
            grid.Children.Add(amountText);
            grid.Children.Add(dirText);
            grid.Children.Add(noteText);

            var row = new Border
            {
                Padding = new Thickness(12, 10),
                BorderBrush = new SolidColorBrush(Color.Parse("#F1F5F9")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid
            };

            LedgerPanel.Children.Add(row);
        }

        LedgerCountText.Text = $"تعداد: {PersianNumber.ToPersian(sorted.Count)}";
    }

    private static string GetTypeDisplay(LedgerType type)
    {
        return type switch
        {
            LedgerType.InitialCapital => "سرمایه اولیه",
            LedgerType.LoanGiven => "وام داده‌شده",
            LedgerType.LoanRepaid => "وام گرفته‌شده",
            LedgerType.ProfitWithdrawal => "برداشت سود",
            LedgerType.ProfitDistribution => "تقسیم سود",
            _ => "سایر"
        };
    }

    private async void OnEditCapitalClick(object? sender, RoutedEventArgs e)
    {
        // اگه کاربر دسترسی مالی نداره، اجازه ویرایش سرمایه رو نمی‌دیم
        if (_isLocked)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = "🔒 دسترسی به ویرایش سرمایه اولیه محدود شده";
            return;
        }

        var inputWindow = new Window
        {
            Title = "ویرایش سرمایه اولیه",
            Width = 400,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new Avalonia.Media.FontFamily("Vazirmatn,IRANSans,Segoe UI"),
            Background = new SolidColorBrush(Color.Parse("#F1F5F9"))
        };

        var mainPanel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14
        };

        mainPanel.Children.Add(new TextBlock
        {
            Text = "سرمایه اولیه (تومان):",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#334155"))
        });

        var amountBox = new TextBox
        {
            FontSize = 15,
            Padding = new Thickness(14, 12),
            Text = "0",
            Watermark = "مثال: 500000000"
        };

        try
        {
            using var db = DatabaseService.CreateContext();
            var settings = db.Settings.FirstOrDefault();
            amountBox.Text = (settings?.InitialCapital ?? 0).ToString("0");
        }
        catch { }

        mainPanel.Children.Add(amountBox);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };

        var saveBtn = new Button
        {
            Content = "ذخیره",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Background = new SolidColorBrush(Color.Parse("#10B981")),
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
            Padding = new Thickness(24, 10)
        };

        saveBtn.Click += (s, args) =>
        {
            try
            {
                var text = PersianNumber.ToEnglishDigits(amountBox.Text ?? "").Trim();
                text = text.Replace(",", "").Replace("٬", "");

                if (!decimal.TryParse(text, out var amount) || amount < 0) return;

                using var db = DatabaseService.CreateContext();
                var settings = db.Settings.FirstOrDefault();
                if (settings == null)
                {
                    settings = new Setting { InitialCapital = amount };
                    db.Settings.Add(settings);
                }
                else
                {
                    settings.InitialCapital = amount;
                }
                db.SaveChanges();

                inputWindow.Close();
                LoadData();

                StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
                StatusText.Text = "✓ سرمایه اولیه ذخیره شد";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"خطا: {ex.Message}";
            }
        };

        var cancelBtn = new Button
        {
            Content = "انصراف",
            FontSize = 14,
            Background = new SolidColorBrush(Color.Parse("#F1F5F9")),
            Foreground = new SolidColorBrush(Color.Parse("#475569")),
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24, 10)
        };
        cancelBtn.Click += (s, args) => inputWindow.Close();

        btnPanel.Children.Add(saveBtn);
        btnPanel.Children.Add(cancelBtn);
        mainPanel.Children.Add(btnPanel);

        inputWindow.Content = mainPanel;
        await inputWindow.ShowDialog(this);
    }

    private void OnAddLedgerClick(object? sender, RoutedEventArgs e)
    {
        // اگه کاربر دسترسی مالی نداره، اجازه ثبت تراکنش رو نمی‌دیم
        if (_isLocked)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = "🔒 دسترسی به ثبت تراکنش مالی محدود شده";
            return;
        }

        StatusText.Text = "";

        var dateText = PersianNumber.ToEnglishDigits(DateTextBox.Text ?? "").Trim();
        var amountText = PersianNumber.ToEnglishDigits(AmountTextBox.Text ?? "").Trim()
                         .Replace(",", "").Replace("٬", "");
        var counterparty = CounterpartyTextBox.Text?.Trim() ?? "";
        var note = NoteTextBox.Text?.Trim() ?? "";
        var typeIndex = TypeComboBox.SelectedIndex;

        if (string.IsNullOrWhiteSpace(dateText))
        {
            StatusText.Text = "تاریخ را وارد کنید";
            return;
        }
        if (!decimal.TryParse(amountText, out var amount) || amount <= 0)
        {
            StatusText.Text = "مبلغ باید عدد مثبت باشد";
            return;
        }

        DateTime gregorianDate;
        try
        {
            gregorianDate = JalaliDate.ToGregorian(dateText);
        }
        catch
        {
            StatusText.Text = "فرمت تاریخ اشتباه است";
            return;
        }

        LedgerType ledgerType;
        decimal? amountIn = null;
        decimal? amountOut = null;

        switch (typeIndex)
        {
            case 0: ledgerType = LedgerType.LoanGiven; amountOut = amount; break;
            case 1: ledgerType = LedgerType.LoanRepaid; amountIn = amount; break;
            case 2: ledgerType = LedgerType.ProfitWithdrawal; amountOut = amount; break;
            case 3: ledgerType = LedgerType.ProfitDistribution; amountOut = amount; break;
            default: ledgerType = LedgerType.Other; amountOut = amount; break;
        }

        try
        {
            using var db = DatabaseService.CreateContext();

            var ledger = new CashLedger
            {
                DateShamsi = dateText,
                DateGregorian = gregorianDate,
                Type = ledgerType,
                Counterparty = string.IsNullOrWhiteSpace(counterparty) ? null : counterparty,
                AmountIn = amountIn,
                AmountOut = amountOut,
                Note = string.IsNullOrWhiteSpace(note) ? null : note,
                CreatedAt = DateTime.UtcNow
            };

            db.CashLedgers.Add(ledger);
            db.SaveChanges();

            StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
            StatusText.Text = $"✓ تراکنش ثبت شد: {GetTypeDisplay(ledgerType)} — {PersianNumber.ToToman(amount)}";

            AmountTextBox.Text = "";
            CounterpartyTextBox.Text = "";
            NoteTextBox.Text = "";
            TypeComboBox.SelectedIndex = 0;

            LoadData();
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    private async void OnExportExcelClick(object? sender, RoutedEventArgs e)
    {
        if (_isLocked)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = "🔒 دسترسی به خروجی مالی محدود شده";
            return;
        }

        try
        {
            using var db = DatabaseService.CreateContext();
            var ledger = db.CashLedgers
                .OrderByDescending(l => l.DateGregorian)
                .ThenByDescending(l => l.Id)
                .ToList();

            if (ledger.Count == 0)
            {
                StatusText.Text = "داده‌ای برای خروجی وجود ندارد";
                return;
            }

            await ExcelExportHelper.ExportCashLedgerAsync(this, ledger);
            StatusText.Text = "✓ فایل اکسل با موفقیت ذخیره شد";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        LoadData();
        StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
        StatusText.Text = "بروزرسانی شد";
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}