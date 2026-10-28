using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JoyNect.App.Services;
using JoyNect.Core.Design;
using JoyNect.Core.Mapping;
using JoyNect.Core.Protocol;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;

namespace JoyNect.App.Pages;

/// <summary>
/// Button remapping and stick shaping.
/// </summary>
/// <remarks>
/// Remaps apply to the live pad immediately: a mapping change that only took effect after a
/// reconnection would be untestable, and this is exactly the page where a user wants to try a change
/// in the game they are already in. Stick changes are written to disk on a short delay so dragging a
/// slider does not mean forty file writes.
/// </remarks>
internal sealed class MappingPage : AppPage
{
    private static readonly (SwitchButtons Button, string Label)[] Mappable =
    [
        (SwitchButtons.A, "A"),
        (SwitchButtons.B, "B"),
        (SwitchButtons.X, "X"),
        (SwitchButtons.Y, "Y"),
        (SwitchButtons.Up, "D-pad up"),
        (SwitchButtons.Down, "D-pad down"),
        (SwitchButtons.Left, "D-pad left"),
        (SwitchButtons.Right, "D-pad right"),
        (SwitchButtons.L, "L"),
        (SwitchButtons.R, "R"),
        (SwitchButtons.Zl, "ZL"),
        (SwitchButtons.Zr, "ZR"),
        (SwitchButtons.Minus, "Minus"),
        (SwitchButtons.Plus, "Plus"),
        (SwitchButtons.Home, "Home"),
        (SwitchButtons.Capture, "Capture"),
        (SwitchButtons.LeftStickClick, "Left stick click"),
        (SwitchButtons.RightStickClick, "Right stick click"),
        (SwitchButtons.LeftStickSr, "Left rail SR"),
        (SwitchButtons.LeftStickSl, "Left rail SL"),
        (SwitchButtons.RightStickSr, "Right rail SR"),
        (SwitchButtons.RightStickSl, "Right rail SL"),
    ];

    private readonly DispatcherTimer _saveDebounce;
    private readonly List<Action> _pendingSaves = [];

    public MappingPage()
        : base("mapping", "Mapping", "Which Switch button becomes which Xbox button, and how the sticks respond.", M3Glyph.Swap)
    {
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _saveDebounce.Tick += (_, _) => FlushSaves();
    }

    public override void OnHidden()
    {
        FlushSaves();
        base.OnHidden();
    }

    protected override void Build()
    {
        var reset = new M3Button("Reset to default", M3ButtonVariant.Outlined, M3Glyph.Refresh);
        reset.Click += (_, _) =>
        {
            State.ResetMapping();
            RebuildPage();
        };

        Body.Children.Add(Hero(null, reset));
        Body.Children.Add(new Border { Height = 20 });

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        columns.Children.Add(BuildButtonsCard());

        var sticks = M3Layout.Column(16, BuildStickEditor("Left stick", isRight: false), BuildStickEditor("Right stick", isRight: true));
        Grid.SetColumn(sticks, 2);
        columns.Children.Add(sticks);

        Body.Children.Add(columns);

        Body.Children.Add(new Border { Height = 24 });

        var note = Widgets.Paragraph(
            "The default mapping keeps each button's physical position: the button on the right confirms, and ZL/ZR become the analog triggers. Changes apply to connected pads straight away and to every controller that connects later.",
            M3Typography.BodyMedium);

        Body.Children.Add(note);
    }

    public override void Refresh()
    {
        // Mapping is edited, not polled. Nothing here changes on its own.
    }

    private UIElement BuildButtonsCard()
    {
        var profile = State.Mapping;
        var options = Xbox360TargetNames.SelectableTargets;
        var names = options.Select(o => o.Name).ToList();
        var optionNames = new List<string> { "Not mapped" };
        optionNames.AddRange(names);

        var rows = new StackPanel { Orientation = Orientation.Vertical };
        var showRails = profile.Buttons.Keys.Any(b => b.Has(SwitchButtonExtensions.ShoulderRailButtons));

        foreach (var (button, label) in Mappable)
        {
            if (!showRails && button.Has(SwitchButtonExtensions.ShoulderRailButtons))
            {
                continue;
            }

            var target = profile.TargetFor(button);
            var selected = target == Xbox360Target.None
                ? "Not mapped"
                : options.FirstOrDefault(o => o.Target == target).Name ?? target.ToString();

            var select = new M3Select($"Map {label}") { MinWidth = 190, HorizontalAlignment = HorizontalAlignment.Right };
            select.SetOptions(optionNames, selected);
            select.SelectionChanged += (_, _) =>
            {
                var chosen = select.Value;
                var match = options.FirstOrDefault(o => string.Equals(o.Name, chosen, StringComparison.Ordinal));
                State.Remap(button, chosen == "Not mapped" ? null : match.Target);
            };

            var name = M3Typography.Create(label, M3Typography.BodyLarge);
            name.VerticalAlignment = VerticalAlignment.Center;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(name);
            select.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(select, 1);
            row.Children.Add(select);
            rows.Children.Add(row);
        }

        var note = Widgets.Paragraph(
            "Mapped by physical position: the right-hand face button is Xbox B, the bottom one is Xbox A, so the layout feels the same in your hands.",
            M3Typography.BodySmall);
        note.Margin = new Thickness(0, 6, 0, 0);

        return new M3Card(M3CardVariant.Filled)
        {
            Child = M3Layout.Column(
                0,
                M3Typography.Create("Buttons", M3Typography.TitleMedium),
                M3Typography.Create(
                    State.Pads.Count > 0 ? "Editing the mapping of the connected pad." : "Editing the mapping used by the next controller.",
                    M3Typography.BodySmall,
                    ColorRole.OnSurfaceVariant),
                note,
                new Border { Height = 16 },
                rows),
        };
    }

