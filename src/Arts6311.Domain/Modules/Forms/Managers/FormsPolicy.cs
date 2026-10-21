namespace Arts6311.Domain.Modules.Forms.Managers;

/// <summary>
/// The public forms' limits and allow-lists (ADR 0047). The choices mirror the site's submission form exactly: the
/// API accepts what the form offers and nothing else, so the inbox never shows a value nobody could pick.
/// </summary>
public static class FormsPolicy
{
    public const int NameMaxLength = 120;
    public const int EmailMaxLength = 254;
    public const int PronounsMaxLength = 40;
    public const int PhoneMaxLength = 40;
    public const int NeighborhoodMaxLength = 80;
    public const int UrlMaxLength = 300;
    public const int InstagramMaxLength = 40;
    public const int OtherMediaMaxLength = 120;
    public const int BioMaxLength = 300;
    public const int StatementMaxLength = 1200;
    public const int TitleMaxLength = 120;
    public const int LargestMaxLength = 40;
    public const int MaterialsMaxLength = 1000;
    public const int MessageMaxLength = 4000;
    public const int NotesMaxLength = 4000;
    public const int MaxYears = 80;
    public const int MaxPieces = 10_000;
    public const int MaxWorkCount = 500;
    public const int MaxStudents = 24;

    /// <summary>How many pictures one submission may carry (ADR 0047).</summary>
    public const int MaxImages = 6;

    /// <summary>
    /// The whole submission request: six images at <c>MediaPolicy.MaxSubmissionImageBytes</c> plus the form and the
    /// multipart framing. The gateway's <c>forms-submissions</c> route allows the same.
    /// </summary>
    public const long MaxSubmissionRequestBytes = 64L * 1024 * 1024;

    /// <summary>The <c>submission</c> part is the form's answers as JSON; anything larger is not that.</summary>
    public const int MaxSubmissionJsonBytes = 64 * 1024;

    /// <summary>
    /// Which wording of the newsletter consent a subscriber agreed to. Change it when the words beside the sign-up
    /// change, so the list can say who agreed to what.
    /// </summary>
    public const string NewsletterConsentVersion = "2026-10-21";

    /// <summary>
    /// How long after an address last used a form before another use earns it a confirmation (ADR 0047). Anyone can
    /// type anyone's address, so this is what stops many senders flooding one stranger's inbox under the centre's name:
    /// a steady trickle stays at one confirmation. Staff are still told about every one.
    /// </summary>
    public static readonly TimeSpan ConfirmationCooldown = TimeSpan.FromHours(1);

    /// <summary>The actor recorded on what the public forms upload: nobody signed in did it.</summary>
    public const string AnonymousActor = "anonymous:forms";

    public static readonly IReadOnlyList<string> Goals = ["sell", "exhibit", "teach", "hello"];

    public static readonly IReadOnlyList<string> Mediums =
    [
        "Painting", "Printmaking", "Drawing", "Photography", "Ceramics",
        "Jewelry", "Sculpture", "Fiber & textile", "Collage", "Mixed media", "Other",
    ];

    public static readonly IReadOnlyList<string> PriceBands =
        ["Under $100", "$100 – $300", "$300 – $750", "$750 – $2,000", "Over $2,000"];

    public static readonly IReadOnlyList<string> Restock = ["yes", "limited", "no"];

    public static readonly IReadOnlyList<string> ShowWindows =
        ["Next 3 months", "3 – 6 months out", "6 – 12 months out", "Whenever you have room"];

    public static readonly IReadOnlyList<string> Galleries = ["East gallery", "West gallery", "Either"];

    public static readonly IReadOnlyList<string> ClassFormats =
        ["One-night workshop", "Three-week course", "Five-week course", "Not sure yet"];

    public static readonly IReadOnlyList<string> Heard =
        ["Walked past the gallery", "Another artist", "A class or workshop", "Instagram", "Search", "Somewhere else"];
}
