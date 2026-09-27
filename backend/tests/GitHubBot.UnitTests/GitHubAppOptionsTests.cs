using GitHubBot.Application.Configuration;

namespace GitHubBot.UnitTests;

public class GitHubAppOptionsTests
{
    [Fact]
    public void GitHubAppOptions_SectionName_ShouldBeGitHubApp()
    {
        Assert.Equal("GitHubApp", GitHubAppOptions.SectionName);
    }
    
    [Fact]
    public void GitHubAppOptions_Properties_ShouldSetAndGet()
    {
        var options = new GitHubAppOptions
        {
            AppId = 12345,
            PrivateKey = "test",
            ClientId = "cid",
            ClientSecret = "sec"
        };
        
        Assert.Equal(12345, options.AppId);
        Assert.Equal("test", options.PrivateKey);
        Assert.Equal("cid", options.ClientId);
        Assert.Equal("sec", options.ClientSecret);
    }
}
