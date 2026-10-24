using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SimplestGitSourceGenerator.Tests;

public class GitInfoGeneratorTests
{
    [Test]
    public async Task SimplestGit_IsNotEmpty()
    {
        await Assert.That(SimplestGit.CommitHash).IsNotEmpty();
        await Assert.That(SimplestGit.CommitDate).IsNotEmpty();
        await Assert.That(SimplestGit.Branch).IsNotEmpty();
        await Assert.That(SimplestGit.Tag).IsNotEmpty();
        await Assert.That(SimplestGit.RemoteUrl).IsNotEmpty();
        await Assert.That(SimplestGit.CommitHash).Matches("^[0-9a-f]{40}([0-9a-f]{24})?$");
        await Assert.That(DateTimeOffset.TryParse(
            SimplestGit.CommitDate,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out _)).IsTrue();
    }

    [Test]
    [Arguments(LanguageVersion.CSharp7_3)]
    [Arguments(LanguageVersion.CSharp9)]
    [Arguments(LanguageVersion.CSharp12)]
    public async Task Generator_EmitsExpectedConstants(LanguageVersion languageVersion)
    {
        var values = new Dictionary<string, string>
        {
            ["CommitHash"] = "0123456789abcdef0123456789abcdef01234567",
            ["Branch"] = "feature/git-info",
            ["CommitDate"] = "2026-01-02T03:04:05+00:00",
            ["Tag"] = "v1.2.3-2-g0123456",
            ["RemoteUrl"] = "https://example.invalid/repo.git"
        };

        var constants = await GenerateConstants(values, languageVersion);

        await Assert.That(constants).IsEquivalentTo(values);
    }

    [Test]
    public async Task Generator_UsesEmptyStringsForMissingProperties()
    {
        var constants = await GenerateConstants(new Dictionary<string, string>());

        await Assert.That(constants).Count().IsEqualTo(5);
        foreach (var value in constants.Values)
        {
            await Assert.That(value).IsEqualTo("");
        }
    }

    public static IEnumerable<(string Property, string Value)> LiteralValues()
    {
        foreach (var property in new[] { "CommitHash", "Branch", "CommitDate", "Tag", "RemoteUrl" })
        {
            foreach (var value in new[] { "", "quotes\"and\\slashes", "line\r\nbreak\t\0", "\u00e9\u2028\u2029" })
            {
                yield return (property, value);
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(LiteralValues))]
    public async Task Generator_PreservesStringLiteralValues(string property, string value)
    {
        var constants = await GenerateConstants(new Dictionary<string, string> { [property] = value });

        await Assert.That(constants[property]).IsEqualTo(value);
        foreach (var pair in constants.Where(pair => pair.Key != property))
        {
            await Assert.That(pair.Value).IsEqualTo("");
        }
    }

    private static async Task<Dictionary<string, string>> GenerateConstants(
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

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).IsEmpty();
        await Assert.That(driver.GetRunResult().GeneratedTrees).Count().IsEqualTo(1);

        var generatedType = await Assert.That(output.GetTypeByMetadataName("SimplestGitSourceGenerator.SimplestGit")).IsNotNull();
        await Assert.That(generatedType.DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(generatedType.IsStatic).IsTrue();

        var constants = new Dictionary<string, string>();
        foreach (var field in generatedType.GetMembers().OfType<IFieldSymbol>())
        {
            await Assert.That(field.IsConst).IsTrue();
            await Assert.That(field.DeclaredAccessibility).IsEqualTo(Accessibility.Public);
            var value = await Assert.That(field.ConstantValue).IsTypeOf<string>().And.IsNotNull();
            constants.Add(field.Name, value);
        }

        return constants;
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
