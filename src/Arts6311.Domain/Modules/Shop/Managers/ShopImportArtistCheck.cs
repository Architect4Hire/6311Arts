using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Shop.Managers;

/// <summary>
/// What the Artists module said about each artist an import names (ADR 0053): for every reference in the
/// <c>artist</c> column, the artists it could mean. The facade asks; Business decides what a row may credit.
/// </summary>
/// <remarks>
/// A reference that is an artist's slug means that artist and no other. Anything else is matched by name, without
/// regard to case, and two artists can share a name — so a reference can mean several, and Business refuses it.
/// </remarks>
internal sealed record ShopImportArtistCheck(IReadOnlyDictionary<string, IReadOnlyList<ShopItemArtistServiceModel>> Matches)
{
    public static ShopImportArtistCheck None { get; } =
        new(new Dictionary<string, IReadOnlyList<ShopItemArtistServiceModel>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>The artists <paramref name="reference"/> could mean; empty when it means nobody on the roster.</summary>
    public IReadOnlyList<ShopItemArtistServiceModel> For(string reference) =>
        Matches.TryGetValue(reference, out var matches) ? matches : [];
}
