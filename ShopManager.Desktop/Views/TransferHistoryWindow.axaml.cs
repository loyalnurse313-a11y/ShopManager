using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ShopManager.Desktop.Services;
using ShopManager.Desktop.ViewModels;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;

using Color = Avalonia.Media.Color;

namespace ShopManager.Desktop.Views;

public partial class TransferHistoryWindow : Window
{
    private List<TransferHistoryRow> _allRows = new();

    public TransferHistoryWindow()
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

            var transfers = db.Transfers
                .Where(t => t.EntryType == EntryType.Normal)
                .OrderByDescending(t => t.DateGregorian)
                .ThenByDescending(t => t.Id)
                .ToList();

            var itemsDict = db.Items.ToDictionary(i => i.Id);

            _allRows = transfers.Select(t =>
            {
                var item = itemsDict.GetValueOrDefault(t.ItemId);

                return new TransferHistoryRow
                {
                    Id = t.Id,
                    ItemId = t.ItemId,
                    DateShamsi = PersianNumber.ToPersianDigits(t.DateShamsi),
                    ItemCode = item != null ? PersianNumber.ToPersian(item.ItemCode) : "—",
                    ItemName = item?.Name ?? "—",
                    Unit = item?.Unit ?? "—",
                    Qty = PersianNumber.ToPersian(t.Qty),
                    Note = t.Note ?? "—",
                    RawQty = t.Qty,
                    RawDateGregorian = t.DateGregorian
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

        var grandQty = list.Sum(r => r.RawQty);
        TotalCountText.Text = PersianNumber.ToPersian(list.Count);
        TotalQtyText.Text = PersianNumber.ToPersian(grandQty);
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

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => LoadData();

    // ═══════════ خروجی اکسل ═══════════

    private async void OnExportExcelClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var fromText = PersianNumber.ToEnglishDigits(FromDateBox.Text ?? "").Trim();
            var toText = PersianNumber.ToEnglishDigits(ToDateBox.Text ?? "").Trim();

            var query = db.Transfers
                .Where(t => t.EntryType == EntryType.Normal)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(fromText))
            {
                try
                {
                    var fromDate = JalaliDate.ToGregorian(fromText);
                    query = query.Where(t => t.DateGregorian >= fromDate);
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(toText))
            {
                try
                {
                    var toDate = JalaliDate.ToGregorian(toText);
                    query = query.Where(t => t.DateGregorian <= toDate.AddDays(1));
                }
                catch { }
            }

            var transfers = query
                .OrderByDescending(t => t.DateGregorian)
                .ThenByDescending(t => t.Id)
                .ToList();

            if (transfers.Count == 0)
            {
                EmptyTitle.Text = "داده‌ای برای خروجی وجود ندارد";
                return;
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "ذخیره فایل اکسل",
                SuggestedFileName = $"سابقه-انتقال-{JalaliDate.TodayShamsi().Replace('/', '-')}.xlsx",
                DefaultExtension = "xlsx",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Excel")
                    {
                        Patterns = new[] { "*.xlsx" }
                    }
                }
            });

            if (file == null) return;

            var path = file.Path.LocalPath;
            var itemsDict = db.Items.ToDictionary(i => i.Id);

            SaveToExcel(path, transfers, itemsDict);

            EmptyTitle.Text = "✓ فایل اکسل با موفقیت ذخیره شد";
        }
        catch (Exception ex)
        {
            EmptyTitle.Text = $"خطا: {ex.Message}";
        }
    }

    private void SaveToExcel(string filePath, List<Transfer> transfers, Dictionary<int, Item> itemsDict)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var ws = workbook.Worksheets.Add("سابقه انتقال");

        ws.RightToLeft = true;

        var headers = new[]
        {
            "ردیف", "تاریخ", "کد کالا", "نام کالا", "واحد", "تعداد", "توضیحات"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#F59E0B");
            cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
        }

        int row = 2;
        int idx = 1;

        foreach (var t in transfers)
        {
            var item = itemsDict.GetValueOrDefault(t.ItemId);

            ws.Cell(row, 1).Value = idx;
            ws.Cell(row, 2).Value = t.DateShamsi;
            ws.Cell(row, 3).Value = item?.ItemCode ?? 0;
            ws.Cell(row, 4).Value = item?.Name ?? "—";
            ws.Cell(row, 5).Value = item?.Unit ?? "—";
            ws.Cell(row, 6).Value = t.Qty;
            ws.Cell(row, 7).Value = t.Note ?? "";

            for (int col = 1; col <= 7; col++)
            {
                ws.Cell(row, col).Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                ws.Cell(row, col).Style.Border.OutsideBorderColor = ClosedXML.Excel.XLColor.FromHtml("#E2E8F0");
            }

            if (row % 2 == 0)
            {
                for (int col = 1; col <= 7; col++)
                {
                    ws.Cell(row, col).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#F8FAFC");
                }
            }

            row++;
            idx++;
        }

        // جمع کل
        var totalRow = row + 1;
        ws.Cell(totalRow, 5).Value = "جمع کل:";
        ws.Cell(totalRow, 5).Style.Font.Bold = true;
        ws.Cell(totalRow, 6).FormulaA1 = $"SUM(F2:F{row - 1})";
        ws.Cell(totalRow, 6).Style.Font.Bold = true;

        ws.Columns().AdjustToContents();

        workbook.SaveAs(filePath);
    }

    // ═══════════ چاپ ═══════════

    private void OnPrintClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var rows = HistoryList.ItemsSource as IEnumerable<TransferHistoryRow>;
            if (rows == null) return;

            var list = rows.ToList();
            if (list.Count == 0)
            {
                EmptyTitle.Text = "داده‌ای برای چاپ وجود ندارد";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html dir='rtl' lang='fa'><head><meta charset='UTF-8'>");
            sb.AppendLine("<title>سابقه انتقال</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("* { font-family: Vazirmatn, Tahoma, sans-serif; }");
            sb.AppendLine("body { margin: 20px; }");
            sb.AppendLine("h1 { text-align: center; color: #2C3E50; font-size: 18px; margin-bottom: 5px; }");
            sb.AppendLine("h2 { text-align: center; color: #F59E0B; font-size: 14px; margin-top: 0; }");
            sb.AppendLine(".range { text-align: center; font-size: 12px; color: #475569; margin-bottom: 15px; }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; font-size: 12px; }");
            sb.AppendLine("th { background: #F59E0B; color: white; padding: 8px; text-align: center; }");
            sb.AppendLine("td { padding: 6px 8px; border-bottom: 1px solid #E2E8F0; text-align: center; }");
            sb.AppendLine("tr:nth-child(even) { background: #FFFBEB; }");
            sb.AppendLine(".total { background: #FEF3C7; font-weight: bold; padding: 12px; margin-top: 15px; text-align: center; font-size: 14px; color: #D97706; border-radius: 6px; }");
            sb.AppendLine("@media print { @page { size: A4; margin: 10mm; } body { margin: 0; } }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>فروشگاه ظروف یکبار مصرف خوی</h1>");
            sb.AppendLine("<h2>سابقه انتقال انبار به مغازه</h2>");
            sb.AppendLine($"<div class='range'>از: {PersianNumber.ToPersianDigits(FromDateBox.Text ?? "—")} — تا: {PersianNumber.ToPersianDigits(ToDateBox.Text ?? "—")}</div>");
            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th>#</th><th>تاریخ</th><th>کد</th><th>نام کالا</th><th>واحد</th><th>تعداد</th><th>توضیحات</th>");
            sb.AppendLine("</tr></thead><tbody>");

            decimal totalQty = 0;
            int idx = 1;
            foreach (var r in list)
            {
                totalQty += r.RawQty;

                sb.AppendLine("<tr>");
                sb.AppendLine($"<td>{PersianNumber.ToPersian(idx)}</td>");
                sb.AppendLine($"<td>{r.DateShamsi}</td>");
                sb.AppendLine($"<td>{r.ItemCode}</td>");
                sb.AppendLine($"<td style='text-align:right;font-weight:600;'>{r.ItemName}</td>");
                sb.AppendLine($"<td>{r.Unit}</td>");
                sb.AppendLine($"<td style='font-weight:bold;color:#4F46E5;'>{r.Qty}</td>");
                sb.AppendLine($"<td style='font-size:11px;color:#94A3B8;'>{r.Note}</td>");
                sb.AppendLine("</tr>");
                idx++;
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine($"<div class='total'>جمع کل ({PersianNumber.ToPersian(list.Count)} قلم): {PersianNumber.ToPersian(totalQty)}</div>");
            sb.AppendLine("</body></html>");

            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"transfer-history-{DateTime.Now:yyyyMMddHHmmss}.html");

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