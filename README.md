# SimplestGitSourceGenerator

[![Nuget](https://img.shields.io/nuget/v/SimplestGitSourceGenerator?style=for-the-badge&logo=nuget)](https://www.nuget.org/packages/SimplestGitSourceGenerator/)
[![GitHub release (latest by date including pre-releases)](https://img.shields.io/github/v/release/BigBang1112/simplest-git-source-generator?include_prereleases&style=for-the-badge&logo=github)](https://github.com/BigBang1112/simplest-git-source-generator/releases)

Happy to showcase the simplest solution for retrieving Git information in your .NET apps.

## Requirements

- Project must be a Git repository with at least 1 commit
- Git must be installed

If these requirements are not fulfilled, the build will not pass!

## Usage

Install the NuGet package:

```
dotnet add package SimplestGitSourceGenerator
```

**Code:**

```cs
using SimplestGitSourceGenerator;

Console.WriteLine(SimplestGit.CommitHash);
Console.WriteLine(SimplestGit.CommitDate);
Console.WriteLine(SimplestGit.Branch);
Console.WriteLine(SimplestGit.Tag);
```

If you want to include remote URL as well, you can enable it via property:

```xml
<PropertyGroup>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
</PropertyGroup>
```

Once that's done, you can access it via `SimplestGit.RemoteUrl`.

The repository must have a default remote when this option is enabled. When the option is disabled, `RemoteUrl` is an empty string and no remote lookup is performed.

### Metadata behavior

The generated `SimplestGit` class is internal to the consuming assembly and exposes constant strings:

| Property | Value |
| --- | --- |
| `CommitHash` | Full hash of `HEAD`. |
| `Branch` | Branch name, or `HEAD` for a detached checkout. |
| `CommitDate` | Committer date, not author date, in ISO 8601 format with a time zone offset. |
| `Tag` | An exact tag, a value such as `v1.2.3-2-gabcdef0`, or an abbreviated commit hash if no reachable tag exists. |
| `RemoteUrl` | Default remote URL when enabled, with URL user information stripped, otherwise an empty string. |

Shallow clones or checkouts without tags can produce a different `Tag` value from a full clone.

### Docker build

Ensure that you remove `.git` folders from your `.dockerignore` file and that you multi-stage your build so that the `.git` folder is not included in the final image.

Make sure to install `git` before building your project (Alpine example):

```dockerfile
RUN apk add --no-cache git
```

## How does it work?

Before the source generator kicks in, several Git commands are called and stored as compiler properties before they are baked into the code.

- `git rev-parse HEAD` (`SimplestGitCommitHash`)
- `git rev-parse --abbrev-ref HEAD` (`SimplestGitBranch`)
- `git log -1 --format=%cI` (`SimplestGitCommitDate`)
- `git describe --tags --always` (`SimplestGitTag`)
- `git ls-remote --get-url` (`SimplestGitRemoteUrl`) + credentials are automatically stripped from the URL

These commands must complete with the exit code 0, otherwise, the build will not pass. All of that is done thanks to the `build/SimplestGitSourceGenerator.targets` file.

This is a simpler alternative to SourceLink or GitInfo packages if you don't mind requiring the Git dependency.
