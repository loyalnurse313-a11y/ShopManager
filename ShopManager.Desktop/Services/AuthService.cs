using System;
using System.Linq;
using System.Threading;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Desktop.Services;

internal sealed class AuthOperationBusyException : InvalidOperationException
{
    internal AuthOperationBusyException() : base("An authentication operation is already in progress.") { }
}

public static class AuthService
{
    private static readonly SessionState ProductionSession = new(
        () => UserLoggedIn?.Invoke(), () => UserLoggedOut?.Invoke());

    internal sealed record SessionSnapshot(User User, LoginHistory LoginRecord);

    // The claim covers DB work, callbacks and cleanup without holding a monitor.
    internal sealed class SessionState(Action? loggedIn = null, Action? loggedOut = null)
    {
        private SessionSnapshot? _session;
        private int _mutating;
        internal SessionSnapshot? Current => Volatile.Read(ref _session);
        internal bool TryClaim() => Interlocked.CompareExchange(ref _mutating, 1, 0) == 0;
        internal void Release() => Volatile.Write(ref _mutating, 0);
        internal void Publish(SessionSnapshot? session) => Volatile.Write(ref _session, session);
        internal void NotifyLogin() => loggedIn?.Invoke();
        internal void NotifyLogout() => loggedOut?.Invoke();
    }

    public static User? CurrentUser => ProductionSession.Current?.User;
    public static bool IsLoggedIn => CurrentUser != null;
    public static bool IsAdmin => CurrentUser?.Role == UserRole.Admin;
    public static int? CurrentLoginHistoryId => ProductionSession.Current?.LoginRecord.Id;
    public static event Action? UserLoggedIn;
    public static event Action? UserLoggedOut;
    public static bool CanViewFinance
    {
        get
        {
            var user = CurrentUser;
            return user?.Role == UserRole.Admin || (user?.CanViewFinance ?? false);
        }
    }

    public static (bool Success, string Message) Login(string username, string password)
        => LoginStandalone(RuntimeOperations.Runtime, ProductionSession, DatabaseService.CreateContext, username, password);

    public static void Logout(string note = "Logout")
        => LogoutStandalone(RuntimeOperations.Runtime, ProductionSession, DatabaseService.CreateContext, note);

    internal static (bool Success, string Message) LoginEnrolled(
        RuntimeOperations.OperationContext operation, string username, string password)
        => LoginEnrolled(RuntimeOperations.Runtime, operation, ProductionSession, DatabaseService.CreateContext, username, password);

    internal static void LogoutEnrolled(RuntimeOperations.OperationContext operation, string note = "Logout")
        => LogoutEnrolled(RuntimeOperations.Runtime, operation, ProductionSession, DatabaseService.CreateContext, note);

    // Explicit dependencies exercise the same production paths with isolated test state.
    internal static (bool Success, string Message) LoginStandalone(RuntimeOperations operations,
        SessionState session, Func<AppDbContext> createContext, string username, string password)
    {
        using var owner = operations.Begin(); // Rejection is outside all business-error catches.
        return LoginEnrolled(operations, owner.Context, session, createContext, username, password);
    }

    internal static void LogoutStandalone(RuntimeOperations operations, SessionState session,
        Func<AppDbContext> createContext, string note = "Logout")
    {
        using var owner = operations.Begin();
        LogoutEnrolled(operations, owner.Context, session, createContext, note);
    }

