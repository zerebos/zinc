using Zinc.Settings;

namespace Zinc.Tests;

internal static class TestConfig
{
    /// <summary>
    /// <paramref name="config"/> with one config.toml setting ("colors.bold") set to a TOML value, as the reader applies
    /// it. Null when the value doesn't fit.
    /// </summary>
    public static Config? With(this Config config, string key, object value) => SettingsCatalog.Apply(config, key, value);
}
