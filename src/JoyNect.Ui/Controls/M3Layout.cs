using System.Windows;
using System.Windows.Controls;
using JoyNect.Core.Design;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>Small layout helpers so pages stay readable.</summary>
/// <remarks>
/// WPF has no gap property, so spacing ends up either repeated per element or hidden in margins that
/// are impossible to reason about. These helpers apply one spacing rule per container instead, which
/// is also what keeps the rhythm consistent across pages.
/// </remarks>
public static class M3Layout
{
    /// <summary>A horizontal stack with a gap.</summary>
    public static StackPanel Row(double spacing = 8.0, params UIElement[] items)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        for (var i = 0; i < items.Length; i++)
        {
            if (i > 0 && spacing > 0)
            {
                items[i].SetValue(FrameworkElement.MarginProperty, new Thickness(spacing, 0, 0, 0));
            }

            panel.Children.Add(items[i]);
        }

        return panel;
    }

    /// <summary>A wrapping row with gaps, for chip groups.</summary>
    public static WrapPanel Wrap(double horizontalSpacing = 8.0, double verticalSpacing = 8.0, params FrameworkElement[] items)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, ItemHeight = double.NaN };
        foreach (var item in items)
        {
            item.Margin = new Thickness(0, 0, horizontalSpacing, verticalSpacing);
            panel.Children.Add(item);
        }

        return panel;
    }

    /// <summary>A vertical stack with a gap.</summary>
    public static StackPanel Column(double spacing = 8.0, params UIElement[] items)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        for (var i = 0; i < items.Length; i++)
        {
            if (i > 0 && spacing > 0)
            {
                items[i].SetValue(FrameworkElement.MarginProperty, new Thickness(0, spacing, 0, 0));
            }

            panel.Children.Add(items[i]);
        }

        return panel;
    }

    /// <summary>Space that grows to fill whatever is left.</summary>
    public static FrameworkElement Spacer() => new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };

    /// <summary>A heading and optional supporting text above some content.</summary>
    public static StackPanel Section(string title, string? supporting, UIElement content, double spacing = 16.0)
    {
        var heading = M3Typography.Create(title, M3Typography.TitleLarge);
        var parts = new List<UIElement> { heading };

        if (!string.IsNullOrWhiteSpace(supporting))
        {
            var subtitle = M3Typography.Create(supporting, M3Typography.BodyMedium, ColorRole.OnSurfaceVariant);
            subtitle.Margin = new Thickness(0, 4, 0, 0);
            parts.Add(subtitle);
        }

        var header = Column(0, [.. parts]);
        header.Margin = new Thickness(0, 0, 0, spacing);
        return Column(0, header, content);
    }

    /// <summary>A page header with a large title and supporting text.</summary>
    public static StackPanel Hero(string title, string subtitle, params UIElement[] trailing)
    {
        var titleBlock = M3Typography.Create(title, M3Typography.DisplaySmall);
        var subtitleBlock = M3Typography.Create(subtitle, M3Typography.BodyLarge, ColorRole.OnSurfaceVariant);
        subtitleBlock.Margin = new Thickness(0, 8, 0, 0);
        subtitleBlock.MaxWidth = 720;

        var text = Column(0, titleBlock, subtitleBlock);
        text.HorizontalAlignment = HorizontalAlignment.Left;
        text.VerticalAlignment = VerticalAlignment.Center;

        if (trailing.Length == 0)
        {
            return text;
        }

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);

        var actions = Row(8, trailing);
        actions.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);

        return Column(0, row);
    }

    /// <summary>A card with a title, supporting text and optional trailing content.</summary>
    public static M3Card SettingCard(string title, string supporting, FrameworkElement trailing, M3CardVariant variant = M3CardVariant.Filled)
    {
        var text = Column(
            0,
            M3Typography.Create(title, M3Typography.TitleMedium),
            M3Typography.Create(supporting, M3Typography.BodyMedium, ColorRole.OnSurfaceVariant));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);

        trailing.VerticalAlignment = VerticalAlignment.Center;
        trailing.Margin = new Thickness(20, 0, 0, 0);
        Grid.SetColumn(trailing, 1);
        grid.Children.Add(trailing);

        return new M3Card(variant) { Child = grid };
    }

    /// <summary>A hairline divider.</summary>
    public static Border Divider(double opacity = 1.0) => new()
    {
        Height = 1,
        Background = M3ThemeManager.Current.Brush(ColorRole.OutlineVariant),
        Opacity = opacity,
        Margin = new Thickness(0, 16, 0, 16),
    };
}
