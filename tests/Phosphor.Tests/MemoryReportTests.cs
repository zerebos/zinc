using System.Diagnostics;
using Phosphor.Pty;

namespace Phosphor.Tests;

public class MemoryReportTests
{
    [Fact]
    public void DescendantsWalkTheTree()
    {
        (int, int, string)[] processes =
        [
            (10, 1, "zinc.exe"), (11, 10, "OpenConsole.exe"), (12, 11, "pwsh.exe"), (13, 12, "git.exe"),
            (20, 1, "explorer.exe"), (21, 20, "other.exe"), (30, 30, "loop.exe"), (31, 13, "x.exe"), (13, 31, "reused.exe"),
        ];
        var found = ProcessTree.Descendants(10, processes).Select(p => p.Name).ToList();
        Assert.Equal(["OpenConsole.exe", "pwsh.exe", "git.exe", "x.exe"], found); // the reused id 13 isn't walked twice
        Assert.Empty(ProcessTree.Descendants(99, processes));
    }

    [Fact]
    public void FindsARealChild()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c ping -n 5 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            Assert.Contains(ProcessTree.Descendants(Environment.ProcessId), p => p.Id == child.Id && p.Name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase));
            var report = MemoryReport.Measure(windows: 1, panes: 2);
            Assert.True(report.PrivateBytes > 0);
            Assert.Contains(report.Children, c => c.Name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("2 panes in 1 window", report.Summary);
            Assert.Contains(".NET heap", report.Text());
        }
        finally
        {
            child.Kill(entireProcessTree: true);
        }
    }
}
