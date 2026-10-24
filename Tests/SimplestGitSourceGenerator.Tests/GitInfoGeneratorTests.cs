using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

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
        Assert.NotEmpty(SimplestGit.RemoteUrl);
        Assert.Matches("^[0-9a-f]{40}([0-9a-f]{24})?$", SimplestGit.CommitHash);
        Assert.True(DateTimeOffset.TryParse(
            SimplestGit.CommitDate,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out _));
    }

    [Theory]
    [InlineData(LanguageVersion.CSharp7_3)]
    [InlineData(LanguageVersion.CSharp9)]
    [InlineData(LanguageVersion.CSharp12)]
    public void Generator_EmitsExpectedConstants(LanguageVersion languageVersion)
    {
        var values = new Dictionary<string, string>
        {
            ["CommitHash"] = "0123456789abcdef0123456789abcdef01234567",
            ["Branch"] = "feature/git-info",
            ["CommitDate"] = "2026-01-02T03:04:05+00:00",
            ["Tag"] = "v1.2.3-2-g0123456",
            ["RemoteUrl"] = "https://example.invalid/repo.git"
        };

        Assert.Equal(
            values.OrderBy(pair => pair.Key),
            GenerateConstants(values, languageVersion).OrderBy(pair => pair.Key));
    }

    [Fact]
    public void Generator_UsesEmptyStringsForMissingProperties()
    {
        var constants = GenerateConstants(new Dictionary<string, string>());

        Assert.Equal(5, constants.Count);
        Assert.All(constants.Values, value => Assert.Equal("", value));
    }

    public static TheoryData<string, string> LiteralValues
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var property in new[] { "CommitHash", "Branch", "CommitDate", "Tag", "RemoteUrl" })
            {
                foreach (var value in new[] { "", "quotes\"and\\slashes", "line\r\nbreak\t\0", "\u00e9\u2028\u2029" })
                {
                    data.Add(property, value);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LiteralValues))]
    public void Generator_PreservesStringLiteralValues(string property, string value)
    {
        var constants = GenerateConstants(new Dictionary<string, string> { [property] = value });

        Assert.Equal(value, constants[property]);
        Assert.All(constants.Where(pair => pair.Key != property), pair => Assert.Equal("", pair.Value));
    }

    private static Dictionary<string, string> GenerateConstants(
        Dictionary<string, string> values,
        LanguageVersion languageVersion = LanguageVersion.CSharp7_3)
    {
        var parseOptions = new CSharpParseOptions(languageVersion);
        var compilation = CSharpCompilation.Create(
            "GeneratorTest",
            [
                CSharpSyntaxTree.ParseText(
                    "public static class Consumer { public static string Hash => SimplestGitSourceGenerator.SimplestGit.CommitHash; }",
                    parseOptions)
            ],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new GitInfoGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            optionsProvider: new TestOptionsProvider(values));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Single(driver.GetRunResult().GeneratedTrees);

        var generatedType = output.GetTypeByMetadataName("SimplestGitSourceGenerator.SimplestGit");
        Assert.NotNull(generatedType);
        Assert.Equal(Accessibility.Internal, generatedType.DeclaredAccessibility);
        Assert.True(generatedType.IsStatic);

        return generatedType.GetMembers().OfType<IFieldSymbol>().ToDictionary(
            field => field.Name,
            field =>
            {
                Assert.True(field.IsConst);
                Assert.Equal(Accessibility.Public, field.DeclaredAccessibility);
                return Assert.IsType<string>(field.ConstantValue);
            });
    }

    private sealed class TestOptionsProvider(Dictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new TestOptions(
            values.ToDictionary(pair => "build_property.SimplestGit" + pair.Key, pair => pair.Value));

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }

    private sealed class TestOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }
}
