namespace Zinc;

/// <summary>Switches for automated screenshots (see .scratch/tools/visual.ps1).</summary>
internal static class TestHooks
{
    /// <summary>
    /// ZINC_TEST_DETERMINISTIC=1: nothing that varies between runs of the same build — the
    /// cursor doesn't blink and always draws as focused (whether a new window gets focus is up
    /// to Windows), windows have no Mica backdrop (it samples what's behind the window, and
    /// shows through unfocused panes' transparent borders), the welcome page's Next/Back draw no
    /// focus outline, nor does the new-workspace page's tab, and that page doesn't focus its filter box (whether either looked
    /// focused depended on the window being in front, or on how Windows took the test's last input).
    /// </summary>
    public static readonly bool Deterministic = Environment.GetEnvironmentVariable("ZINC_TEST_DETERMINISTIC") == "1";

    /// <summary>ZINC_TEST_STILL=1: attention dots don't pulse, so a capture shows them at full strength (Mica stays).</summary>
    public static readonly bool Still = Deterministic || Environment.GetEnvironmentVariable("ZINC_TEST_STILL") == "1";
}
