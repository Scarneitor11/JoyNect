namespace JoyNect.Core.Design;

/// <summary>How the palettes are derived from the seed colour.</summary>
public enum SchemeVariant
{
    /// <summary>
    /// Material You's default: palettes near the seed hue, with a tertiary rotated 60 degrees.
    /// </summary>
    TonalSpot,

    /// <summary>
    /// Material 3 Expressive: a wider hue rotation that deliberately leaves the seed colour behind
    /// for a livelier, less corporate result.
    /// </summary>
    Expressive,
}

/// <summary>How far foreground and background tones are pulled apart.</summary>
public enum ContrastLevel
{
    /// <summary>The Material 3 default.</summary>
    Standard,

    /// <summary>
    /// JoyNect's high contrast mode: foreground roles move towards black or white and outlines
    /// strengthen. Every text pair in the interface then clears 7:1 rather than 4.5:1.
    /// </summary>
    High,
}

/// <summary>
/// A complete Material 3 colour scheme: one colour per role, for one mode.
/// </summary>
public sealed class M3Scheme
{
    private readonly uint[] _colors;

    private M3Scheme(
        uint source,
        bool isDark,
        SchemeVariant variant,
        ContrastLevel contrast,
        TonalPalette primary,
        TonalPalette secondary,
        TonalPalette tertiary,
        TonalPalette neutral,
        TonalPalette neutralVariant,
        TonalPalette error,
        uint[] colors)
    {
        Source = source;
        IsDark = isDark;
        Variant = variant;
        Contrast = contrast;
        PrimaryPalette = primary;
        SecondaryPalette = secondary;
        TertiaryPalette = tertiary;
        NeutralPalette = neutral;
        NeutralVariantPalette = neutralVariant;
        ErrorPalette = error;
        _colors = colors;
    }

    /// <summary>The colour the scheme was generated from.</summary>
    public uint Source { get; }

    public bool IsDark { get; }

    public SchemeVariant Variant { get; }

    public ContrastLevel Contrast { get; }

    public TonalPalette PrimaryPalette { get; }

    public TonalPalette SecondaryPalette { get; }

    public TonalPalette TertiaryPalette { get; }

    public TonalPalette NeutralPalette { get; }

    public TonalPalette NeutralVariantPalette { get; }

    public TonalPalette ErrorPalette { get; }

    /// <summary>The colour of a role.</summary>
    public uint this[ColorRole role] => _colors[(int)role];

    /// <summary>The roles the interface uses most, as properties for readable call sites.</summary>
    public uint Primary => this[ColorRole.Primary];

    public uint OnPrimary => this[ColorRole.OnPrimary];

    public uint PrimaryContainer => this[ColorRole.PrimaryContainer];

    public uint OnPrimaryContainer => this[ColorRole.OnPrimaryContainer];

    public uint Secondary => this[ColorRole.Secondary];

    public uint OnSecondary => this[ColorRole.OnSecondary];

    public uint SecondaryContainer => this[ColorRole.SecondaryContainer];

    public uint OnSecondaryContainer => this[ColorRole.OnSecondaryContainer];

    public uint Tertiary => this[ColorRole.Tertiary];

    public uint TertiaryContainer => this[ColorRole.TertiaryContainer];

    public uint OnTertiaryContainer => this[ColorRole.OnTertiaryContainer];

    public uint Error => this[ColorRole.Error];

    public uint OnError => this[ColorRole.OnError];

    public uint ErrorContainer => this[ColorRole.ErrorContainer];

    public uint OnErrorContainer => this[ColorRole.OnErrorContainer];

    public uint Surface => this[ColorRole.Surface];

    public uint OnSurface => this[ColorRole.OnSurface];

    public uint SurfaceVariant => this[ColorRole.SurfaceVariant];

    public uint OnSurfaceVariant => this[ColorRole.OnSurfaceVariant];

    public uint SurfaceContainer => this[ColorRole.SurfaceContainer];

    public uint SurfaceContainerLow => this[ColorRole.SurfaceContainerLow];

    public uint SurfaceContainerHigh => this[ColorRole.SurfaceContainerHigh];

    public uint SurfaceContainerHighest => this[ColorRole.SurfaceContainerHighest];

    public uint SurfaceContainerLowest => this[ColorRole.SurfaceContainerLowest];

    public uint Outline => this[ColorRole.Outline];

    public uint OutlineVariant => this[ColorRole.OutlineVariant];

    public uint InverseSurface => this[ColorRole.InverseSurface];

    public uint InverseOnSurface => this[ColorRole.InverseOnSurface];

    public uint InversePrimary => this[ColorRole.InversePrimary];

    public uint Scrim => this[ColorRole.Scrim];

    public uint SurfaceTint => this[ColorRole.SurfaceTint];

    public uint Background => this[ColorRole.Background];

    public uint OnBackground => this[ColorRole.OnBackground];

