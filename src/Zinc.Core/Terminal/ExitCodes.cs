using System.Globalization;

namespace Zinc.Terminal;

/// <summary>A program's exit code as people read it: small codes in decimal (1, 255), Windows' status codes in hex.</summary>
public static class ExitCodes
{
    /// <summary>"1", "255", "0xC000013A" (a console closed with Ctrl+C), "0xFFFFFFFF".</summary>
    public static string Format(uint code) =>
        code < 0x10000 ? code.ToString(CultureInfo.InvariantCulture) : $"0x{code:X8}";
}
