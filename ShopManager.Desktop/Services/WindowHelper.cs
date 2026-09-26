using Avalonia.Controls;
using System;

namespace ShopManager.Desktop.Services;

/// <summary>
/// کمک‌کننده برای تنظیم اندازه پنجره‌ها
/// مطمئن می‌شه پنجره در محدوده مانیتور باز بشه
/// </summary>
public static class WindowHelper
{
    /// <summary>
    /// باز کردن پنجره به صورت فول‌اسکرین با رعایت محدوده مانیتور
    /// </summary>
    public static void OpenMaximized(Window window)
    {
        try
        {
            // تنظیم حالت Max
            window.WindowState = WindowState.Maximized;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // وقتی پنجره باز شد، در محدوده مانیتور قرار بگیره
            window.Opened += (sender, e) =>
            {
                try
                {
                    var screen = window.Screens.ScreenFromWindow(window);
                    if (screen != null)
                    {
                        var workingArea = screen.WorkingArea;
                        var scaling = screen.Scaling;

                        // تبدیل pixel به logical
                        var widthLogical = workingArea.Width / scaling;
                        var heightLogical = workingArea.Height / scaling;

                        window.Width = widthLogical;
                        window.Height = heightLogical;

                        // مطمئن شو در محدوده مانیتور هست
                        window.Position = new Avalonia.PixelPoint(
                            workingArea.X,
                            workingArea.Y
                        );
                    }
                }
                catch { }
            };
        }
        catch { }
    }
}
