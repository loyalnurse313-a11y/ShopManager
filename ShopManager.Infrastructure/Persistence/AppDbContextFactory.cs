using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShopManager.Infrastructure.Persistence;

/// <summary>
/// Factory برای ساخت DbContext — فقط برای Migration استفاده می‌شه
/// (وقتی EF Core می‌خواد دیتابیس رو بسازه، از این استفاده می‌کنه)
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // مسیر فایل دیتابیس: کنار فایل اجرایی برنامه
        var dbPath = Path.Combine(AppContext.BaseDirectory, "shop.db");
        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new AppDbContext(optionsBuilder.Options);
    }
}