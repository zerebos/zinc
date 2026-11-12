namespace Zinc;

/// <summary>Switches for automated screenshots.</summary>
internal static class TestHooks
{
    /// <summary>
    /// ZINC_TEST_DETERMINISTIC=1 turns off everything that varies between runs of the same build. The cursor doesn't
    /// blink and always draws as focused, because whether a new window gets focus is up to Windows. Windows have no Mica
    /// backdrop, because Mica samples what's behind the window and shows through unfocused panes' transparent borders.
    /// The welcome page's Next/Back and the new-workspace page's tab draw no focus outline, and that page doesn't focus
    /// its filter box, because whether either looks focused depends on the window being in front, or on how Windows took
    /// the test's last input.
    /// </summary>
    public static readonly bool Deterministic = Environment.GetEnvironmentVariable("ZINC_TEST_DETERMINISTIC") == "1";

    /// <summary>ZINC_TEST_STILL=1: attention dots don't pulse, so a capture shows them at full strength (Mica stays).</summary>
    public static readonly bool Still = Deterministic || Environment.GetEnvironmentVariable("ZINC_TEST_STILL") == "1";

    /// <summary>ZINC_TEST_UNFINISHED_SETTINGS=1: Settings shows the settings the app doesn't honor yet (<c>SettingDef.Unfinished</c>).</summary>
    public static readonly bool UnfinishedSettings = Environment.GetEnvironmentVariable("ZINC_TEST_UNFINISHED_SETTINGS") == "1";
}
