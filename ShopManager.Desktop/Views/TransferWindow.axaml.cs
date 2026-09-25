using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ShopManager.Desktop.Services;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;
using ShopManager.Domain.Services;

namespace ShopManager.Desktop.Views;

public partial class TransferWindow : Window
{
    private List<Item> _allItems = new();
    private Item? _selectedItem = null;

    public TransferWindow()
    {
        InitializeComponent();

        DateTextBox.Text = JalaliDate.TodayShamsi();

        LoadItemsForSearch();
        LoadTodayTransfers();
    }

    private void LoadItemsForSearch()
    {
        using var db = DatabaseService.CreateContext();
        _allItems = db.Items.Where(i => i.IsActive).OrderBy(i => i.ItemCode).ToList();

        var displayList = _allItems
            .Select(i => $"{i.ItemCode} | {i.Name}")
            .ToList();

        ItemSearchBox.ItemsSource = displayList;
    }

    /// <summary>وقتی کاربر یه کالا انتخاب کرد، نام و موجودی رو نشون بده</summary>
    private void OnItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        var text = ItemSearchBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            _selectedItem = null;
            ItemNameText.Text = "—";
            ItemNameText.Foreground = new SolidColorBrush(Color.Parse("#94A3B8"));
            WarehouseStockText.Text = "—";
            WarehouseAfterText.Text = "—";
            ShopAfterText.Text = "—";
            return;
        }

        var codePart = text.Split('|')[0].Trim();
        if (int.TryParse(codePart, out int code))
        {
            _selectedItem = _allItems.FirstOrDefault(i => i.ItemCode == code);
            if (_selectedItem != null)
            {
                ItemNameText.Text = _selectedItem.Name;
                ItemNameText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));

                UpdateStockPreview();
            }
        }
    }

    /// <summary>محاسبه و نمایش موجودی انبار و مغازه</summary>
    private void UpdateStockPreview()
    {
        if (_selectedItem == null)
        {
            WarehouseStockText.Text = "—";
            WarehouseAfterText.Text = "—";
            ShopAfterText.Text = "—";
            return;
        }

        try
        {
            using var db = DatabaseService.CreateContext();
            var purchases = db.Purchases.ToList();
            var transfers = db.Transfers.ToList();
            var sales = db.Sales.ToList();

            var warehouseStock = StockCalculator.GetWarehouseStock(_selectedItem, purchases, transfers);
            var shopStock = StockCalculator.GetShopStock(_selectedItem, transfers, sales);

            WarehouseStockText.Text = PersianNumber.ToPersian(warehouseStock);

            // محاسبه بعد از انتقال
            var qtyText = PersianNumber.ToEnglishDigits(QtyTextBox.Text ?? "").Trim();
            if (decimal.TryParse(qtyText, out var qty) && qty > 0)
            {
                var afterWarehouse = warehouseStock - qty;
                var afterShop = shopStock + qty;

                WarehouseAfterText.Text = PersianNumber.ToPersian(afterWarehouse);
                ShopAfterText.Text = PersianNumber.ToPersian(afterShop);

                // هشدار اگه بیشتر از موجودی
                if (afterWarehouse < 0)
                {
                    WarehouseAfterText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
                }
                else
                {
                    WarehouseAfterText.Foreground = new SolidColorBrush(Color.Parse("#059669"));
                }
            }
            else
            {
                WarehouseAfterText.Text = "—";
                ShopAfterText.Text = "—";
            }
        }
        catch
        {
            WarehouseStockText.Text = "—";
            WarehouseAfterText.Text = "—";
            ShopAfterText.Text = "—";
        }
    }

    /// <summary>گرفتن کد کالا از AutoCompleteBox</summary>
    private int? GetSelectedItemCode()
    {
        var text = ItemSearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;

        text = PersianNumber.ToEnglishDigits(text);

        if (int.TryParse(text, out int directCode))
            return directCode;

        if (text.Contains('|'))
        {
            var codePart = text.Split('|')[0].Trim();
            if (int.TryParse(codePart, out int parsedCode))
                return parsedCode;
        }

        return null;
    }

    private void OnQtyChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateStockPreview();
    }

    /// <summary>ذخیره انتقال جدید</summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "";

            var dateText = PersianNumber.ToEnglishDigits(DateTextBox.Text ?? "").Trim();
            var qtyText = PersianNumber.ToEnglishDigits(QtyTextBox.Text ?? "").Trim();
            var note = NoteTextBox.Text?.Trim();

            var itemCode = GetSelectedItemCode();

            // اعتبارسنجی
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

            using var db = DatabaseService.CreateContext();
            var item = db.Items.FirstOrDefault(i => i.ItemCode == itemCode.Value && i.IsActive);
            if (item == null)
            {
                StatusText.Text = $"کالایی با کد {itemCode} پیدا نشد";
                return;
            }

            // چک موجودی انبار
            var purchases = db.Purchases.ToList();
            var transfers = db.Transfers.ToList();
            var warehouseStock = StockCalculator.GetWarehouseStock(item, purchases, transfers);

            if (warehouseStock < qty)
            {
                StatusText.Text = $"موجودی انبار کافی نیست. موجودی فعلی: {PersianNumber.ToPersian(warehouseStock)}";
                return;
            }

            // تاریخ میلادی
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

            // ذخیره
            var transfer = new Transfer
            {
                ItemId = item.Id,
                DateShamsi = dateText,
                DateGregorian = gregorianDate,
                Qty = qty,
                Note = string.IsNullOrWhiteSpace(note) ? null : note,
                EntryType = EntryType.Normal,
                CreatedAt = DateTime.UtcNow
            };

            db.Transfers.Add(transfer);
            db.SaveChanges();

            // پیام موفقیت
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
            StatusText.Text = $"✓ انتقال ثبت شد: {item.Name} — {PersianNumber.ToPersian(qty)} {item.Unit}";

            // پاک کردن فرم
            ItemSearchBox.Text = "";
            _selectedItem = null;
            ItemNameText.Text = "—";
            ItemNameText.Foreground = new SolidColorBrush(Color.Parse("#94A3B8"));
            QtyTextBox.Text = "";
            NoteTextBox.Text = "";
            WarehouseStockText.Text = "—";
            WarehouseAfterText.Text = "—";
            ShopAfterText.Text = "—";

            LoadTodayTransfers();
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    /// <summary>بارگذاری انتقال‌های امروز</summary>
    private void LoadTodayTransfers()
    {
        try
        {
            var todayShamsi = JalaliDate.TodayShamsi();

            using var db = DatabaseService.CreateContext();
            var transfers = db.Transfers
                .Where(t => t.DateShamsi == todayShamsi && t.EntryType == EntryType.Normal)
                .OrderByDescending(t => t.Id)
                .Select(t => new
                {
                    t.Id,
                    ItemName = db.Items.Where(i => i.Id == t.ItemId).Select(i => i.Name).FirstOrDefault(),
                    ItemUnit = db.Items.Where(i => i.Id == t.ItemId).Select(i => i.Unit).FirstOrDefault(),
                    t.Qty,
                    t.Note
                })
                .ToList();

            TodayTransfersPanel.Children.Clear();

            if (transfers.Count == 0)
            {
                TodayTransfersPanel.Children.Add(new TextBlock
                {
                    Text = "هنوز انتقالی ثبت نشده",
                    Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                    FontSize = 13,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Margin = new Avalonia.Thickness(0, 30, 0, 30)
                });
                return;
            }

            decimal grandTotal = 0;

            foreach (var t in transfers)
            {
                grandTotal += t.Qty;

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,100,100") };

                var nameText = new TextBlock
                {
                    Text = t.ItemName ?? "—",
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(nameText, 0);

                var qtyText = new TextBlock
                {
                    Text = PersianNumber.ToPersian(t.Qty),
                    FontSize = 13,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#4F46E5")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(qtyText, 1);

                var unitText = new TextBlock
                {
                    Text = t.ItemUnit ?? "—",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#475569")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(unitText, 2);

                grid.Children.Add(nameText);
                grid.Children.Add(qtyText);
                grid.Children.Add(unitText);

                var row = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#FFFFFF")),
                    CornerRadius = new Avalonia.CornerRadius(6),
                    Padding = new Avalonia.Thickness(12, 10),
                    BorderBrush = new SolidColorBrush(Color.Parse("#E2E8F0")),
                    BorderThickness = new Avalonia.Thickness(1),
                    Child = grid
                };

                TodayTransfersPanel.Children.Add(row);
            }

            // جمع کل
            var totalGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            var label = new TextBlock
            {
                Text = $"تعداد انتقال‌ها ({PersianNumber.ToPersian(transfers.Count)} قلم)",
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#4F46E5")),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);

            var amount = new TextBlock
            {
                Text = PersianNumber.ToPersian(grandTotal),
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#4F46E5"))
            };
            Grid.SetColumn(amount, 1);

            totalGrid.Children.Add(label);
            totalGrid.Children.Add(amount);

            TodayTransfersPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#EEF2FF")),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(12, 10),
                Margin = new Avalonia.Thickness(0, 8, 0, 0),
                Child = totalGrid
            });
        }
        catch (Exception ex)
        {
            TodayTransfersPanel.Children.Clear();
            TodayTransfersPanel.Children.Add(new TextBlock
            {
                Text = $"خطا: {ex.Message}",
                Foreground = new SolidColorBrush(Color.Parse("#EF4444")),
                FontSize = 13
            });
        }
    }

    /// <summary>باز کردن پنجره سابقه انتقال</summary>
    private void OnHistoryClick(object? sender, RoutedEventArgs e)
    {
        new TransferHistoryWindow().Show();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}