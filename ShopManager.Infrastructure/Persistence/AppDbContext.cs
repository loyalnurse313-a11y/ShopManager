using Microsoft.EntityFrameworkCore;
using ShopManager.Domain.Entities;

namespace ShopManager.Infrastructure.Persistence;

/// <summary>
/// DbContext اصلی برنامه — پل بین C# و دیتابیس SQLite
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // ─── جدول‌ها ───
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<CashLedger> CashLedgers => Set<CashLedger>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<User> Users => Set<User>();
    public DbSet<LoginHistory> LoginHistories => Set<LoginHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ─── Item ───
        modelBuilder.Entity<Item>(entity =>
        {
            entity.HasIndex(e => e.ItemCode).IsUnique();

            entity.Property(e => e.OpeningWarehouseQty).HasPrecision(18, 4);
            entity.Property(e => e.OpeningWarehouseUnitCost).HasPrecision(18, 4);
            entity.Property(e => e.OpeningShopQty).HasPrecision(18, 4);
            entity.Property(e => e.SalePrice).HasPrecision(18, 4);
            entity.Property(e => e.MarkupPct).HasPrecision(5, 4);
            entity.Property(e => e.LowStockCriticalPct).HasPrecision(5, 4);
            entity.Property(e => e.LowStockWarningPct).HasPrecision(5, 4);
            entity.Property(e => e.LowStockCriticalFixed).HasPrecision(18, 4);
            entity.Property(e => e.LowStockWarningFixed).HasPrecision(18, 4);

            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.Unit).IsRequired().HasMaxLength(50);
        });

        // ─── Purchase ───
        modelBuilder.Entity<Purchase>(entity =>
        {
            entity.Property(e => e.Qty).HasPrecision(18, 4);
            entity.Property(e => e.UnitCost).HasPrecision(18, 4);
            entity.Property(e => e.TotalCost).HasPrecision(18, 4);

            entity.Property(e => e.DateShamsi).IsRequired().HasMaxLength(10);
            entity.Property(e => e.SupplierNote).HasMaxLength(500);

            entity.HasIndex(e => new { e.ItemId, e.DateGregorian });

            entity.HasOne(e => e.Item)
                  .WithMany(i => i.Purchases)
                  .HasForeignKey(e => e.ItemId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── Transfer ───
        modelBuilder.Entity<Transfer>(entity =>
        {
            entity.Property(e => e.Qty).HasPrecision(18, 4);
            entity.Property(e => e.DateShamsi).IsRequired().HasMaxLength(10);
            entity.Property(e => e.Note).HasMaxLength(500);

            entity.HasIndex(e => new { e.ItemId, e.DateGregorian });

            entity.HasOne(e => e.Item)
                  .WithMany(i => i.Transfers)
                  .HasForeignKey(e => e.ItemId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── Sale ───
        modelBuilder.Entity<Sale>(entity =>
        {
            entity.Property(e => e.Qty).HasPrecision(18, 4);
            entity.Property(e => e.SaleUnitPrice).HasPrecision(18, 4);
            entity.Property(e => e.LockedUnitCost).HasPrecision(18, 4);
            entity.Property(e => e.Revenue).HasPrecision(18, 4);
            entity.Property(e => e.Cost).HasPrecision(18, 4);
            entity.Property(e => e.Profit).HasPrecision(18, 4);

            entity.Property(e => e.DateShamsi).IsRequired().HasMaxLength(10);
            entity.Property(e => e.InvoiceNumber).HasMaxLength(20);

            entity.HasIndex(e => new { e.ItemId, e.DateGregorian });
            entity.HasIndex(e => e.DateShamsi);
            entity.HasIndex(e => e.InvoiceNumber);

            entity.HasOne(e => e.Item)
                  .WithMany(i => i.Sales)
                  .HasForeignKey(e => e.ItemId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Customer)
                  .WithMany()
                  .HasForeignKey(e => e.CustomerId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ─── CashLedger ───
        modelBuilder.Entity<CashLedger>(entity =>
        {
            entity.Property(e => e.AmountIn).HasPrecision(18, 4);
            entity.Property(e => e.AmountOut).HasPrecision(18, 4);

            entity.Property(e => e.DateShamsi).IsRequired().HasMaxLength(10);
            entity.Property(e => e.Counterparty).HasMaxLength(200);
            entity.Property(e => e.Note).HasMaxLength(500);

            entity.HasIndex(e => e.DateGregorian);
        });

        // ─── Setting ───
        modelBuilder.Entity<Setting>(entity =>
        {
            entity.Property(e => e.InitialCapital).HasPrecision(18, 4);
            entity.Property(e => e.CriticalPct).HasPrecision(5, 4);
            entity.Property(e => e.WarningPct).HasPrecision(5, 4);
        });

        // ─── Customer ───
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Phone).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.TotalPurchasedAmount).HasPrecision(18, 4);

            entity.HasIndex(e => e.Phone).IsUnique();
        });

        // ─── User ───
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(200);
            entity.Property(e => e.PasswordSalt).IsRequired().HasMaxLength(200);
            entity.Property(e => e.FullName).IsRequired().HasMaxLength(200);

            entity.HasIndex(e => e.Username).IsUnique();
        });

        // ─── LoginHistory ───
        modelBuilder.Entity<LoginHistory>(entity =>
        {
            entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
            entity.Property(e => e.FullName).HasMaxLength(200);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Note).HasMaxLength(500);

            entity.HasIndex(e => e.LoginAt);
            entity.HasIndex(e => e.UserId);
        });
    }
}