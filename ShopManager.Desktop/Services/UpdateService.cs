using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace ShopManager.Desktop.Services;

/// <summary>
/// سرویس آپدیت خودکار
/// از Velopack برای بررسی، دانلود و اعمال آپدیت استفاده می‌کنه
/// </summary>
public class UpdateService
{
    // ═══════════════════════════════════════════
    // آدرس سرور آپدیت
    // ═══════════════════════════════════════════
    // گزینه ۱: سرور وب ساده (SimpleWebSource)
    // گزینه ۲: GitHub Releases (GithubSource)
    // ═══════════════════════════════════════════

    /// <summary>
    /// آدرس سرور آپدیت (وب سرور، S3، Azure Blob و...)
    /// </summary>
    private const string UpdateUrl = "https://your-server.com/updates";

    /// <summary>
    /// یا آدرس GitHub Releases
    /// </summary>
    private const string GithubRepoUrl = "https://github.com/loyalnurse313-a11y/ShopManager";

    // ─── انتخاب منبع: SimpleWebSource یا GithubSource ───
    private static readonly bool UseGithub = false; // اگه true باشه از GitHub استفاده می‌کنه

    private UpdateManager? _manager;

    /// <summary>نتیجه آخرین بررسی</summary>
    public UpdateInfo? LastUpdateInfo { get; private set; }

    /// <summary>آیا آپدیت آماده اعماله؟</summary>
    public bool IsUpdateReady { get; private set; }

    /// <summary>درصد دانلود (0-100)</summary>
    public int DownloadProgress { get; private set; }

    /// <summary>رویداد تغییر وضعیت</summary>
    public event Action? StateChanged;

    // ═══════════════════════════════════════════
    // بررسی آپدیت
    // ═══════════════════════════════════════════

    /// <summary>
    /// بررسی وجود آپدیت جدید (غیرمسدودکننده)
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            _manager = CreateUpdateManager();

            // ═══ بررسی نسخه جدید ═══
            LastUpdateInfo = await _manager.CheckForUpdatesAsync();

            if (LastUpdateInfo == null)
            {
                System.Diagnostics.Debug.WriteLine(">>> آپدیت جدیدی وجود ندارد");
                StateChanged?.Invoke();
                return null;
            }

            System.Diagnostics.Debug.WriteLine($">>> آپدیت جدید پیدا شد: {LastUpdateInfo.TargetFullRelease.Version}");
            StateChanged?.Invoke();
            return LastUpdateInfo;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($">>> خطا در بررسی آپدیت: {ex.Message}");
            return null;
        }
    }

    // ═══════════════════════════════════════════
    // دانلود آپدیت
    // ═══════════════════════════════════════════

    /// <summary>
    /// دانلود آپدیت با گزارش پیشرفت
    /// </summary>
    public async Task<bool> DownloadUpdatesAsync()
    {
        if (_manager == null || LastUpdateInfo == null)
            return false;

        try
        {
            DownloadProgress = 0;
            StateChanged?.Invoke();

            await _manager.DownloadUpdatesAsync(LastUpdateInfo, progress =>
            {
                DownloadProgress = progress;
                StateChanged?.Invoke();
            });

            IsUpdateReady = true;
            StateChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($">>> خطا در دانلود آپدیت: {ex.Message}");
            return false;
        }
    }

    // ═══════════════════════════════════════════
    // اعمال آپدیت
    // ═══════════════════════════════════════════

    /// <summary>
    /// اعمال آپدیت و ری‌استارت برنامه
    /// ⚠️ قبل از صدا زدن این، باید دیتابیس بکاپ گرفته بشه!
    /// </summary>
    public void ApplyUpdatesAndRestart()
    {
        if (_manager == null || LastUpdateInfo == null)
            return;

        try
        {
            // ─── بکاپ اضطراری قبل از آپدیت ───
            try
            {
                BackupService.CreateForcedBackup();
                System.Diagnostics.Debug.WriteLine(">>> بکاپ قبل از آپدیت گرفته شد");
            }
            catch { }

            _manager.ApplyUpdatesAndRestart(LastUpdateInfo);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($">>> خطا در اعمال آپدیت: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════
    // ساخت UpdateManager
    // ═══════════════════════════════════════════

    private UpdateManager CreateUpdateManager()
    {
        if (UseGithub)
        {
            // ─── منبع: GitHub Releases ───
            var source = new GithubSource(
                GithubRepoUrl,
                accessToken: null,
                prerelease: false);

            return new UpdateManager(source);
        }
        else
        {
            // ─── منبع: وب سرور ساده ───
            var source = new SimpleWebSource(UpdateUrl);
            return new UpdateManager(source);
        }
    }

    /// <summary>نسخه فعلی برنامه</summary>
    public string CurrentVersion
    {
        get
        {
            try
            {
                var mgr = CreateUpdateManager();
                return mgr.CurrentVersion.ToString();
            }
            catch
            {
                return "—";
            }
        }
    }
}