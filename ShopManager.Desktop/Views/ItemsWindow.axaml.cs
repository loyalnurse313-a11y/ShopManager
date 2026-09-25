using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ShopManager.Desktop.Services;
using ShopManager.Desktop.ViewModels;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Helpers;
using ShopManager.Domain.Services;

// ═══ رفع تداخل با DocumentFormat.OpenXml ═══
using Color = Avalonia.Media.Color;
using FontFamily = Avalonia.Media.FontFamily;

namespace ShopManager.Desktop.Views;

public partial class ItemsWindow : Window
{
    private List<ItemRow> _allRows = new();
    private List<Item> _allItems = new();
    private ItemRow? _selectedRow;

    public ItemsWindow()
    {
        InitializeComponent();
        LoadItems();
    }

    private void LoadItems()
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            _allItems = db.Items.Where(i => i.IsActive).OrderBy(i => i.ItemCode).ToList();
            var purchases = db.Purchases.ToList();
            var transfers = db.Transfers.ToList();
            var sales = db.Sales.ToList();

            _allRows = _allItems.Select(i =>
            {
                var purchasePrice = PricingCalculator.GetLatestPurchasePrice(i, purchases);
                var salePrice = PricingCalculator.GetSuggestedSalePrice(i, purchases);
                var isOverridden = PricingCalculator.IsPriceOverridden(i);

                return new ItemRow
                {
                    Id = i.Id,
                    ItemCode = PersianNumber.ToPersian(i.ItemCode),
                    Name = i.Name,
                    Category = i.Category ?? "—",
                    Unit = i.Unit,
                    WarehouseStock = PersianNumber.ToPersian(
                        StockCalculator.GetWarehouseStock(i, purchases, transfers)),
                    ShopStock = PersianNumber.ToPersian(
                        StockCalculator.GetShopStock(i, transfers, sales)),
                    PurchasePrice = purchasePrice > 0
                                    ? PersianNumber.ToToman(purchasePrice)
                                    : "—",
                    MarkupPct = PersianNumber.ToPersian(decimal.Round(i.MarkupPct, 0)) + "٪",
                    SalePrice = salePrice > 0
                                ? PersianNumber.ToToman(salePrice)
                                : "—",
                    PriceType = isOverridden ? "دستی" : "خودکار"
                };
            }).ToList();

            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        var normalized = Normalize(query);

