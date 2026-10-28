using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace JoyNect.App.Services;

/// <summary>
/// The per-user "start with Windows" entry.
/// </summary>
/// <remarks>
/// Written under HKCU rather than HKLM so it needs no elevation and can be undone by the same user who
/// set it. The path is quoted, because an install under "C:\Program Files" would otherwise be split
/// into two arguments by the shell.
/// </remarks>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "JoyNect";

    /// <summary>Whether the entry exists and points at this executable.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(value)
                && value.Contains(ExecutablePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Creates or removes the entry.</summary>
    /// <returns>True when the registry now matches the request.</returns>
    public static bool Set(bool enabled, ILogger? logger = null)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExecutablePath()}\" --tray", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not update the Windows startup entry");
            return false;
        }
    }

    private static string ExecutablePath() =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "JoyNect.exe");
}
