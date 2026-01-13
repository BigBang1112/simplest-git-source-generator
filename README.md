# SimplestGitSourceGenerator

[![Nuget](https://img.shields.io/nuget/v/TmEssentials?style=for-the-badge&logo=nuget)](https://www.nuget.org/packages/SimplestGitSourceGenerator/)
[![GitHub release (latest by date including pre-releases)](https://img.shields.io/github/v/release/BigBang1112/simplest-git-source-generator?include_prereleases&style=for-the-badge&logo=github)](https://github.com/BigBang1112/simplest-git-source-generator/releases)

Happy to showcase the simplest solution for retrieving Git information in your .NET apps.

## Requirements

- Project must be a Git repository
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

### Docker build

Ensure that you remove `.git` folders from your `.dockerignore` file and that you multi-stage your build so that the `.git` folder is not included in the final image.

Make sure to install `git` before building your project (Alpine example):

```dockerfile
RUN apk add --no-cache git
```

## How does it work?

Before the source generator kicks in, several Git commands are called and stored as compiler properties before they are baked into the code.

- `SimplestGitCommitHash` - `git rev-parse HEAD`
- `SimplestGitBranch` - `git rev-parse --abbrev-ref HEAD`
- `SimplestGitCommitDate` - `git log -1 --format=%cI`
- `SimplestGitTag` - `git describe --tags --always`

These commands must complete with the exit code 0, otherwise, the build will not pass. All of that is done thanks to the `build/SimplestGitSourceGenerator.targets` file.
