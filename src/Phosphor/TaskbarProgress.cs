using System.Runtime.InteropServices;
using Phosphor.Ghostty;
using Phosphor.Terminal;

namespace Phosphor;

/// <summary>
/// A window's progress on its taskbar button (ITaskbarList3), from a program's OSC 9;4 report:
/// Windows Terminal does the same. Green filling, yellow paused, red error, or a sweep when the
/// program gave no number.
/// </summary>
internal static class TaskbarProgress
{
    private static ITaskbarList3? _taskbar;
    private static bool _failed;

    public static void Show(nint hwnd, TerminalProgress progress)
    {
        if (Taskbar() is not { } taskbar)
        {
            return;
        }

        try
        {
            var (flags, value) = progress.State switch
            {
                TerminalProgressState.Set when progress.Value >= 0 => (Normal, progress.Value),
                TerminalProgressState.Set or TerminalProgressState.Indeterminate => (Indeterminate, -1),
                TerminalProgressState.Error => (Error, progress.Value < 0 ? 100 : progress.Value),
                TerminalProgressState.Pause => (Paused, progress.Value < 0 ? 100 : progress.Value),
                _ => (NoProgress, -1),
            };
            taskbar.SetProgressState(hwnd, flags);
            if (value >= 0)
            {
                taskbar.SetProgressValue(hwnd, (ulong)value, 100);
            }
        }
        catch (COMException ex)
        {
            Log.Write($"taskbar progress: {ex.Message}");
        }
    }

    private static ITaskbarList3? Taskbar()
    {
        if (_taskbar == null && !_failed)
        {
            try
            {
                _taskbar = (ITaskbarList3)new TaskbarList();
                _taskbar.HrInit();
            }
            catch (COMException ex)
            {
                _failed = true; // no taskbar (e.g. Explorer isn't running)
                Log.Write($"taskbar progress unavailable: {ex.Message}");
            }
        }

        return _taskbar;
    }

    private const int NoProgress = 0, Indeterminate = 1, Normal = 2, Error = 4, Paused = 8;

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList;

    // Methods in vtable order: ITaskbarList, ITaskbarList2, then the two ITaskbarList3 ones used.
    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(nint hwnd);
        void DeleteTab(nint hwnd);
        void ActivateTab(nint hwnd);
        void SetActiveAlt(nint hwnd);
        void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(nint hwnd, ulong completed, ulong total);
        void SetProgressState(nint hwnd, int flags);
    }
}
