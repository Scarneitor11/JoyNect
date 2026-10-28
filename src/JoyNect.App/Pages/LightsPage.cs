using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JoyNect.App.Services;
using JoyNect.Core.Design;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;

namespace JoyNect.App.Pages;

/// <summary>
/// Rumble strength, a way to test it, and the controller's player lights.
/// </summary>
/// <remarks>
/// Rumble testing is the fastest way to confirm that a controller is actually connected through the
/// whole chain: the pulse originates from the virtual pad's force feedback, not from a shortcut, so a
/// controller that buzzes here is one the app can talk to.
/// </remarks>
internal sealed class LightsPage : AppPage
{
    private readonly DispatcherTimer _stopTimer;
    private readonly DispatcherTimer _saveTimer;
    private M3Slider? _large;
    private M3Slider? _small;
    private M3Switch? _enabled;
    private M3Slider? _strength;
    private TextBlock? _largeValue;
    private TextBlock? _smallValue;
    private TextBlock? _strengthValue;
    private StackPanel? _lights;

    public LightsPage()
        : base("lights", "Lights & rumble", "Test the motors and set the player lights, for each controller.", M3Glyph.Vibration)
    {
        _stopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _stopTimer.Tick += (_, _) =>
        {
            _stopTimer.Stop();
            State.TestRumble(0, 0);
        };

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveRumble();
        };
    }

    public override void OnHidden()
    {
        _stopTimer.Stop();
        _saveTimer.Stop();
        base.OnHidden();
    }

    protected override void Build()
    {
        Body.Children.Add(Hero(null));
        Body.Children.Add(new Border { Height = 20 });

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        columns.Children.Add(BuildRumbleCard());

        var right = M3Layout.Column(16, BuildLightsCard(), BuildExplanationCard());
        Grid.SetColumn(right, 2);
        columns.Children.Add(right);

        Body.Children.Add(columns);
    }

    public override void Refresh()
    {
        if (_lights is null)
        {
            return;
        }

        var signature = string.Join(';', State.Controllers.Select(c => $"{c.Id}:{c.Slot}:{c.IsReady}"));
        if (signature == _lights.Tag as string)
        {
            return;
        }

        BuildLightsList();
    }

    private UIElement BuildRumbleCard()
    {
        _enabled = new M3Switch { IsSelected = State.Settings.RumbleEnabled };
        _enabled.AccessibilityName = "Game rumble";
        _enabled.Click += (_, _) => SaveRumble();

        var enabledRow = M3Layout.SettingCard(
            "Game rumble",
            "Pass force feedback from the game to the controller, scaled by the strength below.",
            _enabled);

        _strength = new M3Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = State.Settings.RumbleStrength,
            Width = 220,
            ValueFormatter = value => $"{value * 100:0}% strength",
        };

        _strengthValue = M3Typography.Create($"{State.Settings.RumbleStrength * 100:0}%", M3Typography.LabelLarge, ColorRole.Primary);
        _strength.ValueChanged += (_, _) =>
        {
            if (_strengthValue is not null)
            {
                _strengthValue.Text = $"{_strength!.Value * 100:0}%";
            }

            _saveTimer.Stop();
            _saveTimer.Start();
        };

        var strengthRow = new Grid();
        strengthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        strengthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        strengthRow.Children.Add(_strength);
        _strengthValue.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_strengthValue, 1);
        strengthRow.Children.Add(_strengthValue);

        _large = new M3Slider { Minimum = 0, Maximum = 255, Value = 160, Width = 220 };
        _small = new M3Slider { Minimum = 0, Maximum = 255, Value = 120, Width = 220 };
        _largeValue = M3Typography.Create("160", M3Typography.LabelLarge, ColorRole.Primary);
        _smallValue = M3Typography.Create("120", M3Typography.LabelLarge, ColorRole.Primary);

        _large.ValueChanged += (_, _) => _largeValue?.SetCurrentValue(TextBlock.TextProperty, $"{_large!.Value:0}");
        _small.ValueChanged += (_, _) => _smallValue?.SetCurrentValue(TextBlock.TextProperty, $"{_small!.Value:0}");

        var buzz = new M3Button("Pulse motors", M3ButtonVariant.Filled, M3Glyph.Vibration);
        buzz.Click += (_, _) =>
        {
            State.TestRumble((byte)_large!.Value, (byte)_small!.Value);
            _stopTimer.Stop();
            _stopTimer.Start();
        };

        var stop = new M3Button("Stop", M3ButtonVariant.Text, M3Glyph.Close);
        stop.Click += (_, _) =>
        {
            _stopTimer.Stop();
            State.TestRumble(0, 0);
        };

        var largeRow = Labelled("Large motor", _large, _largeValue);
        var smallRow = Labelled("Small motor", _small, _smallValue);

        var content = M3Layout.Column(
            16,
            M3Typography.Create("Rumble", M3Typography.TitleMedium),
            enabledRow,
            M3Typography.Create("Strength", M3Typography.LabelLarge),
            strengthRow,
            new Border { Height = 8 },
            M3Typography.Create("Test", M3Typography.LabelLarge),
            largeRow,
            smallRow,
            M3Layout.Row(8, buzz, stop));

        return new M3Card(M3CardVariant.Filled) { Child = content };
    }

    private static UIElement Labelled(string label, M3Slider slider, TextBlock value)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var text = M3Typography.Create(label, M3Typography.BodyMedium, ColorRole.OnSurfaceVariant);
        row.Children.Add(text);

        slider.Width = double.NaN;
        slider.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);

        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 2);
        row.Children.Add(value);

        return row;
    }

    private UIElement BuildLightsCard()
    {
        _lights = new StackPanel { Orientation = Orientation.Vertical };
        BuildLightsList();

        return new M3Card(M3CardVariant.Filled)
        {
            Child = M3Layout.Column(
                12,
                M3Typography.Create("Player lights", M3Typography.TitleMedium),
                M3Typography.Create(
                    "The four lights on the controller show which player it belongs to.",
                    M3Typography.BodySmall,
                    ColorRole.OnSurfaceVariant),
                _lights),
        };
    }

    private void BuildLightsList()
    {
        if (_lights is null)
        {
            return;
        }

        _lights.Children.Clear();
        var controllers = State.Controllers;

        if (controllers.Count == 0)
        {
            _lights.Children.Add(Widgets.Paragraph(
                "Connect a controller to set its lights.",
                M3Typography.BodyMedium));
            _lights.Tag = string.Empty;
            return;
        }

        foreach (var controller in controllers)
        {
            var segmented = new M3SegmentedButtons { Width = 200 };
            segmented.SetSegments("1", "2", "3", "4");
            segmented.SelectedIndex = Math.Clamp(controller.Slot - 1, 0, 3);

            var id = controller.Id;
            segmented.SelectionChanged += (_, _) =>
            {
                if (segmented.SelectedIndex >= 0)
                {
                    State.SetSlot(id, segmented.SelectedIndex + 1);
                }
            };

            var name = M3Typography.Create(controller.Model, M3Typography.LabelLarge);
            var state = M3Typography.Create(
                controller.IsReady ? "ready" : controller.StateLabel.ToLowerInvariant(),
                M3Typography.BodySmall,
                ColorRole.OnSurfaceVariant);

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(M3Layout.Column(2, name, state));

            segmented.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(segmented, 1);
            grid.Children.Add(segmented);
            _lights.Children.Add(grid);
        }

        _lights.Tag = string.Join(';', controllers.Select(c => $"{c.Id}:{c.Slot}:{c.IsReady}"));
    }

    private UIElement BuildExplanationCard() => new M3Card(M3CardVariant.Outlined)
    {
        Child = M3Layout.Column(
            8,
            M3Typography.Create("How this works", M3Typography.TitleMedium),
            Widgets.Paragraph(
                "Games send force feedback to the virtual pad; JoyNect scales it and encodes it into the controller's own rumble format. The HD rumble in a Joy-Con cannot reproduce an Xbox motor exactly, so the strength setting is a taste as much as a limit.",
                M3Typography.BodySmall),
            Widgets.Paragraph(
                "Player lights are set over the controller's own protocol and survive until the controller is turned off. Some third-party controllers ignore them.",
                M3Typography.BodySmall)),
    };

    private void SaveRumble()
    {
        if (_enabled is null || _strength is null)
        {
            return;
        }

        State.SetProfileRumble(_enabled.IsSelected, _strength.Value);
    }

}
