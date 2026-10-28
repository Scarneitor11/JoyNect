using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using JoyNect.Core.Design;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Pages;

/// <summary>
/// A gallery of the design system: colour roles, the type scale and every component.
/// </summary>
/// <remarks>
/// Kept in the shipping app rather than in a mock-up, for two reasons. It is the fastest way to see
/// whether a theme change or a component tweak broke something, and it is genuinely useful to a user
/// who wants to know what the colours on screen mean.
/// </remarks>
public sealed class StyleGuidePage : Grid
{
    private readonly StackPanel _content = new() { Orientation = Orientation.Vertical };

    public StyleGuidePage()
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Content = _content,
        };

        _content.Margin = new Thickness(40, 32, 40, 48);
        _content.MaxWidth = 1000;
        _content.HorizontalAlignment = HorizontalAlignment.Left;

        Children.Add(scroll);
        RefreshTheme();
        Loaded += (_, _) => RefreshTheme();
    }

    /// <summary>Rebuilds the page for the current theme.</summary>
    public void RefreshTheme()
    {
        _content.Children.Clear();

        var theme = M3ThemeManager.Current;
        Background = theme.Surface;

        _content.Children.Add(M3Layout.Hero(
            "Design system",
            "Every colour, type step and control JoyNect uses, rendered from the live theme.",
            new M3Chip(theme.IsDark ? "Dark" : "Light", M3ChipVariant.Assist, theme.IsDark ? M3Glyph.Moon : M3Glyph.Sun)));

        AddSection("Colour", "Roles from the Material 3 scheme, generated from a seed colour with HCT.", BuildColourGrid());
        AddSection("Type", "The Material type scale, mapped to Windows' variable fonts.", BuildTypeSpecimen());
        AddSection("Buttons", "Five levels of emphasis, all reachable by keyboard.", BuildButtons());
        AddSection("Selection", "Chips, segmented buttons, switches and sliders.", BuildSelection());
        AddSection("Surfaces", "Cards carry the app's content and its depth.", BuildCards());
        AddSection("Status", "Progress, badges and metadata.", BuildStatus());
    }

    private void AddSection(string title, string supporting, UIElement content)
    {
        if (_content.Children.Count > 0)
        {
            var divider = M3Layout.Divider();
            divider.Margin = new Thickness(0, 32, 0, 32);
            _content.Children.Add(divider);
        }

        _content.Children.Add(M3Layout.Section(title, supporting, content));
    }

    private UIElement BuildColourGrid()
    {
        var groups = new List<UIElement>();

        foreach (var (name, roles) in new (string, ColorRole[])[]
        {
            ("Primary", [ColorRole.Primary, ColorRole.OnPrimary, ColorRole.PrimaryContainer, ColorRole.OnPrimaryContainer]),
            ("Secondary", [ColorRole.Secondary, ColorRole.OnSecondary, ColorRole.SecondaryContainer, ColorRole.OnSecondaryContainer]),
            ("Tertiary", [ColorRole.Tertiary, ColorRole.OnTertiary, ColorRole.TertiaryContainer, ColorRole.OnTertiaryContainer]),
            ("Error", [ColorRole.Error, ColorRole.OnError, ColorRole.ErrorContainer, ColorRole.OnErrorContainer]),
            ("Surface", [ColorRole.SurfaceContainerLowest, ColorRole.SurfaceContainerLow, ColorRole.SurfaceContainer, ColorRole.SurfaceContainerHigh, ColorRole.SurfaceContainerHighest]),
            ("Outline", [ColorRole.Outline, ColorRole.OutlineVariant, ColorRole.InverseSurface, ColorRole.InverseOnSurface]),
        })
        {
            // Equal columns rather than a wrap panel: the swatches then line up with the page grid and
            // use the full width, instead of huddling in the corner of a wide window.
            var row = new Grid();
            for (var i = 0; i < roles.Length; i++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var swatch = Swatch(roles[i], isLast: i == roles.Length - 1);
                Grid.SetColumn(swatch, i);
                row.Children.Add(swatch);
            }

            groups.Add(M3Layout.Column(
                8,
                M3Typography.Create(name, M3Typography.LabelLarge, ColorRole.OnSurfaceVariant),
                row));
        }

        return M3Layout.Column(20, [.. groups]);
    }

    private static UIElement Swatch(ColorRole role, bool isLast)
    {
        var theme = M3ThemeManager.Current;
        var colour = M3Theme.ToColor(theme.Scheme[role]);

        // Label text has to stay legible on any swatch, so black or white is chosen by measuring the
        // contrast rather than guessing from the tone.
        var textColor = ColorUtils.ContrastRatio(theme.Scheme[role], 0xFF000000) >= ColorUtils.ContrastRatio(theme.Scheme[role], 0xFFFFFFFF)
            ? Brushes.Black
            : Brushes.White;

        var label = new TextBlock
        {
            Text = ColorRoleInfoTable.NameOf(role),
            FontFamily = M3Typography.Text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = textColor,
            Margin = new Thickness(10, 0, 10, 2),
            TextWrapping = TextWrapping.Wrap,
        };

        var hex = new TextBlock
        {
            Text = ColorUtils.ToHex(theme.Scheme[role]),
            FontFamily = M3Typography.Text,
            FontSize = 10,
            Foreground = textColor,
            Opacity = 0.75,
            Margin = new Thickness(10, 0, 10, 9),
        };

        return new Border
        {
            Height = 76,
            Margin = new Thickness(0, 0, isLast ? 0 : 8, 0),
            CornerRadius = new CornerRadius(M3Shape.Medium),
            Background = new SolidColorBrush(colour),

            // Without a hairline the lightest surface swatches vanish into the page.
            BorderBrush = theme.OutlineVariant,
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Children = { label, hex },
            },
        };
    }

    private static UIElement BuildTypeSpecimen()
    {
        var rows = new List<UIElement>();

        foreach (var style in M3Typography.Scale)
        {
            var sample = M3Typography.Create("Bring your controllers home", style);
            var name = M3Typography.Create($"{style.Name} · {style.Size:0}/{style.LineHeight:0}", M3Typography.LabelSmall, ColorRole.OnSurfaceVariant);
            name.VerticalAlignment = VerticalAlignment.Bottom;
            name.Margin = new Thickness(0, 0, 0, 4);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(sample);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            rows.Add(row);
        }

        return M3Layout.Column(12, [.. rows]);
    }

    private static UIElement BuildButtons()
    {
        var variants = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var variant in Enum.GetValues<M3ButtonVariant>())
        {
            var button = new M3Button(Label(variant), variant);
            button.Margin = new Thickness(0, 0, 12, 12);
            variants.Children.Add(button);
        }

        var withIcon = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (variant, glyph) in new (M3ButtonVariant, M3Glyph)[]
        {
            (M3ButtonVariant.Filled, M3Glyph.Bolt),
            (M3ButtonVariant.Tonal, M3Glyph.Refresh),
            (M3ButtonVariant.Outlined, M3Glyph.Swap),
            (M3ButtonVariant.Text, M3Glyph.ExternalLink),
            (M3ButtonVariant.Elevated, M3Glyph.Sparkles),
        })
        {
            var button = new M3Button(Label(variant), variant, glyph);
            button.Margin = new Thickness(0, 0, 12, 12);
            withIcon.Children.Add(button);
        }

        var icons = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (variant, glyph) in new (M3IconButtonVariant, M3Glyph)[]
        {
            (M3IconButtonVariant.Standard, M3Glyph.Power),
            (M3IconButtonVariant.FilledTonal, M3Glyph.Play),
            (M3IconButtonVariant.Filled, M3Glyph.Bolt),
            (M3IconButtonVariant.Outlined, M3Glyph.Sliders),
        })
        {
            var button = new M3IconButton(glyph, variant) { Margin = new Thickness(0, 0, 12, 12) };
            icons.Children.Add(button);
        }

        var disabled = new M3Button("Disabled", M3ButtonVariant.Filled) { IsEnabled = false, Margin = new Thickness(0, 0, 12, 12) };
        icons.Children.Add(disabled);

        return M3Layout.Column(16, variants, withIcon, icons);
    }

    private static UIElement BuildSelection()
    {
        var chips = new WrapPanel();
        chips.Children.Add(new M3Chip("Connected", M3ChipVariant.Assist, M3Glyph.Check) { Margin = new Thickness(0, 0, 8, 8) });
        chips.Children.Add(new M3Chip("Bluetooth", M3ChipVariant.Assist, M3Glyph.Bluetooth) { Margin = new Thickness(0, 0, 8, 8) });

        var filter = new M3Chip("Motion", M3ChipVariant.Filter) { IsSelected = true, Margin = new Thickness(0, 0, 8, 8) };
        chips.Children.Add(filter);
        chips.Children.Add(new M3Chip("Rumble", M3ChipVariant.Filter) { Margin = new Thickness(0, 0, 8, 8) });
        chips.Children.Add(new M3Chip("Player 1", M3ChipVariant.Input) { Margin = new Thickness(0, 0, 8, 8) });

        var segmented = new M3SegmentedButtons();
        segmented.SetSegments("Light", "Dark", "System");
        segmented.SelectedIndex = 0;

        var switches = M3Layout.Row(24);
        switches.Children.Add(new M3Switch { IsSelected = true });
        switches.Children.Add(new M3Switch());

        var slider = new M3Slider { Width = 260, Value = 0.62 };
        var sliderRow = M3Layout.Row(16, M3Typography.Create("Dead zone", M3Typography.BodyMedium, ColorRole.OnSurfaceVariant), slider);

        return M3Layout.Column(20, chips, M3Layout.Row(24, segmented, switches), sliderRow);
    }

    private static UIElement BuildCards()
    {
        var cards = new UniformGrid { Columns = 3 };

        var filled = new M3Card(M3CardVariant.Filled)
        {
            Margin = new Thickness(0, 0, 12, 0),
            Child = M3Layout.Column(
                8,
                M3Typography.Create("Filled", M3Typography.TitleMedium),
                M3Typography.Create("Tinted surface, no shadow. The default for grouped content.", M3Typography.BodyMedium, ColorRole.OnSurfaceVariant)),
        };

        var elevated = new M3Card(M3CardVariant.Elevated)
        {
            Margin = new Thickness(0, 0, 12, 0),
            Child = M3Layout.Column(
                8,
                M3Typography.Create("Elevated", M3Typography.TitleMedium),
                M3Typography.Create("Lifted off the page with a soft shadow. Use for the one thing that matters.", M3Typography.BodyMedium, ColorRole.OnSurfaceVariant)),
        };

        var outlined = new M3Card(M3CardVariant.Outlined)
        {
            Child = M3Layout.Column(
                8,
                M3Typography.Create("Outlined", M3Typography.TitleMedium),
                M3Typography.Create("A hairline outline instead of a fill. Quietest of the three.", M3Typography.BodyMedium, ColorRole.OnSurfaceVariant)),
        };

        cards.Children.Add(filled);
        cards.Children.Add(elevated);
        cards.Children.Add(outlined);
        return cards;
    }

    private static UIElement BuildStatus()
    {
        var linear = new M3LinearProgress { Value = 0.64, Width = 320 };
        var stopped = new M3Chip("Stopped", M3ChipVariant.Assist, M3Glyph.Power);
        var connected = new M3Chip("Player 1", M3ChipVariant.Assist, M3Glyph.Gamepad);

        return M3Layout.Column(
            16,
            M3Layout.Row(12, linear),
            M3Layout.Row(8, connected, stopped));
    }

    private static string Label(M3ButtonVariant variant) => variant switch
    {
        M3ButtonVariant.Filled => "Filled",
        M3ButtonVariant.Tonal => "Tonal",
        M3ButtonVariant.Outlined => "Outlined",
        M3ButtonVariant.Text => "Text",
        _ => "Elevated",
    };
}
