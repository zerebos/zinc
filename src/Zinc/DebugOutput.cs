using System.Runtime.InteropServices;

namespace Zinc;

/// <summary>
/// Keeps the last lines Zinc sends to the debug output (OutputDebugString), so a crash report can include them. XAML
/// describes a layout cycle there, and nowhere else, once <c>DebugSettings.LayoutCycleTracingLevel</c> is on. Nothing
/// reads that output unless a debugger is attached. Only this process's own lines are seen, because they're caught
/// as the exception that OutputDebugString raises in the calling thread.
/// </summary>
internal static unsafe partial class DebugOutput
{
    private const uint PrintException = 0x40010006; // DBG_PRINTEXCEPTION_C, from OutputDebugStringA
    private const uint PrintExceptionWide = 0x4001000A; // DBG_PRINTEXCEPTION_WIDE_C, from OutputDebugStringW
    private const int MaxLines = 400; // a high-level layout cycle trace runs to a few hundred lines
    private const int MaxLineLength = 2000;

    private static readonly Queue<string> Lines = new();
    private static readonly Lock Gate = new();

    // OutputDebugStringW without a debugger raises the wide exception, then the ANSI one with the same
    // text, so `Keep` remembers the wide text to skip its ANSI copy.
    [ThreadStatic] private static string? _lastWide;

    private static nint _handler;

    /// <summary>Starts keeping lines. Only on x64, because the filter below is x64 machine code.</summary>
    public static void Start()
    {
        if (_handler != 0 || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            return;
        }

        try
        {
            _handler = AddVectoredExceptionHandler(0, Filter((nint)(delegate* unmanaged<nint, int>)&Keep));
        }
        catch (Exception ex)
        {
            Log.Write($"debug output: not kept ({ex.Message})");
        }
    }

    /// <summary>Returns the lines kept since the last call, oldest first, and forgets them.</summary>
    public static List<string> Take()
    {
        lock (Gate)
        {
            var lines = Lines.ToList();
            Lines.Clear();
            return lines;
        }
    }

    /// <summary>
    /// Builds the native stub that's registered as the vectored handler, and returns its address. A vectored handler
    /// sees every exception in the process, including the access violations .NET turns into NullReferenceExceptions,
    /// which are raised while the thread runs managed code. Calling into managed code from one of those ends the
    /// process. So the stub returns at once for anything but the two print exceptions, and jumps to
    /// <paramref name="keep"/> for those, which only ever come from native code (OutputDebugString).
    /// </summary>
    private static nint Filter(nint keep)
    {
        byte[] code =
        [
            0x48, 0x8B, 0x01,                   // mov rax, [rcx]      (EXCEPTION_POINTERS.ExceptionRecord)
            0x8B, 0x00,                         // mov eax, [rax]      (ExceptionRecord.ExceptionCode)
            0x3D, 0x06, 0x00, 0x01, 0x40,       // cmp eax, 0x40010006
            0x74, 0x0A,                         // je keep
            0x3D, 0x0A, 0x00, 0x01, 0x40,       // cmp eax, 0x4001000A
            0x74, 0x03,                         // je keep
            0x31, 0xC0,                         // xor eax, eax        (EXCEPTION_CONTINUE_SEARCH)
            0xC3,                               // ret
            0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0, // keep: mov rax, <keep>
            0xFF, 0xE0,                         // jmp rax
        ];
        BitConverter.TryWriteBytes(code.AsSpan(24, 8), keep);

        var memory = VirtualAlloc(0, (nuint)code.Length, 0x1000 | 0x2000, 0x04); // MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE
        if (memory == 0)
        {
            throw new InvalidOperationException("no memory for the filter");
        }

        Marshal.Copy(code, 0, memory, code.Length);
        if (!VirtualProtect(memory, (nuint)code.Length, 0x20, out _)) // PAGE_EXECUTE_READ
        {
            throw new InvalidOperationException("the filter can't be made executable");
        }

        FlushInstructionCache(GetCurrentProcess(), memory, (nuint)code.Length);
        return memory;
    }

    [UnmanagedCallersOnly]
    private static int Keep(nint pointers)
    {
        try
        {
            var record = *(ExceptionRecord**)pointers;
            if (record->NumberParameters >= 2 && record->Information1 != 0)
            {
                var length = (int)Math.Clamp((long)record->Information0 - 1, 0, MaxLineLength);
                if (record->ExceptionCode == PrintExceptionWide)
                {
                    var text = new string((char*)record->Information1, 0, length);
                    _lastWide = text;
                    Add(text);
                }
                else
                {
                    var text = Marshal.PtrToStringAnsi((nint)record->Information1, length);
                    if (text == _lastWide)
                    {
                        _lastWide = null;
                    }
                    else
                    {
                        Add(text);
                    }
                }
            }
        }
        catch
        {
            // A line we couldn't read is only a line missing from a report.
        }

        return 0; // EXCEPTION_CONTINUE_SEARCH, so OutputDebugString carries on as usual
    }

    private static void Add(string text)
    {
        text = text.TrimEnd('\0', '\r', '\n');
        if (text.Length == 0)
        {
            return; // XAML's layout trace sends a blank line after each entry
        }

        lock (Gate)
        {
            Lines.Enqueue(text);
            while (Lines.Count > MaxLines)
            {
                Lines.Dequeue();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExceptionRecord
    {
        public uint ExceptionCode;
        public uint ExceptionFlags;
        public nint Record;
        public nint Address;
        public uint NumberParameters;
        public nuint Information0; // a print exception's length, in characters with the terminating null
        public nuint Information1; // a print exception's text
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint AddVectoredExceptionHandler(uint first, nint handler);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint VirtualAlloc(nint address, nuint size, uint type, uint protect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool VirtualProtect(nint address, nuint size, uint protect, out uint old);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushInstructionCache(nint process, nint address, nuint size);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();
}
