namespace Arts6311.Domain.Modules.Exhibitions.Data.Entities;

/// <summary>
/// One credit: an artist shown in an exhibition. The artist is the Artists module's, reached by a <c>Restrict</c>
/// foreign key only; who they are is asked of <c>IArtistsFacade</c> (backend.md "Module ownership").
/// </summary>
public sealed class ExhibitionArtist
{
    public Guid ExhibitionId { get; set; }

    public Guid ArtistId { get; set; }

    /// <summary>Where the artist comes in the credits, from 0. The spike lists the headliners first.</summary>
    public int Position { get; set; }
}
