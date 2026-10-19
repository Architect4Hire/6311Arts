using System.Xml.Linq;

namespace Arts6311.Tests.Architecture;

/// <summary>
/// Guards the solution's reference rules (CLAUDE.md "Mandatory seams", prompt 1.1): the Domain depends on
/// nothing, Square sees only the Domain, the gateway holds no business code, and every service host uses
/// ServiceDefaults. Reads the project files, so it holds before any code exists to reference.
/// </summary>
public sealed class ProjectReferenceTests
{
    private static readonly string[] ServiceHosts =
        ["Arts6311.Gateway", "Arts6311.Api", "Arts6311.Worker", "Arts6311.MigrationService"];

    [Fact]
    public void DomainReferencesNoOtherProject()
    {
        Assert.Empty(ProjectReferencesOf("Arts6311.Domain"));
    }

    [Fact]
    public void SquareReferencesOnlyTheDomain()
    {
        Assert.Equal(["Arts6311.Domain"], ProjectReferencesOf("Arts6311.Square"));
    }

    [Fact]
    public void GatewayReferencesOnlyServiceDefaults()
    {
        Assert.Equal(["Arts6311.ServiceDefaults"], ProjectReferencesOf("Arts6311.Gateway"));
    }

    [Theory]
    [MemberData(nameof(ServiceHostNames))]
    public void ServiceHostReferencesServiceDefaults(string project)
    {
        Assert.Contains("Arts6311.ServiceDefaults", ProjectReferencesOf(project));
    }

    [Theory]
    [MemberData(nameof(ServiceHostNames))]
    public void ServiceHostCallsAddServiceDefaults(string project)
    {
        var program = File.ReadAllText(Path.Combine(SourceRoot(), project, "Program.cs"));

        Assert.Contains("builder.AddServiceDefaults();", program, StringComparison.Ordinal);
    }

    public static TheoryData<string> ServiceHostNames() => new(ServiceHosts);

    private static string[] ProjectReferencesOf(string project)
    {
        var csproj = XDocument.Load(Path.Combine(SourceRoot(), project, $"{project}.csproj"));

        return
        [
            .. csproj.Descendants("ProjectReference")
                .Select(reference => (string?)reference.Attribute("Include"))
                .OfType<string>()
                .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
                .Order(StringComparer.Ordinal),
        ];
    }

    private static string SourceRoot() => RepoPaths.SourceRoot();
}
