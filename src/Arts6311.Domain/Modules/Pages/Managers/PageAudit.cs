using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Pages.Managers;

/// <summary>
/// The audit vocabulary this module writes. Status transitions are audited (content.md), archive among them
/// (auth.md "Sensitive operations"); creates and edits are too, with what the sanitizer removed, if anything.
/// </summary>
internal static class PageAudit
{
    public const string ResourceType = "page";

    public const string Created = "page.created";

    public const string Updated = "page.updated";

    public const string Seeded = "page.seeded";

    public static string For(PublishCommand command) => command switch
    {
        PublishCommand.Publish => "page.published",
        PublishCommand.Unpublish => "page.unpublished",
        PublishCommand.Archive => "page.archived",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}
