using System;
using System.IO;
using System.Text;

namespace ShopManager.Desktop.Services;

/// <summary>
/// مدیریت خطا و لاگ‌گیری
/// در حالت Debug پیام کامل، در حالت Release پیام عمومی
/// </summary>
public static class ErrorHandler
{
    /// <summary>پوشه لاگ‌ها</summary>
    private static string LogFolder
    {
        get
        {
            var folder = Path.Combine(DatabaseService.DataFolder, "logs");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>مسیر فایل لاگ امروز</summary>
    private static string LogPath
    {
        get
        {
            var name = $"errors-{DateTime.Now:yyyy-MM-dd}.log";
            return Path.Combine(LogFolder, name);
        }
    }

    /// <summary>
    /// پیام مناسب برای نمایش به کاربر
    /// در Debug: پیام کامل با InnerException
    /// در Release: پیام عمومی
    /// </summary>
    public static string GetUserMessage(Exception ex, string context = "")
    {
#if DEBUG
        var sb = new StringBuilder();
        sb.Append("خطا: ");
        sb.Append(ex.Message);

        var inner = ex.InnerException;
        int depth = 0;
        while (inner != null && depth < 5)
        {
            sb.Append("\n  ↳ ");
            sb.Append(inner.Message);
            inner = inner.InnerException;
            depth++;
        }

        return sb.ToString();
#else
        _ = context;
        return "خطایی رخ داد. لطفاً دوباره تلاش کنید.";
#endif
    }

    /// <summary>
    /// لاگ کردن خطا در فایل
    /// </summary>
    public static void LogError(Exception ex, string context = "")
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════");
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
            sb.AppendLine($"Context: {context}");
            sb.AppendLine($"Type: {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");

            var inner = ex.InnerException;
            int depth = 1;
            while (inner != null && depth < 5)
            {
                sb.AppendLine($"  Inner {depth}: {inner.GetType().FullName}");
                sb.AppendLine($"  Inner {depth} Message: {inner.Message}");
                inner = inner.InnerException;
                depth++;
            }

            sb.AppendLine("StackTrace:");
            sb.AppendLine(ex.StackTrace);
            sb.AppendLine("═══════════════════════════════════════════════════");
            sb.AppendLine();

            File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);

            System.Diagnostics.Debug.WriteLine(sb.ToString());
        }
        catch
        {
            // اگه لاگ نشد، مهم نیست
        }
    }
}
