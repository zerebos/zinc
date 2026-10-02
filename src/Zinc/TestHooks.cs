namespace Zinc;

/// <summary>Switches for automated screenshots (see .scratch/tools/visual.ps1).</summary>
internal static class TestHooks
{
    /// <summary>
    /// ZINC_TEST_DETERMINISTIC=1: nothing that varies between runs of the same build — the
    /// cursor doesn't blink and always draws as focused (whether a new window gets focus is up
    /// to Windows), and windows have no Mica backdrop (it samples what's behind the window, and
    /// shows through unfocused panes' transparent borders).
    /// </summary>
    public static readonly bool Deterministic = Environment.GetEnvironmentVariable("ZINC_TEST_DETERMINISTIC") == "1";

    /// <summary>ZINC_TEST_STILL=1: attention dots don't pulse, so a capture shows them at full strength (Mica stays).</summary>
    public static readonly bool Still = Deterministic || Environment.GetEnvironmentVariable("ZINC_TEST_STILL") == "1";
}
