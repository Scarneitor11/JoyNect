using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using JoyNect.Core.Design;

namespace JoyNect.Ui.Theme;

/// <summary>One step of the Material 3 type scale.</summary>
public sealed record M3TextStyle(string Name, double Size, double LineHeight, FontWeight Weight, bool Emphasised = false)
{
    /// <summary>The font to use for this step.</summary>
    public FontFamily Family => Size >= 24.0 ? M3Typography.Display : M3Typography.Text;

    public double LineSpacing => LineHeight / Size;

    /// <summary>A typeface for this style, with the right weight.</summary>
    public Typeface Typeface => new(Family, FontStyles.Normal, Weight, FontStretches.Normal);

    /// <summary>The same style at the expressive emphasis weight.</summary>
    public M3TextStyle WithEmphasis() => this with { Weight = FontWeights.Bold, Emphasised = true };
}

/// <summary>
/// The Material 3 type scale, mapped onto Windows fonts.
/// </summary>
/// <remarks>
/// Material specifies Roboto, which Windows does not ship. Segoe UI Variable is the right Windows
/// stand-in: it is a variable font with a display and a text optical size, so the large steps get the
/// tighter, more expressive shapes and the small steps stay legible — the same division of labour
/// Material's two optical sizes have. The scale values themselves are Material's.
/// </remarks>
public static class M3Typography
{
    /// <summary>Font used for display and headline steps.</summary>
    public static FontFamily Display { get; } = Resolve("Segoe UI Variable Display", "Segoe UI", "Tahoma");

    /// <summary>Font used for title, body and label steps.</summary>
    public static FontFamily Text { get; } = Resolve("Segoe UI Variable Text", "Segoe UI", "Tahoma");

    public static M3TextStyle DisplayLarge { get; } = new("Display large", 57, 64, FontWeights.Regular);

    public static M3TextStyle DisplayMedium { get; } = new("Display medium", 45, 52, FontWeights.Regular);

    public static M3TextStyle DisplaySmall { get; } = new("Display small", 36, 44, FontWeights.Regular);

    public static M3TextStyle HeadlineLarge { get; } = new("Headline large", 32, 40, FontWeights.Regular);

    public static M3TextStyle HeadlineMedium { get; } = new("Headline medium", 28, 36, FontWeights.Regular);

    public static M3TextStyle HeadlineSmall { get; } = new("Headline small", 24, 32, FontWeights.Regular);

    public static M3TextStyle TitleLarge { get; } = new("Title large", 22, 28, FontWeights.Regular);

    public static M3TextStyle TitleMedium { get; } = new("Title medium", 16, 24, FontWeights.SemiBold);

    public static M3TextStyle TitleSmall { get; } = new("Title small", 14, 20, FontWeights.SemiBold);

    public static M3TextStyle BodyLarge { get; } = new("Body large", 16, 24, FontWeights.Regular);

    public static M3TextStyle BodyMedium { get; } = new("Body medium", 14, 20, FontWeights.Regular);

    public static M3TextStyle BodySmall { get; } = new("Body small", 12, 16, FontWeights.Regular);

    public static M3TextStyle LabelLarge { get; } = new("Label large", 14, 20, FontWeights.SemiBold);

    public static M3TextStyle LabelMedium { get; } = new("Label medium", 12, 16, FontWeights.SemiBold);

    public static M3TextStyle LabelSmall { get; } = new("Label small", 11, 16, FontWeights.SemiBold);

    /// <summary>Every step, for the type specimen page.</summary>
    public static IReadOnlyList<M3TextStyle> Scale { get; } =
    [
        DisplayLarge, DisplayMedium, DisplaySmall,
        HeadlineLarge, HeadlineMedium, HeadlineSmall,
        TitleLarge, TitleMedium, TitleSmall,
        BodyLarge, BodyMedium, BodySmall,
        LabelLarge, LabelMedium, LabelSmall,
    ];

    /// <summary>Creates a text block in the given style and colour.</summary>
    public static TextBlock Create(string text, M3TextStyle style, ColorRole role = ColorRole.OnSurface)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = style.Family,
            FontSize = style.Size,
            FontWeight = style.Weight,
            LineHeight = style.LineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap,
            Foreground = M3ThemeManager.Current.Brush(role),
            VerticalAlignment = VerticalAlignment.Center,
        };

        TextOptions.SetTextFormattingMode(block, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(block, TextRenderingMode.ClearType);
        return block;
    }

    /// <summary>Applies a style and colour to an existing text block.</summary>
    public static void Apply(TextBlock block, M3TextStyle style, ColorRole role = ColorRole.OnSurface)
    {
        block.FontFamily = style.Family;
        block.FontSize = style.Size;
        block.FontWeight = style.Weight;
        block.LineHeight = style.LineHeight;
        block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        block.Foreground = M3ThemeManager.Current.Brush(role);
    }

    private static FontFamily Resolve(params string[] candidates)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var family in Fonts.SystemFontFamilies)
            {
                installed.Add(family.Source);
            }
        }
        catch (Exception)
        {
            // Font enumeration can fail in constrained environments; the first candidate is a fine
            // default and WPF falls back on its own if it is missing.
        }

        var chosen = candidates.FirstOrDefault(installed.Contains) ?? candidates[0];
        return new FontFamily(chosen);
    }
}
