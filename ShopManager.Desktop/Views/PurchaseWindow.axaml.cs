using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ShopManager.Desktop.Services;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShopManager.Desktop.Views;

public partial class PurchaseWindow : Window
{
    private List<Item> _allItems = new();

    public PurchaseWindow()
    {
        InitializeComponent();

        // تاریخ امروز
        DateTextBox.Text = JalaliDate.TodayShamsi();

        // بارگذاری کالاها برای جستجو
        LoadItemsForSearch();

        // بارگذاری خریدهای امروز
        LoadTodayPurchases();
    }

    /// <summary>بارگذاری همه کالاها برای جستجوی خودکار</summary>
    private void LoadItemsForSearch()
    {
        using var db = DatabaseService.CreateContext();
        _allItems = db.Items.Where(i => i.IsActive).OrderBy(i => i.ItemCode).ToList();

        // نمایش به فرمت: "1001 | لیوان یکبار مصرف"
        var displayList = _allItems
            .Select(i => $"{i.ItemCode} | {i.Name}")
            .ToList();

        ItemSearchBox.ItemsSource = displayList;
    }

    /// <summary>وقتی کاربر یه کالا از لیست انتخاب کرد</summary>
    private void OnItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        var text = ItemSearchBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            ItemNameText.Text = "—";
            ItemNameText.Foreground = new SolidColorBrush(Color.Parse("#94A3B8"));
            return;
        }

        // جدا کردن کد از متن: "1001 | لیوان یکبار مصرف"
        var codePart = text.Split('|')[0].Trim();
        if (int.TryParse(codePart, out int code))
        {
            var item = _allItems.FirstOrDefault(i => i.ItemCode == code);
            if (item != null)
            {
                ItemNameText.Text = item.Name;
                ItemNameText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
            }
        }
    }

    /// <summary>گرفتن کد کالا از AutoCompleteBox</summary>
    private int? GetSelectedItemCode()
    {
        var text = ItemSearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;

        text = PersianNumber.ToEnglishDigits(text);

        // اگه کاربر خودش کد رو تایپ کرده
        if (int.TryParse(text, out int directCode))
            return directCode;

        // اگه از لیست انتخاب کرده: "1001 | ..."
        if (text.Contains('|'))
        {
            var codePart = text.Split('|')[0].Trim();
            if (int.TryParse(codePart, out int parsedCode))
                return parsedCode;
        }

        return null;
    }

    /// <summary>وقتی تعداد یا قیمت عوض شد، جمع کل رو حساب کن</summary>
    private void OnQtyChanged(object? sender, TextChangedEventArgs e)
    {
        var qtyText = PersianNumber.ToEnglishDigits(QtyTextBox.Text ?? "").Trim();
        var costText = PersianNumber.ToEnglishDigits(UnitCostTextBox.Text ?? "").Trim();

        if (decimal.TryParse(qtyText, out var qty) &&
            decimal.TryParse(costText, out var cost))
        {
            TotalCostText.Text = PersianNumber.ToToman(qty * cost);
        }
        else
        {
            TotalCostText.Text = "۰ تومان";
        }
    }

    /// <summary>ذخیره خرید جدید</summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "";

            // ─── خواندن مقادیر ───
            var dateText = PersianNumber.ToEnglishDigits(DateTextBox.Text ?? "").Trim();
            var qtyText = PersianNumber.ToEnglishDigits(QtyTextBox.Text ?? "").Trim();
            var costText = PersianNumber.ToEnglishDigits(UnitCostTextBox.Text ?? "").Trim();
            var supplier = SupplierTextBox.Text?.Trim();
            var paymentStatus = CashRadio.IsChecked == true
                                ? PaymentStatus.Cash
                                : PaymentStatus.Credit;

            var itemCode = GetSelectedItemCode();

            // ─── اعتبارسنجی ───
            if (string.IsNullOrWhiteSpace(dateText))
            {
                StatusText.Text = "تاریخ را وارد کنید";
                return;
            }
            if (itemCode == null)
            {
                StatusText.Text = "کالا را انتخاب کنید";
                return;
            }
            if (!decimal.TryParse(qtyText, out var qty) || qty <= 0)
            {
                StatusText.Text = "تعداد باید عدد مثبت باشد";
                return;
            }
            if (!decimal.TryParse(costText, out var unitCost) || unitCost < 0)
            {
                StatusText.Text = "قیمت خرید باید عدد باشد";
                return;
            }

            // ─── پیدا کردن کالا ───
            using var db = DatabaseService.CreateContext();
            var item = db.Items.FirstOrDefault(i => i.ItemCode == itemCode.Value && i.IsActive);
            if (item == null)
            {
                StatusText.Text = $"کالایی با کد {itemCode} پیدا نشد";
                return;
            }

            // ─── تاریخ میلادی ───
            DateTime gregorianDate;
            try
            {
                gregorianDate = JalaliDate.ToGregorian(dateText);
            }
            catch
            {
                StatusText.Text = "فرمت تاریخ اشتباه است (مثال: 1405/07/05)";
                return;
            }

            // ─── ذخیره در دیتابیس ───
            var purchase = new Purchase
            {
                ItemId = item.Id,
                DateShamsi = dateText,
                DateGregorian = gregorianDate,
                Qty = qty,
                UnitCost = unitCost,
                TotalCost = qty * unitCost,
                SupplierNote = string.IsNullOrWhiteSpace(supplier) ? null : supplier,
                PaymentStatus = paymentStatus,
                EntryType = EntryType.Normal,
                CreatedAt = DateTime.UtcNow
            };

            db.Purchases.Add(purchase);
            db.SaveChanges();

            // ─── پیام موفقیت ───
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
            StatusText.Text = $"✓ خرید ثبت شد: {item.Name} — {PersianNumber.ToToman(qty * unitCost)}";

            // ─── پاک کردن فرم ───
            ItemSearchBox.Text = "";
            ItemNameText.Text = "—";
            ItemNameText.Foreground = new SolidColorBrush(Color.Parse("#94A3B8"));
            QtyTextBox.Text = "";
            UnitCostTextBox.Text = "";
            TotalCostText.Text = "۰ تومان";
            SupplierTextBox.Text = "";
            CashRadio.IsChecked = true;

            // ─── بروزرسانی لیست خریدهای امروز ───
            LoadTodayPurchases();
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    /// <summary>بارگذاری خریدهای امروز</summary>
    private void LoadTodayPurchases()
    {
        try
        {
            var todayShamsi = JalaliDate.TodayShamsi();

            using var db = DatabaseService.CreateContext();
            var purchases = db.Purchases
                .Where(p => p.DateShamsi == todayShamsi && p.EntryType == EntryType.Normal)
                .OrderByDescending(p => p.Id)
                .Select(p => new
                {
                    p.Id,
                    ItemName = db.Items.Where(i => i.Id == p.ItemId).Select(i => i.Name).FirstOrDefault(),
                    p.Qty,
                    p.UnitCost,
                    p.TotalCost
                })
                .ToList();

            TodayPurchasesPanel.Children.Clear();

            if (purchases.Count == 0)
            {
                TodayPurchasesPanel.Children.Add(new TextBlock
                {
                    Text = "هنوز خریدی ثبت نشده",
                    Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                    FontSize = 13,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Margin = new Avalonia.Thickness(0, 30, 0, 30)
                });
                return;
            }

            decimal grandTotal = 0;

            foreach (var p in purchases)
            {
                grandTotal += p.TotalCost;

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80,120,120") };

                var nameText = new TextBlock
                {
                    Text = p.ItemName ?? "—",
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(nameText, 0);

                var qtyText = new TextBlock
                {
                    Text = PersianNumber.ToPersian(p.Qty),
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.Parse("#475569")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(qtyText, 1);

                var costText = new TextBlock
                {
                    Text = PersianNumber.ToToman(p.UnitCost),
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.Parse("#475569")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(costText, 2);

                var totalText = new TextBlock
                {
                    Text = PersianNumber.ToToman(p.TotalCost),
                    FontSize = 13,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#059669")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(totalText, 3);

                grid.Children.Add(nameText);
                grid.Children.Add(qtyText);
                grid.Children.Add(costText);
                grid.Children.Add(totalText);

                var row = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#FFFFFF")),
                    CornerRadius = new Avalonia.CornerRadius(6),
                    Padding = new Avalonia.Thickness(12, 10),
                    BorderBrush = new SolidColorBrush(Color.Parse("#E2E8F0")),
                    BorderThickness = new Avalonia.Thickness(1),
                    Child = grid
                };

                TodayPurchasesPanel.Children.Add(row);
            }

            // ─── جمع کل ───
            var totalGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            var label = new TextBlock
            {
                Text = $"جمع کل ({PersianNumber.ToPersian(purchases.Count)} قلم)",
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#059669")),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);

            var amount = new TextBlock
            {
                Text = PersianNumber.ToToman(grandTotal),
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#059669"))
            };
            Grid.SetColumn(amount, 1);

            totalGrid.Children.Add(label);
            totalGrid.Children.Add(amount);

            TodayPurchasesPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#ECFDF5")),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(12, 10),
                Margin = new Avalonia.Thickness(0, 8, 0, 0),
                Child = totalGrid
            });
        }
        catch (Exception ex)
        {
            TodayPurchasesPanel.Children.Clear();
            TodayPurchasesPanel.Children.Add(new TextBlock
            {
                Text = $"خطا: {ex.Message}",
                Foreground = new SolidColorBrush(Color.Parse("#EF4444")),
                FontSize = 13
            });
        }
    }

    /// <summary>باز کردن پنجره سابقه خرید</summary>
    private void OnHistoryClick(object? sender, RoutedEventArgs e)
    {
        new PurchaseHistoryWindow().Show();
    }

    /// <summary>بستن پنجره</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}