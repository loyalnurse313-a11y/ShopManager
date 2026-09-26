using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ShopManager.Desktop.Services;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;
using ShopManager.Domain.Services;

using Color = Avalonia.Media.Color;
using TextAlignment = Avalonia.Media.TextAlignment;
using Border = Avalonia.Controls.Border;

namespace ShopManager.Desktop.Views;

public partial class DashboardWindow : Window
{
    private bool _reportsLoaded = false;

    private static readonly string[] TerminalColors = new[]
    {
        "#4F46E5", "#0284C7", "#059669", "#D97706", "#DC2626",
        "#7C3AED", "#0891B2", "#16A34A", "#EA580C", "#DB2777",
    };

    public DashboardWindow()
    {
        InitializeComponent();
        TodayDateText.Text = PersianNumber.ToPersianDigits(JalaliDate.ToShamsi(DateTime.Today));
        LoadDashboard();
    }

    // ═══ تب‌ها ═══
    private void OnTabOverviewClick(object? sender, RoutedEventArgs e)
    {
        OverviewPanel.IsVisible = true;
        ReportsPanel.IsVisible = false;
        TabOverviewBtn.Classes.Set("tabActive", true);
        TabOverviewBtn.Classes.Set("tabInactive", false);
        TabReportsBtn.Classes.Set("tabActive", false);
        TabReportsBtn.Classes.Set("tabInactive", true);
    }

    private void OnTabReportsClick(object? sender, RoutedEventArgs e)
    {
        OverviewPanel.IsVisible = false;
        ReportsPanel.IsVisible = true;
        TabOverviewBtn.Classes.Set("tabActive", false);
        TabOverviewBtn.Classes.Set("tabInactive", true);
        TabReportsBtn.Classes.Set("tabActive", true);
        TabReportsBtn.Classes.Set("tabInactive", false);

        if (!_reportsLoaded)
        {
            SetMonthRange();
            LoadStats();
            _reportsLoaded = true;
        }
    }

    // ═══ تب ۱: نمای کلی ═══
    private void LoadDashboard()
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var items = db.Items.Where(i => i.IsActive).ToList();
            var purchases = db.Purchases.Where(p => p.EntryType == EntryType.Normal).ToList();
            var transfers = db.Transfers.Where(t => t.EntryType == EntryType.Normal).ToList();
            var sales = db.Sales.Where(s => s.EntryType == EntryType.Normal).ToList();
            var ledger = db.CashLedgers.ToList();
            var settings = db.Settings.FirstOrDefault();

            decimal warehouseValue = 0;
            int warehouseItemCount = 0;
            decimal shopValue = 0;
            int shopItemCount = 0;

            foreach (var item in items)
            {
                var whStock = StockCalculator.GetWarehouseStock(item, purchases, transfers);
                var shopStock = StockCalculator.GetShopStock(item, transfers, sales);
                var avgCost = LockedCostCalculator.CalculateCurrentAverageCost(item, purchases);

                if (whStock > 0) { warehouseValue += whStock * avgCost; warehouseItemCount++; }
                if (shopStock > 0) { shopValue += shopStock * avgCost; shopItemCount++; }
            }

            KpiWarehouseValueText.Text = MoneyMask.Toman(warehouseValue);
            KpiWarehouseCountText.Text = $"{PersianNumber.ToPersian(warehouseItemCount)} قلم";

            KpiShopValueText.Text = MoneyMask.Toman(shopValue);
            KpiShopCountText.Text = $"{PersianNumber.ToPersian(shopItemCount)} قلم";

            var initialCapital = settings?.InitialCapital ?? 0;
            var cashbox = CashboxCalculator.CalculateCashbox(initialCapital, sales, purchases, ledger);
            KpiCashboxText.Text = MoneyMask.Toman(cashbox);

            var receivables = CashboxCalculator.CalculateReceivables(sales);
            KpiCashboxSubText.Text = MoneyMask.IsLocked ? "🔒 محدود" : $"طلب: {PersianNumber.ToToman(receivables)}";

            var todayShamsi = JalaliDate.TodayShamsi();
            var todaySales = sales.Where(s => s.DateShamsi == todayShamsi).ToList();
            var todayProfit = todaySales.Sum(s => s.Profit);
            var todayInvoices = todaySales.Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count();

            KpiTodayProfitText.Text = MoneyMask.Toman(todayProfit);
            KpiTodaySalesText.Text = $"{PersianNumber.ToPersian(todayInvoices)} فروش";

            var todayCashSales = todaySales.Where(s => s.PaymentStatus == PaymentStatus.Cash).Sum(s => s.Revenue);
            var todayCardSales = todaySales.Where(s => s.PaymentStatus == PaymentStatus.Card).Sum(s => s.Revenue);
            var todayCreditSales = todaySales.Where(s => s.PaymentStatus == PaymentStatus.Credit).Sum(s => s.Revenue);

            var todayCashInvoices = todaySales.Where(s => s.PaymentStatus == PaymentStatus.Cash)
                .Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count();
            var todayCardInvoices = todaySales.Where(s => s.PaymentStatus == PaymentStatus.Card)
                .Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count();
            var todayCreditInvoices = todaySales.Where(s => s.PaymentStatus == PaymentStatus.Credit)
                .Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count();

            KpiTodayCashText.Text = MoneyMask.Toman(todayCashSales);
            KpiTodayCashCountText.Text = $"{PersianNumber.ToPersian(todayCashInvoices)} فاکتور";

            KpiTodayCardText.Text = MoneyMask.Toman(todayCardSales);
            KpiTodayCardCountText.Text = $"{PersianNumber.ToPersian(todayCardInvoices)} فاکتور";

            KpiTodayCreditText.Text = MoneyMask.Toman(todayCreditSales);
            KpiTodayCreditCountText.Text = $"{PersianNumber.ToPersian(todayCreditInvoices)} فاکتور";

            KpiTodayInvoiceCountText.Text = PersianNumber.ToPersian(todayInvoices);

            LoadTodayTerminalBreakdown(todaySales);
            LoadWeekChart(sales);
            LoadCriticalItems(items, purchases, transfers, sales);
            LoadTopToday(todaySales, db);
            LoadRecentSales(sales, db);

