using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace ShopManager.Desktop.Services;

/// <summary>
/// سرویس آپدیت خودکار با Velopack
/// </summary>
public class UpdateService
{
    /// <summary>استفاده از GitHub (true) یا وب سرور (false)</summary>
    private const bool UseGithub = true;

    /// <summary>آدرس ریپو GitHub</summary>
    private const string RepoUrl = "https://github.com/loyalnurse313-a11y/ShopManager";

    private UpdateManager? _manager;

    public UpdateInfo? LastUpdateInfo { get; private set; }
    public bool IsUpdateReady { get; private set; }
    public int DownloadProgress { get; private set; }

    public event Action? StateChanged;

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

    // ═══════════════════════════════════════════
    // بررسی آپدیت
    // ═══════════════════════════════════════════

    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            _manager = CreateUpdateManager();
            LastUpdateInfo = await _manager.CheckForUpdatesAsync();

            if (LastUpdateInfo == null)
            {
                System.Diagnostics.Debug.WriteLine(">>> آپدیت جدیدی وجود ندارد");
                StateChanged?.Invoke();
                return null;
            }

            System.Diagnostics.Debug.WriteLine($">>> آپدیت جدید: {LastUpdateInfo.TargetFullRelease.Version}");
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

    public void ApplyUpdatesAndRestart()
    {
        if (_manager == null || LastUpdateInfo == null)
            return;

        try
        {
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
            var source = new GithubSource(RepoUrl, accessToken: null, prerelease: false);
            return new UpdateManager(source);
        }
        else
        {
            var source = new SimpleWebSource("https://your-server.com/updates");
            return new UpdateManager(source);
        }
    }
}