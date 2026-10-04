using Microsoft.EntityFrameworkCore;
using ShopManager.Domain.Entities;
using System.Runtime.ExceptionServices;

namespace ShopManager.Infrastructure.Persistence;

/// <summary>
/// DbContext اصلی برنامه — پل بین C# و دیتابیس SQLite
/// </summary>
public class AppDbContext : DbContext
{
    private readonly Action? _cleanupSucceeded;
    private readonly Action<Exception>? _cleanupFailed;
    // 0 = not started, 1 = cleanup owned, 2 = succeeded, 3 = failed.
    private int _cleanupState;
    private ExceptionDispatchInfo? _cleanupError;

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    /// <summary>Optional resource ownership hooks; the EF model and context type stay unchanged.</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options,
        Action cleanupSucceeded, Action<Exception> cleanupFailed) : base(options)
    {
        ArgumentNullException.ThrowIfNull(cleanupSucceeded);
        ArgumentNullException.ThrowIfNull(cleanupFailed);
        _cleanupSucceeded = cleanupSucceeded;
        _cleanupFailed = cleanupFailed;
    }

    public override void Dispose()
    {
        if (!TryOwnCleanup()) return;
        try { base.Dispose(); }
        catch (Exception error)
        {
            ReportCleanupFailure(error);
            throw;
        }
        ReportCleanupSuccess();
    }

    public override async ValueTask DisposeAsync()
    {
        if (!TryOwnCleanup()) return;
        try { await base.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error)
        {
            ReportCleanupFailure(error);
            throw;
        }
        ReportCleanupSuccess();
    }

    private void ReportCleanupSuccess()
    {
        _cleanupSucceeded?.Invoke();
        Volatile.Write(ref _cleanupState, 2);
    }

    private void ReportCleanupFailure(Exception error)
    {
        _cleanupError = ExceptionDispatchInfo.Capture(error);
        try { _cleanupFailed?.Invoke(error); }
        finally { Volatile.Write(ref _cleanupState, 3); }
    }

    private bool TryOwnCleanup()
    {
        var state = Interlocked.CompareExchange(ref _cleanupState, 1, 0);
        if (state == 0) return true;
        if (state == 2) return false;
        if (state == 3) _cleanupError!.Throw();
        // Do not enter EF concurrently, wait on the cleanup owner, or report success.
        throw new InvalidOperationException("Context cleanup is already in progress.");
    }

    // ─── جدول‌ها ───
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleOperation> SaleOperations => Set<SaleOperation>();
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
            entity.Property(e => e.DiscountAmount).HasPrecision(18, 4);

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

        modelBuilder.Entity<SaleOperation>(entity =>
        {
            entity.HasKey(e => e.OperationId);
            entity.Property(e => e.OperationId).IsRequired().ValueGeneratedNever();
            entity.Property(e => e.InvoiceNumber).IsRequired();
            entity.Property(e => e.RequestFingerprint).IsRequired();
            entity.HasIndex(e => e.InvoiceNumber).IsUnique();
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
