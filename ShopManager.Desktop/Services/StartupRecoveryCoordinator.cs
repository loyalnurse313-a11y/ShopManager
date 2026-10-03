using System;
using System.IO;
using System.Text.Json;

namespace ShopManager.Desktop.Services;

/// <summary>Offline recovery admission before any desktop database consumer.</summary>
internal static class StartupRecoveryCoordinator
{
    internal static void Run(
        Action normalStartup,
        Action<string> blockedStartup,
        Func<RestoreRecoveryResult>? recoverForTests = null)
    {
        string? blockedReason;
        try
        {
            var result = recoverForTests is null
                ? RestoreRecoveryService.Recover(validateRegisteredIdentity: ValidateRegisteredIdentity)
                : recoverForTests();
            var admitted = result.Outcome is RestoreRecoveryOutcome.NoIntent or RestoreRecoveryOutcome.Completed;
            blockedReason = admitted && !RestoreRecoveryService.IsArmed
                ? null
                : result.Reason ?? "Startup recovery did not grant database admission.";
        }
        catch (Exception ex)
        {
            // Only recovery errors belong here; downstream startup/presentation errors
            // must never be mislabeled as recovery failures or retried.
            blockedReason = $"Startup recovery failed ({ex.GetType().Name}): {ex.Message}";
        }

        if (blockedReason is not null)
        {
            blockedStartup(blockedReason);
            return;
        }

        normalStartup();
    }

    /// <summary>Read registered identity only; a tombstoned live file need not exist.</summary>
    internal static string? ValidateRegisteredIdentity(RestoreIntent intent)
    {
        try
        {
            var markerPath = Path.Combine(RestoreRecoveryService.AppOwnedRoot, "database-location.json");
            using var stream = new FileStream(markerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var marker = JsonSerializer.Deserialize<RegisteredDatabaseLocation>(stream);
            if (marker?.Version != 1 || string.IsNullOrWhiteSpace(marker.DataFolder)
                || !Path.IsPathFullyQualified(marker.DataFolder))
                return "Registered database identity is invalid or unsupported.";

            var expected = Path.Combine(Path.GetFullPath(marker.DataFolder), "shop.db");
            return string.Equals(expected, Path.GetFullPath(intent.LiveDatabasePath), StringComparison.OrdinalIgnoreCase)
                ? null
                : "Restore intent does not match the registered database identity.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or JsonException or ArgumentException or NotSupportedException)
        {
            return "Registered database identity could not be read: " + ex.Message;
        }
    }

    private sealed class RegisteredDatabaseLocation
    {
        public int Version { get; set; }
        public string DataFolder { get; set; } = "";
    }
}
