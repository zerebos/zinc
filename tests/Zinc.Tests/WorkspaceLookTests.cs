using Zinc.Panes;

namespace Zinc.Tests;

public class WorkspaceLookTests
{
    [Fact]
    public void IconsHaveUniqueNamesAndGlyphs()
    {
        Assert.Equal(WorkspaceIcons.All.Count, WorkspaceIcons.All.Select(i => i.Name).Distinct().Count());
        Assert.Equal(WorkspaceIcons.All.Count, WorkspaceIcons.All.Select(i => i.Glyph).Distinct().Count());
        Assert.Equal('\uE943', WorkspaceIcons.Glyph("code"));
        Assert.Null(WorkspaceIcons.Glyph("gone"));
        Assert.Null(WorkspaceIcons.Glyph(null));
    }

    [Fact]
    public void AFolderAlwaysGetsTheSameColor()
    {
        Assert.Equal(WorkspaceColors.ForFolder(@"C:\Src\app"), WorkspaceColors.ForFolder(@"c:\src\app\"));
        Assert.Equal(WorkspaceColors.ForFolder("/home/zack/src/ghostty"), WorkspaceColors.ForFolder("/home/zack/src/ghostty/"));
        Assert.InRange(WorkspaceColors.ForFolder("/tmp"), 0, WorkspaceColors.All.Count - 1);

        // Different folders spread over the colors (not all on one).
        var used = Enumerable.Range(0, 40).Select(i => WorkspaceColors.ForFolder($"/home/zack/src/project{i}")).Distinct().Count();
        Assert.True(used >= 6, $"only {used} colors for 40 folders");
    }
}
