using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Zinc.Pty;

/// <summary>
/// Where this process's memory goes, for Settings > About. It covers the process's own private memory, with the .NET
/// heap's share of it (the rest is native, such as WinUI, Direct3D and the graphics driver, and libghostty's screens),
/// and the programs it started (shells and their console hosts), which Task Manager adds to Zinc's row. It's measured
/// on demand, so nothing runs in the background.
/// </summary>
public sealed record MemoryReport(
    long PrivateBytes,
    long WorkingSetBytes,
    long ManagedHeapBytes,
    long ManagedCommittedBytes,
    int Windows,
    int Panes,
    IReadOnlyList<(string Name, int Count, long PrivateBytes)> Children)
{
    public long ChildBytes => Children.Sum(c => c.PrivateBytes);

    public int ChildCount => Children.Sum(c => c.Count);

    private static string Mb(long bytes) => $"{bytes / 1048576.0:N0} MB";

    /// <summary>One line for the About row.</summary>
    public string Summary =>
        $"Zinc {Mb(PrivateBytes)} (.NET {Mb(ManagedHeapBytes)}), {Plural(Panes, "pane")} in {Plural(Windows, "window")}; " +
        $"programs it started {Mb(ChildBytes)} ({Plural(ChildCount, "process", "processes")})";

    /// <summary>Everything, for a bug report.</summary>
    public string Text()
    {
        var text = new StringBuilder();
        text.AppendLine($"Zinc memory: private {Mb(PrivateBytes)}, working set {Mb(WorkingSetBytes)}");
        text.AppendLine($".NET heap {Mb(ManagedHeapBytes)} (committed {Mb(ManagedCommittedBytes)})");
        text.AppendLine($"{Plural(Panes, "pane")} in {Plural(Windows, "window")}");
        foreach (var (name, count, bytes) in Children.OrderByDescending(c => c.PrivateBytes))
        {
            text.AppendLine($"  {name} x{count}: {Mb(bytes)}");
        }

        return text.ToString().TrimEnd();
    }

    private static string Plural(int n, string one, string? many = null) => $"{n} {(n == 1 ? one : many ?? one + "s")}";

    /// <summary>Measures now (a full collection first, so the .NET figure is what's live).</summary>
    public static MemoryReport Measure(int windows, int panes)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var self = Process.GetCurrentProcess();
        var gc = GC.GetGCMemoryInfo();
        var children = new Dictionary<string, (int Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in ProcessTree.Descendants(self.Id))
        {
            try
            {
                using var child = Process.GetProcessById(id);
                var (count, bytes) = children.GetValueOrDefault(name);
                children[name] = (count + 1, bytes + child.PrivateMemorySize64);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Gone already, or not ours to look at.
            }
        }

        return new MemoryReport(self.PrivateMemorySize64, self.WorkingSet64, gc.HeapSizeBytes, gc.TotalCommittedBytes, windows, panes,
            children.Select(c => (c.Key, c.Value.Count, c.Value.Bytes)).ToList());
    }
}

/// <summary>A process's descendants, from a Toolhelp snapshot of every process and its parent.</summary>
public static partial class ProcessTree
{
    /// <summary>The descendants of <paramref name="root"/> (id, exe name), children before grandchildren.</summary>
    public static IReadOnlyList<(int Id, string Name)> Descendants(int root) => Descendants(root, Snapshot());

    /// <summary>
    /// The same over a given list of (id, parent, name). Ids can be reused, so a cycle stops at the first repeat.
    /// </summary>
    public static IReadOnlyList<(int Id, string Name)> Descendants(int root, IReadOnlyList<(int Id, int Parent, string Name)> processes)
    {
        var byParent = processes.Where(p => p.Id != p.Parent).ToLookup(p => p.Parent);
        var found = new List<(int, string)>();
        var seen = new HashSet<int> { root };
        var queue = new Queue<int>([root]);
        while (queue.Count > 0)
        {
            foreach (var child in byParent[queue.Dequeue()])
            {
                if (seen.Add(child.Id))
                {
                    found.Add((child.Id, child.Name));
                    queue.Enqueue(child.Id);
                }
            }
        }

        return found;
    }

    private static List<(int Id, int Parent, string Name)> Snapshot()
    {
        var list = new List<(int, int, string)>();
        var snapshot = CreateToolhelp32Snapshot(2 /* TH32CS_SNAPPROCESS */, 0);
        if (snapshot == -1)
        {
            return list;
        }

        try
        {
            var entry = new ProcessEntry { Size = Marshal.SizeOf<ProcessEntry>() };
            for (var more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
            {
                list.Add(((int)entry.ProcessId, (int)entry.ParentProcessId, entry.ExeFile));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return list;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public int Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(nint snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(nint snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
