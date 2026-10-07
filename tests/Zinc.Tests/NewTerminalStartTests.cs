using Zinc.Terminal;

namespace Zinc.Tests;

public class NewTerminalStartTests
{
    private static StartSource[] Order(StartKind kind, bool folder, NewTerminalFolder startIn) =>
        [.. NewTerminalStart.Order(kind, folder, startIn)];

    [Fact]
    public void NewTabsFollowTheWorkspacesFolderElseWhereYouAre()
    {
        Assert.Equal([StartSource.Workspace, StartSource.Usual], Order(StartKind.NewTab, true, NewTerminalFolder.Current));
        Assert.Equal([StartSource.Current, StartSource.Usual], Order(StartKind.NewTab, false, NewTerminalFolder.Current));
        Assert.Equal([StartSource.Workspace, StartSource.Usual], Order(StartKind.NewTab, true, NewTerminalFolder.Profile));
        Assert.Equal([StartSource.Usual], Order(StartKind.NewTab, false, NewTerminalFolder.Profile));
    }

    [Fact]
    public void SplitsStartWhereYouAreEvenInAWorkspaceWithAFolder()
    {
        Assert.Equal([StartSource.Current, StartSource.Workspace, StartSource.Usual], Order(StartKind.Split, true, NewTerminalFolder.Current));
        Assert.Equal([StartSource.Current, StartSource.Usual], Order(StartKind.Split, false, NewTerminalFolder.Current));
        // The old way: the workspace's folder, else the profile's.
        Assert.Equal([StartSource.Workspace, StartSource.Usual], Order(StartKind.Split, true, NewTerminalFolder.Profile));
        Assert.Equal([StartSource.Usual], Order(StartKind.Split, false, NewTerminalFolder.Profile));
    }

    [Theory]
    [InlineData(true, NewTerminalFolder.Current)]
    [InlineData(false, NewTerminalFolder.Profile)]
    public void ADuplicateStartsWhereTheOriginalIsNow(bool folder, NewTerminalFolder startIn) =>
        Assert.Equal([StartSource.Current, StartSource.Original, StartSource.Usual], Order(StartKind.Duplicate, folder, startIn));

    [Fact]
    public void TheCurrentFolderBelongsToItsDistroOnlyWhenItsALinuxPath()
    {
        Assert.Equal(new WorkspaceFolder("/home/zack/src", "Fedora"), NewTerminalStart.Current("file://localhost/home/zack/src", "Fedora"));
        Assert.Equal(new WorkspaceFolder(@"C:\dev"), NewTerminalStart.Current("file:///C:/dev", "Fedora"));
        Assert.Equal(new WorkspaceFolder("/c/dev"), NewTerminalStart.Current("/c/dev", null));
        Assert.Null(NewTerminalStart.Current(null, null));
        Assert.Null(NewTerminalStart.Current("", "Fedora"));
    }

    [Fact]
    public void StartInIsReadFromTheWindowSection()
    {
        Assert.Equal(NewTerminalFolder.Current, ConfigFile.Parse("").Global.StartIn);
        Assert.Equal(NewTerminalFolder.Profile, ConfigFile.Parse("[window]\nstart-in = \"profile\"\n").Global.StartIn);
    }
}
