using JoyNect.Core.Design;
using Xunit;
using JoyNect.Core.Mapping;
using JoyNect.Core.Settings;

namespace JoyNect.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "joynect-settings-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void MissingFileReturnsDefaults()
    {
        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(AppearanceMode.System, settings.Appearance);
        Assert.True(settings.FollowWallpaper);
        Assert.Equal(SchemeVariant.TonalSpot, settings.Variant);
        Assert.Equal(26760, settings.MotionPort);
    }

    [Fact]
    public void ValuesSurviveASaveAndLoad()
    {
        var store = new SettingsStore(SettingsPath);
        var original = new AppSettings
        {
            Appearance = AppearanceMode.Dark,
            SeedColour = 0xFF1B6C4A,
            FollowWallpaper = false,
            Variant = SchemeVariant.TonalSpot,
            Contrast = ContrastLevel.High,
            JoyConMode = JoyConMode.Combined,
            RumbleStrength = 0.35,
            LeftDeadzone = 0.12,
            MotionServerEnabled = true,
            MotionPort = 26761,
            OnboardingComplete = true,
            LastPage = "mapping",
        };

        Assert.True(store.Save(original));

        var loaded = new SettingsStore(SettingsPath).Load();
        Assert.Equal(AppearanceMode.Dark, loaded.Appearance);
        Assert.Equal(0xFF1B6C4A, loaded.SeedColour);
        Assert.False(loaded.FollowWallpaper);
        Assert.Equal(SchemeVariant.TonalSpot, loaded.Variant);
        Assert.Equal(ContrastLevel.High, loaded.Contrast);
        Assert.Equal(JoyConMode.Combined, loaded.JoyConMode);
        Assert.Equal(0.35, loaded.RumbleStrength, 3);
        Assert.Equal(0.12, loaded.LeftDeadzone, 3);
        Assert.True(loaded.MotionServerEnabled);
        Assert.Equal(26761, loaded.MotionPort);
        Assert.True(loaded.OnboardingComplete);
        Assert.Equal("mapping", loaded.LastPage);
    }

    [Fact]
    public void ABrokenFileFallsBackWithoutLosingIt()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ not json at all");

        var store = new SettingsStore(SettingsPath);
        var settings = store.Load();

        Assert.Equal(AppearanceMode.System, settings.Appearance);
        Assert.True(File.Exists(SettingsPath + ".broken"));

        // The next save must succeed, otherwise a single bad byte would disable persistence forever.
        Assert.True(store.Save(settings));
        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void OutOfRangeValuesAreClamped()
    {
        var settings = new AppSettings
        {
            MaxPads = 99,
            RumbleStrength = 4.2,
            LeftDeadzone = -1,
            RightCurve = 12,
            MotionPort = 7,
            SeedColour = 0x00123456,
            LastPage = "  ",
            JoyConMode = (JoyConMode)99,
        }.Normalise();

        Assert.Equal(4, settings.MaxPads);
        Assert.Equal(1.0, settings.RumbleStrength, 3);
        Assert.Equal(0.0, settings.LeftDeadzone, 3);
        Assert.Equal(2.0, settings.RightCurve, 3);
        Assert.Equal(1024, settings.MotionPort);
        Assert.Equal(0xFF123456, settings.SeedColour);
        Assert.Equal("home", settings.LastPage);
        Assert.Equal(JoyConMode.Sideways, settings.JoyConMode);
    }

    [Fact]
    public void AnUnknownEnumNameDoesNotStopTheLoad()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """
            {
              "Appearance": "Ultraviolet",
              "Variant": "Expressive",
              "Contrast": "Standard",
              "JoyConMode": "Sideways",
              "MotionPort": 26760
            }
            """);

        var settings = new SettingsStore(SettingsPath).Load();

        // JSON enum names are case sensitive; a hand-edited file with the wrong case is treated as
        // unreadable rather than silently changing behaviour.
        Assert.Equal(AppearanceMode.System, settings.Appearance);
    }
}
