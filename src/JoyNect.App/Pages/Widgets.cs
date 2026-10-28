using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using JoyNect.App.Services;
using JoyNect.Core.Design;
using JoyNect.Core.Protocol;
using JoyNect.Core.Devices;
using JoyNect.Core.Hid;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;

namespace JoyNect.App.Pages;

/// <summary>Small pieces shared by more than one page.</summary>
internal static class Widgets
{
    /// <summary>
    /// A coloured status pill.
    /// </summary>
    /// <remarks>
    /// Built from a border rather than a chip control because these pills are labels, not buttons:
    /// nothing here is clickable, and a state label that looks pressable is a small lie.
    /// </remarks>
    public static Border Pill(string text, ColorRole container, ColorRole content, M3Glyph? glyph = null)
    {
        var theme = M3ThemeManager.Current;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (glyph is not null)
        {
            var icon = M3Icons.Create(glyph.Value, 14, theme.Brush(content));
            icon.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(icon);
        }

        row.Children.Add(new TextBlock
        {
            Text = text,
            FontFamily = M3Typography.Text,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = theme.Brush(content),
            VerticalAlignment = VerticalAlignment.Center,
        });

        return new Border
        {
            Background = theme.Brush(container),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 4, 10, 4),
            Height = 28,
            Child = row,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>A pill for a controller's link state.</summary>
    public static Border StatePill(ControllerSnapshot controller) => controller.State switch
    {
        ControllerLinkState.Ready => Pill("Ready", ColorRole.SecondaryContainer, ColorRole.OnSecondaryContainer, M3Glyph.Check),
        ControllerLinkState.Handshaking => Pill("Connecting", ColorRole.TertiaryContainer, ColorRole.OnTertiaryContainer, M3Glyph.Refresh),
        ControllerLinkState.Stale => Pill("No signal", ColorRole.ErrorContainer, ColorRole.OnErrorContainer, M3Glyph.Warning),
        ControllerLinkState.Failed => Pill("Failed", ColorRole.ErrorContainer, ColorRole.OnErrorContainer, M3Glyph.Error),
        ControllerLinkState.Stopped => Pill("Disconnected", ColorRole.SurfaceContainerHighest, ColorRole.OnSurfaceVariant, M3Glyph.Bluetooth),
        _ => Pill("Waiting", ColorRole.SurfaceContainerHighest, ColorRole.OnSurfaceVariant, M3Glyph.Bluetooth),
    };

    /// <summary>A small label and value pair, stacked.</summary>
    public static StackPanel Stat(string label, string value)
    {
        var valueBlock = M3Typography.Create(value, M3Typography.TitleMedium);
        var labelBlock = M3Typography.Create(label, M3Typography.LabelSmall, ColorRole.OnSurfaceVariant);
        labelBlock.Margin = new Thickness(0, 2, 0, 0);

        var column = new StackPanel { Orientation = Orientation.Vertical };
        column.Children.Add(valueBlock);
        column.Children.Add(labelBlock);
        return column;
    }

    /// <summary>A name on the left, a value on the right.</summary>
    public static Grid Detail(string name, string value, bool wrap = true)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = M3Typography.Create(name, M3Typography.BodyMedium, ColorRole.OnSurfaceVariant);
        var content = M3Typography.Create(value, M3Typography.BodyMedium);
        content.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        content.TextTrimming = TextTrimming.CharacterEllipsis;

        grid.Children.Add(label);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    /// <summary>An empty state: an icon, an explanation and one action.</summary>
    public static M3Card Empty(M3Glyph glyph, string title, string body, FrameworkElement? action = null)
    {
        var icon = M3Icons.Create(glyph, 34, M3ThemeManager.Current.OnSurfaceVariant);

        var text = M3Layout.Column(
            8,
            M3Typography.Create(title, M3Typography.TitleLarge),
            M3Typography.Create(body, M3Typography.BodyMedium, ColorRole.OnSurfaceVariant));

        ((TextBlock)text.Children[1]).MaxWidth = 560;

        var content = M3Layout.Row(20, icon, text);
        content.VerticalAlignment = VerticalAlignment.Center;

        if (action is null)
        {
            return new M3Card(M3CardVariant.Filled, 24) { Child = content };
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(content);

        action.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(action, 1);
        grid.Children.Add(action);

        return new M3Card(M3CardVariant.Filled, 24) { Child = grid };
    }

    /// <summary>A notice at the top of a page, for a problem the user can act on.</summary>
    public static Border Banner(string title, string body, M3Glyph glyph, FrameworkElement? action = null, bool error = true)
    {
        var theme = M3ThemeManager.Current;
        var container = error ? theme.ErrorContainer : theme.PrimaryContainer;
        var content = error ? theme.OnErrorContainer : theme.OnPrimaryContainer;

        var icon = M3Icons.Create(glyph, 24, content);

        var text = M3Layout.Column(
            4,
            M3Typography.Create(title, M3Typography.TitleSmall),
            M3Typography.Create(body, M3Typography.BodySmall));

        ((TextBlock)text.Children[0]).Foreground = content;
        ((TextBlock)text.Children[1]).Foreground = content;
        ((TextBlock)text.Children[1]).MaxWidth = 620;

        var row = M3Layout.Row(14, icon, text);
        row.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(row);

        if (action is not null)
        {
            action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(action, 1);
            grid.Children.Add(action);
        }

        return new Border
        {
            Background = container,
            CornerRadius = new CornerRadius(M3Shape.Medium),
            Padding = new Thickness(16),
            Child = grid,
        };
    }

    /// <summary>A button that opens a URL in the default browser.</summary>
    public static M3Button LinkButton(string label, string uri, M3Glyph icon = M3Glyph.ExternalLink)
    {
        var button = new M3Button(label, M3ButtonVariant.Text, icon);
        button.Click += (_, _) => Open(uri);
        ToolTipService.SetToolTip(button, uri);
        return button;
    }

    /// <summary>Opens a URL, or does nothing if there is no shell to hand it to.</summary>
    public static void Open(string uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // A locked-down machine may not have a browser association; there is nothing useful to do.
        }
    }

    /// <summary>Copies text to the clipboard, tolerating another process holding it.</summary>
    public static bool Copy(string text, Action<string>? notify = null)
    {
        try
        {
            Clipboard.SetText(text);
            notify?.Invoke("Copied to the clipboard");
            return true;
        }
        catch (Exception)
        {
            notify?.Invoke("The clipboard is busy; try again in a moment");
            return false;
        }
    }

    /// <summary>A body paragraph.</summary>
    public static TextBlock Paragraph(
        string text,
        M3TextStyle? style = null,
        ColorRole role = ColorRole.OnSurfaceVariant,
        double maxWidth = 720)
    {
        var block = M3Typography.Create(text, style ?? M3Typography.BodyMedium, role);
        block.MaxWidth = maxWidth;
        block.HorizontalAlignment = HorizontalAlignment.Left;
        return block;
    }

    /// <summary>A paragraph with an inline link.</summary>
    public static TextBlock WithLink(string before, string linkText, string uri, string after = "")
    {
        var block = M3Typography.Create(string.Empty, M3Typography.BodyMedium, ColorRole.OnSurfaceVariant);
        block.MaxWidth = 720;

        if (before.Length > 0)
        {
            block.Inlines.Add(new Run(before));
        }

        var link = new Hyperlink(new Run(linkText))
        {
            Foreground = M3ThemeManager.Current.Primary,
            TextDecorations = null,
        };

        link.Click += (_, _) => Open(uri);
        block.Inlines.Add(link);

        if (after.Length > 0)
        {
            block.Inlines.Add(new Run(after));
        }

        return block;
    }

    /// <summary>A battery row: a bar, a label and a charging note.</summary>
    public static FrameworkElement Battery(ControllerSnapshot controller)
    {
        var theme = M3ThemeManager.Current;
        var bar = new M3LinearProgress
        {
            Value = controller.BatteryFraction,
            Width = 120,
            IsLow = controller.Battery is BatteryLevel.Critical or BatteryLevel.Empty,
        };

        var label = M3Typography.Create(
            controller.Battery == BatteryLevel.Unknown ? "Battery unknown" : $"{controller.BatteryLabel} battery",
            M3Typography.LabelMedium,
            ColorRole.OnSurfaceVariant);

        var row = M3Layout.Row(10, M3Icons.Create(M3Glyph.Battery, 18, theme.OnSurfaceVariant), bar, label);

        if (controller.Charging)
        {
            row.Children.Add(Pill("Charging", ColorRole.SecondaryContainer, ColorRole.OnSecondaryContainer, M3Glyph.Bolt));
        }

        return row;
    }

    /// <summary>A rounding helper for values that come from a controller.</summary>
    public static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>The connection kind in words.</summary>
    public static string ConnectionLabel(ControllerSnapshot controller) => controller.Connection switch
    {
        ConnectionKind.Usb => "USB",
        ConnectionKind.Bluetooth => "Bluetooth",
        _ => "Unknown link",
    };
}
