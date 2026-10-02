using System.Runtime.InteropServices;
using Zinc.Pty;

namespace Zinc;

/// <summary>
/// Receiving consoles as the default terminal (<see cref="DefaultTerminal"/>): Zinc's COM class,
/// registered with COM while Zinc runs (CoRegisterClassObject) on a thread of its own in the
/// multithreaded apartment, so calls don't wait for the UI. Windows Terminal's OpenConsole creates
/// it and calls <see cref="ITerminalHandoff3.EstablishPtyHandoff"/> with the console's handles; the
/// console becomes a ConPty (<see cref="ConPty.FromHandoff"/>) and a tab opens on the UI thread.
/// </summary>
internal static class TerminalHandoff
{
    private static Thread? _thread;
    private static readonly ManualResetEventSlim Stop = new();

    /// <summary>Called on a COM thread with each console: open it (on the UI thread).</summary>
    public static Action<ConPty, string>? Received { get; set; }

    public static void StartListening(Guid clsid)
    {
        if (_thread != null)
        {
            return;
        }

        _thread = new Thread(() =>
        {
            var hr = CoRegisterClassObject(clsid, new TerminalHandoffFactory(), ClsctxLocalServer, RegclsMultipleUse, out var cookie);
            Log.Write($"default terminal: listening for consoles as {clsid} (0x{hr:X8})");
            Stop.Wait();
            if (hr == 0)
            {
                CoRevokeClassObject(cookie);
            }
        })
        { IsBackground = true, Name = "Default terminal" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public static void StopListening() => Stop.Set();

    private const int ClsctxLocalServer = 4, RegclsMultipleUse = 1;

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(in Guid clsid, [MarshalAs(UnmanagedType.IUnknown)] object unknown, int context, int flags,
        out int cookie);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(int cookie);
}

// COM sees only public types: these are what OpenConsole talks to (see TerminalHandoff).

[ComVisible(true), Guid("6F23DA90-15C5-4203-9DB0-64E73F1B1B00"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITerminalHandoff3
{
    [PreserveSig]
    int EstablishPtyHandoff(out nint input, out nint output, nint signal, nint reference, nint server, nint client, nint startupInfo);
}

[ComVisible(true), Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(nint outer, ref Guid iid, out nint obj);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool doLock);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class TerminalHandoffObject : ITerminalHandoff3
{
    public int EstablishPtyHandoff(out nint input, out nint output, nint signal, nint reference, nint server, nint client, nint startupInfo)
    {
        input = output = 0;
        try
        {
            // TERMINAL_STARTUP_INFO starts with the title (a BSTR): the program's path, or a shortcut's name.
            var title = startupInfo != 0 ? Marshal.PtrToStringBSTR(Marshal.ReadIntPtr(startupInfo)) ?? "" : "";
            var pty = ConPty.FromHandoff(server, reference, signal, client, out input, out output);
            Log.Write($"default terminal: a console arrived: {title} ({pty.ProgramPath}, pid {pty.ProcessId})");
            if (TerminalHandoff.Received is { } received)
            {
                received(pty, title);
            }
            else
            {
                pty.Dispose();
            }

            return 0;
        }
        catch (Exception ex)
        {
            Log.Crash("receiving a console", ex);
            return Marshal.GetHRForException(ex);
        }
    }
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class TerminalHandoffFactory : IClassFactory
{
    public int CreateInstance(nint outer, ref Guid iid, out nint obj)
    {
        obj = 0;
        if (outer != 0)
        {
            return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION
        }

        var unknown = Marshal.GetIUnknownForObject(new TerminalHandoffObject());
        var hr = Marshal.QueryInterface(unknown, in iid, out obj);
        Marshal.Release(unknown);
        return hr;
    }

    public int LockServer(bool doLock) => 0;
}
