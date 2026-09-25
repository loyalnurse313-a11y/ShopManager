using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShopManager.Desktop.Services;
using ShopManager.Desktop.ViewModels;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;

namespace ShopManager.Desktop.Views;

public partial class PurchaseHistoryWindow : Window
{
    private List<PurchaseHistoryRow> _allRows = new();

    public PurchaseHistoryWindow()
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

            var purchases = db.Purchases
                .Where(p => p.EntryType == EntryType.Normal)
                .OrderByDescending(p => p.DateGregorian)
                .ThenByDescending(p => p.Id)
                .ToList();

            var itemsDict = db.Items.ToDictionary(i => i.Id);

            _allRows = purchases.Select(p =>
            {
                var item = itemsDict.GetValueOrDefault(p.ItemId);

                return new PurchaseHistoryRow
                {
                    Id = p.Id,
                    ItemId = p.ItemId,
                    DateShamsi = PersianNumber.ToPersianDigits(p.DateShamsi),
                    ItemCode = item != null ? PersianNumber.ToPersian(item.ItemCode) : "—",
                    ItemName = item?.Name ?? "—",
                    Unit = item?.Unit ?? "—",
                    Qty = PersianNumber.ToPersian(p.Qty),
                    UnitCost = PersianNumber.ToToman(p.UnitCost),
                    TotalCost = PersianNumber.ToToman(p.TotalCost),
                    PaymentType = p.PaymentStatus == PaymentStatus.Cash ? "نقدی" : "نسیه",
                    SupplierNote = p.SupplierNote ?? "",
                    RawTotalCost = p.TotalCost,
                    RawIsCash = p.PaymentStatus == PaymentStatus.Cash,
                    RawDateGregorian = p.DateGregorian
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
                Normalize(r.ItemCode).Contains(normalized) ||
                Normalize(r.SupplierNote).Contains(normalized));
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

        var grandTotal = list.Sum(r => r.RawTotalCost);
        var cashTotal = list.Where(r => r.RawIsCash).Sum(r => r.RawTotalCost);
        var creditTotal = list.Where(r => !r.RawIsCash).Sum(r => r.RawTotalCost);

        TotalCountText.Text = PersianNumber.ToPersian(list.Count);
        TotalAmountText.Text = PersianNumber.ToToman(grandTotal);
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

    private void OnApplyFilterClick(object? sender, RoutedEventArgs e)
    {
        ApplyFilters();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyFilters();
    }

    /// <summary>خروجی اکسل از سابقه خرید</summary>
    private async void OnExportExcelClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var fromText = PersianNumber.ToEnglishDigits(FromDateBox.Text ?? "").Trim();
            var toText = PersianNumber.ToEnglishDigits(ToDateBox.Text ?? "").Trim();

            var query = db.Purchases
                .Where(p => p.EntryType == EntryType.Normal)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(fromText))
            {
                try
                {
                    var fromDate = JalaliDate.ToGregorian(fromText);
                    query = query.Where(p => p.DateGregorian >= fromDate);
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(toText))
            {
                try
                {
                    var toDate = JalaliDate.ToGregorian(toText);
                    query = query.Where(p => p.DateGregorian <= toDate.AddDays(1));
                }
                catch { }
            }

            var purchases = query
                .OrderByDescending(p => p.DateGregorian)
                .ThenByDescending(p => p.Id)
                .ToList();

            var itemsDict = db.Items.ToDictionary(i => i.Id);

            if (purchases.Count == 0)
            {
                EmptyTitle.Text = "داده‌ای برای خروجی وجود ندارد";
                return;
            }

            await ExcelExportHelper.ExportPurchasesAsync(this, purchases, itemsDict);

            EmptyTitle.Text = "✓ فایل اکسل با موفقیت ذخیره شد";
        }
        catch (Exception ex)
        {
            EmptyTitle.Text = $"خطا: {ex.Message}";
        }
    }

    /// <summary>چاپ گزارش سابقه خرید</summary>
    private void OnPrintClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var fromText = PersianNumber.ToEnglishDigits(FromDateBox.Text ?? "").Trim();
            var toText = PersianNumber.ToEnglishDigits(ToDateBox.Text ?? "").Trim();

