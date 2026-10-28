using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace JoyNect.Ui.Controls;

/// <summary>Control templates for the code-built controls.</summary>
/// <remarks>
/// The built-in templates add Windows chrome — borders, gradients, a pressed offset — that fights a
/// Material surface. These controls draw everything themselves, so the template only has to present
/// their content; everything else (shape, state layer, focus ring, elevation) is composed in code
/// where it can be animated and shared.
/// </remarks>
internal static class M3Templates
{
    /// <summary>Presents content and nothing else.</summary>
    public static ControlTemplate ContentOnly { get; } = CreateContentOnly();

    private static ControlTemplate CreateContentOnly()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(
            FrameworkElement.HorizontalAlignmentProperty,
            HorizontalAlignment.Stretch);
        presenter.SetValue(
            FrameworkElement.VerticalAlignmentProperty,
            VerticalAlignment.Stretch);


        var template = new ControlTemplate(typeof(ButtonBase))
        {
            VisualTree = presenter,
        };

        template.Seal();
        return template;
    }
}
