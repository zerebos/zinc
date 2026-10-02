using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Zinc;

/// <summary>
/// <c>backdrop = acrylic</c> ("Frosted glass") with its tint under the config's control
/// (<c>backdrop-tint</c>): WinUI's DesktopAcrylicBackdrop has fixed tints. Kept glassy while the window
/// is inactive too (Windows' default turns acrylic solid then, which loses the look).
/// </summary>
internal sealed class GlassBackdrop(Color tint, double tintOpacity) : SystemBackdrop
{
    private DesktopAcrylicController? _controller;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (!DesktopAcrylicController.IsSupported())
        {
            Log.Write("backdrop: acrylic isn't supported here");
            return;
        }

        _controller = new DesktopAcrylicController
        {
            TintColor = tint,
            TintOpacity = (float)tintOpacity,
            LuminosityOpacity = (float)Math.Max(tintOpacity, 0.05), // how much the wallpaper's light shows: with the tint
            FallbackColor = tint,
        };
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(new SystemBackdropConfiguration { IsInputActive = true, Theme = SystemBackdropTheme.Dark });
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
        base.OnTargetDisconnected(disconnectedTarget);
    }
}
