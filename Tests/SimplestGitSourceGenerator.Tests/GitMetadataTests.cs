using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace SimplestGitSourceGenerator.Tests;

[NotInParallel(nameof(GitMetadataTests))]
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

    [Test]
    [Arguments(CommitDate)]
    [Arguments("2026-01-02T03:04:05Z")]
    public async Task Metadata_MatchesTaggedCommit_AndStripsRemoteCredentials(string expectedCommitDate)
    {
        InitializeRepository();
        Git("tag", "v1.2.3");
        Git("remote", "add", "origin", "https://user:token@example.invalid/repo.git");

        var metadata = ReadMetadata(publishRepositoryUrl: true);

        await Assert.That(metadata["SimplestGitCommitHash"]).IsEqualTo(Git("rev-parse", "HEAD"));
        await Assert.That(metadata["SimplestGitBranch"]).IsEqualTo("main");
        await Assert.That(DateTimeOffset.Parse(metadata["SimplestGitCommitDate"], CultureInfo.InvariantCulture))
            .IsEqualTo(DateTimeOffset.Parse(expectedCommitDate, CultureInfo.InvariantCulture));
        await Assert.That(metadata["SimplestGitTag"]).IsEqualTo("v1.2.3");
        await Assert.That(metadata["SimplestGitRemoteUrl"]).IsEqualTo("https://example.invalid/repo.git");
    }

    [Test]
    public async Task Metadata_UsesHashWithoutTags_AndOmitsRemoteByDefault()
    {
        InitializeRepository();
        Git("remote", "add", "origin", "https://example.invalid/repo.git");

        var metadata = ReadMetadata();

        await Assert.That(metadata["SimplestGitTag"]).IsEqualTo(Git("rev-parse", "--short", "HEAD"));
        await Assert.That(metadata["SimplestGitRemoteUrl"]).IsEqualTo("");
    }

    [Test]
    public async Task Metadata_DescribesCommitsAfterTag_AndReportsDetachedHead()
    {
        InitializeRepository();
        Git("tag", "v1.2.3");
        Commit();
        Git("checkout", "--detach", "HEAD");

        var metadata = ReadMetadata();

        await Assert.That(metadata["SimplestGitBranch"]).IsEqualTo("HEAD");
        await Assert.That(metadata["SimplestGitTag"]).IsEqualTo("v1.2.3-1-g" + Git("rev-parse", "--short", "HEAD"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Metadata_ProjectInsideSubmodule_UsesSubmoduleRatherThanParent(bool detachedHead)
    {
        InitializeRepository();
        Git("remote", "add", "origin", "https://example.invalid/parent.git");

        var sourceDirectory = Path.Combine(directory, "SubmoduleSource");
        var relativeProjectPath = Path.Combine("src", "Library", "Metadata.proj");
        Git("init", "--initial-branch=library", sourceDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(sourceDirectory, relativeProjectPath))!);
        File.Copy(Path.Combine(directory, "Metadata.proj"), Path.Combine(sourceDirectory, relativeProjectPath));
        Git("-C", sourceDirectory, "add", relativeProjectPath);
        Commit(sourceDirectory);
        Git("-C", sourceDirectory, "-c", "tag.gpgSign=false", "tag", "library-v1.2.3");
        var submoduleHash = Git("-C", sourceDirectory, "rev-parse", "HEAD");

        var relativeSubmodulePath = Path.Combine("modules", "Library Module");
        var submoduleDirectory = Path.Combine(directory, relativeSubmodulePath);
        Git("-c", "protocol.file.allow=always", "submodule", "add", sourceDirectory, relativeSubmodulePath);
        Git("-C", submoduleDirectory, "remote", "set-url", "origin", "https://example.invalid/library.git");
        if (detachedHead)
        {
            Git("-C", submoduleDirectory, "checkout", "--detach", "HEAD");
        }

        Git("add", "Metadata.proj");
        Commit();
        Git("-c", "tag.gpgSign=false", "tag", "parent-v9.0.0");
        await Assert.That(File.Exists(Path.Combine(submoduleDirectory, ".git"))).IsTrue();

        var parentMetadata = ReadMetadata(publishRepositoryUrl: true);
        var submoduleMetadata = ReadMetadata(
            publishRepositoryUrl: true,
            projectPath: Path.Combine(submoduleDirectory, relativeProjectPath));

        await Assert.That(parentMetadata["SimplestGitCommitHash"]).IsEqualTo(Git("rev-parse", "HEAD"));
        await Assert.That(parentMetadata["SimplestGitBranch"]).IsEqualTo("main");
        await Assert.That(parentMetadata["SimplestGitTag"]).IsEqualTo("parent-v9.0.0");
        await Assert.That(parentMetadata["SimplestGitRemoteUrl"]).IsEqualTo("https://example.invalid/parent.git");

        await Assert.That(submoduleMetadata["SimplestGitCommitHash"]).IsEqualTo(submoduleHash);
        await Assert.That(submoduleMetadata["SimplestGitBranch"]).IsEqualTo(detachedHead ? "HEAD" : "library");
        await Assert.That(DateTimeOffset.Parse(submoduleMetadata["SimplestGitCommitDate"], CultureInfo.InvariantCulture))
            .IsEqualTo(DateTimeOffset.Parse(CommitDate, CultureInfo.InvariantCulture));
        await Assert.That(submoduleMetadata["SimplestGitTag"]).IsEqualTo("library-v1.2.3");
        await Assert.That(submoduleMetadata["SimplestGitRemoteUrl"]).IsEqualTo("https://example.invalid/library.git");
        foreach (var property in new[] { "SimplestGitCommitHash", "SimplestGitBranch", "SimplestGitTag", "SimplestGitRemoteUrl" })
        {
            await Assert.That(submoduleMetadata[property]).IsNotEqualTo(parentMetadata[property]);
        }

        var submoduleProjectPath = Path.Combine(submoduleDirectory, relativeProjectPath);
        foreach (var workingDirectory in new[]
        {
            directory,
            Path.GetRelativePath(Path.GetDirectoryName(submoduleProjectPath)!, directory)
        })
        {
            var overriddenMetadata = ReadMetadata(
                publishRepositoryUrl: true,
                projectPath: submoduleProjectPath,
                workingDirectory: workingDirectory);

            await Assert.That(overriddenMetadata).IsEquivalentTo(parentMetadata);
        }
    }

    [Test]
    public async Task Metadata_FailsForMissingWorkingDirectoryOverride_WithoutFallingBackToProjectDirectory()
    {
        InitializeRepository();
        var result = Run(
            "dotnet", "msbuild", "Metadata.proj", "-target:GetGitMetadata", "-nologo", "-verbosity:quiet",
            "-property:SimplestGitWorkingDirectory=" + Path.Combine(directory, "Missing Directory"));

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Output + result.Error).Contains("MSB");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Metadata_FailsOutsideRepositoryOrWithoutCommits(bool initializeRepository)
    {
        if (initializeRepository)
        {
            Git("init", "--initial-branch=main");
        }

        var result = Run("dotnet", "msbuild", "Metadata.proj", "-target:GetGitMetadata", "-nologo", "-verbosity:quiet");

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Output + result.Error).Contains("MSB3073");
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

    private void Commit(string? repositoryDirectory = null) => Git(
        "-C", repositoryDirectory ?? directory,
        "-c", "user.name=Generator Tests",
        "-c", "user.email=generator-tests@example.invalid",
        "-c", "core.hooksPath=" + Path.Combine(directory, "no-hooks"),
        "commit", "--allow-empty", "--no-gpg-sign", "-m", "Test commit");

    private Dictionary<string, string> ReadMetadata(
        bool publishRepositoryUrl = false,
        string projectPath = "Metadata.proj",
        string? workingDirectory = null)
    {
        var result = Run(
            "dotnet", "msbuild", projectPath, "-target:GetGitMetadata", "-nologo", "-verbosity:quiet",
            "-property:PublishRepositoryUrl=" + publishRepositoryUrl.ToString().ToLowerInvariant(),
            "-property:SimplestGitWorkingDirectory=" + workingDirectory,
            "-getProperty:SimplestGitCommitHash,SimplestGitBranch,SimplestGitCommitDate,SimplestGitTag,SimplestGitRemoteUrl");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("Reading Git metadata failed: " + result.Output + result.Error);
        }

        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.GetProperty("Properties").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString()!);
    }

    private string Git(params string[] arguments)
    {
        var result = Run("git", arguments);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("Git command failed: " + result.Output + result.Error);
        }
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
