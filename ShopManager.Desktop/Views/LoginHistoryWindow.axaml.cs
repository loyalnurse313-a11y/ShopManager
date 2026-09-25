using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ShopManager.Desktop.Services;
using ShopManager.Desktop.ViewModels;
using ShopManager.Domain.Helpers;

// ═══ رفع تداخل با DocumentFormat.OpenXml ═══
using Color = Avalonia.Media.Color;

namespace ShopManager.Desktop.Views;

public partial class LoginHistoryWindow : Window
{
    private List<LoginHistoryRow> _allRows = new();

    public LoginHistoryWindow()
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

            var history = db.LoginHistories
                .OrderByDescending(h => h.LoginAt)
                .ToList();

            _allRows = history.Select((h, index) => new LoginHistoryRow
            {
                Id = h.Id,
                RowNumber = PersianNumber.ToPersian(index + 1),
                Username = h.Username,
                FullName = h.FullName,
                LoginAt = PersianNumber.ToPersianDigits(JalaliDate.ToShamsi(h.LoginAt)) + " " +
                          PersianNumber.ToPersianDigits(h.LoginAt.ToLocalTime().ToString("HH:mm")),
                LogoutAt = h.LogoutAt.HasValue
                    ? PersianNumber.ToPersianDigits(JalaliDate.ToShamsi(h.LogoutAt.Value)) + " " +
                      PersianNumber.ToPersianDigits(h.LogoutAt.Value.ToLocalTime().ToString("HH:mm"))
                    : "—",
                Duration = h.DurationSeconds > 0 ? GetDurationDisplay(h.DurationSeconds) : "—",
                Status = h.Status == "موفق" ? "✅ موفق" : "❌ ناموفق",
                IsSuccess = h.Status == "موفق",
                Note = h.Note ?? "—",
                RawLoginAt = h.LoginAt,
                RawUsername = h.Username,
                RawFullName = h.FullName
            }).ToList();

