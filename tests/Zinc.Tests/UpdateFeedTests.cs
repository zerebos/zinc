namespace Zinc.Tests;

public class UpdateFeedTests
{
    [Theory]
    [InlineData("https://github.com/owner/zinc", "https://github.com/owner/zinc")]
    [InlineData("https://github.com/owner/zinc/", "https://github.com/owner/zinc")]
    [InlineData("https://GitHub.com/Owner/Zinc.git", "https://github.com/Owner/Zinc")]
    [InlineData(" https://github.com/owner/zinc ", "https://github.com/owner/zinc")]
    public void AGitHubRepositoryAddressNamesTheRepository(string address, string repository) =>
        Assert.Equal(repository, UpdateFeed.GitHubRepository(address));

    [Theory]
    [InlineData("https://downloads.example.com/zinc/")]
    [InlineData("https://example.com/github.com/owner/zinc")]
    [InlineData("http://github.com/owner/zinc")] // not https
    [InlineData("https://github.com/owner")]
    [InlineData("https://github.com/owner/zinc/releases")]
    [InlineData("https://github.com/owner/zinc/releases/download/v1/releases.win.json")]
    [InlineData(@"C:\Temp\feed")]
    [InlineData("")]
    public void AnyOtherAddressIsServedAsItIs(string address) => Assert.Null(UpdateFeed.GitHubRepository(address));
}
