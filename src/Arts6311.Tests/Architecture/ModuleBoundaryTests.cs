namespace Arts6311.Tests.Architecture;

/// <summary>
/// backend.md "Module rules": the shared kernel depends on no module (ArtsDbContext excepted), a module reaches
/// another only through its facade, ServiceModels or FK-crossing entities, and there are no layer-first folders at
/// the Domain root. There are no modules yet, so the rules are also proven against synthetic files below; the
/// real scan starts biting the moment the first module lands.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly string[] RepoWideBuildFiles = ["Directory.Build.props", "Directory.Build.targets"];

    [Fact]
    public void TheDomainHasNoBoundaryViolations()
    {
        Assert.Empty(ModuleBoundaryScanner.Violations(DomainFiles()));
    }

    [Fact]
    public void NoProjectFileLeaksADomainNamespaceAsAGlobalUsing()
    {
        Assert.Empty(ModuleBoundaryScanner.ProjectFileViolations(DomainProjectFiles()));
    }

    [Fact]
    public void TheScanActuallySeesTheKernel()
    {
        var paths = DomainFiles().Select(file => file.RelativePath).ToList();

        Assert.Contains("Managers/Persistence/ArtsDbContext.cs", paths);
        Assert.Contains(paths, path => path.StartsWith("Managers/Audit/", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, path => path.StartsWith("bin/", StringComparison.Ordinal) || path.StartsWith("obj/", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryKernelExemptionStillExists()
    {
        var paths = DomainFiles().Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);

        Assert.All(ModuleBoundaryScanner.KernelExemptions, exemption => Assert.Contains(exemption, paths));
    }

    [Theory]
    [MemberData(nameof(ForbiddenRootFolders))]
    public void NoLayerFirstFolderAtTheDomainRoot(string folder)
    {
        Assert.False(Directory.Exists(Path.Combine(DomainRoot(), folder)), $"src/Arts6311.Domain/{folder}/ is a layer-first folder (backend.md).");
    }

    [Fact]
    public void KernelNamingAModuleIsAViolation()
    {
        var file = new DomainSourceFile(
            "Managers/Caching/Leak.cs", "using Arts6311.Domain.Modules.Artists.Facade;\nclass Leak {}");

        Assert.Single(ModuleBoundaryScanner.Violations([file]));
    }

    [Fact]
    public void ArtsDbContextMayNameModules()
    {
        var file = new DomainSourceFile(
            "Managers/Persistence/ArtsDbContext.cs", "using Arts6311.Domain.Modules.Artists.Data.Entities;");

        Assert.Empty(ModuleBoundaryScanner.Violations([file]));
    }

    [Theory]
    [InlineData("using Arts6311.Domain.Modules.Artists.Business;")]
    [InlineData("using Arts6311.Domain.Modules.Artists.Data.Repositories;")]
    [InlineData("using Arts6311.Domain.Modules.Artists.Managers;")]
    [InlineData("using Arts6311.Domain.Modules.Artists;")]
    [InlineData("var x = new Arts6311.Domain.Modules.Artists.Business.ArtistBusiness();")]
    public void ReachingPastAnotherModulesFacadeIsAViolation(string code)
    {
        var file = new DomainSourceFile("Modules/Shop/Business/ShopBusiness.cs", code);

        Assert.Single(ModuleBoundaryScanner.Violations([file]));
    }

    [Theory]
    [InlineData("using Arts6311.Domain.Modules.Artists.Facade;")]
    [InlineData("using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;")]
    [InlineData("using Arts6311.Domain.Modules.Artists.Data.Entities;")]
    [InlineData("using Arts6311.Domain.Modules.Shop.Data.Repositories;")]
    [InlineData("// see Arts6311.Domain.Modules.Artists.Business for the rule")]
    public void FacadesServiceModelsEntitiesAndOwnInternalsAreAllowed(string code)
    {
        var file = new DomainSourceFile("Modules/Shop/Business/ShopBusiness.cs", code);

        Assert.Empty(ModuleBoundaryScanner.Violations([file]));
    }

    [Theory]
    [InlineData("Shop/Business/ShopBusiness.cs")]
    [InlineData("GlobalUsings.cs")]
    [InlineData("modules/Shop/Business/ShopBusiness.cs")]
    [InlineData("Modules/Orphan.cs")]
    public void SourceOutsideTheKnownFoldersIsAViolation(string path)
    {
        Assert.Single(ModuleBoundaryScanner.Violations([new DomainSourceFile(path, "class X {}")]));
    }

    [Theory]
    [InlineData("using M = Arts6311.Domain.Modules;")]
    [InlineData("using D = Arts6311.Domain;")]
    [InlineData("using Biz = Arts6311.Domain.Modules.Artists.Business;")]
    public void NamespaceAliasesIntoTheDomainAreViolations(string code)
    {
        var file = new DomainSourceFile("Modules/Shop/Business/ShopBusiness.cs", code);

        Assert.NotEmpty(ModuleBoundaryScanner.Violations([file]));
    }

    [Theory]
    [InlineData("Managers/Caching/Leak.cs", "namespace Arts6311.Domain.Managers;\nusing Modules.Artists.Facade;")]
    [InlineData("Managers/Caching/Leak.cs", "namespace Arts6311;\nusing Domain.Modules.Artists.Facade;")]
    [InlineData("Modules/Shop/Business/ShopBusiness.cs", "namespace Arts6311.Domain;\nusing Modules.Artists.Business;")]
    public void RelativeModuleNamesAreSeen(string path, string code)
    {
        Assert.Single(ModuleBoundaryScanner.Violations([new DomainSourceFile(path, code)]));
    }

    [Fact]
    public void AGlobalUsingOfAModuleIsAViolationEvenInsideThatModule()
    {
        var file = new DomainSourceFile(
            "Modules/Artists/ArtistsGlobalUsings.cs", "global using Arts6311.Domain.Modules.Artists.Business;");

        Assert.NotEmpty(ModuleBoundaryScanner.Violations([file]));
    }

    [Theory]
    [InlineData("using Arts6311.Domain.Modules.Artists.FacadeHelpers;")]
    [InlineData("using Arts6311.Domain.Modules.Artists.Managers.ServiceModelsInternal;")]
    public void PermittedNamespacesMatchOnABoundary(string code)
    {
        var file = new DomainSourceFile("Modules/Shop/Business/ShopBusiness.cs", code);

        Assert.Single(ModuleBoundaryScanner.Violations([file]));
    }

    [Fact]
    public void BlockCommentsAreNotReferences()
    {
        var file = new DomainSourceFile(
            "Managers/Caching/Notes.cs", "/* the old Arts6311.Domain.Modules.Artists.Business call */\nclass Notes {}");

        Assert.Empty(ModuleBoundaryScanner.Violations([file]));
    }

    [Fact]
    public void AGlobalUsingItemInAProjectFileIsAViolation()
    {
        var file = new DomainSourceFile(
            "Arts6311.Domain.csproj", """<ItemGroup><Using Include="Arts6311.Domain.Modules.Artists.Business" /></ItemGroup>""");

        Assert.Single(ModuleBoundaryScanner.ProjectFileViolations([file]));
    }

    public static TheoryData<string> ForbiddenRootFolders() => new(ModuleBoundaryScanner.ForbiddenRootFolders);

    // Every MSBuild file that shapes the Domain's compilation: its own project and props/targets, plus the
    // repo-wide Directory.Build.props/targets.
    private static IEnumerable<DomainSourceFile> DomainProjectFiles()
    {
        var root = DomainRoot();
        var repo = Path.GetFullPath(Path.Combine(RepoPaths.SourceRoot(), ".."));
        var inDomain = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var repoWide = RepoWideBuildFiles
            .Select(name => Path.Combine(repo, name))
            .Where(File.Exists);

        return inDomain.Concat(repoWide)
            .Select(path => new DomainSourceFile(Path.GetFileName(path), File.ReadAllText(path)));
    }

    private static string DomainRoot() => Path.Combine(RepoPaths.SourceRoot(), "Arts6311.Domain");

    private static IEnumerable<DomainSourceFile> DomainFiles()
    {
        var root = DomainRoot();
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.StartsWith("bin/", StringComparison.Ordinal) && !path.StartsWith("obj/", StringComparison.Ordinal))
            .Select(path => new DomainSourceFile(path, File.ReadAllText(Path.Combine(root, path))));
    }
}
