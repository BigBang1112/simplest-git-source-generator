using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace SimplestGitSourceGenerator.Tests;

public sealed class GitMetadataTests : IDisposable
{
    private const string CommitDate = "2026-01-02T03:04:05+00:00";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "SimplestGitTests-" + Guid.NewGuid().ToString("N"));

    public GitMetadataTests()
    {
        Directory.CreateDirectory(directory);
        new XDocument(
            new XElement("Project",
                new XElement("Import",
                    new XAttribute("Project", Path.Combine(AppContext.BaseDirectory, "SimplestGitSourceGenerator.targets")))))
            .Save(Path.Combine(directory, "Metadata.proj"));
    }

    [Fact]
    public void Metadata_MatchesTaggedCommit_AndStripsRemoteCredentials()
    {
        InitializeRepository();
        Git("tag", "v1.2.3");
        Git("remote", "add", "origin", "https://user:token@example.invalid/repo.git");

        var metadata = ReadMetadata(publishRepositoryUrl: true);

        Assert.Equal(Git("rev-parse", "HEAD"), metadata["SimplestGitCommitHash"]);
        Assert.Equal("main", metadata["SimplestGitBranch"]);
        Assert.Equal(CommitDate, metadata["SimplestGitCommitDate"]);
        Assert.Equal("v1.2.3", metadata["SimplestGitTag"]);
        Assert.Equal("https://example.invalid/repo.git", metadata["SimplestGitRemoteUrl"]);
    }

    [Fact]
    public void Metadata_UsesHashWithoutTags_AndOmitsRemoteByDefault()
    {
        InitializeRepository();
        Git("remote", "add", "origin", "https://example.invalid/repo.git");

        var metadata = ReadMetadata();

        Assert.Equal(Git("rev-parse", "--short", "HEAD"), metadata["SimplestGitTag"]);
        Assert.Equal("", metadata["SimplestGitRemoteUrl"]);
    }

    [Fact]
    public void Metadata_DescribesCommitsAfterTag_AndReportsDetachedHead()
    {
        InitializeRepository();
        Git("tag", "v1.2.3");
        Commit();
        Git("checkout", "--detach", "HEAD");

        var metadata = ReadMetadata();

        Assert.Equal("HEAD", metadata["SimplestGitBranch"]);
        Assert.Equal("v1.2.3-1-g" + Git("rev-parse", "--short", "HEAD"), metadata["SimplestGitTag"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Metadata_FailsOutsideRepositoryOrWithoutCommits(bool initializeRepository)
    {
        if (initializeRepository)
        {
            Git("init", "--initial-branch=main");
        }

        var result = Run("dotnet", "msbuild", "Metadata.proj", "-target:GetGitMetadata", "-nologo", "-verbosity:quiet");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("MSB3073", result.Output + result.Error);
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        }

        Directory.Delete(directory, recursive: true);
    }

    private void InitializeRepository()
    {
        Git("init", "--initial-branch=main");
        Commit();
    }

    private void Commit() => Git(
        "-c", "user.name=Generator Tests",
        "-c", "user.email=generator-tests@example.invalid",
        "-c", "core.hooksPath=" + Path.Combine(directory, "no-hooks"),
        "commit", "--allow-empty", "--no-gpg-sign", "-m", "Test commit");

    private Dictionary<string, string> ReadMetadata(bool publishRepositoryUrl = false)
    {
        var result = Run(
            "dotnet", "msbuild", "Metadata.proj", "-target:GetGitMetadata", "-nologo", "-verbosity:quiet",
            "-property:PublishRepositoryUrl=" + publishRepositoryUrl.ToString().ToLowerInvariant(),
            "-getProperty:SimplestGitCommitHash,SimplestGitBranch,SimplestGitCommitDate,SimplestGitTag,SimplestGitRemoteUrl");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);

        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.GetProperty("Properties").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString()!);
    }

    private string Git(params string[] arguments)
    {
        var result = Run("git", arguments);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        return result.Output.Trim();
    }

    private (int ExitCode, string Output, string Error) Run(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_AUTHOR_DATE"] = CommitDate;
        startInfo.Environment["GIT_COMMITTER_DATE"] = CommitDate;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start " + executable);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException(executable + " did not exit within 30 seconds.");
        }

        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
