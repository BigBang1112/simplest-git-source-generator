namespace SimplestGitSourceGenerator.Tests;

public class GitInfoGeneratorTests
{
    [Fact]
    public void SimplestGit_IsNotEmpty()
    {
        Assert.NotEmpty(SimplestGit.CommitHash);
        Assert.NotEmpty(SimplestGit.CommitDate);
        Assert.NotEmpty(SimplestGit.Branch);
        Assert.NotEmpty(SimplestGit.Tag);
    }
}