    internal static (bool Success, string Message) LoginEnrolled(RuntimeOperations operations,
        RuntimeOperations.OperationContext operation, SessionState session,
        Func<AppDbContext> createContext, string username, string password)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var use = operation.Use(operations);
        if (!session.TryClaim()) throw new AuthOperationBusyException();
        try { return LoginCore(session, createContext, use, username, password); }
        finally { session.Release(); }
    }

    internal static void LogoutEnrolled(RuntimeOperations operations, RuntimeOperations.OperationContext operation,
        SessionState session, Func<AppDbContext> createContext, string note = "Logout")
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var use = operation.Use(operations);
        if (!session.TryClaim()) throw new AuthOperationBusyException();
        try { LogoutCore(session, createContext, use, note); }
        finally { session.Release(); }
    }

    private static (bool Success, string Message) LoginCore(SessionState session,
        Func<AppDbContext> createContext, RuntimeOperations.BindingUse use, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username)) return (false, "نام کاربری را وارد کنید");
        if (string.IsNullOrWhiteSpace(password)) return (false, "رمز عبور را وارد کنید");
        string? publishedMessage = null;
        try
        {
            return WithContext(createContext, use, db =>
            {
                username = username.Trim().ToLower();
                var user = db.Users.FirstOrDefault(u => u.Username.ToLower() == username);
                if (user == null)
                {
                    RecordFailedLogin(createContext, use, username, "کاربر پیدا نشد");
                    return (false, "نام کاربری یا رمز عبور اشتباه است");
                }
                if (!user.IsActive)
                {
                    RecordFailedLogin(createContext, use, username, "کاربر غیرفعال");
                    return (false, "این حساب کاربری غیرفعال شده است");
                }
                if (!PasswordHasher.VerifyPassword(password, user.PasswordSalt, user.PasswordHash))
                {
                    RecordFailedLogin(createContext, use, username, "رمز اشتباه");
                    return (false, "نام کاربری یا رمز عبور اشتباه است");
                }
                user.LastLoginAt = DateTime.UtcNow;
                db.Users.Update(user);
                if (PasswordHasher.NeedsUpgrade(user.PasswordHash))
                {
                    try
                    {
                        var salt = PasswordHasher.GenerateSalt();
                        user.PasswordHash = PasswordHasher.HashPassword(password, salt);
                        user.PasswordSalt = salt;
                        System.Diagnostics.Debug.WriteLine($">>> رمز کاربر {user.Username} به PBKDF2 ارتقا یافت");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($">>> خطا در ارتقای رمز: {ex.Message}");
                    }
                }
                var record = new LoginHistory
                {
                    UserId = user.Id, Username = user.Username, FullName = user.FullName,
                    LoginAt = DateTime.UtcNow, Status = "موفق"
                };
                db.LoginHistories.Add(record);
                db.SaveChanges();
                var successMessage = $"خوش آمدید {user.FullName}";
                session.Publish(new SessionSnapshot(user, record));
                // Only publication by this attempt establishes a successful Login result.
                publishedMessage = successMessage;
                session.NotifyLogin();
                return (true, successMessage);
            });
        }
        catch (Exception ex)
        {
            if (publishedMessage is not null)
                return (true, $"{publishedMessage}\nهشدار: ورود انجام شد، اما خطایی پس از ثبت نشست رخ داد: {ex.Message}");
            return (false, $"خطا: {ex.Message}");
        }
    }

    private static void LogoutCore(SessionState session, Func<AppDbContext> createContext,
        RuntimeOperations.BindingUse use, string note)
    {
        var snapshot = session.Current;
        if (snapshot is null) return;
        try
        {
            WithContext(createContext, use, db =>
            {
                var record = db.LoginHistories.FirstOrDefault(h => h.Id == snapshot.LoginRecord.Id);
                if (record != null)
                {
                    record.LogoutAt = DateTime.UtcNow;
                    record.DurationSeconds = (int)(DateTime.UtcNow - record.LoginAt).TotalSeconds;
                    record.Note = note;
                    var user = db.Users.FirstOrDefault(u => u.Id == snapshot.User.Id);
                    if (user != null) user.LastLogoutAt = DateTime.UtcNow;
                    db.SaveChanges();
                }
                session.Publish(null);
                session.NotifyLogout();
                return true;
            });
        }
        catch { session.Publish(null); } // Cleanup was classified before this legacy catch.
    }

    private static void RecordFailedLogin(Func<AppDbContext> createContext, RuntimeOperations.BindingUse use,
        string username, string reason)
    {
        try
        {
            WithContext(createContext, use, db =>
            {
                db.LoginHistories.Add(new LoginHistory
                {
                    UserId = null, Username = username, FullName = "—", LoginAt = DateTime.UtcNow,
                    LogoutAt = DateTime.UtcNow, DurationSeconds = 0, Status = "ناموفق", Note = reason
                });
                db.SaveChanges();
                return true;
            });
        }
        catch (DatabaseContextCleanupUnprovenException) { throw; }
        catch { } // Failed-history business errors remain best effort.
    }

    private static T WithContext<T>(Func<AppDbContext> createContext, RuntimeOperations.BindingUse use,
        Func<AppDbContext, T> body)
    {
        AppDbContext db;
        try { db = createContext(); }
        catch (DatabaseContextCleanupUnprovenException error)
        {
            use.MarkCompletionUnproven(error);
            throw;
        }
        Exception? bodyError = null;
        try { return body(db); }
        catch (Exception error) { bodyError = error; throw; }
        finally
        {
            try { db.Dispose(); }
            catch (Exception cleanupError)
            {
                var diagnostic = new DatabaseContextCleanupUnprovenException(bodyError, cleanupError);
                use.MarkCompletionUnproven(diagnostic);
                throw diagnostic;
            }
        }
    }

    public static bool HasAccess(string section) => CurrentUser?.HasAccess(section) ?? false;
    public static string GetCurrentUserDisplay()
    {
        var user = CurrentUser;
        return user is null ? "—" : $"{user.FullName} ({user.Username})";
    }
}
