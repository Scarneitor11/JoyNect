using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JoyNect.Core.Settings;

/// <summary>
/// Reads and writes <see cref="AppSettings"/> as JSON.
/// </summary>
/// <remarks>
/// Writing goes through a temporary file and a replace, so a crash or a full disk during a save
/// cannot leave a half-written settings file behind — the failure mode that makes an app forget
/// everything and look broken. A file that cannot be parsed is moved aside instead of deleted, so a
/// user can still recover values by hand.
/// </remarks>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger _logger;

    public SettingsStore(string path, ILogger? logger = null)
    {
        _path = path;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>The default location: <c>%APPDATA%\JoyNect\settings.json</c>.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JoyNect",
        "settings.json");

    public string FilePath => _path;

    /// <summary>Loads the settings, falling back to defaults for anything missing or unreadable.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            return (settings ?? new AppSettings()).Normalise();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read settings from {Path}; using defaults", _path);
            Quarantine();
            return new AppSettings();
        }
    }

    /// <summary>Saves the settings, creating the directory if needed.</summary>
    /// <returns>True when the file was written.</returns>
    public bool Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings.Normalise(), SerializerOptions);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save settings to {Path}", _path);
            return false;
        }
    }

    /// <summary>Moves an unreadable file aside so the next save starts from a clean slate.</summary>
    private void Quarantine()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Move(_path, _path + ".broken", overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not move the unreadable settings file aside");
        }
    }
}