            var query = db.Purchases
                .Where(p => p.EntryType == EntryType.Normal)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(fromText))
            {
                try
                {
                    var fromDate = JalaliDate.ToGregorian(fromText);
                    query = query.Where(p => p.DateGregorian >= fromDate);
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(toText))
            {
                try
                {
                    var toDate = JalaliDate.ToGregorian(toText);
                    query = query.Where(p => p.DateGregorian <= toDate.AddDays(1));
                }
                catch { }
            }

            var purchases = query
                .OrderBy(p => p.DateGregorian)
                .ThenBy(p => p.Id)
                .ToList();

            var itemsDict = db.Items.ToDictionary(i => i.Id);

            if (purchases.Count == 0)
            {
                EmptyTitle.Text = "داده‌ای برای چاپ وجود ندارد";
                return;
            }

            // ─── ساخت HTML ───
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html dir='rtl' lang='fa'><head><meta charset='UTF-8'>");
            sb.AppendLine("<title>سابقه خرید</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("* { font-family: Vazirmatn, Tahoma, sans-serif; }");
            sb.AppendLine("body { margin: 20px; }");
            sb.AppendLine("h1 { text-align: center; color: #2C3E50; font-size: 18px; margin-bottom: 5px; }");
            sb.AppendLine("h2 { text-align: center; color: #4F46E5; font-size: 14px; margin-top: 0; margin-bottom: 10px; }");
            sb.AppendLine(".range { text-align: center; font-size: 12px; color: #475569; margin-bottom: 15px; }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; font-size: 12px; }");
            sb.AppendLine("th { background: #4F46E5; color: white; padding: 8px; text-align: center; }");
            sb.AppendLine("td { padding: 6px 8px; border-bottom: 1px solid #E2E8F0; text-align: center; }");
            sb.AppendLine("tr:nth-child(even) { background: #F8FAFC; }");
            sb.AppendLine(".total { background: #ECFDF5; font-weight: bold; padding: 12px; margin-top: 15px; text-align: center; font-size: 14px; color: #059669; border-radius: 6px; }");
            sb.AppendLine("@media print { @page { size: A4; margin: 10mm; } body { margin: 0; } }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>فروشگاه ظروف یکبار مصرف خوی گلاست</h1>");
            sb.AppendLine("<h2>سابقه خرید از تأمین‌کننده</h2>");
            sb.AppendLine($"<div class='range'>از تاریخ: {PersianNumber.ToPersianDigits(FromDateBox.Text ?? "—")} — تا تاریخ: {PersianNumber.ToPersianDigits(ToDateBox.Text ?? "—")}</div>");
            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th>#</th>");
            sb.AppendLine("<th>تاریخ</th>");
            sb.AppendLine("<th>کد</th>");
            sb.AppendLine("<th>نام کالا</th>");
            sb.AppendLine("<th>واحد</th>");
            sb.AppendLine("<th>تعداد</th>");
            sb.AppendLine("<th>قیمت واحد</th>");
            sb.AppendLine("<th>جمع</th>");
            sb.AppendLine("<th>پرداخت</th>");
            sb.AppendLine("</tr></thead><tbody>");

            decimal total = 0;
            int idx = 1;
            foreach (var p in purchases)
            {
                var item = itemsDict.GetValueOrDefault(p.ItemId);
                total += p.TotalCost;

                sb.AppendLine("<tr>");
                sb.AppendLine($"<td>{PersianNumber.ToPersian(idx)}</td>");
                sb.AppendLine($"<td>{PersianNumber.ToPersianDigits(p.DateShamsi)}</td>");
                sb.AppendLine($"<td>{PersianNumber.ToPersian(item?.ItemCode ?? 0)}</td>");
                sb.AppendLine($"<td style='text-align:right; font-weight:600;'>{item?.Name ?? "—"}</td>");
                sb.AppendLine($"<td>{item?.Unit ?? "—"}</td>");
                sb.AppendLine($"<td>{PersianNumber.ToPersian(p.Qty)}</td>");
                sb.AppendLine($"<td>{PersianNumber.ToPersian(p.UnitCost)}</td>");
                sb.AppendLine($"<td style='color:#059669;font-weight:bold;'>{PersianNumber.ToPersian(p.TotalCost)}</td>");
                sb.AppendLine($"<td>{(p.PaymentStatus == PaymentStatus.Cash ? "نقدی" : "نسیه")}</td>");
                sb.AppendLine("</tr>");
                idx++;
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine($"<div class='total'>جمع کل خرید ({PersianNumber.ToPersian(purchases.Count)} قلم): {PersianNumber.ToToman(total)}</div>");
            sb.AppendLine("</body></html>");

            // ─── ذخیره و باز کردن در مرورگر ───
            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"purchase-history-{DateTime.Now:yyyyMMddHHmmss}.html");

            File.WriteAllText(tempPath, sb.ToString(), Encoding.UTF8);

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