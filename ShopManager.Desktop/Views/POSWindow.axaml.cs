using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ShopManager.Desktop.Model;
using ShopManager.Desktop.Services;
using ShopManager.Desktop.ViewModels;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;
using ShopManager.Domain.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Border = Avalonia.Controls.Border;
using Color = Avalonia.Media.Color;
using FontFamily = Avalonia.Media.FontFamily;

namespace ShopManager.Desktop.Views;

public partial class POSWindow : Window
{
    private List<POSCartItem> _cart = new();
    private List<Item> _allItems = new();
    private string _currentInvoiceNumber = "";
    private decimal _discountAmount = 0;
    private string _activeTab = "popular";

    private int? _selectedCustomerId = null;
    private string _activePOSTerminal = "";

    private BarcodeScannerService? _scanner;

    private DispatcherTimer? _searchDebounce;
    private DispatcherTimer? _clockTimer;
    private string _pendingSearch = "";

    public POSWindow()
    {
        InitializeComponent();

        GenerateInvoiceNumber();
        LoadAllItems();
        LoadPOSTerminals();
        LoadProductTiles("popular");
        SetupBarcodeScanner();
        StartClock();

        _searchDebounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _searchDebounce.Tick += OnSearchDebounceTick;

        Loaded += (s, e) => BarcodeSearchBox.Focus();

        RefreshCart();
    }

    private void StartClock()
    {
        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _clockTimer.Tick += (s, e) => UpdateClock();
        UpdateClock();
        _clockTimer.Start();
    }

    private void UpdateClock()
    {
        try
        {
            var now = DateTime.Now;
            var shamsi = JalaliDate.ToShamsi(now);
            var time = now.ToString("HH:mm");
            ClockText.Text = $"{PersianNumber.ToPersianDigits(shamsi)} — {PersianNumber.ToPersianDigits(time)}";
        }
        catch { }
    }

    // ═══════════════════════════════════════════
    // پایانه POS
    // ═══════════════════════════════════════════

    private void LoadPOSTerminals()
    {
        try
        {
            var settings = StoreSettingsService.Current;
            var terminals = settings.POSTerminals ?? new List<string>();

            var items = new List<string>();

            if (terminals.Count == 0)
            {
                items.Add("— تعریف نشده —");
            }
            else
            {
                items.Add("— بدون پایانه —");
                items.AddRange(terminals);
            }

            POSTerminalCombo.ItemsSource = items;

            var last = PreferencesService.Current.LastPOSTerminal;
            if (!string.IsNullOrWhiteSpace(last) && items.Contains(last))
            {
                POSTerminalCombo.SelectedItem = last;
            }
            else
            {
                POSTerminalCombo.SelectedIndex = 0;
            }

            UpdateActiveTerminal();
            POSTerminalCombo.SelectionChanged += (s, e) => UpdateActiveTerminal();
        }
        catch { }
    }

    private void UpdateActiveTerminal()
    {
        try
        {
            var selected = POSTerminalCombo.SelectedItem?.ToString() ?? "";

            if (selected == "— تعریف نشده —" || selected == "— بدون پایانه —")
            {
                _activePOSTerminal = "";
            }
            else
            {
                _activePOSTerminal = selected;
            }

            if (!string.IsNullOrWhiteSpace(_activePOSTerminal))
            {
                var prefs = PreferencesService.Current;
                prefs.LastPOSTerminal = _activePOSTerminal;
                PreferencesService.Save(prefs);
            }
        }
        catch { }
    }

    // ═══════════════════════════════════════════
    // بارکدخوان
    // ═══════════════════════════════════════════

    private void SetupBarcodeScanner()
    {
        _scanner = new BarcodeScannerService(80);
        _scanner.BarcodeScanned += OnBarcodeScanned;
        _scanner.StartListening();
    }

    private void OnBarcodeScanned(object? sender, string barcode)
    {
        Dispatcher.UIThread.InvokeAsync(() => ProcessBarcode(barcode));
    }

    private void ProcessBarcode(string barcode)
    {
        try
        {
            if (int.TryParse(barcode, out int itemCode))
            {
                var item = _allItems.FirstOrDefault(i => i.ItemCode == itemCode);
                if (item != null)
                {
                    AddItemToCart(item, 1);
                    BarcodeSearchBox.Text = "";

                    StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
                    StatusText.Text = $"✓ {item.Name} اضافه شد";
                    return;
                }
            }

            BarcodeSearchBox.Text = barcode;
            BarcodeSearchBox.CaretIndex = barcode.Length;

            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = $"کالایی با بارکد «{barcode}» پیدا نشد";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F2:
                if (_cart.Count > 0) OnPayClick(null, new RoutedEventArgs());
                e.Handled = true;
                return;

            case Key.F4:
                BarcodeSearchBox.Focus();
                BarcodeSearchBox.SelectAll();
                e.Handled = true;
                return;

            case Key.Escape:
                if (SearchResultsBorder.IsVisible)
                {
                    SearchResultsBorder.IsVisible = false;
                    BarcodeSearchBox.Text = "";
                }
                else if (_cart.Count > 0)
                {
                    ClearCart();
                }
                e.Handled = true;
                return;
        }
    }

