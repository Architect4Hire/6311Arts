using Arts6311.Domain.Modules.Forms.Managers;

namespace Arts6311.Domain.Modules.Forms.Managers.ViewModels;

/// <summary>
/// The artist submission form's answers, in the shape the site's form builds them (the <c>submission</c> part of
/// <c>POST /api/v1/forms/submissions</c>; the images follow as their own parts). The panels for selling, showing and
/// teaching are present only when the artist picked that goal.
/// </summary>
/// <param name="Company">The honeypot. A person never sees it; anything in it means a bot, and nothing is kept.</param>
public sealed record ArtistSubmissionViewModel(
    SubmissionArtistViewModel? Artist,
    SubmissionWorkViewModel? Work,
    IReadOnlyList<string>? Wants,
    SubmissionSellViewModel? Sell = null,
    SubmissionExhibitViewModel? Exhibit = null,
    SubmissionTeachViewModel? Teach = null,
    string? Portfolio = null,
    string? Heard = null,
    SubmissionConsentViewModel? Consent = null,
    string? Company = null);

/// <param name="Instagram">A handle, with or without the <c>@</c>.</param>
public sealed record SubmissionArtistViewModel(
    string? Name,
    string? Email,
    string? Pronouns = null,
    string? Phone = null,
    string? Neighborhood = null,
    string? Zip = null,
    string? Website = null,
    string? Instagram = null);

/// <param name="Medium">One of <see cref="FormsPolicy.Mediums"/>.</param>
/// <param name="PriceBand">One of <see cref="FormsPolicy.PriceBands"/>.</param>
public sealed record SubmissionWorkViewModel(
    string? Medium = null,
    string? OtherMedia = null,
    int? Years = null,
    string? PriceBand = null,
    string? Bio = null,
    string? Statement = null);

/// <param name="Restock">One of <see cref="FormsPolicy.Restock"/>.</param>
public sealed record SubmissionSellViewModel(int? PiecesReady = null, string? Restock = null);

/// <param name="Window">One of <see cref="FormsPolicy.ShowWindows"/>.</param>
/// <param name="Gallery">One of <see cref="FormsPolicy.Galleries"/>.</param>
public sealed record SubmissionExhibitViewModel(
    string? ShowTitle = null,
    int? WorkCount = null,
    string? Largest = null,
    string? Window = null,
    string? Gallery = null);

/// <param name="Format">One of <see cref="FormsPolicy.ClassFormats"/>.</param>
public sealed record SubmissionTeachViewModel(
    string? ClassTitle = null,
    int? MaxStudents = null,
    string? Format = null,
    string? Materials = null,
    bool? TaughtBefore = null);

/// <param name="OwnWork">Required: the work is theirs and they hold the rights to it.</param>
/// <param name="Newsletter">Also join the newsletter.</param>
public sealed record SubmissionConsentViewModel(bool? OwnWork, bool? Newsletter = null);

/// <summary>One image part of a submission, still streaming from the request.</summary>
public sealed record SubmissionImageViewModel(string FileName, Stream Content);

/// <summary><c>POST /api/v1/forms/contact</c>: the contact form.</summary>
/// <param name="Company">The honeypot.</param>
public sealed record ContactMessageViewModel(string? Name, string? Email, string? Message, string? Company = null);

/// <summary><c>POST /api/v1/forms/newsletter</c>: the sign-up.</summary>
/// <param name="Company">The honeypot.</param>
public sealed record NewsletterSignupViewModel(string? Email, string? Company = null);

/// <summary>A page of the submissions inbox, newest first unless <paramref name="Sort"/> says otherwise.</summary>
/// <param name="Sort">
/// <c>name</c>, <c>email</c>, <c>status</c>, <c>pictures</c> or <c>received</c>, with a leading <c>-</c> for
/// descending (ADR 0062).
/// </param>
public sealed record SubmissionListViewModel(
    string? Cursor = null, int? Limit = null, SubmissionStatus? Status = null, string? Sort = null);

/// <summary>Staff's change to a submission. A field left out is unchanged.</summary>
public sealed record SubmissionUpdateViewModel(SubmissionStatus? Status = null, string? Notes = null);

/// <summary>A page of contact messages, newest first unless <paramref name="Sort"/> says otherwise.</summary>
/// <param name="Sort"><c>from</c>, <c>status</c> or <c>received</c>, with a leading <c>-</c> for descending (ADR 0062).</param>
public sealed record ContactListViewModel(string? Cursor = null, int? Limit = null, bool? Handled = null, string? Sort = null);

/// <summary>Staff marking a contact message handled, or not.</summary>
public sealed record ContactUpdateViewModel(bool? Handled);

/// <summary>A page of the newsletter list, newest consent first unless <paramref name="Sort"/> says otherwise.</summary>
/// <param name="Search">Part of an address, matched without regard to case.</param>
/// <param name="Sort">
/// <c>email</c>, <c>consented</c>, <c>wording</c> or <c>source</c>, with a leading <c>-</c> for descending (ADR 0062).
/// </param>
public sealed record NewsletterListViewModel(string? Cursor = null, int? Limit = null, string? Search = null, string? Sort = null);
