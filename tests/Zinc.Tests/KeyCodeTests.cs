using Zinc.Input;

namespace Zinc.Tests;

public class KeyCodeTests
{
#if WINDOWS
    /// <summary>
    /// The app turns a WinRT key into a <see cref="KeyCode"/> with a cast, and shows unnamed keys by the enum's
    /// name. Both only work while the two enums have the same names and values.
    /// </summary>
    [Fact]
    public void MatchesWinRtsVirtualKey()
    {
        static IEnumerable<string> Members<T>() where T : struct, Enum =>
            Enum.GetNames<T>().Select(name => $"{name} = {Convert.ToInt32(Enum.Parse<T>(name))}").Order();

        Assert.Equal(Members<Windows.System.VirtualKey>(), Members<KeyCode>());
    }
#endif

    [Fact]
    public void LettersDigitsAndFunctionKeysAreContiguous()
    {
        // Key names are parsed and written by offset from the first of each run.
        Assert.Equal(KeyCode.Z, KeyCode.A + 25);
        Assert.Equal(KeyCode.Number9, KeyCode.Number0 + 9);
        Assert.Equal(KeyCode.NumberPad9, KeyCode.NumberPad0 + 9);
        Assert.Equal(KeyCode.F24, KeyCode.F1 + 23);
        Assert.Equal(0x41, (int)KeyCode.A);
        Assert.Equal(0x70, (int)KeyCode.F1);
    }
}