    /// <summary>Builds a scheme from prepared palettes and tones.</summary>
    internal static M3Scheme Build(
        uint source,
        bool isDark,
        SchemeVariant variant,
        ContrastLevel contrast,
        TonalPalette primary,
        TonalPalette secondary,
        TonalPalette tertiary,
        TonalPalette neutral,
        TonalPalette neutralVariant,
        TonalPalette error)
    {
        var colors = new uint[ColorRoleInfoTable.All.Count];

        foreach (var info in ColorRoleInfoTable.All)
        {
            var palette = MaterialSchemes.PaletteFor(info.Role, primary, secondary, tertiary, neutral, neutralVariant, error);
            var tone = MaterialSchemes.ToneFor(info.Role, isDark, contrast);
            colors[(int)info.Role] = palette.Tone(tone);
        }

        return new M3Scheme(source, isDark, variant, contrast, primary, secondary, tertiary, neutral, neutralVariant, error, colors);
    }

    /// <summary>Returns the same scheme with a different contrast level.</summary>
    public M3Scheme WithContrast(ContrastLevel contrast) => contrast == Contrast
        ? this
        : MaterialSchemes.Create(Source, IsDark, Variant, contrast);

    /// <summary>Returns the scheme for the other mode.</summary>
    public M3Scheme WithMode(bool isDark) => isDark == IsDark
        ? this
        : MaterialSchemes.Create(Source, isDark, Variant, Contrast);

    /// <summary>A tonal strip of one palette, for the palette page.</summary>
    public static IReadOnlyList<uint> Tones(TonalPalette palette, IEnumerable<double> tones) =>
        tones.Select(palette.Tone).ToArray();
}

/// <summary>
/// Generates Material 3 colour schemes from a seed colour.
/// </summary>
/// <remarks>
/// The palettes and tone choices follow Material Color Utilities, and
/// <c>tests/JoyNect.Core.Tests/Design/MaterialColorTests.cs</c> checks the output against fixtures
/// generated by that library, so "Material 3 colours" is a measured claim rather than an aspiration.
/// </remarks>
public static class MaterialSchemes
{
    /// <summary>The Material baseline purple, used when there is nothing better to seed from.</summary>
    public const uint BaselineSeed = 0xFF6750A4;

    /// <summary>Builds a scheme from a seed colour.</summary>
    public static M3Scheme Create(
        uint source,
        bool isDark,
        SchemeVariant variant = SchemeVariant.Expressive,
        ContrastLevel contrast = ContrastLevel.Standard)
    {
        var seed = Hct.FromArgb(source);

        TonalPalette primary, secondary, tertiary, neutral, neutralVariant;

        switch (variant)
        {
            case SchemeVariant.TonalSpot:
                primary = new TonalPalette(seed.Hue, 36.0);
                secondary = new TonalPalette(seed.Hue, 16.0);
                tertiary = new TonalPalette(seed.Hue + 60.0, 24.0);
                neutral = new TonalPalette(seed.Hue, 6.0);
                neutralVariant = new TonalPalette(seed.Hue, 8.0);
                break;

            default:
                primary = new TonalPalette(seed.Hue + 240.0, 40.0);
                secondary = new TonalPalette(RotatedHue(seed, ExpressiveHues, ExpressiveSecondaryRotations), 24.0);
                tertiary = new TonalPalette(RotatedHue(seed, ExpressiveHues, ExpressiveTertiaryRotations), 32.0);
                neutral = new TonalPalette(seed.Hue + 15.0, 8.0);
                neutralVariant = new TonalPalette(seed.Hue + 15.0, 12.0);
                break;
        }

        // The error palette is fixed so that "something went wrong" reads the same in every theme.
        var error = new TonalPalette(25.0, 84.0);

        return M3Scheme.Build(source, isDark, variant, contrast, primary, secondary, tertiary, neutral, neutralVariant, error);
    }

    internal static TonalPalette PaletteFor(
        ColorRole role,
        TonalPalette primary,
        TonalPalette secondary,
        TonalPalette tertiary,
        TonalPalette neutral,
        TonalPalette neutralVariant,
        TonalPalette error) => role switch
        {
            >= ColorRole.Primary and <= ColorRole.OnPrimaryFixedVariant => primary,
            ColorRole.InversePrimary or ColorRole.SurfaceTint => primary,

            >= ColorRole.Secondary and <= ColorRole.OnSecondaryFixedVariant => secondary,
            >= ColorRole.Tertiary and <= ColorRole.OnTertiaryFixedVariant => tertiary,

            >= ColorRole.Error and <= ColorRole.OnErrorFixedVariant => error,

            ColorRole.SurfaceVariant or ColorRole.OnSurfaceVariant
                or ColorRole.Outline or ColorRole.OutlineVariant => neutralVariant,

            _ => neutral,
        };

