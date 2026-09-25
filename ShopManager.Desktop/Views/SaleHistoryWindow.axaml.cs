using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ShopManager.Desktop.Services;
using ShopManager.Desktop.ViewModels;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;

namespace ShopManager.Desktop.Views;

public partial class SaleHistoryWindow : Window
{
    private List<SaleHistoryRow> _allRows = new();

    public SaleHistoryWindow()
    {
        InitializeComponent();

        SetMonthRange();
        LoadData();
    }

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

    private void LoadData()
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var sales = db.Sales
                .Where(s => s.EntryType == EntryType.Normal)
                .OrderByDescending(s => s.DateGregorian)
                .ThenByDescending(s => s.Id)
                .ToList();

            var itemsDict = db.Items.ToDictionary(i => i.Id);

            _allRows = sales.Select(s =>
            {
                var item = itemsDict.GetValueOrDefault(s.ItemId);

                // 🆕 ساخت متن نوع پرداخت با پایانه
                var payType = s.PaymentStatus switch
                {
                    PaymentStatus.Cash => "نقدی",
                    PaymentStatus.Card => string.IsNullOrWhiteSpace(s.CardTerminal)
                        ? "کارتی"
                        : $"کارتی ({s.CardTerminal})",
                    PaymentStatus.Credit => "نسیه",
                    _ => "—"
                };

                return new SaleHistoryRow
                {
                    Id = s.Id,
                    ItemId = s.ItemId,
                    DateShamsi = PersianNumber.ToPersianDigits(s.DateShamsi),
                    ItemCode = item != null ? PersianNumber.ToPersian(item.ItemCode) : "—",
                    ItemName = item?.Name ?? "—",
                    Unit = item?.Unit ?? "—",
                    Qty = PersianNumber.ToPersian(s.Qty),
                    SaleUnitPrice = PersianNumber.ToToman(s.SaleUnitPrice),
                    Revenue = PersianNumber.ToToman(s.Revenue),
                    LockedCost = PersianNumber.ToToman(s.LockedUnitCost),
                    Profit = PersianNumber.ToToman(s.Profit),
                    PaymentType = payType,
                    RawRevenue = s.Revenue,
                    RawProfit = s.Profit,
                    RawQty = s.Qty,
                    RawIsCash = s.PaymentStatus == PaymentStatus.Cash,
                    RawDateGregorian = s.DateGregorian
                };
            }).ToList();

