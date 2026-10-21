namespace Arts6311.Domain.Modules.Forms.Data.Entities;

/// <summary>One picture on a submission. A <c>Restrict</c> key to the asset, as media.md requires of every user.</summary>
public sealed class ArtistSubmissionImage
{
    public Guid SubmissionId { get; set; }

    public Guid MediaAssetId { get; set; }

    public int Position { get; set; }
}
