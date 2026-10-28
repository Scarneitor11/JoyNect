namespace JoyNect.Core.Design;

/// <summary>
/// A colour in the Material colour system: CAM16 hue and chroma with a CIE L* tone.
/// </summary>
/// <remarks>
/// Tone is L*, which is linear in perceived lightness and therefore in contrast: a tone difference of
/// 40 always means a contrast ratio of at least 3:1. That is what makes it possible to pick a palette
/// by tone and know a role pair will be legible without measuring every combination.
/// </remarks>
public readonly struct Hct
{
    private Hct(uint argb, double hue, double chroma, double tone)
    {
        Argb = argb;
        Hue = hue;
        Chroma = chroma;
        Tone = tone;
    }

    /// <summary>The colour as <c>0xAARRGGBB</c>.</summary>
    public uint Argb { get; }

    /// <summary>CAM16 hue in degrees, 0 to 360.</summary>
    public double Hue { get; }

    /// <summary>
    /// CAM16 chroma: how colourful the colour is. Zero is a pure grey, and the usable maximum
    /// depends on the hue and the tone.
    /// </summary>
    public double Chroma { get; }

    /// <summary>L*, 0 (black) to 100 (white).</summary>
    public double Tone { get; }

    /// <summary>True when the colour carries a visible hue rather than being a grey.</summary>
    public bool IsChromatic => Chroma >= 1.0;

    /// <summary>Reads the hue, chroma and tone of an existing colour.</summary>
    public static Hct FromArgb(uint argb)
    {
        var cam = Cam16.FromInt(argb);
        return new Hct(argb, cam.Hue, cam.Chroma, ColorUtils.LstarFromArgb(argb));
    }

    /// <summary>Finds the displayable colour closest to the requested hue, chroma and tone.</summary>
    /// <remarks>
    /// Hue and tone are honoured exactly when possible; chroma is reduced to the largest value the
    /// display can show at that hue and tone. The resulting colour's own hue, chroma and tone are
    /// returned, so they may differ slightly from what was asked for.
    /// </remarks>
    public static Hct From(double hue, double chroma, double tone) =>
        FromArgb(HctSolver.SolveToInt(hue, chroma, tone));

    public override string ToString() => $"{ColorUtils.ToHex(Argb)} (hue {Hue:F1}, chroma {Chroma:F1}, tone {Tone:F1})";
}

/// <summary>
/// A family of colours with a fixed hue and chroma, varying only in tone.
/// </summary>
/// <remarks>
/// This is the unit the Material scheme is built from: one palette per role family, and each role is
/// a tone from one of them. Palettes are cached per tone because a theme asks for the same tone from
/// many places and solving the colour is the expensive part.
/// </remarks>
public sealed class TonalPalette
{
    private readonly Dictionary<int, uint> _cache = [];

    public TonalPalette(double hue, double chroma)
    {
        Hue = ColorUtils.SanitizeDegrees(hue);
        Chroma = Math.Max(0.0, chroma);
    }

    /// <summary>CAM16 hue of every colour in the palette.</summary>
    public double Hue { get; }

    /// <summary>Requested chroma. Tones that cannot carry it are reduced automatically.</summary>
    public double Chroma { get; }

    /// <summary>The colour at a tone from 0 to 100.</summary>
    public uint Tone(double tone)
    {
        var key = (int)Math.Round(Math.Clamp(tone, 0.0, 100.0) * 100.0, MidpointRounding.AwayFromZero);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var argb = Hct.From(Hue, Chroma, key / 100.0).Argb;
        _cache[key] = argb;
        return argb;
    }

    /// <summary>The tone of the colour that best represents this palette.</summary>
    /// <remarks>
    /// The key colour is the most chromatic tone available, which is what a picker should show when
    /// it wants one swatch per palette rather than a strip.
    /// </remarks>
    public Hct KeyColour
    {
        get
        {
            var best = Hct.From(Hue, Chroma, 50.0);
            for (var tone = 10; tone <= 90; tone += 10)
            {
                var candidate = Hct.From(Hue, Chroma, tone);
                if (candidate.Chroma > best.Chroma + 0.01)
                {
                    best = candidate;
                }
            }

            return best;
        }
    }

    public override string ToString() => $"hue {Hue:F1}, chroma {Chroma:F1}";
}