    // ═══════════════════════════════════════════
    // مشتری
    // ═══════════════════════════════════════════

    private void OnCustomerPhoneChanged(object? sender, TextChangedEventArgs e)
    {
        var phone = PersianNumber.ToEnglishDigits(CustomerPhoneBox.Text ?? "").Trim();

        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 5)
        {
            _selectedCustomerId = null;
            return;
        }

        try
        {
            using var db = DatabaseService.CreateContext();
            var customer = db.Customers.FirstOrDefault(c => c.Phone == phone);

            if (customer != null)
            {
                _selectedCustomerId = customer.Id;

                if (string.IsNullOrWhiteSpace(CustomerNameBox.Text))
                {
                    CustomerNameBox.Text = customer.Name;
                }

                StatusText.Foreground = new SolidColorBrush(Color.Parse("#059669"));
                StatusText.Text = $"✓ مشتری موجود: {customer.Name} — " +
                                  $"{PersianNumber.ToPersian(customer.PurchaseCount)} خرید قبلی — " +
                                  $"جمع: {PersianNumber.ToToman(customer.TotalPurchasedAmount)}";
            }
            else
            {
                _selectedCustomerId = null;

                StatusText.Foreground = new SolidColorBrush(Color.Parse("#0284C7"));
                StatusText.Text = "مشتری جدید — با ثبت فروش، اطلاعات ذخیره می‌شه";
            }
        }
        catch
        {
            _selectedCustomerId = null;
        }
    }

    // ═══════════════════════════════════════════
    // جستجو
    // ═══════════════════════════════════════════

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = BarcodeSearchBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text)) return;

            var results = SearchItems(text);
            if (results.Count > 0)
            {
                AddItemToCart(results[0], 1);
                BarcodeSearchBox.Text = "";
                SearchResultsBorder.IsVisible = false;

                StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
                StatusText.Text = $"✓ {results[0].Name} اضافه شد";
            }
            else
            {
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
                StatusText.Text = "کالایی پیدا نشد";
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            BarcodeSearchBox.Text = "";
            SearchResultsBorder.IsVisible = false;
            e.Handled = true;
        }
    }

    private void OnSearchBoxChanged(object? sender, TextChangedEventArgs e)
    {
        _pendingSearch = BarcodeSearchBox.Text?.Trim() ?? "";

        _searchDebounce?.Stop();

        if (string.IsNullOrWhiteSpace(_pendingSearch))
        {
            SearchResultsBorder.IsVisible = false;
            return;
        }

        _searchDebounce?.Start();
    }

    private void OnSearchDebounceTick(object? sender, EventArgs e)
    {
        _searchDebounce?.Stop();
        PerformSearch(_pendingSearch);
    }

    private void PerformSearch(string query)
    {
        var results = SearchItems(query);

        SearchResultsPanel.Children.Clear();

        if (results.Count == 0)
        {
            SearchResultsBorder.IsVisible = true;
            SearchResultsPanel.Children.Add(new TextBlock
            {
                Text = "کالایی پیدا نشد",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20)
            });
            return;
        }

        SearchResultsBorder.IsVisible = true;

        foreach (var item in results.Take(10))
        {
            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(14, 10),
                Background = new SolidColorBrush(Color.Parse("#FFFFFF")),
                BorderBrush = new SolidColorBrush(Color.Parse("#F1F5F9")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(0)
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*,110,140") };

            var codeText = new TextBlock
            {
                Text = PersianNumber.ToPersian(item.ItemCode),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#4F46E5")),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(codeText, 0);

            var nameText = new TextBlock
            {
                Text = item.Name,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameText, 1);

            var unitText = new TextBlock
            {
                Text = item.Unit,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#64748B")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(unitText, 2);

            decimal stock = 0;
            try
            {
                using var db = DatabaseService.CreateContext();
                var transfers = db.Transfers.ToList();
                var sales = db.Sales.ToList();
                stock = StockCalculator.GetShopStock(item, transfers, sales);
            }
            catch { }

            var stockText = new TextBlock
            {
                Text = $"موجودی: {PersianNumber.ToPersian(stock)}",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse(stock > 0 ? "#059669" : "#EF4444")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(stockText, 3);

            grid.Children.Add(codeText);
            grid.Children.Add(nameText);
            grid.Children.Add(unitText);
            grid.Children.Add(stockText);

            btn.Content = grid;

            var capturedItem = item;
            btn.Click += (s, args) =>
            {
                AddItemToCart(capturedItem, 1);
                BarcodeSearchBox.Text = "";
                SearchResultsBorder.IsVisible = false;
                BarcodeSearchBox.Focus();
            };

            SearchResultsPanel.Children.Add(btn);
        }
    }

    private List<Item> SearchItems(string query)
    {
        query = PersianNumber.ToEnglishDigits(query).Trim().ToLower();

        return _allItems
            .Where(i =>
                i.Name.ToLower().Contains(query) ||
                i.ItemCode.ToString().Contains(query) ||
                (i.Category ?? "").ToLower().Contains(query))
            .OrderBy(i => i.Name)
            .Take(10)
            .ToList();
    }

    // ═══════════════════════════════════════════
    // تب‌های کالا
    // ═══════════════════════════════════════════

    private void OnTabPopularClick(object? sender, RoutedEventArgs e) => SwitchTab("popular");
    private void OnTabRecentClick(object? sender, RoutedEventArgs e) => SwitchTab("recent");
    private void OnTabAllClick(object? sender, RoutedEventArgs e) => SwitchTab("all");

    private void SwitchTab(string tab)
    {
        _activeTab = tab;

        TabPopularBtn.Background = new SolidColorBrush(Color.Parse(tab == "popular" ? "#EEF2FF" : "#00000000"));
        TabPopularBtn.Foreground = new SolidColorBrush(Color.Parse(tab == "popular" ? "#4F46E5" : "#94A3B8"));

        TabRecentBtn.Background = new SolidColorBrush(Color.Parse(tab == "recent" ? "#EEF2FF" : "#00000000"));
        TabRecentBtn.Foreground = new SolidColorBrush(Color.Parse(tab == "recent" ? "#4F46E5" : "#94A3B8"));

        TabAllBtn.Background = new SolidColorBrush(Color.Parse(tab == "all" ? "#EEF2FF" : "#00000000"));
        TabAllBtn.Foreground = new SolidColorBrush(Color.Parse(tab == "all" ? "#4F46E5" : "#94A3B8"));

        LoadProductTiles(tab);
    }

    private void LoadProductTiles(string tab)
    {
        ProductTilesPanel.Children.Clear();

        List<Item> itemsToShow;

        using (var db = DatabaseService.CreateContext())
        {
            var allSales = db.Sales.Where(s => s.EntryType == EntryType.Normal).ToList();

            switch (tab)
            {
                case "popular":
                    var thirtyDaysAgo = DateTime.Today.AddDays(-30);
                    var popular = allSales
                        .Where(s => s.DateGregorian >= thirtyDaysAgo)
                        .GroupBy(s => s.ItemId)
                        .Select(g => new { ItemId = g.Key, Qty = g.Sum(s => s.Qty) })
                        .OrderByDescending(x => x.Qty)
                        .Take(20)
                        .Select(x => x.ItemId)
                        .ToList();

                    itemsToShow = _allItems.Where(i => popular.Contains(i.Id)).ToList();

                    if (itemsToShow.Count < 12)
                    {
                        var existingIds = itemsToShow.Select(i => i.Id).ToHashSet();
                        itemsToShow.AddRange(_allItems.Where(i => !existingIds.Contains(i.Id)).Take(12 - itemsToShow.Count));
                    }
                    break;

                case "recent":
                    var recent = allSales
                        .OrderByDescending(s => s.DateGregorian)
                        .ThenByDescending(s => s.Id)
                        .Select(s => s.ItemId)
                        .Distinct()
                        .Take(20)
                        .ToList();

                    itemsToShow = _allItems.Where(i => recent.Contains(i.Id)).ToList();

                    if (itemsToShow.Count < 12)
                    {
                        var existingIds = itemsToShow.Select(i => i.Id).ToHashSet();
                        itemsToShow.AddRange(_allItems.Where(i => !existingIds.Contains(i.Id)).Take(12 - itemsToShow.Count));
                    }
                    break;

                default:
                    itemsToShow = _allItems.Take(30).ToList();
                    break;
            }
        }

        foreach (var item in itemsToShow)
        {
            ProductTilesPanel.Children.Add(CreateProductTile(item));
        }
    }

    private Border CreateProductTile(Item item)
    {
        decimal stock = 0;
        decimal salePrice = 0;

        try
        {
            using var db = DatabaseService.CreateContext();
            var purchases = db.Purchases.ToList();
            var transfers = db.Transfers.ToList();
            var sales = db.Sales.ToList();

            stock = StockCalculator.GetShopStock(item, transfers, sales);
            salePrice = PricingCalculator.GetSuggestedSalePrice(item, purchases);
        }
        catch { }

        var btn = new Button
        {
            Width = 160,
            Height = 90,
            Margin = new Thickness(5),
            Padding = new Thickness(10, 8),
            Background = new SolidColorBrush(Color.Parse(stock > 0 ? "#FFFFFF" : "#FEF2F2")),
            BorderBrush = new SolidColorBrush(Color.Parse(stock > 0 ? "#E2E8F0" : "#FECACA")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        var panel = new StackPanel { Spacing = 4 };

        panel.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 34,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        panel.Children.Add(new TextBlock
        {
            Text = salePrice > 0 ? PersianNumber.ToToman(salePrice) : "—",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#059669"))
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"موجودی: {PersianNumber.ToPersian(stock)} {item.Unit}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse(stock > 0 ? "#64748B" : "#DC2626"))
        });

        btn.Content = panel;

        var capturedItem = item;
        btn.Click += (s, e) => AddItemToCart(capturedItem, 1);

        return new Border { Child = btn, Margin = new Thickness(0) };
    }

    // ═══════════════════════════════════════════
    // سبد
    // ═══════════════════════════════════════════

    private void AddItemToCart(Item item, decimal qty)
    {
        using (var db = DatabaseService.CreateContext())
        {
            var transfers = db.Transfers.ToList();
            var sales = db.Sales.ToList();
            var shopStock = StockCalculator.GetShopStock(item, transfers, sales);

            var alreadyInCart = _cart.Where(c => c.ItemId == item.Id).Sum(c => c.Qty);
            var available = shopStock - alreadyInCart;

            if (available < qty)
            {
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
                StatusText.Text = $"⚠️ موجودی {item.Name} کافی نیست (موجودی: {PersianNumber.ToPersian(available)})";
                return;
            }
        }

        decimal salePrice = 0;
        decimal lockedCost = 0;

        using (var db = DatabaseService.CreateContext())
        {
            var purchases = db.Purchases.ToList();
            salePrice = PricingCalculator.GetSuggestedSalePrice(item, purchases);

            if (salePrice <= 0)
            {
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
                StatusText.Text = "قیمت فروش تعریف نشده";
                return;
            }

            lockedCost = LockedCostCalculator.CalculateLockedCost(item, DateTime.Today, purchases);
        }

        var existing = _cart.FirstOrDefault(c => c.ItemId == item.Id);
        if (existing != null)
        {
            existing.Qty += qty;
        }
        else
        {
            _cart.Add(new POSCartItem
            {
                ItemId = item.Id,
                ItemCode = item.ItemCode,
                ItemName = item.Name,
                Unit = item.Unit,
                Qty = qty,
                SaleUnitPrice = salePrice,
                LockedCost = lockedCost
            });
        }

        RefreshCart();
    }

    private void RefreshCart()
    {
        CartPanel.Children.Clear();

        if (_cart.Count == 0)
        {
            EmptyCartPanel.IsVisible = true;
            CartScrollViewer.IsVisible = false;
            CartCountText.Text = "۰ قلم";
            TotalAmountText.Text = "۰ تومان";
            DiscountRow.IsVisible = false;
            return;
        }

        EmptyCartPanel.IsVisible = false;
        CartScrollViewer.IsVisible = true;

        foreach (var cartItem in _cart.ToList())
        {
            CartPanel.Children.Add(CreateCartRow(cartItem));
        }

        var totalQty = _cart.Sum(c => c.Qty);
        var totalAmount = _cart.Sum(c => c.Revenue) - _discountAmount;

        CartCountText.Text = $"{PersianNumber.ToPersian(_cart.Count)} قلم — {PersianNumber.ToPersian(totalQty)} عدد";
        TotalAmountText.Text = PersianNumber.ToToman(totalAmount);

        if (_discountAmount > 0)
        {
            DiscountRow.IsVisible = true;
            DiscountAmountText.Text = PersianNumber.ToToman(_discountAmount);
        }
        else
        {
            DiscountRow.IsVisible = false;
        }
    }

    private Border CreateCartRow(POSCartItem cartItem)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,140,80,110,120,60")
        };

        var nameText = new TextBlock
        {
            Text = cartItem.ItemName,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(nameText, 0);

        var qtyBox = new TextBox
        {
            Text = cartItem.Qty.ToString("0.##"),
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#4F46E5")),
            Background = new SolidColorBrush(Color.Parse("#F8FAFC")),
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Width = 100,
            Height = 40,
            Padding = new Thickness(8, 6),
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var capturedItem = cartItem;

        qtyBox.TextChanged += (s, e) =>
        {
            var text = PersianNumber.ToEnglishDigits(qtyBox.Text ?? "").Trim();
            text = text.Replace(" ", "").Replace(",", "").Replace("٬", "");

            if (decimal.TryParse(text, out var newQty))
            {
                if (newQty <= 0) return;

                try
                {
                    using var db = DatabaseService.CreateContext();
                    var item = db.Items.FirstOrDefault(i => i.Id == capturedItem.ItemId);
                    if (item == null) return;

                    var transfers = db.Transfers.ToList();
                    var sales = db.Sales.ToList();
                    var shopStock = StockCalculator.GetShopStock(item, transfers, sales);

                    if (newQty > shopStock)
                    {
                        StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
                        StatusText.Text = $"⚠️ موجودی {item.Name} فقط {PersianNumber.ToPersian(shopStock)} عدد است";
                        return;
                    }

                    capturedItem.Qty = newQty;
                    UpdateTotals();
                }
                catch { }
            }
        };

        qtyBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                var text = PersianNumber.ToEnglishDigits(qtyBox.Text ?? "").Trim()
                    .Replace(" ", "").Replace(",", "").Replace("٬", "");

                if (decimal.TryParse(text, out var newQty) && newQty > 0)
                {
                    capturedItem.Qty = newQty;
                    qtyBox.Text = capturedItem.Qty.ToString("0.##");
                    UpdateTotals();
                    StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
                    StatusText.Text = $"✓ تعداد به {PersianNumber.ToPersian(newQty)} تغییر کرد";
                }
                else
                {
                    qtyBox.Text = capturedItem.Qty.ToString("0.##");
                }

                BarcodeSearchBox.Focus();
                e.Handled = true;
            }
        };

        qtyBox.LostFocus += (s, e) =>
        {
            var text = PersianNumber.ToEnglishDigits(qtyBox.Text ?? "").Trim()
                .Replace(" ", "").Replace(",", "").Replace("٬", "");

            if (decimal.TryParse(text, out var newQty) && newQty > 0)
            {
                capturedItem.Qty = newQty;
            }

            qtyBox.Text = capturedItem.Qty.ToString("0.##");
            UpdateTotals();
        };

        qtyBox.GotFocus += (s, e) =>
        {
            qtyBox.SelectAll();
        };

        Grid.SetColumn(qtyBox, 1);

        var unitText = new TextBlock
        {
            Text = cartItem.Unit,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#64748B")),
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
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#059669")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(revenueText, 4);

        var deleteBtn = new Button
        {
            Content = "✕",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.Parse("#FEF2F2")),
            Foreground = new SolidColorBrush(Color.Parse("#DC2626")),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        deleteBtn.Click += (s, e) =>
        {
            _cart.Remove(capturedItem);
            RefreshCart();
        };
        Grid.SetColumn(deleteBtn, 5);

        grid.Children.Add(nameText);
        grid.Children.Add(qtyBox);
        grid.Children.Add(unitText);
        grid.Children.Add(priceText);
        grid.Children.Add(revenueText);
        grid.Children.Add(deleteBtn);

        return new Border
        {
            Padding = new Thickness(14, 10),
            BorderBrush = new SolidColorBrush(Color.Parse("#F1F5F9")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private void UpdateTotals()
    {
        var totalQty = _cart.Sum(c => c.Qty);
        var totalAmount = _cart.Sum(c => c.Revenue) - _discountAmount;

        CartCountText.Text = $"{PersianNumber.ToPersian(_cart.Count)} قلم — {PersianNumber.ToPersian(totalQty)} عدد";
        TotalAmountText.Text = PersianNumber.ToToman(totalAmount);

        if (_discountAmount > 0)
        {
            DiscountRow.IsVisible = true;
            DiscountAmountText.Text = PersianNumber.ToToman(_discountAmount);
        }
        else
        {
            DiscountRow.IsVisible = false;
        }
    }

    // ═══════════════════════════════════════════
    // عملیات
    // ═══════════════════════════════════════════

    private async void OnClearCartClick(object? sender, RoutedEventArgs e)
    {
        if (_cart.Count == 0) return;

        var confirm = await ShowConfirmDialog("پاک کردن سبد",
            $"آیا مطمئنی می‌خوای {PersianNumber.ToPersian(_cart.Count)} قلم رو پاک کنی؟");

        if (confirm) ClearCart();
    }

    private void ClearCart()
    {
        _cart.Clear();
        _discountAmount = 0;
        RefreshCart();

        StatusText.Foreground = new SolidColorBrush(Color.Parse("#4F46E5"));
        StatusText.Text = "سبد پاک شد";
    }

    private void OnEditQtyClick(object? sender, RoutedEventArgs e)
    {
        if (_cart.Count == 0) return;
        StatusText.Text = "تعداد هر کالا رو مستقیم توی کادر خودش تایپ کن";
    }

    private async void OnDiscountClick(object? sender, RoutedEventArgs e)
    {
        if (_cart.Count == 0)
        {
            StatusText.Text = "سبد خالیه";
            return;
        }

        var amount = await ShowAmountInputDialog(
            "تخفیف",
            "مبلغ تخفیف (تومان):",
            "مثال: 50000");

        if (amount > 0)
        {
            var totalBefore = _cart.Sum(c => c.Revenue);

            if (amount > totalBefore)
            {
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
                StatusText.Text = "تخفیف بیشتر از جمع کل است";
                return;
            }

            _discountAmount = amount;
            RefreshCart();

            StatusText.Foreground = new SolidColorBrush(Color.Parse("#D97706"));
            StatusText.Text = $"✓ تخفیف {PersianNumber.ToToman(amount)} اعمال شد";
        }
    }

    private void OnHistoryClick(object? sender, RoutedEventArgs e)
    {
        new SaleHistoryWindow().Show();
    }

    private void OnPrintLastClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            using var db = DatabaseService.CreateContext();
            var lastInvoice = db.Sales
                .Where(s => s.EntryType == EntryType.Normal && s.InvoiceNumber != null)
                .OrderByDescending(s => s.Id)
                .Select(s => s.InvoiceNumber)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(lastInvoice))
            {
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#D97706"));
                StatusText.Text = "فاکتوری برای چاپ وجود ندارد";
                return;
            }

            var invoiceWindow = new SaleInvoiceWindow();
            invoiceWindow.LoadInvoice(lastInvoice);
            invoiceWindow.Show();

            StatusText.Foreground = new SolidColorBrush(Color.Parse("#4F46E5"));
            StatusText.Text = $"✓ فاکتور {PersianNumber.ToPersianDigits(lastInvoice)} باز شد";
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════
    // پرداخت
    // ═══════════════════════════════════════════

    private async void OnPayClick(object? sender, RoutedEventArgs e)
    {
        if (_cart.Count == 0)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = "سبد خالی است";
            return;
        }

        foreach (var cartItem in _cart)
        {
            using var db = DatabaseService.CreateContext();
            var item = db.Items.FirstOrDefault(i => i.Id == cartItem.ItemId);
            if (item == null)
            {
                StatusText.Text = "کالا پیدا نشد";
                return;
            }

            var transfers = db.Transfers.ToList();
            var sales = db.Sales.ToList();
            var shopStock = StockCalculator.GetShopStock(item, transfers, sales);

            if (shopStock < cartItem.Qty)
            {
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
                StatusText.Text = $"⚠️ موجودی {item.Name} کافی نیست";
                return;
            }
        }

        var paymentType = await ShowPaymentDialog();

        if (paymentType == null) return;

        try
        {
            SaveSale(paymentType.Value);
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    private void SaveSale(PaymentStatus paymentStatus)
    {
        using var db = DatabaseService.CreateContext();

        var today = DateTime.Today;

        int? customerId = null;
        var customerName = CustomerNameBox.Text?.Trim() ?? "";
        var customerPhone = PersianNumber.ToEnglishDigits(CustomerPhoneBox.Text ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(customerPhone))
        {
            var totalCartRevenue = _cart.Sum(c => c.Revenue) - _discountAmount;

            var existingCustomer = db.Customers.FirstOrDefault(c => c.Phone == customerPhone);

            if (existingCustomer != null)
            {
                if (!string.IsNullOrWhiteSpace(customerName))
                {
                    existingCustomer.Name = customerName;
                }
                existingCustomer.LastPurchaseAt = DateTime.UtcNow;
                existingCustomer.TotalPurchasedAmount += totalCartRevenue;
                existingCustomer.PurchaseCount += 1;

                db.Customers.Update(existingCustomer);
                customerId = existingCustomer.Id;
            }
            else if (!string.IsNullOrWhiteSpace(customerName))
            {
                var newCustomer = new Customer
                {
                    Name = customerName,
                    Phone = customerPhone,
                    FirstPurchaseAt = DateTime.UtcNow,
                    LastPurchaseAt = DateTime.UtcNow,
                    TotalPurchasedAmount = totalCartRevenue,
                    PurchaseCount = 1
                };
                db.Customers.Add(newCustomer);
                db.SaveChanges();
                customerId = newCustomer.Id;
            }
        }

        decimal totalRevenue = 0;
        decimal totalProfit = 0;
        decimal totalCartRev = _cart.Sum(c => c.Revenue);

        foreach (var cartItem in _cart)
        {
            var revenue = cartItem.Revenue;
            var cost = cartItem.Qty * cartItem.LockedCost;
            var profit = revenue - cost;

            if (_discountAmount > 0 && totalCartRev > 0)
            {
                var discountShare = (revenue / totalCartRev) * _discountAmount;
                profit -= discountShare;
            }

            var sale = new Sale
            {
                ItemId = cartItem.ItemId,
                InvoiceNumber = _currentInvoiceNumber,
                CustomerId = customerId,
                DateShamsi = JalaliDate.TodayShamsi(),
                DateGregorian = today,
                Qty = cartItem.Qty,
                SaleUnitPrice = cartItem.SaleUnitPrice,
                LockedUnitCost = cartItem.LockedCost,
                Revenue = revenue,
                Cost = cost,
                Profit = profit,
                PaymentStatus = paymentStatus,
                CardTerminal = paymentStatus == PaymentStatus.Card && !string.IsNullOrWhiteSpace(_activePOSTerminal)
                    ? _activePOSTerminal
                    : null,
                EntryType = EntryType.Normal,
                CreatedAt = DateTime.UtcNow
            };

            db.Sales.Add(sale);

            totalRevenue += revenue;
            totalProfit += profit;
        }

        db.SaveChanges();

        var savedInvoice = _currentInvoiceNumber;

        var payTypeText = paymentStatus switch
        {
            PaymentStatus.Cash => "نقدی",
            PaymentStatus.Card => string.IsNullOrWhiteSpace(_activePOSTerminal)
                ? "کارتی"
                : $"کارتی ({_activePOSTerminal})",
            PaymentStatus.Credit => "نسیه",
            _ => "—"
        };

        // ═══ پاک کردن سبد قبل از چاپ ═══
        _cart.Clear();
        _discountAmount = 0;
        _selectedCustomerId = null;
        CustomerNameBox.Text = "";
        CustomerPhoneBox.Text = "";

        GenerateInvoiceNumber();
        RefreshCart();
        LoadProductTiles(_activeTab);

        // ═══ مدیریت چاپ ═══
        var settings = StoreSettingsService.Current;

        if (settings.AutoPrintAfterSale)
        {
            // 🆕 چاپ خودکار — بدون پنجره فاکتور
            PrintInvoiceDirectly(savedInvoice, settings);

            StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
            StatusText.Text = $"✓ فروش {payTypeText} — {PersianNumber.ToToman(totalRevenue)} — فاکتور {PersianNumber.ToPersianDigits(savedInvoice)} — در حال چاپ خودکار...";
        }
        else
        {
            // پنجره فاکتور باز می‌شه
            try
            {
                var invoiceWindow = new SaleInvoiceWindow();
                invoiceWindow.LoadInvoice(savedInvoice);
                invoiceWindow.Show();
            }
            catch { }

            StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
            StatusText.Text = $"✓ فروش {payTypeText} ثبت شد — {PersianNumber.ToToman(totalRevenue)} — فاکتور {PersianNumber.ToPersianDigits(savedInvoice)}";
        }

        // فوکوس برگرده به بارکد
        Dispatcher.UIThread.Post(() =>
        {
            BarcodeSearchBox.Focus();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 🆕 چاپ خودکار فاکتور — بدون باز کردن پنجره، مستقیم HTML با auto-print
    /// </summary>
    private void PrintInvoiceDirectly(string invoiceNumber, StoreSettings settings)
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var sales = db.Sales
                .Where(s => s.InvoiceNumber == invoiceNumber && s.EntryType == EntryType.Normal)
                .OrderBy(s => s.Id)
                .ToList();

            if (sales.Count == 0) return;

            var itemsDict = db.Items.ToDictionary(i => i.Id);

            var dateShamsi = sales.First().DateShamsi;
            var paymentStatus = sales.First().PaymentStatus;
            var cardTerminal = sales.First().CardTerminal;

            var customerName = "—";
            var customerPhone = "—";

            var customerId = sales.First().CustomerId;
            if (customerId.HasValue)
            {
                var customer = db.Customers.FirstOrDefault(c => c.Id == customerId.Value);
                if (customer != null)
                {
                    customerName = customer.Name;
                    customerPhone = customer.Phone;
                }
            }

            var items = sales.Select(s =>
            {
                var item = itemsDict.GetValueOrDefault(s.ItemId);
                return new InvoiceItemData
                {
                    ItemName = item?.Name ?? "—",
                    Unit = item?.Unit ?? "—",
                    Qty = s.Qty,
                    UnitPrice = s.SaleUnitPrice,
                    Total = s.Revenue
                };
            }).ToList();

            var totalAmount = items.Sum(i => i.Total);

            var html = SaleInvoiceHtmlBuilder.BuildInvoiceHtml(
                settings,
                settings.PaperSize,
                settings.ShowLogoOnInvoice,
                invoiceNumber,
                dateShamsi,
                customerName,
                customerPhone,
                paymentStatus == PaymentStatus.Cash,
                cardTerminal,
                paymentStatus,
                items,
                totalAmount,
                autoPrint: true);   // ⭐ چاپ خودکار فعال

            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"invoice-auto-{invoiceNumber}-{DateTime.Now:HHmmss}.html");

            File.WriteAllText(tempPath, html, Encoding.UTF8);

            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = $"خطا در چاپ خودکار: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════
    // دیالوگ پرداخت
    // ═══════════════════════════════════════════

    private async System.Threading.Tasks.Task<PaymentStatus?> ShowPaymentDialog()
    {
        var total = _cart.Sum(c => c.Revenue) - _discountAmount;

        var dialog = new Window
        {
            Title = "انتخاب روش پرداخت",
            Width = 480,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Vazirmatn,IRANSans,Segoe UI"),
            Background = new SolidColorBrush(Color.Parse("#F1F5F9")),
            CanResize = false
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(30),
            Spacing = 16
        };

        panel.Children.Add(new TextBlock
        {
            Text = "💰 مبلغ قابل پرداخت",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#334155")),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        panel.Children.Add(new TextBlock
        {
            Text = PersianNumber.ToToman(total),
            FontSize = 32,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#059669")),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        if (!string.IsNullOrWhiteSpace(_activePOSTerminal))
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"🏧 پایانه فعال: {_activePOSTerminal}",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.Parse("#4F46E5")),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }

        PaymentStatus? result = null;

        var cashBtn = new Button
        {
            Content = "💵  نقدی  (F1)",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Height = 65,
            Background = new SolidColorBrush(Color.Parse("#10B981")),
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        cashBtn.Click += (s, e) => { result = PaymentStatus.Cash; dialog.Close(); };

        var cardBtn = new Button
        {
            Content = string.IsNullOrWhiteSpace(_activePOSTerminal)
                ? "💳  کارتی  (F2)"
                : $"💳  کارتی — {_activePOSTerminal}  (F2)",
            FontSize = 17,
            FontWeight = FontWeight.Bold,
            Height = 65,
            Background = new SolidColorBrush(Color.Parse("#4F46E5")),
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        cardBtn.Click += (s, e) => { result = PaymentStatus.Card; dialog.Close(); };

        var creditBtn = new Button
        {
            Content = "📝  نسیه  (F3)",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Height = 65,
            Background = new SolidColorBrush(Color.Parse("#F59E0B")),
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        creditBtn.Click += (s, e) => { result = PaymentStatus.Credit; dialog.Close(); };

        var cancelBtn = new Button
        {
            Content = "انصراف (Esc)",
            FontSize = 14,
            Height = 42,
            Background = new SolidColorBrush(Color.Parse("#F1F5F9")),
            Foreground = new SolidColorBrush(Color.Parse("#475569")),
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        cancelBtn.Click += (s, e) => dialog.Close();

        panel.Children.Add(cashBtn);
        panel.Children.Add(cardBtn);
        panel.Children.Add(creditBtn);
        panel.Children.Add(cancelBtn);

        dialog.Content = panel;
        dialog.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) dialog.Close();
            if (e.Key == Key.F1 || e.Key == Key.Enter) { result = PaymentStatus.Cash; dialog.Close(); }
            if (e.Key == Key.F2) { result = PaymentStatus.Card; dialog.Close(); }
            if (e.Key == Key.F3) { result = PaymentStatus.Credit; dialog.Close(); }
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private async System.Threading.Tasks.Task<decimal> ShowAmountInputDialog(string title, string label, string watermark)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Vazirmatn,IRANSans,Segoe UI"),
            Background = new SolidColorBrush(Color.Parse("#F1F5F9")),
            CanResize = false
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16
        };

        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#334155"))
        });

        var inputBox = new TextBox
        {
            FontSize = 18,
            Padding = new Thickness(14, 12),
            Watermark = watermark
        };
        panel.Children.Add(inputBox);

        decimal result = 0;

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };

        var saveBtn = new Button
        {
            Content = "تأیید",
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Background = new SolidColorBrush(Color.Parse("#10B981")),
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
            Padding = new Thickness(28, 12),
            CornerRadius = new CornerRadius(8)
        };
        saveBtn.Click += (s, e) =>
        {
            var text = PersianNumber.ToEnglishDigits(inputBox.Text ?? "").Trim()
                .Replace(",", "").Replace("٬", "");

            if (decimal.TryParse(text, out var val) && val >= 0)
            {
                result = val;
                dialog.Close();
            }
        };

        var cancelBtn = new Button
        {
            Content = "انصراف",
            FontSize = 15,
            Background = new SolidColorBrush(Color.Parse("#F1F5F9")),
            Foreground = new SolidColorBrush(Color.Parse("#475569")),
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(28, 12),
            CornerRadius = new CornerRadius(8)
        };
        cancelBtn.Click += (s, e) => dialog.Close();

        btnPanel.Children.Add(saveBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        dialog.Content = panel;

        await dialog.ShowDialog(this);
        return result;
    }

    private async System.Threading.Tasks.Task<bool> ShowConfirmDialog(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Vazirmatn,IRANSans,Segoe UI"),
            Background = new SolidColorBrush(Color.Parse("#F1F5F9")),
            CanResize = false
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
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.Parse("#334155")),
            TextWrapping = TextWrapping.Wrap
        });

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };

        bool result = false;

        var yesBtn = new Button
        {
            Content = "بله",
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Background = new SolidColorBrush(Color.Parse("#DC2626")),
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
            Padding = new Thickness(28, 12),
            CornerRadius = new CornerRadius(8)
        };
        yesBtn.Click += (s, e) => { result = true; dialog.Close(); };

        var noBtn = new Button
        {
            Content = "انصراف",
            FontSize = 15,
            Background = new SolidColorBrush(Color.Parse("#F1F5F9")),
            Foreground = new SolidColorBrush(Color.Parse("#475569")),
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(28, 12),
            CornerRadius = new CornerRadius(8)
        };
        noBtn.Click += (s, e) => dialog.Close();

        btnPanel.Children.Add(yesBtn);
        btnPanel.Children.Add(noBtn);
        panel.Children.Add(btnPanel);

        dialog.Content = panel;

        await dialog.ShowDialog<bool>(this);
        return result;
    }

    // ═══════════════════════════════════════════
    // کمکی‌ها
    // ═══════════════════════════════════════════

    private void LoadAllItems()
    {
        using var db = DatabaseService.CreateContext();
        _allItems = db.Items.Where(i => i.IsActive).OrderBy(i => i.ItemCode).ToList();
    }

    private void GenerateInvoiceNumber()
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var todayShamsi = JalaliDate.TodayShamsi();
            var yearPart = todayShamsi.Split('/')[0];

            var lastInvoice = db.Sales
                .Where(s => s.InvoiceNumber != null && s.InvoiceNumber.StartsWith($"POS-{yearPart}-"))
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

            _currentInvoiceNumber = $"POS-{yearPart}-{nextNumber:D4}";
            InvoiceNoText.Text = PersianNumber.ToPersianDigits(_currentInvoiceNumber);
        }
        catch
        {
            _currentInvoiceNumber = $"POS-{DateTime.Now:HHmmss}";
            InvoiceNoText.Text = _currentInvoiceNumber;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _scanner?.StopListening();
        _scanner?.Dispose();
        _clockTimer?.Stop();
        Close();
    }
}