namespace Phosphor.Terminal;

/// <summary>
/// Zoom levels: steps of 10% of the configured font size (level 0 = 100%, 1 = 110%, -1 = 90%),
/// so the size follows the configured one and reset always lands on 100%.
/// </summary>
public static class ZoomSteps
{
    public const int StepPercent = 10;

    public static int Percent(int level) => 100 + level * StepPercent;

    public static float Size(float configured, int level) => configured * Percent(level) / 100f;

    /// <summary>
    /// <paramref name="level"/> kept to the levels whose size fits within [min, max] (and above 0%),
    /// so the badge never shows a percentage the font size can't have.
    /// </summary>
    public static int Clamp(int level, float configured, float min, float max)
    {
        var lowest = Math.Max(-(100 / StepPercent) + 1, (int)Math.Ceiling((min / configured * 100 - 100) / StepPercent - 1e-4));
        var highest = (int)Math.Floor((max / configured * 100 - 100) / StepPercent + 1e-4);
        return lowest > highest ? 0 : Math.Clamp(level, lowest, highest);
    }
}
