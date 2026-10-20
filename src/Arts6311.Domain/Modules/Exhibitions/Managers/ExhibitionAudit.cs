using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Exhibitions.Managers;

/// <summary>
/// The audit vocabulary this module writes. Status transitions are audited (content.md), archive among them
/// (auth.md "Sensitive operations"); creates and edits are too, as in Artists.
/// </summary>
internal static class ExhibitionAudit
{
    public const string ResourceType = "exhibition";

    public const string Created = "exhibition.created";

    public const string Updated = "exhibition.updated";

    public const string Seeded = "exhibition.seeded";

    public static string For(PublishCommand command) => command switch
    {
        PublishCommand.Publish => "exhibition.published",
        PublishCommand.Unpublish => "exhibition.unpublished",
        PublishCommand.Archive => "exhibition.archived",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}
