using Zinc.Settings;

namespace Zinc.Tests;

internal static class TestConfig
{
    /// <summary>
    /// <paramref name="config"/> with one config.toml setting ("colors.bold") set to a value, as the reader applies it.
    /// Null when the value doesn't fit. An int or float literal is passed on as the long or double Tomlyn would give.
    /// </summary>
    public static Config? With(this Config config, string key, object value) =>
        SettingsCatalog.Apply(config, key, value switch
        {
            int i => (long)i,
            float f => (double)f,
            _ => value,
        });
}
