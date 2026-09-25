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

namespace ShopManager.Desktop.Views;

public partial class SaleWindow : Window
{
    private List<Item> _allItems = new();
    private Item? _selectedItem = null;
    private List<SaleCartItem> _cart = new();
    private string _currentInvoiceNumber = "";

    public SaleWindow()
    {
        InitializeComponent();

        DateTextBox.Text = JalaliDate.TodayShamsi();

        GenerateInvoiceNumber();
        LoadItemsForSearch();
        RefreshCart();
    }

    /// <summary>ساخت شماره فاکتور جدید (مثل INV-1405-0001)</summary>
    private void GenerateInvoiceNumber()
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var todayShamsi = JalaliDate.TodayShamsi();
            var yearPart = todayShamsi.Split('/')[0]; // مثلاً 1405

            // پیدا کردن آخرین شماره فاکتور سال جاری
            var lastInvoice = db.Sales
                .Where(s => s.InvoiceNumber != null && s.InvoiceNumber.StartsWith($"INV-{yearPart}-"))
                .OrderByDescending(s => s.Id)
                .Select(s => s.InvoiceNumber)
                .FirstOrDefault();

            int nextNumber = 1;
            if (!string.IsNullOrWhiteSpace(lastInvoice))
            {
                var parts = lastInvoice.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out int lastNum))
                {
                    nextNumber = lastNum + 1;
                }
            }

            _currentInvoiceNumber = $"INV-{yearPart}-{nextNumber:D4}";
            InvoiceNumberText.Text = PersianNumber.ToPersianDigits(_currentInvoiceNumber);
        }
        catch
        {
            _currentInvoiceNumber = $"INV-{JalaliDate.TodayShamsi().Replace("/", "")}-001";
            InvoiceNumberText.Text = _currentInvoiceNumber;
        }
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

    /// <summary>وقتی شماره تماس عوض شد، مشتری موجود رو نشون بده</summary>
    private void OnPhoneChanged(object? sender, TextChangedEventArgs e)
    {
        var phone = PersianNumber.ToEnglishDigits(CustomerPhoneTextBox.Text ?? "").Trim();

        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 5)
        {
            CustomerInfoBorder.IsVisible = false;
            return;
        }

        try
        {
            using var db = DatabaseService.CreateContext();
            var customer = db.Customers.FirstOrDefault(c => c.Phone == phone);

            if (customer != null)
            {
                CustomerInfoBorder.IsVisible = true;
                CustomerInfoText.Text = $"✓ مشتری موجود: {customer.Name} — " +
                                        $"{PersianNumber.ToPersian(customer.PurchaseCount)} خرید قبلی — " +
                                        $"جمع: {PersianNumber.ToToman(customer.TotalPurchasedAmount)}";

                // اگه اسم خالی باشه، خودکار پر کن
                if (string.IsNullOrWhiteSpace(CustomerNameTextBox.Text))
                {
                    CustomerNameTextBox.Text = customer.Name;
                }
            }
            else
            {
                CustomerInfoBorder.IsVisible = true;
                CustomerInfoText.Text = "مشتری جدید — با ثبت فروش، اطلاعات ذخیره می‌شه";
                CustomerInfoText.Foreground = new SolidColorBrush(Color.Parse("#0284C7"));
            }
        }
        catch
        {
            CustomerInfoBorder.IsVisible = false;
        }
    }

    /// <summary>وقتی کاربر یه کالا انتخاب کرد</summary>
    private void OnItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        var text = ItemSearchBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            _selectedItem = null;
            ItemNameText.Text = "—";
            ItemNameText.Foreground = new SolidColorBrush(Color.Parse("#94A3B8"));
            ShopStockText.Text = "—";
            SaleUnitPriceTextBox.Text = "";
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

                using var db = DatabaseService.CreateContext();
                var transfers = db.Transfers.ToList();
                var sales = db.Sales.ToList();
                var shopStock = StockCalculator.GetShopStock(_selectedItem, transfers, sales);
                ShopStockText.Text = PersianNumber.ToPersian(shopStock);

                var purchases = db.Purchases.ToList();
                var suggestedPrice = PricingCalculator.GetSuggestedSalePrice(_selectedItem, purchases);
                SaleUnitPriceTextBox.Text = suggestedPrice > 0
                    ? suggestedPrice.ToString("0")
                    : "";
            }
        }
    }

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

    /// <summary>افزودن قلم به سبد</summary>
    private void OnAddToCartClick(object? sender, RoutedEventArgs e)
    {
        StatusText.Text = "";

        if (_selectedItem == null)
        {
            StatusText.Text = "کالا را انتخاب کنید";
            return;
        }

        var qtyText = PersianNumber.ToEnglishDigits(QtyTextBox.Text ?? "").Trim();
        var priceText = PersianNumber.ToEnglishDigits(SaleUnitPriceTextBox.Text ?? "").Trim();

        if (!decimal.TryParse(qtyText, out var qty) || qty <= 0)
        {
            StatusText.Text = "تعداد باید عدد مثبت باشد";
            return;
        }
        if (!decimal.TryParse(priceText, out var unitPrice) || unitPrice < 0)
        {
            StatusText.Text = "قیمت فروش را وارد کنید";
            return;
        }

        using (var db = DatabaseService.CreateContext())
        {
            var transfers = db.Transfers.ToList();
            var sales = db.Sales.ToList();
            var shopStock = StockCalculator.GetShopStock(_selectedItem, transfers, sales);

            var alreadyInCart = _cart.Where(c => c.ItemId == _selectedItem.Id).Sum(c => c.Qty);
            var availableStock = shopStock - alreadyInCart;

            if (availableStock < qty)
            {
                StatusText.Text = $"موجودی مغازه کافی نیست. موجودی: {PersianNumber.ToPersian(availableStock)}";
                return;
            }
        }

        var existing = _cart.FirstOrDefault(c => c.ItemId == _selectedItem.Id);
        if (existing != null)
        {
            existing.Qty += qty;
            existing.SaleUnitPrice = unitPrice;
        }
        else
        {
            _cart.Add(new SaleCartItem
            {
                ItemId = _selectedItem.Id,
                ItemCode = _selectedItem.ItemCode,
                ItemName = _selectedItem.Name,
                Unit = _selectedItem.Unit,
                Qty = qty,
                SaleUnitPrice = unitPrice
            });
        }

        ItemSearchBox.Text = "";
        _selectedItem = null;
        ItemNameText.Text = "—";
        ItemNameText.Foreground = new SolidColorBrush(Color.Parse("#94A3B8"));
        ShopStockText.Text = "—";
        QtyTextBox.Text = "";
        SaleUnitPriceTextBox.Text = "";

        RefreshCart();

        StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
        StatusText.Text = "✓ قلم به سبد اضافه شد";
    }

    /// <summary>نمایش سبد</summary>
    private void RefreshCart()
    {
        CartPanel.Children.Clear();

        if (_cart.Count == 0)
        {
            CartPanel.Children.Add(new TextBlock
            {
                Text = "سبد خالی است",
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 40)
            });

            TotalRevenueText.Text = "۰ تومان";
            ItemCountText.Text = "۰";
            return;
        }

        foreach (var cartItem in _cart)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,80,80,110,120,50")
            };

            var nameText = new TextBlock
            {
                Text = cartItem.ItemName,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameText, 0);

            var qtyText = new TextBlock
            {
                Text = PersianNumber.ToPersian(cartItem.Qty),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#4F46E5")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(qtyText, 1);

            var unitText = new TextBlock
            {
                Text = cartItem.Unit,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#475569")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(unitText, 2);

            var priceText = new TextBlock
            {
                Text = PersianNumber.ToToman(cartItem.SaleUnitPrice),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.Parse("#475569")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(priceText, 3);

            var revenueText = new TextBlock
            {
                Text = PersianNumber.ToToman(cartItem.Revenue),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#059669")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(revenueText, 4);

            var deleteBtn = new Button
            {
                Content = "✕",
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Background = new SolidColorBrush(Color.Parse("#FEF2F2")),
                Foreground = new SolidColorBrush(Color.Parse("#EF4444")),
                Padding = new Thickness(8, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(deleteBtn, 5);

            var capturedItem = cartItem;
            deleteBtn.Click += (s, e) =>
            {
                _cart.Remove(capturedItem);
                RefreshCart();
            };

            grid.Children.Add(nameText);
            grid.Children.Add(qtyText);
            grid.Children.Add(unitText);
            grid.Children.Add(priceText);
            grid.Children.Add(revenueText);
            grid.Children.Add(deleteBtn);

            var row = new Border
            {
                Padding = new Thickness(12, 10),
                BorderBrush = new SolidColorBrush(Color.Parse("#F1F5F9")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid
            };

            CartPanel.Children.Add(row);
        }

        var totalRevenue = _cart.Sum(c => c.Revenue);
        TotalRevenueText.Text = PersianNumber.ToToman(totalRevenue);
        ItemCountText.Text = PersianNumber.ToPersian(_cart.Count);
    }

    private void OnClearCartClick(object? sender, RoutedEventArgs e)
    {
        _cart.Clear();
        RefreshCart();
        StatusText.Foreground = new SolidColorBrush(Color.Parse("#4F46E5"));
        StatusText.Text = "سبد پاک شد";
    }

    /// <summary>ثبت نهایی فروش</summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "";

            if (_cart.Count == 0)
            {
                StatusText.Text = "سبد خالی است";
                return;
            }

            var dateText = PersianNumber.ToEnglishDigits(DateTextBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(dateText))
            {
                StatusText.Text = "تاریخ را وارد کنید";
                return;
            }

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

            var paymentStatus = CashRadio.IsChecked == true
                                ? PaymentStatus.Cash
                                : PaymentStatus.Credit;

            var customerName = CustomerNameTextBox.Text?.Trim() ?? "";
            var customerPhone = PersianNumber.ToEnglishDigits(CustomerPhoneTextBox.Text ?? "").Trim();

            using var db = DatabaseService.CreateContext();

            var allPurchases = db.Purchases.ToList();
            var transfers = db.Transfers.ToList();
            var sales = db.Sales.ToList();

            // ─── اعتبارسنجی نهایی ───
            foreach (var cartItem in _cart)
            {
                var item = db.Items.FirstOrDefault(i => i.Id == cartItem.ItemId);
                if (item == null)
                {
                    StatusText.Text = $"کالا با کد {cartItem.ItemCode} پیدا نشد";
                    return;
                }

                var shopStock = StockCalculator.GetShopStock(item, transfers, sales);

                var cartQtyBefore = _cart
                    .TakeWhile(c => c != cartItem)
                    .Where(c => c.ItemId == cartItem.ItemId)
                    .Sum(c => c.Qty);

                var availableStock = shopStock - cartQtyBefore;

                if (availableStock < cartItem.Qty)
                {
                    StatusText.Text = $"موجودی {item.Name} کافی نیست. موجودی: {PersianNumber.ToPersian(availableStock)}";
                    return;
                }
            }

            // ─── مدیریت مشتری ───
            int? customerId = null;
            if (!string.IsNullOrWhiteSpace(customerPhone))
            {
                var existingCustomer = db.Customers.FirstOrDefault(c => c.Phone == customerPhone);

                decimal totalAmount = _cart.Sum(c => c.Revenue);

                if (existingCustomer != null)
                {
                    // آپدیت مشتری موجود
                    if (!string.IsNullOrWhiteSpace(customerName))
                    {
                        existingCustomer.Name = customerName;
                    }
                    existingCustomer.LastPurchaseAt = DateTime.UtcNow;
                    existingCustomer.TotalPurchasedAmount += totalAmount;
                    existingCustomer.PurchaseCount += 1;

                    db.Customers.Update(existingCustomer);
                    customerId = existingCustomer.Id;
                }
                else if (!string.IsNullOrWhiteSpace(customerName))
                {
                    // مشتری جدید
                    var newCustomer = new Customer
                    {
                        Name = customerName,
                        Phone = customerPhone,
                        FirstPurchaseAt = DateTime.UtcNow,
                        LastPurchaseAt = DateTime.UtcNow,
                        TotalPurchasedAmount = totalAmount,
                        PurchaseCount = 1
                    };
                    db.Customers.Add(newCustomer);
                    db.SaveChanges(); // برای گرفتن Id
                    customerId = newCustomer.Id;
                }
            }

            // ─── ثبت همه اقلام ───
            decimal totalRevenue = 0;
            decimal totalProfit = 0;

            foreach (var cartItem in _cart)
            {
                var item = db.Items.First(i => i.Id == cartItem.ItemId);

                var lockedCost = LockedCostCalculator.CalculateLockedCost(
                    item,
                    gregorianDate,
                    allPurchases);

                var revenue = cartItem.Qty * cartItem.SaleUnitPrice;
                var cost = cartItem.Qty * lockedCost;
                var profit = revenue - cost;

                var sale = new Sale
                {
                    ItemId = cartItem.ItemId,
                    InvoiceNumber = _currentInvoiceNumber,
                    CustomerId = customerId,
                    DateShamsi = dateText,
                    DateGregorian = gregorianDate,
                    Qty = cartItem.Qty,
                    SaleUnitPrice = cartItem.SaleUnitPrice,
                    LockedUnitCost = lockedCost,
                    Revenue = revenue,
                    Cost = cost,
                    Profit = profit,
                    PaymentStatus = paymentStatus,
                    EntryType = EntryType.Normal,
                    CreatedAt = DateTime.UtcNow
                };

                db.Sales.Add(sale);

                totalRevenue += revenue;
                totalProfit += profit;
            }

            db.SaveChanges();

            StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
            StatusText.Text = $"✓ فروش ثبت شد — فاکتور {PersianNumber.ToPersianDigits(_currentInvoiceNumber)} — " +
                              $"درآمد: {PersianNumber.ToToman(totalRevenue)} — سود: {PersianNumber.ToToman(totalProfit)}";

            // ─── ذخیره شماره فاکتور قبل از ریست ───
            var savedInvoiceNumber = _currentInvoiceNumber;

            // ─── پاک کردن سبد و تولید شماره فاکتور جدید ───
            _cart.Clear();
            CustomerNameTextBox.Text = "";
            CustomerPhoneTextBox.Text = "";
            CustomerInfoBorder.IsVisible = false;
            CashRadio.IsChecked = true;
            RefreshCart();

            GenerateInvoiceNumber();

            // ─── باز کردن فاکتور برای چاپ ───
            var invoiceWindow = new SaleInvoiceWindow();
            invoiceWindow.LoadInvoice(savedInvoiceNumber);
            invoiceWindow.Show();
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    private void OnHistoryClick(object? sender, RoutedEventArgs e)
    {
        new SaleHistoryWindow().Show();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}