    private UIElement BuildStickEditor(string title, bool isRight)
    {
        var settings = isRight
            ? new StickSettings
            {
                Deadzone = State.Settings.RightDeadzone,
                Curve = State.Settings.RightCurve,
                InvertX = State.Settings.RightInvertX,
                InvertY = State.Settings.RightInvertY,
            }
            : new StickSettings
            {
                Deadzone = State.Settings.LeftDeadzone,
                Curve = State.Settings.LeftCurve,
                InvertX = State.Settings.LeftInvertX,
                InvertY = State.Settings.LeftInvertY,
            };

        var preview = new CurvePreview { Settings = settings, Width = double.NaN, HorizontalAlignment = HorizontalAlignment.Stretch };

        var deadzone = new M3Slider
        {
            Minimum = 0,
            Maximum = 0.35,
            Value = settings.Deadzone,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ValueFormatter = value => $"{value * 100:0}%",
        };

        var curve = new M3Slider
        {
            Minimum = 0.5,
            Maximum = 2.0,
            Value = settings.Curve,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ValueFormatter = value => value.ToString("0.00", CultureInfo.InvariantCulture),
        };

        var deadzoneValue = M3Typography.Create($"{settings.Deadzone * 100:0}%", M3Typography.LabelLarge, ColorRole.Primary);
        var curveValue = M3Typography.Create(settings.Curve.ToString("0.00", CultureInfo.InvariantCulture), M3Typography.LabelLarge, ColorRole.Primary);

        var invertX = new M3Switch { IsSelected = settings.InvertX };
        invertX.AccessibilityName = $"Invert {title} X";
        var invertY = new M3Switch { IsSelected = settings.InvertY };
        invertY.AccessibilityName = $"Invert {title} Y";

        void Apply()
        {
            settings = settings with
            {
                Deadzone = deadzone.Value,
                Curve = curve.Value,
                InvertX = invertX.IsSelected,
                InvertY = invertY.IsSelected,
            };

            preview.Settings = settings;
            deadzoneValue.Text = $"{deadzone.Value * 100:0}%";
            curveValue.Text = curve.Value.ToString("0.00", CultureInfo.InvariantCulture);

            _pendingSaves.Clear();
            _pendingSaves.Add(() => State.SetStickSettings(isRight, settings));
            _saveDebounce.Stop();
            _saveDebounce.Start();
        }

        deadzone.ValueChanged += (_, _) => Apply();
        curve.ValueChanged += (_, _) => Apply();
        invertX.Click += (_, _) => Apply();
        invertY.Click += (_, _) => Apply();

        var invertRow = new StackPanel { Orientation = Orientation.Horizontal };
        invertRow.Children.Add(M3Typography.Create("Invert X", M3Typography.BodyMedium, ColorRole.OnSurfaceVariant));
        invertRow.Children.Add(new Border { Width = 8 });
        invertRow.Children.Add(invertX);
        invertRow.Children.Add(new Border { Width = 20 });
        invertRow.Children.Add(M3Typography.Create("Invert Y", M3Typography.BodyMedium, ColorRole.OnSurfaceVariant));
        invertRow.Children.Add(new Border { Width = 8 });
        invertRow.Children.Add(invertY);

        return new M3Card(M3CardVariant.Filled)
        {
            Child = M3Layout.Column(
                14,
                M3Typography.Create(title, M3Typography.TitleMedium),
                preview,
                M3Layout.Column(4, LabelRow("Dead zone", deadzoneValue), deadzone),
                M3Layout.Column(4, LabelRow("Response curve", curveValue), curve),
                invertRow),
        };
    }

    // A slider with no name is a guessing game, so each one gets its own labelled header row.
    private static Grid LabelRow(string label, TextBlock value)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(M3Typography.Create(label, M3Typography.BodyMedium, ColorRole.OnSurfaceVariant));

        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        return row;
    }

    private void FlushSaves()
    {
        _saveDebounce.Stop();
        foreach (var save in _pendingSaves)
        {
            save();
        }

        _pendingSaves.Clear();
    }
}