            StatusText.Text = $"آخرین بروزرسانی: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(ex, "Dashboard");
            StatusText.Text = ErrorHandler.GetUserMessage(ex);
        }
    }

    private void LoadTodayTerminalBreakdown(List<Sale> todaySales)
    {
        TodayTerminalPanel.Children.Clear();
        var cardSales = todaySales.Where(s => s.PaymentStatus == PaymentStatus.Card).ToList();

        if (cardSales.Count == 0)
        {
            TodayTerminalTotalText.Text = "امروز فروش کارتی ثبت نشده";
            TodayTerminalPanel.Children.Add(new TextBlock
            {
                Text = "امروز فروش کارتی ثبت نشده",
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                FontSize = 13,
                Margin = new Thickness(0, 20, 0, 20),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        var byTerminal = cardSales
            .GroupBy(s => string.IsNullOrWhiteSpace(s.CardTerminal) ? "بدون پایانه" : s.CardTerminal!)
            .Select(g => new
            {
                Terminal = g.Key,
                Revenue = g.Sum(s => s.Revenue),
                InvoiceCount = g.Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count()
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();

        var totalCard = cardSales.Sum(s => s.Revenue);
        TodayTerminalTotalText.Text = $"جمع کارتی: {MoneyMask.Toman(totalCard)}";

        int colorIndex = 0;
        foreach (var t in byTerminal)
        {
            var color = TerminalColors[colorIndex % TerminalColors.Length];
            colorIndex++;
            var percent = totalCard > 0 ? (t.Revenue / totalCard) * 100 : 0;
            TodayTerminalPanel.Children.Add(CreateTerminalCard(t.Terminal, t.Revenue, t.InvoiceCount, percent, color));
        }
    }

    private Border CreateTerminalCard(string terminal, decimal revenue, int invoiceCount, decimal percent, string colorHex)
    {
        var color = Color.Parse(colorHex);

        // ─── کارت سفید با نوار رنگی ۳ پیکسلی کناری (سبک Solid Modern) ───
        var card = new Border
        {
            Width = 240,
            Margin = new Thickness(5),
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.Parse("#FFFFFF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#E2E8F0")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };

        // ریشه: نوار کناری (۳px) + محتوا
        var rootGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("3,*") };

        var sideBar = new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(0, 6, 6, 0)
        };
        Grid.SetColumn(sideBar, 0);
        rootGrid.Children.Add(sideBar);

        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(16, 14) };
        Grid.SetColumn(panel, 1);

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        // آیکون Material به جای ایموجی 🏧
        var icon = new PathIcon
        {
            Data = this.FindResource("IconCreditCard") as Geometry
                   ?? Geometry.Parse("M20 4H4A2 2 0 0 0 2 6V18A2 2 0 0 0 4 20H20A2 2 0 0 0 22 18V6A2 2 0 0 0 20 4M20 11H4V8H20Z"),
            Width = 18,
            Height = 18,
            Foreground = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(icon, 0);

        var nameText = new TextBlock
        {
            Text = terminal,
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(nameText, 1);

        var percentBadge = new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2)
        };
        percentBadge.Child = new TextBlock
        {
            Text = $"{PersianNumber.ToPersian(decimal.Round(percent, 0))}٪",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF"))
        };
        Grid.SetColumn(percentBadge, 2);

        headerGrid.Children.Add(icon);
        headerGrid.Children.Add(nameText);
        headerGrid.Children.Add(percentBadge);
        panel.Children.Add(headerGrid);

        panel.Children.Add(new TextBlock
        {
            Text = MoneyMask.Toman(revenue),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(color),
            Margin = new Thickness(0, 4, 0, 0)
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"{PersianNumber.ToPersian(invoiceCount)} فاکتور",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#64748B"))
        });

        rootGrid.Children.Add(panel);

        card.Child = rootGrid;
        return card;
    }

    private void LoadWeekChart(List<Sale> sales)
    {
        ChartGrid.Children.Clear();

        var today = DateTime.Today;
        var days = new List<(DateTime Date, decimal Revenue, decimal Profit)>();

        for (int i = 6; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var dayShamsi = JalaliDate.ToShamsi(date);
            var daySales = sales.Where(s => s.DateShamsi == dayShamsi).ToList();
            days.Add((date, daySales.Sum(s => s.Revenue), daySales.Sum(s => s.Profit)));
        }

        var maxRevenue = days.Max(d => d.Revenue);
        if (maxRevenue == 0) maxRevenue = 1;

        var weekRevenue = days.Sum(d => d.Revenue);
        var weekProfit = days.Sum(d => d.Profit);
        WeekRevenueText.Text = MoneyMask.Toman(weekRevenue);
        WeekProfitText.Text = MoneyMask.Toman(weekProfit);

        var bestDay = days.OrderByDescending(d => d.Revenue).First();
        BestDayText.Text = bestDay.Revenue > 0
            ? $"{PersianNumber.ToPersianDigits(JalaliDate.ToShamsi(bestDay.Date))}\n{MoneyMask.Toman(bestDay.Revenue)}"
            : "—";

        for (int i = 0; i < 7; i++)
        {
            var day = days[i];
            var height = maxRevenue > 0 ? (double)(day.Revenue / maxRevenue) * 140 : 0;
            if (height < 3 && day.Revenue > 0) height = 3;

            var column = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Spacing = 6,
                Margin = new Thickness(4, 0, 4, 0)
            };

            var valueText = new TextBlock
            {
                Text = day.Revenue > 0 && !MoneyMask.IsLocked ? PersianNumber.ToPersian(day.Revenue / 1000) + "ک" : "",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.Parse("#475569")),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            var bar = new Avalonia.Controls.Border
            {
                Height = height,
                Background = new SolidColorBrush(Color.Parse("#1E40AF")),
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 20
            };

            var dayLabel = new TextBlock
            {
                Text = PersianNumber.ToPersianDigits(JalaliDate.ToShamsi(day.Date).Substring(8, 2)),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            column.Children.Add(valueText);
            column.Children.Add(bar);
            column.Children.Add(dayLabel);
            Grid.SetColumn(column, i);
            ChartGrid.Children.Add(column);
        }
    }

    private void LoadCriticalItems(
        List<Domain.Entities.Item> items,
        List<Domain.Entities.Purchase> purchases,
        List<Domain.Entities.Transfer> transfers,
        List<Domain.Entities.Sale> sales)
    {
        var criticalList = new List<(Domain.Entities.Item Item, decimal Stock, string Location, decimal Threshold)>();

        foreach (var item in items)
        {
            var totalInput = StockAlertCalculator.GetTotalHistoricalInput(item, purchases);
            var whStock = StockCalculator.GetWarehouseStock(item, purchases, transfers);
            var shopStock = StockCalculator.GetShopStock(item, transfers, sales);

            if (StockAlertCalculator.GetStatus(item, whStock, totalInput) == StockStatus.Critical)
                criticalList.Add((item, whStock, "انبار", 0));

            if (StockAlertCalculator.GetStatus(item, shopStock, totalInput) == StockStatus.Critical)
                criticalList.Add((item, shopStock, "مغازه", 0));
        }

        CriticalCountText.Text = PersianNumber.ToPersian(criticalList.Count);
        CriticalItemsPanel.Children.Clear();

        if (criticalList.Count == 0)
        {
            CriticalItemsPanel.Children.Add(new TextBlock
            {
                Text = "✓ همه کالاها موجودی کافی دارند",
                Foreground = new SolidColorBrush(Color.Parse("#10B981")),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 40)
            });
            return;
        }

        foreach (var c in criticalList.Take(10))
        {
            var row = new Avalonia.Controls.Border
            {
                Padding = new Thickness(12, 10),
                BorderBrush = new SolidColorBrush(Color.Parse("#FEE2E2")),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*,90,110") };

            var codeText = new TextBlock
            {
                Text = PersianNumber.ToPersian(c.Item.ItemCode),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#DC2626")),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(codeText, 0);

            var nameText = new TextBlock
            {
                Text = c.Item.Name,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(nameText, 1);

            var locationText = new TextBlock
            {
                Text = c.Location,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#DC2626")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(locationText, 2);

            var stockText = new TextBlock
            {
                Text = PersianNumber.ToPersian(c.Stock) + " " + c.Item.Unit,
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#DC2626")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(stockText, 3);

            grid.Children.Add(codeText);
            grid.Children.Add(nameText);
            grid.Children.Add(locationText);
            grid.Children.Add(stockText);

            row.Child = grid;
            CriticalItemsPanel.Children.Add(row);
        }
    }

    private void LoadTopToday(List<Sale> todaySales, Infrastructure.Persistence.AppDbContext db)
    {
        TopTodayPanel.Children.Clear();

        if (todaySales.Count == 0)
        {
            TopTodayPanel.Children.Add(new TextBlock
            {
                Text = "امروز فروشی ثبت نشده",
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 40)
            });
            return;
        }

        var itemsDict = db.Items.ToDictionary(i => i.Id);

        var topItems = todaySales
            .GroupBy(s => s.ItemId)
            .Select(g => new
            {
                ItemId = g.Key,
                Qty = g.Sum(s => s.Qty),
                Profit = g.Sum(s => s.Profit)
            })
            .OrderByDescending(x => x.Qty)
            .Take(5)
            .ToList();

        int rank = 1;
        foreach (var t in topItems)
        {
            var item = itemsDict.GetValueOrDefault(t.ItemId);

            var row = new Avalonia.Controls.Border
            {
                Padding = new Thickness(12, 10),
                BorderBrush = new SolidColorBrush(Color.Parse("#F1F5F9")),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,90,110") };

            var rankText = new TextBlock
            {
                Text = PersianNumber.ToPersian(rank),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse(rank <= 3 ? "#D97706" : "#94A3B8")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(rankText, 0);

            var nameText = new TextBlock
            {
                Text = item?.Name ?? "—",
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(nameText, 1);

            var qtyText = new TextBlock
            {
                Text = PersianNumber.ToPersian(t.Qty) + " " + (item?.Unit ?? ""),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#4F46E5")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(qtyText, 2);

            var profitText = new TextBlock
            {
                Text = MoneyMask.Toman(t.Profit),
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#10B981")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(profitText, 3);

            grid.Children.Add(rankText);
            grid.Children.Add(nameText);
            grid.Children.Add(qtyText);
            grid.Children.Add(profitText);

            row.Child = grid;
            TopTodayPanel.Children.Add(row);
            rank++;
        }
    }

    private void LoadRecentSales(List<Sale> sales, Infrastructure.Persistence.AppDbContext db)
    {
        RecentSalesPanel.Children.Clear();

        var recentSales = sales
            .OrderByDescending(s => s.DateGregorian)
            .ThenByDescending(s => s.Id)
            .Take(8)
            .ToList();

        if (recentSales.Count == 0)
        {
            RecentSalesPanel.Children.Add(new TextBlock
            {
                Text = "فروشی ثبت نشده",
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 40)
            });
            return;
        }

        var itemsDict = db.Items.ToDictionary(i => i.Id);

        foreach (var s in recentSales)
        {
            var item = itemsDict.GetValueOrDefault(s.ItemId);

            var row = new Avalonia.Controls.Border
            {
                Padding = new Thickness(12, 10),
                BorderBrush = new SolidColorBrush(Color.Parse("#F1F5F9")),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,70,*,80,120,90,120") };

            var dateText = new TextBlock
            {
                Text = PersianNumber.ToPersianDigits(s.DateShamsi),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#475569")),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dateText, 0);

            var codeText = new TextBlock
            {
                Text = PersianNumber.ToPersian(item?.ItemCode ?? 0),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(codeText, 1);

            var nameText = new TextBlock
            {
                Text = item?.Name ?? "—",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(nameText, 2);

            var qtyText = new TextBlock
            {
                Text = PersianNumber.ToPersian(s.Qty),
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#4F46E5")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(qtyText, 3);

            var revenueText = new TextBlock
            {
                Text = MoneyMask.Toman(s.Revenue),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#059669")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(revenueText, 4);

            var payText = s.PaymentStatus switch
            {
                PaymentStatus.Cash => "نقدی",
                PaymentStatus.Card => string.IsNullOrWhiteSpace(s.CardTerminal) ? "کارتی" : $"کارتی ({s.CardTerminal})",
                PaymentStatus.Credit => "نسیه",
                _ => "—"
            };
            var payColor = s.PaymentStatus switch
            {
                PaymentStatus.Cash => "#059669",
                PaymentStatus.Card => "#4F46E5",
                PaymentStatus.Credit => "#D97706",
                _ => "#94A3B8"
            };

            var payTextBlock = new TextBlock
            {
                Text = payText,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse(payColor)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(payTextBlock, 5);

            var profitText = new TextBlock
            {
                Text = MoneyMask.Toman(s.Profit),
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#10B981")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(profitText, 6);

            grid.Children.Add(dateText);
            grid.Children.Add(codeText);
            grid.Children.Add(nameText);
            grid.Children.Add(qtyText);
            grid.Children.Add(revenueText);
            grid.Children.Add(payTextBlock);
            grid.Children.Add(profitText);

            row.Child = grid;
            RecentSalesPanel.Children.Add(row);
        }
    }

    // ═══ تب ۲: آمار تفصیلی ═══
    private void SetMonthRange()
    {
        var todayShamsi = JalaliDate.TodayShamsi();
        var parts = todayShamsi.Split('/');
        if (parts.Length == 3)
        {
            FromDateBox.Text = $"{parts[0]}/{parts[1]}/01";
            ToDateBox.Text = todayShamsi;
        }
    }

    private void LoadStats()
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var allSales = db.Sales.Where(s => s.EntryType == EntryType.Normal).ToList();
            var filtered = allSales.AsEnumerable();

            var fromText = PersianNumber.ToEnglishDigits(FromDateBox.Text ?? "").Trim();
            var toText = PersianNumber.ToEnglishDigits(ToDateBox.Text ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(fromText))
            {
                try
                {
                    var fromDate = JalaliDate.ToGregorian(fromText);
                    filtered = filtered.Where(s => s.DateGregorian.Date >= fromDate.Date);
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(toText))
            {
                try
                {
                    var toDate = JalaliDate.ToGregorian(toText);
                    filtered = filtered.Where(s => s.DateGregorian.Date <= toDate.Date);
                }
                catch { }
            }

            var sales = filtered.ToList();

            var totalRevenue = sales.Sum(s => s.Revenue);
            var totalProfit = sales.Sum(s => s.Profit);
            var salesCount = sales.Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count();
            var avgProfit = salesCount > 0 ? totalProfit / salesCount : 0;

            KpiRevenueText.Text = MoneyMask.Toman(totalRevenue);
            KpiProfitText.Text = MoneyMask.Toman(totalProfit);
            KpiSalesCountText.Text = PersianNumber.ToPersian(salesCount);
            KpiAvgProfitText.Text = MoneyMask.Toman(avgProfit);

            var cashTotal = sales.Where(s => s.PaymentStatus == PaymentStatus.Cash).Sum(s => s.Revenue);
            var cardTotal = sales.Where(s => s.PaymentStatus == PaymentStatus.Card).Sum(s => s.Revenue);
            var creditTotal = sales.Where(s => s.PaymentStatus == PaymentStatus.Credit).Sum(s => s.Revenue);
            var totalAll = cashTotal + cardTotal + creditTotal;
            var avgInvoice = salesCount > 0 ? totalAll / salesCount : 0;

            var cashPct = totalAll > 0 ? (cashTotal / totalAll) * 100 : 0;
            var cardPct = totalAll > 0 ? (cardTotal / totalAll) * 100 : 0;
            var creditPct = totalAll > 0 ? (creditTotal / totalAll) * 100 : 0;

            KpiCashTotalText.Text = MoneyMask.Toman(cashTotal);
            KpiCashPctText.Text = MoneyMask.IsLocked ? "🔒" : $"{PersianNumber.ToPersian(decimal.Round(cashPct, 1))}٪ از کل";

            KpiCardTotalText.Text = MoneyMask.Toman(cardTotal);
            KpiCardPctText.Text = MoneyMask.IsLocked ? "🔒" : $"{PersianNumber.ToPersian(decimal.Round(cardPct, 1))}٪ از کل";

            KpiCreditTotalText.Text = MoneyMask.Toman(creditTotal);
            KpiCreditPctText.Text = MoneyMask.IsLocked ? "🔒" : $"{PersianNumber.ToPersian(decimal.Round(creditPct, 1))}٪ از کل";

            KpiAvgInvoiceText.Text = MoneyMask.Toman(avgInvoice);
            KpiInvoiceCountText.Text = $"{PersianNumber.ToPersian(salesCount)} فاکتور";

            LoadReportsTerminalBreakdown(sales);

            var topSelling = sales
                .GroupBy(s => s.ItemId)
                .Select(g => new { ItemId = g.Key, Qty = g.Sum(s => s.Qty), Revenue = g.Sum(s => s.Revenue) })
                .OrderByDescending(x => x.Qty)
                .Take(10)
                .ToList();

            var itemsDict = db.Items.ToDictionary(i => i.Id);

            TopSellingPanel.Children.Clear();

            if (topSelling.Count == 0)
            {
                TopSellingPanel.Children.Add(MakeEmptyText());
            }
            else
            {
                int rank = 1;
                foreach (var t in topSelling)
                {
                    var item = itemsDict.GetValueOrDefault(t.ItemId);
                    TopSellingPanel.Children.Add(MakeTopItemRow(
                        rank, item?.ItemCode.ToString() ?? "—", item?.Name ?? "—",
                        item?.Unit ?? "—", PersianNumber.ToPersian(t.Qty),
                        MoneyMask.Toman(t.Revenue), "#4F46E5"));
                    rank++;
                }
            }

            var topProfit = sales
                .GroupBy(s => s.ItemId)
                .Select(g => new { ItemId = g.Key, Profit = g.Sum(s => s.Profit), Revenue = g.Sum(s => s.Revenue) })
                .OrderByDescending(x => x.Profit)
                .Take(10)
                .ToList();

            TopProfitPanel.Children.Clear();

            if (topProfit.Count == 0)
            {
                TopProfitPanel.Children.Add(MakeEmptyText());
            }
            else
            {
                int rank = 1;
                foreach (var t in topProfit)
                {
                    var item = itemsDict.GetValueOrDefault(t.ItemId);
                    TopProfitPanel.Children.Add(MakeTopItemRow(
                        rank, item?.ItemCode.ToString() ?? "—", item?.Name ?? "—",
                        item?.Unit ?? "—", MoneyMask.Toman(t.Profit),
                        MoneyMask.Toman(t.Revenue), "#059669"));
                    rank++;
                }
            }

            var topCustomers = sales
                .Where(s => s.CustomerId.HasValue)
                .GroupBy(s => s.CustomerId!.Value)
                .Select(g => new
                {
                    CustomerId = g.Key,
                    Revenue = g.Sum(s => s.Revenue),
                    Count = g.Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count()
                })
                .OrderByDescending(x => x.Revenue)
                .Take(10)
                .ToList();

            var customersDict = db.Customers.ToDictionary(c => c.Id);

            TopCustomerPanel.Children.Clear();

            if (topCustomers.Count == 0)
            {
                TopCustomerPanel.Children.Add(MakeEmptyText());
            }
            else
            {
                int rank = 1;
                foreach (var t in topCustomers)
                {
                    var customer = customersDict.GetValueOrDefault(t.CustomerId);
                    if (customer == null) continue;

                    TopCustomerPanel.Children.Add(MakeTopCustomerRow(
                        rank, customer.Name, PersianNumber.ToPersianDigits(customer.Phone),
                        PersianNumber.ToPersian(t.Count), MoneyMask.Toman(t.Revenue)));
                    rank++;
                }
            }

            StatusText.Text = MoneyMask.IsLocked
                ? $"تعداد کل فروش‌ها در این بازه: {PersianNumber.ToPersian(sales.Count)} قلم — 🔒 اعداد مالی محدود شده"
                : $"تعداد کل فروش‌ها در این بازه: {PersianNumber.ToPersian(sales.Count)} قلم — " +
                  $"نقد: {PersianNumber.ToPersian(decimal.Round(cashPct, 0))}٪ — " +
                  $"کارت: {PersianNumber.ToPersian(decimal.Round(cardPct, 0))}٪ — " +
                  $"نسیه: {PersianNumber.ToPersian(decimal.Round(creditPct, 0))}٪";
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(ex, "Dashboard");
            StatusText.Text = ErrorHandler.GetUserMessage(ex);
        }
    }

    private void LoadReportsTerminalBreakdown(List<Sale> sales)
    {
        ReportsTerminalPanel.Children.Clear();
        var cardSales = sales.Where(s => s.PaymentStatus == PaymentStatus.Card).ToList();

        if (cardSales.Count == 0)
        {
            ReportsTerminalTotalText.Text = "در این بازه فروش کارتی ثبت نشده";
            ReportsTerminalPanel.Children.Add(new TextBlock
            {
                Text = "در این بازه فروش کارتی ثبت نشده",
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                FontSize = 13,
                Margin = new Thickness(0, 20, 0, 20),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        var byTerminal = cardSales
            .GroupBy(s => string.IsNullOrWhiteSpace(s.CardTerminal) ? "بدون پایانه" : s.CardTerminal!)
            .Select(g => new
            {
                Terminal = g.Key,
                Revenue = g.Sum(s => s.Revenue),
                InvoiceCount = g.Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count()
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();

        var totalCard = cardSales.Sum(s => s.Revenue);
        ReportsTerminalTotalText.Text = $"جمع کارتی بازه: {MoneyMask.Toman(totalCard)}";

        int colorIndex = 0;
        foreach (var t in byTerminal)
        {
            var color = TerminalColors[colorIndex % TerminalColors.Length];
            colorIndex++;
            var percent = totalCard > 0 ? (t.Revenue / totalCard) * 100 : 0;
            ReportsTerminalPanel.Children.Add(CreateTerminalCard(t.Terminal, t.Revenue, t.InvoiceCount, percent, color));
        }
    }

    private TextBlock MakeEmptyText()
    {
        return new TextBlock
        {
            Text = "داده‌ای در این بازه وجود ندارد",
            Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 30, 0, 30)
        };
    }

    private Border MakeTopItemRow(int rank, string code, string name, string unit,
                                   string mainValue, string secondary, string colorHex)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("60,80,*,80,110,120") };

        var rankBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse(rank <= 3 ? "#FEF3C7" : "#F1F5F9")),
            CornerRadius = new CornerRadius(20),
            Width = 32,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        rankBorder.Child = new TextBlock
        {
            Text = PersianNumber.ToPersian(rank),
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse(rank <= 3 ? "#D97706" : "#475569")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(rankBorder, 0);

        var codeText = new TextBlock
        {
            Text = PersianNumber.ToPersianDigits(code),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(codeText, 1);

        var nameText = new TextBlock
        {
            Text = name,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameText, 2);

        var unitText = new TextBlock
        {
            Text = unit,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#475569")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(unitText, 3);

        var mainText = new TextBlock
        {
            Text = mainValue,
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse(colorHex)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(mainText, 4);

        var secText = new TextBlock
        {
            Text = secondary,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#475569")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(secText, 5);

        grid.Children.Add(rankBorder);
        grid.Children.Add(codeText);
        grid.Children.Add(nameText);
        grid.Children.Add(unitText);
        grid.Children.Add(mainText);
        grid.Children.Add(secText);

        return new Border
        {
            Padding = new Thickness(12, 10),
            BorderBrush = new SolidColorBrush(Color.Parse("#F1F5F9")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private Border MakeTopCustomerRow(int rank, string name, string phone, string count, string amount)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("60,*,180,120,150") };

        var rankBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse(rank <= 3 ? "#FEF3C7" : "#F1F5F9")),
            CornerRadius = new CornerRadius(20),
            Width = 32,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        rankBorder.Child = new TextBlock
        {
            Text = PersianNumber.ToPersian(rank),
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse(rank <= 3 ? "#D97706" : "#475569")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(rankBorder, 0);

        var nameText = new TextBlock
        {
            Text = name,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameText, 1);

        var phoneText = new TextBlock
        {
            Text = phone,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#4F46E5")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(phoneText, 2);

        var countText = new TextBlock
        {
            Text = count,
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(countText, 3);

        var amountText = new TextBlock
        {
            Text = amount,
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#059669")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(amountText, 4);

        grid.Children.Add(rankBorder);
        grid.Children.Add(nameText);
        grid.Children.Add(phoneText);
        grid.Children.Add(countText);
        grid.Children.Add(amountText);

        return new Border
        {
            Padding = new Thickness(12, 10),
            BorderBrush = new SolidColorBrush(Color.Parse("#F1F5F9")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    // ═══ فیلتر تاریخ ═══
    private void OnTodayClick(object? sender, RoutedEventArgs e)
    {
        var today = JalaliDate.TodayShamsi();
        FromDateBox.Text = today;
        ToDateBox.Text = today;
        LoadStats();
    }

    private void OnWeekClick(object? sender, RoutedEventArgs e)
    {
        var today = JalaliDate.TodayGregorian();
        var weekAgo = today.AddDays(-7);
        FromDateBox.Text = JalaliDate.ToShamsi(weekAgo);
        ToDateBox.Text = JalaliDate.ToShamsi(today);
        LoadStats();
    }

    private void OnMonthClick(object? sender, RoutedEventArgs e)
    {
        SetMonthRange();
        LoadStats();
    }

    private void OnAllClick(object? sender, RoutedEventArgs e)
    {
        FromDateBox.Text = "";
        ToDateBox.Text = "";
        LoadStats();
    }

    private void OnApplyFilterClick(object? sender, RoutedEventArgs e) => LoadStats();

    // ═══ چاپ ═══
    private void OnPrintClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (MoneyMask.IsLocked)
            {
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
                StatusText.Text = "🔒 دسترسی به گزارش‌های مالی محدود شده";
                return;
            }

            if (OverviewPanel.IsVisible) PrintOverview();
            else PrintReports();
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError(ex, "Dashboard");
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = ErrorHandler.GetUserMessage(ex);
        }
    }

    private void PrintOverview()
    {
        using var db = DatabaseService.CreateContext();

        var items = db.Items.Where(i => i.IsActive).ToList();
        var purchases = db.Purchases.Where(p => p.EntryType == EntryType.Normal).ToList();
        var transfers = db.Transfers.Where(t => t.EntryType == EntryType.Normal).ToList();
        var sales = db.Sales.Where(s => s.EntryType == EntryType.Normal).ToList();
        var ledger = db.CashLedgers.ToList();
        var settings = db.Settings.FirstOrDefault();

        decimal warehouseValue = 0, shopValue = 0;
        int warehouseItemCount = 0, shopItemCount = 0;

        foreach (var item in items)
        {
            var whStock = StockCalculator.GetWarehouseStock(item, purchases, transfers);
            var shopStock = StockCalculator.GetShopStock(item, transfers, sales);
            var avgCost = LockedCostCalculator.CalculateCurrentAverageCost(item, purchases);

            if (whStock > 0) { warehouseValue += whStock * avgCost; warehouseItemCount++; }
            if (shopStock > 0) { shopValue += shopStock * avgCost; shopItemCount++; }
        }

        var initialCapital = settings?.InitialCapital ?? 0;
        var cashbox = CashboxCalculator.CalculateCashbox(initialCapital, sales, purchases, ledger);
        var receivables = CashboxCalculator.CalculateReceivables(sales);

        var todayShamsi = JalaliDate.TodayShamsi();
        var todaySales = sales.Where(s => s.DateShamsi == todayShamsi).ToList();
        var todayProfit = todaySales.Sum(s => s.Profit);
        var todayInvoices = todaySales.Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count();

        var todayCash = todaySales.Where(s => s.PaymentStatus == PaymentStatus.Cash).Sum(s => s.Revenue);
        var todayCard = todaySales.Where(s => s.PaymentStatus == PaymentStatus.Card).Sum(s => s.Revenue);
        var todayCredit = todaySales.Where(s => s.PaymentStatus == PaymentStatus.Credit).Sum(s => s.Revenue);

        var todayCashInv = todaySales.Where(s => s.PaymentStatus == PaymentStatus.Cash)
            .Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count();
        var todayCardInv = todaySales.Where(s => s.PaymentStatus == PaymentStatus.Card)
            .Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count();
        var todayCreditInv = todaySales.Where(s => s.PaymentStatus == PaymentStatus.Credit)
            .Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count();

        var cardSalesToday = todaySales.Where(s => s.PaymentStatus == PaymentStatus.Card).ToList();
        var terminalToday = cardSalesToday
            .GroupBy(s => string.IsNullOrWhiteSpace(s.CardTerminal) ? "بدون پایانه" : s.CardTerminal!)
            .Select(g => new
            {
                Terminal = g.Key,
                Revenue = g.Sum(s => s.Revenue),
                Count = g.Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count()
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();

        var criticalList = new List<(string Name, string Location, decimal Stock, string Unit)>();
        foreach (var item in items)
        {
            var totalInput = StockAlertCalculator.GetTotalHistoricalInput(item, purchases);
            var whStock = StockCalculator.GetWarehouseStock(item, purchases, transfers);
            var shopStock = StockCalculator.GetShopStock(item, transfers, sales);

            if (StockAlertCalculator.GetStatus(item, whStock, totalInput) == StockStatus.Critical)
                criticalList.Add((item.Name, "انبار", whStock, item.Unit));

            if (StockAlertCalculator.GetStatus(item, shopStock, totalInput) == StockStatus.Critical)
                criticalList.Add((item.Name, "مغازه", shopStock, item.Unit));
        }

        var itemsDict = db.Items.ToDictionary(i => i.Id);
        var topToday = todaySales
            .GroupBy(s => s.ItemId)
            .Select(g => new
            {
                Name = itemsDict.GetValueOrDefault(g.Key)?.Name ?? "—",
                Unit = itemsDict.GetValueOrDefault(g.Key)?.Unit ?? "",
                Qty = g.Sum(s => s.Qty),
                Profit = g.Sum(s => s.Profit)
            })
            .OrderByDescending(x => x.Qty)
            .Take(5)
            .ToList();

        var recentSales = sales
            .OrderByDescending(s => s.DateGregorian)
            .ThenByDescending(s => s.Id)
            .Take(10)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html dir='rtl' lang='fa'><head><meta charset='UTF-8'>");
        sb.AppendLine("<title>گزارش داشبورد</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("* { font-family: Vazirmatn, Tahoma, sans-serif; box-sizing: border-box; }");
        sb.AppendLine("body { margin: 20px; background: #F5F7FA; color: #0F172A; font-size: 12px; }");
        sb.AppendLine(".container { max-width: 900px; margin: 0 auto; }");
        // هدر
        sb.AppendLine(".header { background: #0F172A; color: white; padding: 24px 30px; border-radius: 6px 6px 0 0; }");
        sb.AppendLine(".header h1 { margin: 0; font-size: 20px; font-weight: bold; }");
        sb.AppendLine(".header .meta { font-size: 11px; color: #94A3B8; margin-top: 6px; }");
        // بخش
        sb.AppendLine(".section { background: white; margin-bottom: 16px; border-radius: 6px; border: 1px solid #E2E8F0; overflow: hidden; }");
        sb.AppendLine(".section-header { padding: 14px 20px; border-bottom: 1px solid #E2E8F0; background: white; }");
        sb.AppendLine(".section-header h2 { margin: 0; font-size: 14px; font-weight: 600; color: #0F172A; }");
        sb.AppendLine(".section-body { padding: 20px; }");
        // KPI
        sb.AppendLine(".kpi-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; }");
        sb.AppendLine(".kpi { background: white; border: 1px solid #E2E8F0; border-radius: 6px; padding: 16px; border-right: 3px solid #1E40AF; }");
        sb.AppendLine(".kpi.success { border-right-color: #047857; }");
        sb.AppendLine(".kpi.warning { border-right-color: #B45309; }");
        sb.AppendLine(".kpi.danger { border-right-color: #B91C1C; }");
        sb.AppendLine(".kpi .label { font-size: 11px; color: #64748B; margin-bottom: 8px; font-weight: 600; }");
        sb.AppendLine(".kpi .value { font-size: 16px; font-weight: bold; color: #0F172A; }");
        sb.AppendLine(".kpi .sub { font-size: 10px; color: #94A3B8; margin-top: 4px; }");
        // جدول
        sb.AppendLine("table { width: 100%; border-collapse: collapse; font-size: 11px; }");
        sb.AppendLine("th { background: #F1F5F9; color: #0F172A; padding: 10px 8px; font-size: 11px; font-weight: 600; text-align: center; border-bottom: 1px solid #E2E8F0; }");
        sb.AppendLine("td { padding: 8px; border-bottom: 1px solid #F1F5F9; text-align: center; color: #334155; }");
        sb.AppendLine("td.name { text-align: right; font-weight: 600; color: #0F172A; }");
        sb.AppendLine("tr:last-child td { border-bottom: none; }");
        sb.AppendLine("tr:nth-child(even) td { background: #FAFBFC; }");
        sb.AppendLine("tr.total td { background: #F1F5F9; font-weight: bold; border-top: 2px solid #E2E8F0; }");
        // رنگ‌های وضعیت
        sb.AppendLine(".text-success { color: #047857; font-weight: 600; }");
        sb.AppendLine(".text-warning { color: #B45309; font-weight: 600; }");
        sb.AppendLine(".text-danger { color: #B91C1C; font-weight: 600; }");
        sb.AppendLine(".text-accent { color: #1E40AF; font-weight: 600; }");
        // فوتر
        sb.AppendLine(".footer { text-align: center; color: #94A3B8; font-size: 10px; margin-top: 24px; padding: 16px; }");
        // چاپ
        sb.AppendLine("@media print { @page { size: A4; margin: 10mm; } body { margin: 0; background: white; } .section { page-break-inside: avoid; } .header { border-radius: 0; } }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<div class='container'>");
        sb.AppendLine("<div class='header'>");
        sb.AppendLine($"<h1>{HtmlEncoder.Encode(string.IsNullOrWhiteSpace(StoreSettingsService.Current.StoreName) ? "فروشگاه" : StoreSettingsService.Current.StoreName)}</h1>");
        sb.AppendLine($"<div class='meta'>گزارش داشبورد — نمای کلی | تاریخ: {PersianNumber.ToPersianDigits(JalaliDate.ToShamsi(DateTime.Today))} | ساعت: {PersianNumber.ToPersianDigits(DateTime.Now.ToString("HH:mm"))}</div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='section'>");
        sb.AppendLine("<div class='section-header'><h2>شاخص‌های کلی</h2></div>");
        sb.AppendLine("<div class='section-body'>");
        sb.AppendLine("<div class='kpi-grid'>");
        sb.AppendLine($"<div class='kpi'><div class='label'>موجودی انبار</div><div class='value'>{PersianNumber.ToToman(warehouseValue)}</div><div class='sub'>{PersianNumber.ToPersian(warehouseItemCount)} قلم</div></div>");
        sb.AppendLine($"<div class='kpi success'><div class='label'>موجودی مغازه</div><div class='value'>{PersianNumber.ToToman(shopValue)}</div><div class='sub'>{PersianNumber.ToPersian(shopItemCount)} قلم</div></div>");
        sb.AppendLine($"<div class='kpi warning'><div class='label'>صندوق نقدی</div><div class='value'>{PersianNumber.ToToman(cashbox)}</div><div class='sub'>طلب: {PersianNumber.ToToman(receivables)}</div></div>");
        sb.AppendLine($"<div class='kpi success'><div class='label'>سود امروز</div><div class='value'>{PersianNumber.ToToman(todayProfit)}</div><div class='sub'>{PersianNumber.ToPersian(todayInvoices)} فروش</div></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div></div>");

        var todayTotal = todayCash + todayCard + todayCredit;
        var cashPct = todayTotal > 0 ? (todayCash / todayTotal) * 100 : 0;
        var cardPct = todayTotal > 0 ? (todayCard / todayTotal) * 100 : 0;
        var creditPct = todayTotal > 0 ? (todayCredit / todayTotal) * 100 : 0;

        sb.AppendLine("<div class='section'>");
        sb.AppendLine("<div class='section-header'><h2>فروش امروز — تفکیک پرداخت</h2></div>");
        sb.AppendLine("<div class='section-body'>");
        sb.AppendLine("<div class='kpi-grid'>");
        sb.AppendLine($"<div class='kpi success'><div class='label'>نقدی</div><div class='value'>{PersianNumber.ToToman(todayCash)}</div><div class='sub'>{PersianNumber.ToPersian(todayCashInv)} فاکتور — {PersianNumber.ToPersian(decimal.Round(cashPct, 1))}٪</div></div>");
        sb.AppendLine($"<div class='kpi'><div class='label'>کارتی</div><div class='value'>{PersianNumber.ToToman(todayCard)}</div><div class='sub'>{PersianNumber.ToPersian(todayCardInv)} فاکتور — {PersianNumber.ToPersian(decimal.Round(cardPct, 1))}٪</div></div>");
        sb.AppendLine($"<div class='kpi warning'><div class='label'>نسیه</div><div class='value'>{PersianNumber.ToToman(todayCredit)}</div><div class='sub'>{PersianNumber.ToPersian(todayCreditInv)} فاکتور — {PersianNumber.ToPersian(decimal.Round(creditPct, 1))}٪</div></div>");
        sb.AppendLine($"<div class='kpi'><div class='label'>تعداد فاکتور</div><div class='value'>{PersianNumber.ToPersian(todayInvoices)}</div><div class='sub'>جمع: {PersianNumber.ToToman(todayTotal)}</div></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div></div>");

        if (terminalToday.Count > 0)
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine("<div class='section-header'><h2>فروش کارتی — تفکیک پایانه‌ها</h2></div>");
            sb.AppendLine("<div class='section-body'>");
            sb.AppendLine("<table><thead><tr><th style='text-align: right;'>پایانه</th><th>مبلغ</th><th>تعداد فاکتور</th><th>درصد</th></tr></thead><tbody>");
            var totalCardToday = terminalToday.Sum(t => t.Revenue);
            foreach (var t in terminalToday)
            {
                var pct = totalCardToday > 0 ? (t.Revenue / totalCardToday) * 100 : 0;
                // escape نام پایانه برای جلوگیری از XSS
                sb.AppendLine($"<tr><td class='name'>{HtmlEncoder.Encode(t.Terminal)}</td><td class='text-accent'>{PersianNumber.ToToman(t.Revenue)}</td><td>{PersianNumber.ToPersian(t.Count)}</td><td>{PersianNumber.ToPersian(decimal.Round(pct, 1))}٪</td></tr>");
            }
            sb.AppendLine($"<tr class='total'><td class='name'>جمع کل</td><td class='text-accent'>{PersianNumber.ToToman(totalCardToday)}</td><td>{PersianNumber.ToPersian(terminalToday.Sum(t => t.Count))}</td><td>۱۰۰٪</td></tr>");
            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</div></div>");
        }

        sb.AppendLine("<div class='section'>");
        sb.AppendLine("<div class='section-header'><h2>کالاهای بحرانی</h2></div>");
        sb.AppendLine("<div class='section-body'>");
        if (criticalList.Count == 0)
        {
            sb.AppendLine("<div style='text-align:center;color:#047857;padding:20px;'>✓ همه کالاها موجودی کافی دارند</div>");
        }
        else
        {
            sb.AppendLine("<table><thead><tr><th>#</th><th>نام کالا</th><th>موقعیت</th><th>موجودی</th></tr></thead><tbody>");
            int idx = 1;
            foreach (var c in criticalList.Take(15))
            {
                // escape نام و واحد کالای بحرانی برای جلوگیری از XSS
                sb.AppendLine($"<tr><td>{PersianNumber.ToPersian(idx)}</td><td class='name'>{HtmlEncoder.Encode(c.Name)}</td><td class='text-danger'>{c.Location}</td><td class='text-danger'>{PersianNumber.ToPersian(c.Stock)} {HtmlEncoder.Encode(c.Unit)}</td></tr>");
                idx++;
            }
            sb.AppendLine("</tbody></table>");
        }
        sb.AppendLine("</div></div>");

        sb.AppendLine("<div class='section'>");
        sb.AppendLine("<div class='section-header'><h2>پرفروش‌ترین امروز</h2></div>");
        sb.AppendLine("<div class='section-body'>");
        if (topToday.Count == 0)
        {
            sb.AppendLine("<div style='text-align:center;color:#94A3B8;padding:20px;'>امروز فروشی ثبت نشده</div>");
        }
        else
        {
            sb.AppendLine("<table><thead><tr><th>رتبه</th><th>نام کالا</th><th>تعداد</th><th>سود</th></tr></thead><tbody>");
            int idx = 1;
            foreach (var t in topToday)
            {
                // escape نام و واحد کالا برای جلوگیری از XSS
                sb.AppendLine($"<tr><td>{PersianNumber.ToPersian(idx)}</td><td class='name'>{HtmlEncoder.Encode(t.Name)}</td><td class='text-accent'>{PersianNumber.ToPersian(t.Qty)} {HtmlEncoder.Encode(t.Unit)}</td><td class='text-success'>{PersianNumber.ToToman(t.Profit)}</td></tr>");
                idx++;
            }
            sb.AppendLine("</tbody></table>");
        }
        sb.AppendLine("</div></div>");

        sb.AppendLine("<div class='section'>");
        sb.AppendLine("<div class='section-header'><h2>آخرین فروش‌ها</h2></div>");
        sb.AppendLine("<div class='section-body'>");
        if (recentSales.Count == 0)
        {
            sb.AppendLine("<div style='text-align:center;color:#94A3B8;padding:20px;'>فروشی ثبت نشده</div>");
        }
        else
        {
            sb.AppendLine("<table><thead><tr><th>#</th><th>تاریخ</th><th>کالا</th><th>تعداد</th><th>درآمد</th><th>پرداخت</th><th>سود</th></tr></thead><tbody>");
            int idx = 1;
            foreach (var s in recentSales)
            {
                var item = itemsDict.GetValueOrDefault(s.ItemId);
                var payType = s.PaymentStatus switch
                {
                    PaymentStatus.Cash => "نقدی",
                    // escape شماره پایانه برای جلوگیری از XSS
                    PaymentStatus.Card => string.IsNullOrWhiteSpace(s.CardTerminal) ? "کارتی" : $"کارتی ({HtmlEncoder.Encode(s.CardTerminal)})",
                    PaymentStatus.Credit => "نسیه",
                    _ => "—"
                };
                var payClass = s.PaymentStatus switch
                {
                    PaymentStatus.Cash => "text-success",
                    PaymentStatus.Card => "text-accent",
                    PaymentStatus.Credit => "text-warning",
                    _ => ""
                };
                // escape تاریخ و نام کالا برای جلوگیری از XSS
                sb.AppendLine($"<tr><td>{PersianNumber.ToPersian(idx)}</td><td>{HtmlEncoder.Encode(PersianNumber.ToPersianDigits(s.DateShamsi))}</td><td class='name'>{HtmlEncoder.Encode(item?.Name ?? "—")}</td><td class='text-accent'>{PersianNumber.ToPersian(s.Qty)}</td><td class='text-success'>{PersianNumber.ToToman(s.Revenue)}</td><td class='{payClass}'>{payType}</td><td class='text-success'>{PersianNumber.ToToman(s.Profit)}</td></tr>");
                idx++;
            }
            sb.AppendLine("</tbody></table>");
        }
        sb.AppendLine("</div></div>");

        sb.AppendLine("<div class='footer'>گزارش خودکار از سیستم مدیریت فروشگاه</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</body></html>");

        OpenPrintPreview(sb.ToString(), "dashboard-overview");
    }

    private void PrintReports()
    {
        using var db = DatabaseService.CreateContext();

        var allSales = db.Sales.Where(s => s.EntryType == EntryType.Normal).ToList();
        var filtered = allSales.AsEnumerable();

        var fromText = PersianNumber.ToEnglishDigits(FromDateBox.Text ?? "").Trim();
        var toText = PersianNumber.ToEnglishDigits(ToDateBox.Text ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(fromText))
        {
            try
            {
                var fromDate = JalaliDate.ToGregorian(fromText);
                filtered = filtered.Where(s => s.DateGregorian.Date >= fromDate.Date);
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(toText))
        {
            try
            {
                var toDate = JalaliDate.ToGregorian(toText);
                filtered = filtered.Where(s => s.DateGregorian.Date <= toDate.Date);
            }
            catch { }
        }

        var sales = filtered.ToList();

        var totalRevenue = sales.Sum(s => s.Revenue);
        var totalProfit = sales.Sum(s => s.Profit);
        var salesCount = sales.Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count();
        var avgProfit = salesCount > 0 ? totalProfit / salesCount : 0;

        var cashTotal = sales.Where(s => s.PaymentStatus == PaymentStatus.Cash).Sum(s => s.Revenue);
        var cardTotal = sales.Where(s => s.PaymentStatus == PaymentStatus.Card).Sum(s => s.Revenue);
        var creditTotal = sales.Where(s => s.PaymentStatus == PaymentStatus.Credit).Sum(s => s.Revenue);
        var totalAll = cashTotal + cardTotal + creditTotal;
        var avgInvoice = salesCount > 0 ? totalAll / salesCount : 0;

        var cashPct = totalAll > 0 ? (cashTotal / totalAll) * 100 : 0;
        var cardPct = totalAll > 0 ? (cardTotal / totalAll) * 100 : 0;
        var creditPct = totalAll > 0 ? (creditTotal / totalAll) * 100 : 0;

        var cardSalesInRange = sales.Where(s => s.PaymentStatus == PaymentStatus.Card).ToList();
        var terminalInRange = cardSalesInRange
            .GroupBy(s => string.IsNullOrWhiteSpace(s.CardTerminal) ? "بدون پایانه" : s.CardTerminal!)
            .Select(g => new
            {
                Terminal = g.Key,
                Revenue = g.Sum(s => s.Revenue),
                Count = g.Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count()
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();

        var itemsDict = db.Items.ToDictionary(i => i.Id);
        var customersDict = db.Customers.ToDictionary(c => c.Id);

        var topSelling = sales
            .GroupBy(s => s.ItemId)
            .Select(g => new
            {
                Name = itemsDict.GetValueOrDefault(g.Key)?.Name ?? "—",
                Unit = itemsDict.GetValueOrDefault(g.Key)?.Unit ?? "",
                Qty = g.Sum(s => s.Qty),
                Revenue = g.Sum(s => s.Revenue)
            })
            .OrderByDescending(x => x.Qty)
            .Take(10)
            .ToList();

        var topProfit = sales
            .GroupBy(s => s.ItemId)
            .Select(g => new
            {
                Name = itemsDict.GetValueOrDefault(g.Key)?.Name ?? "—",
                Unit = itemsDict.GetValueOrDefault(g.Key)?.Unit ?? "",
                Profit = g.Sum(s => s.Profit),
                Revenue = g.Sum(s => s.Revenue)
            })
            .OrderByDescending(x => x.Profit)
            .Take(10)
            .ToList();

        var topCustomers = sales
            .Where(s => s.CustomerId.HasValue)
            .GroupBy(s => s.CustomerId!.Value)
            .Select(g => new
            {
                Name = customersDict.GetValueOrDefault(g.Key)?.Name ?? "—",
                Phone = customersDict.GetValueOrDefault(g.Key)?.Phone ?? "—",
                Count = g.Select(s => s.InvoiceNumber ?? s.Id.ToString()).Distinct().Count(),
                Revenue = g.Sum(s => s.Revenue)
            })
            .OrderByDescending(x => x.Revenue)
            .Take(10)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html dir='rtl' lang='fa'><head><meta charset='UTF-8'>");
        sb.AppendLine("<title>گزارش آمار تفصیلی</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("* { font-family: Vazirmatn, Tahoma, sans-serif; box-sizing: border-box; }");
        sb.AppendLine("body { margin: 20px; background: #F5F7FA; color: #0F172A; font-size: 12px; }");
        sb.AppendLine(".container { max-width: 900px; margin: 0 auto; }");
        // هدر
        sb.AppendLine(".header { background: #0F172A; color: white; padding: 24px 30px; border-radius: 6px 6px 0 0; }");
        sb.AppendLine(".header h1 { margin: 0; font-size: 20px; font-weight: bold; }");
        sb.AppendLine(".header .meta { font-size: 11px; color: #94A3B8; margin-top: 6px; }");
        // بخش
        sb.AppendLine(".section { background: white; margin-bottom: 16px; border-radius: 6px; border: 1px solid #E2E8F0; overflow: hidden; }");
        sb.AppendLine(".section-header { padding: 14px 20px; border-bottom: 1px solid #E2E8F0; background: white; }");
        sb.AppendLine(".section-header h2 { margin: 0; font-size: 14px; font-weight: 600; color: #0F172A; }");
        sb.AppendLine(".section-body { padding: 20px; }");
        // KPI
        sb.AppendLine(".kpi-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; }");
        sb.AppendLine(".kpi { background: white; border: 1px solid #E2E8F0; border-radius: 6px; padding: 16px; border-right: 3px solid #1E40AF; }");
        sb.AppendLine(".kpi.success { border-right-color: #047857; }");
        sb.AppendLine(".kpi.warning { border-right-color: #B45309; }");
        sb.AppendLine(".kpi.danger { border-right-color: #B91C1C; }");
        sb.AppendLine(".kpi .label { font-size: 11px; color: #64748B; margin-bottom: 8px; font-weight: 600; }");
        sb.AppendLine(".kpi .value { font-size: 16px; font-weight: bold; color: #0F172A; }");
        sb.AppendLine(".kpi .sub { font-size: 10px; color: #94A3B8; margin-top: 4px; }");
        // جدول
        sb.AppendLine("table { width: 100%; border-collapse: collapse; font-size: 11px; }");
        sb.AppendLine("th { background: #F1F5F9; color: #0F172A; padding: 10px 8px; font-size: 11px; font-weight: 600; text-align: center; border-bottom: 1px solid #E2E8F0; }");
        sb.AppendLine("td { padding: 8px; border-bottom: 1px solid #F1F5F9; text-align: center; color: #334155; }");
        sb.AppendLine("td.name { text-align: right; font-weight: 600; color: #0F172A; }");
        sb.AppendLine("tr:last-child td { border-bottom: none; }");
        sb.AppendLine("tr:nth-child(even) td { background: #FAFBFC; }");
        sb.AppendLine("tr.total td { background: #F1F5F9; font-weight: bold; border-top: 2px solid #E2E8F0; }");
        // رنگ‌های وضعیت
        sb.AppendLine(".text-success { color: #047857; font-weight: 600; }");
        sb.AppendLine(".text-warning { color: #B45309; font-weight: 600; }");
        sb.AppendLine(".text-danger { color: #B91C1C; font-weight: 600; }");
        sb.AppendLine(".text-accent { color: #1E40AF; font-weight: 600; }");
        // فوتر
        sb.AppendLine(".footer { text-align: center; color: #94A3B8; font-size: 10px; margin-top: 24px; padding: 16px; }");
        // چاپ
        sb.AppendLine("@media print { @page { size: A4; margin: 10mm; } body { margin: 0; background: white; } .section { page-break-inside: avoid; } .header { border-radius: 0; } }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<div class='container'>");
        sb.AppendLine("<div class='header'>");
        sb.AppendLine($"<h1>{HtmlEncoder.Encode(string.IsNullOrWhiteSpace(StoreSettingsService.Current.StoreName) ? "فروشگاه" : StoreSettingsService.Current.StoreName)}</h1>");
        // escape بازه تاریخ ورودی کاربر برای جلوگیری از XSS
        sb.AppendLine($"<div class='meta'>گزارش آمار تفصیلی | بازه: از {HtmlEncoder.Encode(PersianNumber.ToPersianDigits(FromDateBox.Text ?? "—"))} تا {HtmlEncoder.Encode(PersianNumber.ToPersianDigits(ToDateBox.Text ?? "—"))} | تاریخ چاپ: {PersianNumber.ToPersianDigits(JalaliDate.ToShamsi(DateTime.Today))}</div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='section'>");
        sb.AppendLine("<div class='section-header'><h2>شاخص‌های کلی بازه</h2></div>");
        sb.AppendLine("<div class='section-body'>");
        sb.AppendLine("<div class='kpi-grid'>");
        sb.AppendLine($"<div class='kpi'><div class='label'>جمع درآمد</div><div class='value'>{PersianNumber.ToToman(totalRevenue)}</div></div>");
        sb.AppendLine($"<div class='kpi success'><div class='label'>جمع سود</div><div class='value'>{PersianNumber.ToToman(totalProfit)}</div></div>");
        sb.AppendLine($"<div class='kpi'><div class='label'>تعداد فروش</div><div class='value'>{PersianNumber.ToPersian(salesCount)} فاکتور</div></div>");
        sb.AppendLine($"<div class='kpi warning'><div class='label'>میانگین سود هر فروش</div><div class='value'>{PersianNumber.ToToman(avgProfit)}</div></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div></div>");

        sb.AppendLine("<div class='section'>");
        sb.AppendLine("<div class='section-header'><h2>تفکیک فروش — نقد / کارت / نسیه</h2></div>");
        sb.AppendLine("<div class='section-body'>");
        sb.AppendLine("<div class='kpi-grid'>");
        sb.AppendLine($"<div class='kpi success'><div class='label'>نقدی</div><div class='value'>{PersianNumber.ToToman(cashTotal)}</div><div class='sub'>{PersianNumber.ToPersian(decimal.Round(cashPct, 1))}٪ از کل</div></div>");
        sb.AppendLine($"<div class='kpi'><div class='label'>کارتی</div><div class='value'>{PersianNumber.ToToman(cardTotal)}</div><div class='sub'>{PersianNumber.ToPersian(decimal.Round(cardPct, 1))}٪ از کل</div></div>");
        sb.AppendLine($"<div class='kpi warning'><div class='label'>نسیه</div><div class='value'>{PersianNumber.ToToman(creditTotal)}</div><div class='sub'>{PersianNumber.ToPersian(decimal.Round(creditPct, 1))}٪ از کل</div></div>");
        sb.AppendLine($"<div class='kpi'><div class='label'>میانگین هر فاکتور</div><div class='value'>{PersianNumber.ToToman(avgInvoice)}</div><div class='sub'>از {PersianNumber.ToPersian(salesCount)} فاکتور</div></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div></div>");

        if (terminalInRange.Count > 0)
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine("<div class='section-header'><h2>فروش کارتی در بازه — تفکیک پایانه‌ها</h2></div>");
            sb.AppendLine("<div class='section-body'>");
            sb.AppendLine("<table><thead><tr><th style='text-align: right;'>پایانه</th><th>مبلغ</th><th>تعداد فاکتور</th><th>درصد</th></tr></thead><tbody>");
            var totalCardInRange = terminalInRange.Sum(t => t.Revenue);
            foreach (var t in terminalInRange)
            {
                var pct = totalCardInRange > 0 ? (t.Revenue / totalCardInRange) * 100 : 0;
                // escape نام پایانه برای جلوگیری از XSS
                sb.AppendLine($"<tr><td class='name'>{HtmlEncoder.Encode(t.Terminal)}</td><td class='text-accent'>{PersianNumber.ToToman(t.Revenue)}</td><td>{PersianNumber.ToPersian(t.Count)}</td><td>{PersianNumber.ToPersian(decimal.Round(pct, 1))}٪</td></tr>");
            }
            sb.AppendLine($"<tr class='total'><td class='name'>جمع کل</td><td class='text-accent'>{PersianNumber.ToToman(totalCardInRange)}</td><td>{PersianNumber.ToPersian(terminalInRange.Sum(t => t.Count))}</td><td>۱۰۰٪</td></tr>");
            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</div></div>");
        }

        sb.AppendLine("<div class='section'>");
        sb.AppendLine("<div class='section-header'><h2>پرفروش‌ترین کالاها</h2></div>");
        sb.AppendLine("<div class='section-body'>");
        if (topSelling.Count == 0)
        {
            sb.AppendLine("<div style='text-align:center;color:#94A3B8;padding:20px;'>داده‌ای وجود ندارد</div>");
        }
        else
        {
            sb.AppendLine("<table><thead><tr><th>رتبه</th><th>نام کالا</th><th>واحد</th><th>تعداد</th><th>درآمد</th></tr></thead><tbody>");
            int idx = 1;
            foreach (var t in topSelling)
            {
                // escape نام و واحد کالا برای جلوگیری از XSS
                sb.AppendLine($"<tr><td>{PersianNumber.ToPersian(idx)}</td><td class='name'>{HtmlEncoder.Encode(t.Name)}</td><td>{HtmlEncoder.Encode(t.Unit)}</td><td class='text-accent'>{PersianNumber.ToPersian(t.Qty)}</td><td class='text-success'>{PersianNumber.ToToman(t.Revenue)}</td></tr>");
                idx++;
            }
            sb.AppendLine("</tbody></table>");
        }
        sb.AppendLine("</div></div>");

        sb.AppendLine("<div class='section'>");
        sb.AppendLine("<div class='section-header'><h2>پرسودترین کالاها</h2></div>");
        sb.AppendLine("<div class='section-body'>");
        if (topProfit.Count == 0)
        {
            sb.AppendLine("<div style='text-align:center;color:#94A3B8;padding:20px;'>داده‌ای وجود ندارد</div>");
        }
        else
        {
            sb.AppendLine("<table><thead><tr><th>رتبه</th><th>نام کالا</th><th>واحد</th><th>سود</th><th>درآمد</th></tr></thead><tbody>");
            int idx = 1;
            foreach (var t in topProfit)
            {
                // escape نام و واحد کالا برای جلوگیری از XSS
                sb.AppendLine($"<tr><td>{PersianNumber.ToPersian(idx)}</td><td class='name'>{HtmlEncoder.Encode(t.Name)}</td><td>{HtmlEncoder.Encode(t.Unit)}</td><td class='text-success'>{PersianNumber.ToToman(t.Profit)}</td><td class='text-success'>{PersianNumber.ToToman(t.Revenue)}</td></tr>");
                idx++;
            }
            sb.AppendLine("</tbody></table>");
        }
        sb.AppendLine("</div></div>");

        sb.AppendLine("<div class='section'>");
        sb.AppendLine("<div class='section-header'><h2>بهترین مشتری‌ها</h2></div>");
        sb.AppendLine("<div class='section-body'>");
        if (topCustomers.Count == 0)
        {
            sb.AppendLine("<div style='text-align:center;color:#94A3B8;padding:20px;'>داده‌ای وجود ندارد</div>");
        }
        else
        {
            sb.AppendLine("<table><thead><tr><th>رتبه</th><th>نام مشتری</th><th>شماره تماس</th><th>تعداد خرید</th><th>جمع خرید</th></tr></thead><tbody>");
            int idx = 1;
            foreach (var c in topCustomers)
            {
                // escape نام و تلفن مشتری برای جلوگیری از XSS
                sb.AppendLine($"<tr><td>{PersianNumber.ToPersian(idx)}</td><td class='name'>{HtmlEncoder.Encode(c.Name)}</td><td class='text-accent'>{HtmlEncoder.Encode(PersianNumber.ToPersianDigits(c.Phone))}</td><td>{PersianNumber.ToPersian(c.Count)}</td><td class='text-success'>{PersianNumber.ToToman(c.Revenue)}</td></tr>");
                idx++;
            }
            sb.AppendLine("</tbody></table>");
        }
        sb.AppendLine("</div></div>");

        sb.AppendLine("<div class='footer'>گزارش خودکار از سیستم مدیریت فروشگاه</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</body></html>");

        OpenPrintPreview(sb.ToString(), "dashboard-reports");
    }

    private void OpenPrintPreview(string html, string prefix)
    {
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"{prefix}-{DateTime.Now:yyyyMMddHHmmss}.html");

        File.WriteAllText(tempPath, html, Encoding.UTF8);

        Process.Start(new ProcessStartInfo
        {
            FileName = tempPath,
            UseShellExecute = true
        });

        StatusText.Foreground = new SolidColorBrush(Color.Parse("#8B5CF6"));
        StatusText.Text = "✓ گزارش در مرورگر باز شد — Ctrl+P برای چاپ";
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        LoadDashboard();
        if (_reportsLoaded) LoadStats();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}