        List<ItemRow> filtered;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            filtered = _allRows;
        }
        else
        {
            filtered = _allRows
                .Where(r => Normalize(r.Name).Contains(normalized) ||
                            Normalize(r.ItemCode).Contains(normalized))
                .ToList();
        }

        ItemsList.ItemsSource = null;
        ItemsList.ItemsSource = filtered;

        if (filtered.Count == 0)
        {
            EmptyStatePanel.IsVisible = true;
            ItemsScrollViewer.IsVisible = false;
            EmptyStateTitle.Text = string.IsNullOrWhiteSpace(normalized)
                ? "هنوز کالایی ثبت نشده است"
                : "کالایی با این جستجو یافت نشد";
        }
        else
        {
            EmptyStatePanel.IsVisible = false;
            ItemsScrollViewer.IsVisible = true;
        }

        StatusText.Text = $"تعداد: {PersianNumber.ToPersian(filtered.Count)} از {PersianNumber.ToPersian(_allRows.Count)}";
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return s.Replace("ي", "ی")
                .Replace("ك", "ک")
                .Replace("ة", "ه")
                .ToLowerInvariant()
                .Trim();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    /// <summary>وقتی کاربر یه ردیف رو انتخاب کرد</summary>
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedRow = ItemsList.SelectedItem as ItemRow;
    }

    /// <summary>گرفتن کالای انتخاب‌شده</summary>
    private Item? GetSelectedItem()
    {
        if (_selectedRow == null)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = "اول یه کالا رو از لیست انتخاب کنید";
            return null;
        }

        return _allItems.FirstOrDefault(i => i.Id == _selectedRow.Id);
    }

    /// <summary>افزودن کالا</summary>
    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        var addWindow = new AddItemWindow(null);
        var result = await addWindow.ShowDialog<bool>(this);

        if (result)
        {
            await Dispatcher.UIThread.InvokeAsync(() => LoadItems());
        }
    }

    /// <summary>ویرایش کالا</summary>
    private async void OnEditClick(object? sender, RoutedEventArgs e)
    {
        var item = GetSelectedItem();
        if (item == null) return;

        var editWindow = new AddItemWindow(item);
        var result = await editWindow.ShowDialog<bool>(this);

        if (result)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
            StatusText.Text = "✓ تغییرات ذخیره شد";
            await Dispatcher.UIThread.InvokeAsync(() => LoadItems());
        }
    }

    /// <summary>حذف کالا (نرم — فقط غیرفعال)</summary>
    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        var item = GetSelectedItem();
        if (item == null) return;

        try
        {
            // ─── بررسی: کالا تراکنش داره؟ ───
            using var db = DatabaseService.CreateContext();

            var hasTransactions = db.Purchases.Any(p => p.ItemId == item.Id) ||
                                  db.Transfers.Any(t => t.ItemId == item.Id) ||
                                  db.Sales.Any(s => s.ItemId == item.Id);

            var warningText = hasTransactions
                ? $"⚠️ این کالا تراکنش داره!\n\n" +
                  $"حذف فقط باعث غیرفعال شدنش می‌شه، ولی سابقه تراکنش‌هاش می‌مونه.\n\n" +
                  $"آیا مطمئنی می‌خوای «{item.Name}» رو غیرفعال کنی؟"
                : $"آیا مطمئنی می‌خوای «{item.Name}» رو حذف کنی؟";

            var confirm = await ShowConfirmDialog("تأیید حذف", warningText);
            if (!confirm) return;

            var dbItem = db.Items.FirstOrDefault(i => i.Id == item.Id);
            if (dbItem == null) return;

            dbItem.IsActive = false;
            db.SaveChanges();

            StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
            StatusText.Text = $"✓ کالا «{item.Name}» غیرفعال شد";

            await Dispatcher.UIThread.InvokeAsync(() => LoadItems());
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    /// <summary>خروجی اکسل لیست کالاها</summary>
    private async void OnExportExcelClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var items = db.Items.Where(i => i.IsActive).ToList();
            var purchases = db.Purchases.ToList();
            var transfers = db.Transfers.ToList();
            var sales = db.Sales.ToList();

            if (items.Count == 0)
            {
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
                StatusText.Text = "کالایی برای خروجی وجود ندارد";
                return;
            }

            var rows = items.Select(i =>
            {
                var avgCost = LockedCostCalculator.CalculateCurrentAverageCost(i, purchases);
                var salePrice = PricingCalculator.GetSuggestedSalePrice(i, purchases);

                return (
                    i,
                    StockCalculator.GetWarehouseStock(i, purchases, transfers),
                    StockCalculator.GetShopStock(i, transfers, sales),
                    avgCost,
                    salePrice
                );
            }).ToList();

            await ExcelExportHelper.ExportItemsAsync(this, rows);

            StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
            StatusText.Text = "✓ فایل اکسل با موفقیت ذخیره شد";
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        LoadItems();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>پنجره تأیید</summary>
    private async System.Threading.Tasks.Task<bool> ShowConfirmDialog(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 450,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Vazirmatn,IRANSans,Segoe UI"),
            Background = new SolidColorBrush(Color.Parse("#F1F5F9"))
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16
        };

        panel.Children.Add(new TextBlock
        {
            Text = "⚠️ " + title,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#DC2626"))
        });

        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#334155")),
            TextWrapping = TextWrapping.Wrap
        });

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };

        var yesBtn = new Button
        {
            Content = "بله، حذف کن",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Background = new SolidColorBrush(Color.Parse("#DC2626")),
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
            Padding = new Thickness(22, 10)
        };
        yesBtn.Click += (s, e) => dialog.Close(true);

        var noBtn = new Button
        {
            Content = "انصراف",
            FontSize = 14,
            Background = new SolidColorBrush(Color.Parse("#F1F5F9")),
            Foreground = new SolidColorBrush(Color.Parse("#475569")),
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22, 10)
        };
        noBtn.Click += (s, e) => dialog.Close(false);

        btnPanel.Children.Add(yesBtn);
        btnPanel.Children.Add(noBtn);
        panel.Children.Add(btnPanel);
        dialog.Content = panel;

        return await dialog.ShowDialog<bool>(this);
    }
}