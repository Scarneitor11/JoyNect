using System.Windows;
using System.Windows.Markup;
using JoyNect.Core.Design;

namespace JoyNect.Ui.Theme;

/// <summary>
/// Replaces Windows' scroll bars with Material ones.
/// </summary>
/// <remarks>
/// The stock scroll bar is the single most visible piece of Windows chrome left in a WPF window: a
/// grey trough with two arrow buttons and a bevelled thumb. It cannot be tinted with the scheme, so
/// it is replaced once, globally, with a slim thumb on an invisible track. The style is parsed from
/// XAML because building a template through <c>FrameworkElementFactory</c> is far longer and no easier
/// to read.
/// </remarks>
public static class M3Scrollbars
{
    private const string Xaml = """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               xmlns:primitives="clr-namespace:System.Windows.Controls.Primitives;assembly=PresentationFramework"
               TargetType="{x:Type ScrollBar}">
          <Setter Property="Width" Value="10" />
          <Setter Property="Background" Value="Transparent" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="{x:Type ScrollBar}">
                <Grid Background="Transparent" Width="12">
                  <primitives:Track x:Name="PART_Track" IsDirectionReversed="True" Margin="0,4">
                    <primitives:Track.DecreaseRepeatButton>
                      <RepeatButton Command="{x:Static ScrollBar.PageUpCommand}" Opacity="0" Focusable="False" />
                    </primitives:Track.DecreaseRepeatButton>
                    <primitives:Track.Thumb>
                      <Thumb Width="4" HorizontalAlignment="Center" Cursor="Hand">
                        <Thumb.Template>
                          <ControlTemplate TargetType="{x:Type Thumb}">
                            <Border x:Name="ThumbBody" CornerRadius="2" Background="{DynamicResource M3ScrollThumb}" />
                            <ControlTemplate.Triggers>
                              <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="ThumbBody" Property="Background" Value="{DynamicResource M3ScrollThumbHover}" />
                              </Trigger>
                            </ControlTemplate.Triggers>
                          </ControlTemplate>
                        </Thumb.Template>
                      </Thumb>
                    </primitives:Track.Thumb>
                    <primitives:Track.IncreaseRepeatButton>
                      <RepeatButton Command="{x:Static ScrollBar.PageDownCommand}" Opacity="0" Focusable="False" />
                    </primitives:Track.IncreaseRepeatButton>
                  </primitives:Track>
                </Grid>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """;

    private static bool _installed;

    /// <summary>Installs the implicit scroll bar style and keeps its brushes in step with the theme.</summary>
    public static void Install(Application application)
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        var style = (Style)XamlReader.Parse(Xaml);
        application.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = style;
        UpdateBrushes();

        M3ThemeManager.Changed += (_, _) => UpdateBrushes();
    }

    private static void UpdateBrushes()
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        var theme = M3ThemeManager.Current;
        application.Resources["M3ScrollThumb"] = theme.Overlay(ColorRole.OnSurfaceVariant, 0.32);
        application.Resources["M3ScrollThumbHover"] = theme.Overlay(ColorRole.OnSurfaceVariant, 0.7);
    }
}
