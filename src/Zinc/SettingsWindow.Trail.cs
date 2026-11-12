using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Zinc.Settings;

namespace Zinc;

// Shows where the page is, the way Windows Settings does. The heading is a breadcrumb ("Appearance › Color schemes",
// "Profiles › Fedora › Shaders") in which each earlier step goes back to that step's page, and the title bar's back
// arrow goes one step up.
internal sealed partial class SettingsWindow
{
    private TitleBar _titleBar = null!;

    /// <summary>The steps to the page shown: each one's name, and how to go back to it (null for the page itself).</summary>
    private List<(string Label, Action? Go)> Trail()
    {
        if (_search.Text.Trim().Length > 0)
        {
            return [("Results", null)];
        }

        var trail = new List<(string Label, Action? Go)>();
        if (_section == "profiles" && _editing is { } id)
        {
            trail.Add(("Profiles", () => GoUp(keepProfile: false)));
            trail.Add((_config.FindProfile(id)?.Name ?? id, () => GoUp(keepProfile: true)));
        }
        else
        {
            trail.Add((SettingsCatalog.Sections.First(s => s.Id == _section).Title, () => GoUp(keepProfile: false)));
        }

        if (_editingShaders)
        {
            trail.Add(("Shaders", LeaveSchemeEditor));
        }

        if (_editingSchemes)
        {
            trail.Add(("Color schemes", LeaveSchemeEditor));
        }

        if (_editingScheme is { } scheme)
        {
            trail.Add((scheme, null));
        }

        trail[^1] = (trail[^1].Label, null); // the page itself
        return trail;
    }

    /// <summary>
    /// Goes back to the section's page (or to the profile's when <paramref name="keepProfile"/> is set), leaving any
    /// sub-page.
    /// </summary>
    private void GoUp(bool keepProfile)
    {
        _editingScheme = null;
        _schemeOriginal = null;
        if (_editingShaders || _editingSchemes)
        {
            _editingShaders = _editingSchemes = false;
            _previewedShader = null;
            _previewedScheme = null;
            PreviewForLibrary(open: false);
        }

        if (!keepProfile)
        {
            _editing = null;
        }

        Render();
        _scroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    /// <summary>
    /// The page's heading, which is the trail as a breadcrumb at the title's size. A trail of one step shows just its name.
    /// </summary>
    private FrameworkElement Heading()
    {
        var trail = Trail();
        UpdateBackButton(trail);
        var bar = new BreadcrumbBar
        {
            ItemsSource = trail.Select(t => t.Label).ToList(),
            Margin = new Thickness(0, 8, 0, 12),
        };
        // The breadcrumb gets the title's size and weight, because Windows Settings' breadcrumbs are its page titles.
        // These resources are set before the template applies.
        var title = TextStyle("TitleTextBlockStyle");
        bar.Resources["BreadcrumbBarItemThemeFontSize"] = Setter(title, TextBlock.FontSizeProperty) ?? 28.0;
        bar.Resources["BreadcrumbBarItemFontWeight"] = FontWeights.SemiBold;
        bar.Resources["BreadcrumbBarChevronFontSize"] = 16.0;
        // Room around the chevron at this size. WinUI's 2 px suits 14 px text, and at 28 px the words crowd the chevron.
        bar.Resources["BreadcrumbBarChevronPadding"] = new Thickness(10, 0, 10, 0);
        AutomationProperties.SetName(bar, string.Join(", ", trail.Select(t => t.Label)));
        bar.ItemClicked += (_, e) =>
        {
            if (e.Index < trail.Count && trail[e.Index].Go is { } go)
            {
                go();
            }
        };
        return bar;
    }

    /// <summary>Updates the title bar's back arrow, which is shown on a sub-page and goes one step up the trail.</summary>
    private void UpdateBackButton(List<(string Label, Action? Go)> trail)
    {
        _titleBar.IsBackButtonVisible = trail.Count > 1;
        _backTo = trail.Count > 1 ? trail[^2].Go : null;
    }

    private Action? _backTo;

    /// <summary>A style's value for <paramref name="property"/>, or null (the theme's type ramp, read rather than copied).</summary>
    private static object? Setter(Style style, DependencyProperty property) =>
        style.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == property)?.Value
        ?? (style.BasedOn is { } basedOn ? Setter(basedOn, property) : null);
}
