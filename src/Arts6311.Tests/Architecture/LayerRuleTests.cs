namespace Arts6311.Tests.Architecture;

/// <summary>
/// The seam's layer rules (backend.md) checked against the real Domain, and proven against synthetic files so the
/// scan cannot pass by seeing nothing.
/// </summary>
public sealed class LayerRuleTests
{
    [Fact]
    public void NoBusinessOrFacadeReachesBelowItsLayer()
    {
        Assert.Empty(LayerRuleScanner.Violations(DomainFiles()));
    }

    [Fact]
    public void TheScanActuallySeesTheLayers()
    {
        var paths = DomainFiles().Select(file => file.RelativePath).ToList();

        Assert.Contains("Modules/Identity/Business/IdentityBusiness.cs", paths);
        Assert.Contains("Modules/Identity/Facade/IdentityFacade.cs", paths);
    }

    [Theory]
    [InlineData("Modules/Shop/Business/ShopBusiness.cs", "outbox.Enqueue(x); IOutboxWriter outbox;", "IOutboxWriter")]
    [InlineData("Modules/Shop/Business/ShopBusiness.cs", "public ShopBusiness(IShopRepository items) { }", "IShopRepository")]
    [InlineData("Modules/Shop/Business/ShopBusiness.cs", "using Microsoft.EntityFrameworkCore;", "Microsoft.EntityFrameworkCore")]
    [InlineData("Modules/Shop/Business/ShopBusiness.cs", "ISquareCatalogGateway catalog;", "ISquareCatalogGateway")]
    [InlineData("Modules/Identity/Business/IdentityBusiness.cs", "IdentityResult result;", "IdentityResult")]
    [InlineData("Modules/Identity/Business/IdentityBusiness.cs", "using Microsoft.AspNetCore.Identity;", "Microsoft.AspNetCore.Identity")]
    [InlineData("Modules/Shop/Facade/ShopFacade.cs", "IShopDataLayer dataLayer;", "IShopDataLayer")]
    [InlineData("Modules/Shop/Facade/ShopFacade.cs", "ArtsDbContext context;", "ArtsDbContext")]
    [InlineData("Modules/Shop/Facade/ShopFacade.cs", "IAuditWriter audit;", "IAuditWriter")]
    public void AForbiddenReferenceIsFlagged(string path, string code, string reference)
    {
        var violation = Assert.Single(LayerRuleScanner.Violations([new DomainSourceFile(path, code)]));

        Assert.Contains(reference, violation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Modules/Shop/Data/ShopDataLayer.cs", "IOutboxWriter outbox; IShopRepository items;")]
    [InlineData("Modules/Shop/Business/ShopBusiness.cs", "IShopDataLayer dataLayer; IClock clock;")]
    [InlineData("Modules/Shop/Business/ShopBusiness.cs", "// no IShopRepository here: Business calls the DataLayer")]
    [InlineData("Modules/Shop/Facade/ShopFacade.cs", "IShopBusiness business; IValidator<ItemViewModel> validator;")]
    [InlineData("Managers/Outbox/OutboxWriter.cs", "ArtsDbContext context;")]
    public void WhatTheLayerMayUseIsNotFlagged(string path, string code)
    {
        Assert.Empty(LayerRuleScanner.Violations([new DomainSourceFile(path, code)]));
    }

    private static IEnumerable<DomainSourceFile> DomainFiles()
    {
        var root = Path.Combine(RepoPaths.SourceRoot(), "Arts6311.Domain");
        return Directory.EnumerateFiles(Path.Combine(root, "Modules"), "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Select(path => new DomainSourceFile(path, File.ReadAllText(Path.Combine(root, path))));
    }
}