            ApplyFilters();
        }
        catch (Exception ex)
        {
            EmptyTitle.Text = $"خطا: {ex.Message}";
        }
    }

    private void ApplyFilters()
    {
        var filtered = _allRows.AsEnumerable();

        var fromText = PersianNumber.ToEnglishDigits(FromDateBox.Text ?? "").Trim();
        var toText = PersianNumber.ToEnglishDigits(ToDateBox.Text ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(fromText))
        {
            try
            {
                var fromDate = JalaliDate.ToGregorian(fromText);
                filtered = filtered.Where(r => r.RawDateGregorian.Date >= fromDate.Date);
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(toText))
        {
            try
            {
                var toDate = JalaliDate.ToGregorian(toText);
                filtered = filtered.Where(r => r.RawDateGregorian.Date <= toDate.Date);
            }
            catch { }
        }

        var query = SearchBox.Text?.Trim() ?? "";
        var normalized = Normalize(query);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            filtered = filtered.Where(r =>
                Normalize(r.ItemName).Contains(normalized) ||
                Normalize(r.ItemCode).Contains(normalized));
        }

        var list = filtered.ToList();

        HistoryList.ItemsSource = null;
        HistoryList.ItemsSource = list;

        if (list.Count == 0)
        {
            EmptyPanel.IsVisible = true;
            HistoryScrollViewer.IsVisible = false;
        }
        else
        {
            EmptyPanel.IsVisible = false;
            HistoryScrollViewer.IsVisible = true;
        }

        var grandRevenue = list.Sum(r => r.RawRevenue);
        var grandProfit = list.Sum(r => r.RawProfit);
        var cashTotal = list.Where(r => r.RawIsCash).Sum(r => r.RawRevenue);
        var creditTotal = list.Where(r => !r.RawIsCash).Sum(r => r.RawRevenue);

        TotalCountText.Text = PersianNumber.ToPersian(list.Count);
        TotalRevenueText.Text = PersianNumber.ToToman(grandRevenue);
        TotalProfitText.Text = PersianNumber.ToToman(grandProfit);
        CashTotalText.Text = PersianNumber.ToToman(cashTotal);
        CreditTotalText.Text = PersianNumber.ToToman(creditTotal);
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

    private void OnTodayClick(object? sender, RoutedEventArgs e)
    {
        var today = JalaliDate.TodayShamsi();
        FromDateBox.Text = today;
        ToDateBox.Text = today;
        ApplyFilters();
    }

    private void OnWeekClick(object? sender, RoutedEventArgs e)
    {
        var today = JalaliDate.TodayGregorian();
        var weekAgo = today.AddDays(-7);
        FromDateBox.Text = JalaliDate.ToShamsi(weekAgo);
        ToDateBox.Text = JalaliDate.ToShamsi(today);
        ApplyFilters();
    }

    private void OnMonthClick(object? sender, RoutedEventArgs e)
    {
        SetMonthRange();
        ApplyFilters();
    }

    private void OnAllClick(object? sender, RoutedEventArgs e)
    {
        FromDateBox.Text = "";
        ToDateBox.Text = "";
        SearchBox.Text = "";
        ApplyFilters();
    }

    private void OnApplyFilterClick(object? sender, RoutedEventArgs e) => ApplyFilters();

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilters();

    private async void OnExportExcelClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var fromText = PersianNumber.ToEnglishDigits(FromDateBox.Text ?? "").Trim();
            var toText = PersianNumber.ToEnglishDigits(ToDateBox.Text ?? "").Trim();

            var query = db.Sales
                .Where(s => s.EntryType == EntryType.Normal)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(fromText))
            {
                try
                {
                    var fromDate = JalaliDate.ToGregorian(fromText);
                    query = query.Where(s => s.DateGregorian >= fromDate);
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(toText))
            {
                try
                {
                    var toDate = JalaliDate.ToGregorian(toText);
                    query = query.Where(s => s.DateGregorian <= toDate.AddDays(1));
                }
                catch { }
            }

            var sales = query
                .OrderByDescending(s => s.DateGregorian)
                .ThenByDescending(s => s.Id)
                .ToList();

            var itemsDict = db.Items.ToDictionary(i => i.Id);

            if (sales.Count == 0)
            {
                EmptyTitle.Text = "داده‌ای برای خروجی وجود ندارد";
                return;
            }

            await ExcelExportHelper.ExportSalesAsync(this, sales, itemsDict);

            EmptyTitle.Text = "✓ فایل اکسل با موفقیت ذخیره شد";
        }
        catch (Exception ex)
        {
            EmptyTitle.Text = $"خطا: {ex.Message}";
        }
    }

    private void OnPrintClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var fromText = PersianNumber.ToEnglishDigits(FromDateBox.Text ?? "").Trim();
            var toText = PersianNumber.ToEnglishDigits(ToDateBox.Text ?? "").Trim();

            var query = db.Sales
                .Where(s => s.EntryType == EntryType.Normal)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(fromText))
            {
                try
                {
                    var fromDate = JalaliDate.ToGregorian(fromText);
                    query = query.Where(s => s.DateGregorian >= fromDate);
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(toText))
            {
                try
                {
                    var toDate = JalaliDate.ToGregorian(toText);
                    query = query.Where(s => s.DateGregorian <= toDate.AddDays(1));
                }
                catch { }
            }

            var sales = query.OrderBy(s => s.DateGregorian).ThenBy(s => s.Id).ToList();
            var itemsDict = db.Items.ToDictionary(i => i.Id);

            if (sales.Count == 0)
            {
                EmptyTitle.Text = "داده‌ای برای چاپ وجود ندارد";
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html dir='rtl' lang='fa'><head><meta charset='UTF-8'>");
            sb.AppendLine("<title>سابقه فروش</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("* { font-family: Vazirmatn, Tahoma, sans-serif; }");
            sb.AppendLine("body { margin: 20px; }");
            sb.AppendLine("h1 { text-align: center; color: #2C3E50; font-size: 18px; }");
            sb.AppendLine("h2 { text-align: center; color: #10B981; font-size: 14px; margin-top: 5px; }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 15px; font-size: 11px; }");
            sb.AppendLine("th { background: #10B981; color: white; padding: 8px; text-align: center; }");
            sb.AppendLine("td { padding: 6px 8px; border-bottom: 1px solid #E2E8F0; text-align: center; }");
            sb.AppendLine("tr:nth-child(even) { background: #F8FAFC; }");
            sb.AppendLine(".total { background: #ECFDF5; font-weight: bold; padding: 12px; margin-top: 15px; text-align: center; font-size: 14px; color: #059669; }");
            sb.AppendLine("@media print { @page { size: A4; margin: 10mm; } body { margin: 0; } }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>فروشگاه ظروف یکبار مصرف خوی </h1>");
            sb.AppendLine("<h2>سابقه فروش روزانه</h2>");
            sb.AppendLine($"<p style='text-align:center; font-size: 12px;'>از: {FromDateBox.Text} — تا: {ToDateBox.Text}</p>");
            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th>#</th><th>تاریخ</th><th>فاکتور</th><th>نام کالا</th><th>تعداد</th><th>قیمت</th><th>درآمد</th><th>سود</th><th>پرداخت</th>");
            sb.AppendLine("</tr></thead><tbody>");

            decimal totalRev = 0, totalProfit = 0;
            int idx = 1;
            foreach (var s in sales)
            {
                var item = itemsDict.GetValueOrDefault(s.ItemId);
                totalRev += s.Revenue;
                totalProfit += s.Profit;

                var payType = s.PaymentStatus switch
                {
                    PaymentStatus.Cash => "نقدی",
                    PaymentStatus.Card => string.IsNullOrWhiteSpace(s.CardTerminal)
                        ? "کارتی"
                        : $"کارتی ({s.CardTerminal})",
                    PaymentStatus.Credit => "نسیه",
                    _ => "—"
                };

                sb.AppendLine("<tr>");
                sb.AppendLine($"<td>{PersianNumber.ToPersian(idx)}</td>");
                sb.AppendLine($"<td>{PersianNumber.ToPersianDigits(s.DateShamsi)}</td>");
                sb.AppendLine($"<td>{PersianNumber.ToPersianDigits(s.InvoiceNumber ?? "—")}</td>");
                sb.AppendLine($"<td style='text-align:right;'>{item?.Name ?? "—"}</td>");
                sb.AppendLine($"<td>{PersianNumber.ToPersian(s.Qty)}</td>");
                sb.AppendLine($"<td>{PersianNumber.ToPersian(s.SaleUnitPrice)}</td>");
                sb.AppendLine($"<td style='color:#059669;font-weight:bold;'>{PersianNumber.ToPersian(s.Revenue)}</td>");
                sb.AppendLine($"<td style='color:#10B981;font-weight:bold;'>{PersianNumber.ToPersian(s.Profit)}</td>");
                sb.AppendLine($"<td>{payType}</td>");
                sb.AppendLine("</tr>");
                idx++;
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine($"<div class='total'>جمع درآمد: {PersianNumber.ToToman(totalRev)} — جمع سود: {PersianNumber.ToToman(totalProfit)}</div>");
            sb.AppendLine("</body></html>");

            var tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"sale-history-{DateTime.Now:yyyyMMddHHmmss}.html");

            System.IO.File.WriteAllText(tempPath, sb.ToString(), System.Text.Encoding.UTF8);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });

            EmptyTitle.Text = "گزارش توی مرورگر باز شد — Ctrl+P برای چاپ";
        }
        catch (Exception ex)
        {
            EmptyTitle.Text = $"خطا: {ex.Message}";
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}