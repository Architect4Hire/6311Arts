namespace Arts6311.Domain.Modules.Media.Managers;

/// <summary>The audit vocabulary this module writes. Deletes are audited (auth.md "Sensitive operations").</summary>
internal static class MediaAudit
{
    public const string AssetResourceType = "media.asset";

    public const string Uploaded = "media.uploaded";

    public const string Updated = "media.updated";

    public const string Deleted = "media.deleted";

    /// <summary>The Worker gave up on an asset's renditions.</summary>
    public const string RenditionsFailed = "media.renditions-failed";

    /// <summary>The Worker gave up on removing a deleted asset's blobs.</summary>
    public const string PurgeFailed = "media.purge-failed";
}
