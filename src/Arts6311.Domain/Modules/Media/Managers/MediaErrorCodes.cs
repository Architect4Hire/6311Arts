namespace Arts6311.Domain.Modules.Media.Managers;

/// <summary>
/// The Media module's stable error codes. Part of the API contract; each has its status in
/// <c>ApiProblemDetails</c> (api-contract.md).
/// </summary>
public static class MediaErrorCodes
{
    /// <summary>The file is not a JPEG, PNG or WebP by its signature (SVG included), or it is animated. 415.</summary>
    public const string TypeUnsupported = "media.type_unsupported";

    /// <summary>The file's signature and its extension disagree, for example a PNG named <c>.jpg</c>. 415.</summary>
    public const string ExtensionMismatch = "media.extension_mismatch";

    /// <summary>The file is larger than <see cref="MediaPolicy.MaxUploadBytes"/>. 413.</summary>
    public const string TooLarge = "media.too_large";

    /// <summary>Too small, too large on a side, or too many pixels. 400.</summary>
    public const string DimensionsOutOfRange = "media.dimensions_out_of_range";

    /// <summary>The header says image, but the pixels do not decode. 400.</summary>
    public const string Undecodable = "media.undecodable";

    /// <summary>The same bytes are already in the library. Carries <c>existingAssetId</c>. 409.</summary>
    public const string Duplicate = "media.duplicate";

    /// <summary>The asset is referenced by content or a Square link, so it cannot be deleted. 409.</summary>
    public const string InUse = "media.in_use";
}