            ApplyFilters();
        }
        catch (Exception ex)
        {
            EmptyTitle.Text = $"خطا: {ex.Message}";
        }
    }

    private string GetDurationDisplay(int seconds)
    {
        if (seconds <= 0) return "—";
        var ts = TimeSpan.FromSeconds(seconds);

        if (ts.TotalHours >= 1)
            return $"{PersianNumber.ToPersian((int)ts.TotalHours)}:{(int)ts.Minutes:D2} ساعت";

        if (ts.TotalMinutes >= 1)
            return $"{PersianNumber.ToPersian(ts.Minutes)} دقیقه و {PersianNumber.ToPersian(ts.Seconds)} ثانیه";

        return $"{PersianNumber.ToPersian(ts.Seconds)} ثانیه";
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
                filtered = filtered.Where(r => r.RawLoginAt.Date >= fromDate.Date);
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(toText))
        {
            try
            {
                var toDate = JalaliDate.ToGregorian(toText);
                filtered = filtered.Where(r => r.RawLoginAt.Date <= toDate.Date);
            }
            catch { }
        }

        var query = SearchBox.Text?.Trim() ?? "";
        var normalized = Normalize(query);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            filtered = filtered.Where(r =>
                Normalize(r.RawUsername).Contains(normalized) ||
                Normalize(r.RawFullName).Contains(normalized));
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

        // ─── آمار ───
        var successCount = list.Count(r => r.IsSuccess);
        var failedCount = list.Count - successCount;
        var totalSeconds = list.Where(r => r.IsSuccess).Sum(r =>
        {
            try
            {
                using var db = DatabaseService.CreateContext();
                return 0;
            }
            catch { return 0; }
        });

        TotalCountText.Text = $"تعداد: {PersianNumber.ToPersian(list.Count)}";
        TotalCountBottomText.Text = PersianNumber.ToPersian(list.Count);
        SuccessCountText.Text = PersianNumber.ToPersian(successCount);
        FailedCountText.Text = PersianNumber.ToPersian(failedCount);

        // محاسبه جمع مدت
        using (var db = DatabaseService.CreateContext())
        {
            var ids = list.Select(r => r.Id).ToList();
            var records = db.LoginHistories.Where(h => ids.Contains(h.Id)).ToList();
            var totalSec = records.Where(h => h.Status == "موفق").Sum(h => h.DurationSeconds);

            if (totalSec > 0)
            {
                var ts = TimeSpan.FromSeconds(totalSec);
                if (ts.TotalHours >= 1)
                    TotalDurationText.Text = $"{PersianNumber.ToPersian((int)ts.TotalHours)} ساعت و {PersianNumber.ToPersian(ts.Minutes)} دقیقه";
                else
                    TotalDurationText.Text = $"{PersianNumber.ToPersian(ts.Minutes)} دقیقه";
            }
            else
            {
                TotalDurationText.Text = "—";
            }
        }
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

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        LoadData();
    }

    // ═══════════ خروجی اکسل ═══════════

    private async void OnExportExcelClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var fromText = PersianNumber.ToEnglishDigits(FromDateBox.Text ?? "").Trim();
            var toText = PersianNumber.ToEnglishDigits(ToDateBox.Text ?? "").Trim();

            using var db = DatabaseService.CreateContext();

            var query = db.LoginHistories.AsQueryable();

            if (!string.IsNullOrWhiteSpace(fromText))
            {
                try
                {
                    var fromDate = JalaliDate.ToGregorian(fromText);
                    query = query.Where(h => h.LoginAt >= fromDate);
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(toText))
            {
                try
                {
                    var toDate = JalaliDate.ToGregorian(toText);
                    query = query.Where(h => h.LoginAt <= toDate.AddDays(1));
                }
                catch { }
            }

            var records = query
                .OrderByDescending(h => h.LoginAt)
                .ToList();

            if (records.Count == 0)
            {
                EmptyTitle.Text = "داده‌ای برای خروجی وجود ندارد";
                return;
            }

            // ─── ساخت فایل اکسل ───
            var path = await GetSavePath();
            if (string.IsNullOrWhiteSpace(path)) return;

            SaveToExcel(path, records);

            EmptyTitle.Text = "✓ فایل اکسل با موفقیت ذخیره شد";
        }
        catch (Exception ex)
        {
            EmptyTitle.Text = $"خطا: {ex.Message}";
        }
    }

    private async System.Threading.Tasks.Task<string?> GetSavePath()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "ذخیره فایل اکسل",
            SuggestedFileName = $"سابقه-ورود-{JalaliDate.TodayShamsi().Replace('/', '-')}.xlsx",
            DefaultExtension = "xlsx",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Excel")
                {
                    Patterns = new[] { "*.xlsx" }
                }
            }
        });

        return file?.Path.LocalPath;
    }

    private void SaveToExcel(string filePath, List<ShopManager.Domain.Entities.LoginHistory> records)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var ws = workbook.Worksheets.Add("سابقه ورود");

        ws.RightToLeft = true;

        var headers = new[]
        {
            "ردیف", "نام کاربری", "نام کامل", "زمان ورود", "زمان خروج",
            "مدت حضور (ثانیه)", "وضعیت", "توضیحات"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#8B5CF6");
            cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
        }

        int row = 2;
        int idx = 1;

        foreach (var h in records)
        {
            ws.Cell(row, 1).Value = idx;
            ws.Cell(row, 2).Value = h.Username;
            ws.Cell(row, 3).Value = h.FullName;
            ws.Cell(row, 4).Value = h.LoginAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            ws.Cell(row, 5).Value = h.LogoutAt.HasValue
                ? h.LogoutAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : "—";
            ws.Cell(row, 6).Value = h.DurationSeconds;
            ws.Cell(row, 7).Value = h.Status;
            ws.Cell(row, 8).Value = h.Note ?? "";

            for (int col = 1; col <= 8; col++)
            {
                ws.Cell(row, col).Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                ws.Cell(row, col).Style.Border.OutsideBorderColor = ClosedXML.Excel.XLColor.FromHtml("#E2E8F0");
            }

            if (row % 2 == 0)
            {
                for (int col = 1; col <= 8; col++)
                {
                    ws.Cell(row, col).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#F8FAFC");
                }
            }

            row++;
            idx++;
        }

        ws.Columns().AdjustToContents();

        workbook.SaveAs(filePath);
    }

    // ═══════════ چاپ ═══════════

    private void OnPrintClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var rows = HistoryList.ItemsSource as IEnumerable<LoginHistoryRow>;
            if (rows == null) return;

            var list = rows.ToList();
            if (list.Count == 0)
            {
                EmptyTitle.Text = "داده‌ای برای چاپ وجود ندارد";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html dir='rtl' lang='fa'><head><meta charset='UTF-8'>");
            sb.AppendLine("<title>سابقه ورود کاربران</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("* { font-family: Vazirmatn, Tahoma, sans-serif; }");
            sb.AppendLine("body { margin: 20px; }");
            sb.AppendLine("h1 { text-align: center; color: #2C3E50; font-size: 18px; margin-bottom: 5px; }");
            sb.AppendLine("h2 { text-align: center; color: #8B5CF6; font-size: 14px; margin-top: 0; }");
            sb.AppendLine(".range { text-align: center; font-size: 12px; color: #475569; margin-bottom: 15px; }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; font-size: 11px; }");
            sb.AppendLine("th { background: #8B5CF6; color: white; padding: 8px; text-align: center; }");
            sb.AppendLine("td { padding: 6px 8px; border-bottom: 1px solid #E2E8F0; text-align: center; }");
            sb.AppendLine("tr:nth-child(even) { background: #F8FAFC; }");
            sb.AppendLine(".stats { background: #F3E8FF; padding: 12px; margin-top: 15px; text-align: center; font-size: 13px; color: #7C3AED; border-radius: 6px; }");
            sb.AppendLine("@media print { @page { size: A4; margin: 10mm; } body { margin: 0; } }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>فروشگاه ظروف یکبار مصرف خوی</h1>");
            sb.AppendLine("<h2>سابقه ورود و خروج کاربران</h2>");
            sb.AppendLine($"<div class='range'>از: {PersianNumber.ToPersianDigits(FromDateBox.Text ?? "—")} — تا: {PersianNumber.ToPersianDigits(ToDateBox.Text ?? "—")}</div>");
            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th>#</th><th>نام کاربری</th><th>نام کامل</th><th>زمان ورود</th><th>زمان خروج</th><th>مدت</th><th>وضعیت</th>");
            sb.AppendLine("</tr></thead><tbody>");

            int idx = 1;
            foreach (var r in list)
            {
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td>{PersianNumber.ToPersian(idx)}</td>");
                sb.AppendLine($"<td style='font-weight:600;color:#4F46E5;'>{r.Username}</td>");
                sb.AppendLine($"<td>{r.FullName}</td>");
                sb.AppendLine($"<td>{r.LoginAt}</td>");
                sb.AppendLine($"<td>{r.LogoutAt}</td>");
                sb.AppendLine($"<td style='font-weight:600;'>{r.Duration}</td>");
                sb.AppendLine($"<td>{r.Status}</td>");
                sb.AppendLine("</tr>");
                idx++;
            }

            sb.AppendLine("</tbody></table>");

            var successCount = list.Count(r => r.IsSuccess);
            var failedCount = list.Count - successCount;

            sb.AppendLine($"<div class='stats'>جمع کل: {PersianNumber.ToPersian(list.Count)} — موفق: {PersianNumber.ToPersian(successCount)} — ناموفق: {PersianNumber.ToPersian(failedCount)}</div>");
            sb.AppendLine("</body></html>");

            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"login-history-{DateTime.Now:yyyyMMddHHmmss}.html");

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