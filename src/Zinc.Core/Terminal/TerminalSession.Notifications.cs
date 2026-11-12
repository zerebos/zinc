using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Zinc.Ghostty;

namespace Zinc.Terminal;

/// <summary>A program's progress (OSC 9;4), where <see cref="Value"/> is 0–100, or -1 when it didn't say.</summary>
public readonly record struct TerminalProgress(TerminalProgressState State, int Value)
{
    public static readonly TerminalProgress None = new(TerminalProgressState.Remove, -1);

    public bool Active => State != TerminalProgressState.Remove;
}

// Programs asking for attention: desktop notifications (OSC 9 text, OSC 777 notify) and progress
// reports (OSC 9;4, which winget and some build tools send, and Windows Terminal shows on the
// taskbar). libghostty parses both and calls back mid-write, and we announce them after the write.
public sealed unsafe partial class TerminalSession
{
    private readonly List<(string Title, string Body)> _notifications = [];
    private TerminalProgress? _progressReported;

    /// <summary>A program asked for a desktop notification (title may be empty). Raised on the reader thread.</summary>
    public event Action<string, string>? Notified;

    /// <summary>A program reported progress, or cleared it. Raised on the reader thread.</summary>
    public event Action<TerminalProgress>? ProgressChanged;

    /// <summary>The last progress a program reported (<see cref="TerminalProgress.None"/> once cleared).</summary>
    public TerminalProgress Progress { get; private set; } = TerminalProgress.None;

    private void InstallNotificationCallbacks()
    {
        Native.TerminalSet(_terminal, TerminalOption.DesktopNotification,
            (delegate* unmanaged[Cdecl]<nint, nint, GhosttyTerminalDesktopNotification*, void>)&OnDesktopNotification);
        Native.TerminalSet(_terminal, TerminalOption.ProgressReport,
            (delegate* unmanaged[Cdecl]<nint, nint, GhosttyTerminalProgressReport*, void>)&OnProgressReport);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDesktopNotification(nint terminal, nint userdata, GhosttyTerminalDesktopNotification* n)
    {
        var session = FromUserdata(userdata);
        if (session._notifications.Count < 16) // a flood of them is one too many already
        {
            session._notifications.Add((Utf8(n->Title), Utf8(n->Body)));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnProgressReport(nint terminal, nint userdata, GhosttyTerminalProgressReport* report) =>
        FromUserdata(userdata)._progressReported = new TerminalProgress(report->State, report->Progress);

    /// <summary>Raises the events the last write collected. Call it after that write, outside the lock.</summary>
    private void AnnounceNotifications()
    {
        List<(string, string)>? notifications = null;
        TerminalProgress? progress = null;
        lock (Lock)
        {
            if (_notifications.Count > 0)
            {
                notifications = [.. _notifications];
                _notifications.Clear();
            }

            if (_progressReported is { } reported)
            {
                _progressReported = null;
                if (reported != Progress)
                {
                    Progress = reported.Active ? reported : TerminalProgress.None;
                    progress = Progress;
                }
            }
        }

        foreach (var (title, body) in notifications ?? [])
        {
            Notified?.Invoke(title, body);
        }

        if (progress is { } changed)
        {
            ProgressChanged?.Invoke(changed);
        }
    }
}