    /// <summary>The tone of a role, following Material's tone table.</summary>
    internal static double ToneFor(ColorRole role, bool isDark, ContrastLevel contrast)
    {
        var high = contrast == ContrastLevel.High;

        return role switch
        {
            // Fixed roles keep the same tone in both modes: that is what makes them fixed.
            ColorRole.PrimaryFixed or ColorRole.SecondaryFixed or ColorRole.TertiaryFixed or ColorRole.ErrorFixed => 90.0,
            ColorRole.PrimaryFixedDim or ColorRole.SecondaryFixedDim or ColorRole.TertiaryFixedDim or ColorRole.ErrorFixedDim => 80.0,
            ColorRole.OnPrimaryFixed or ColorRole.OnSecondaryFixed or ColorRole.OnTertiaryFixed or ColorRole.OnErrorFixed => 10.0,
            ColorRole.OnPrimaryFixedVariant or ColorRole.OnSecondaryFixedVariant
                or ColorRole.OnTertiaryFixedVariant or ColorRole.OnErrorFixedVariant => 30.0,

            ColorRole.Shadow or ColorRole.Scrim => 0.0,

            ColorRole.InversePrimary => isDark ? 40.0 : 80.0,
            ColorRole.InverseSurface => isDark ? 90.0 : 20.0,
            ColorRole.InverseOnSurface => isDark ? 20.0 : 95.0,

            ColorRole.SurfaceTint => isDark ? 80.0 : 40.0,

            // Surfaces: the six container levels step in tone so nested layers can be told apart.
            ColorRole.Background or ColorRole.Surface => isDark ? 6.0 : 98.0,
            ColorRole.OnBackground or ColorRole.OnSurface => isDark ? (high ? 100.0 : 90.0) : (high ? 0.0 : 10.0),
            ColorRole.SurfaceVariant => isDark ? 30.0 : 90.0,
            ColorRole.OnSurfaceVariant => isDark ? (high ? 95.0 : 80.0) : (high ? 10.0 : 30.0),
            ColorRole.SurfaceDim => isDark ? 6.0 : 87.0,
            ColorRole.SurfaceBright => isDark ? 24.0 : 98.0,
            ColorRole.SurfaceContainerLowest => isDark ? 4.0 : 100.0,
            ColorRole.SurfaceContainerLow => isDark ? 10.0 : 96.0,
            ColorRole.SurfaceContainer => isDark ? 12.0 : 94.0,
            ColorRole.SurfaceContainerHigh => isDark ? 17.0 : 92.0,
            ColorRole.SurfaceContainerHighest => isDark ? 22.0 : 90.0,

            ColorRole.Outline => isDark ? (high ? 75.0 : 60.0) : (high ? 30.0 : 50.0),
            ColorRole.OutlineVariant => isDark ? (high ? 45.0 : 30.0) : (high ? 55.0 : 80.0),

            // Chromatic roles: a filled surface, something to put on it, a container and its content.
            ColorRole.Primary or ColorRole.Secondary or ColorRole.Tertiary or ColorRole.Error => isDark ? 80.0 : 40.0,
            ColorRole.OnPrimary or ColorRole.OnSecondary or ColorRole.OnTertiary or ColorRole.OnError => isDark ? 20.0 : 100.0,
            ColorRole.PrimaryContainer or ColorRole.SecondaryContainer
                or ColorRole.TertiaryContainer or ColorRole.ErrorContainer => isDark ? 30.0 : 90.0,

            // 30 in light mode, not 10: this is what the reference implementation produces, and it
            // still clears 7:1 against the tone 90 container.
            ColorRole.OnPrimaryContainer or ColorRole.OnSecondaryContainer or ColorRole.OnTertiaryContainer
                or ColorRole.OnErrorContainer => isDark ? (high ? 100.0 : 90.0) : (high ? 10.0 : 30.0),

            _ => 50.0,
        };
    }

    // Hue rotations for the expressive variant, interpolated between breakpoints so the result does
    // not jump as the seed hue moves.
    private static readonly double[] ExpressiveHues = [0.0, 21.0, 51.0, 121.0, 151.0, 191.0, 271.0, 321.0, 360.0];

    private static readonly double[] ExpressiveSecondaryRotations = [45.0, 95.0, 45.0, 20.0, 45.0, 90.0, 45.0, 45.0, 45.0];

    private static readonly double[] ExpressiveTertiaryRotations = [120.0, 120.0, 20.0, 45.0, 20.0, 15.0, 20.0, 120.0, 120.0];

    /// <summary>
    /// Rotates a hue by the amount registered for the band it falls in.
    /// </summary>
    /// <remarks>
    /// The rotation is constant within a band rather than interpolated across it. That is what the
    /// reference implementation does, and it is deliberate: a band is a region of hue where one
    /// rotation looks right, and interpolating would smear the designed relationships.
    /// </remarks>
    internal static double RotatedHue(Hct source, double[] hues, double[] rotations)
    {
        var hue = ColorUtils.SanitizeDegrees(source.Hue);

        if (rotations.Length == 1)
        {
            return ColorUtils.SanitizeDegrees(hue + rotations[0]);
        }

        for (var i = 0; i <= hues.Length - 2; i++)
        {
            if (hues[i] < hue && hue < hues[i + 1])
            {
                return ColorUtils.SanitizeDegrees(hue + rotations[i]);
            }
        }

        return hue;
    }
}
