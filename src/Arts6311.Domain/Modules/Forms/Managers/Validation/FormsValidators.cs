using Arts6311.Domain.Managers.Validation;
using System.Text.RegularExpressions;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Forms.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Forms.Managers.Validation;

/// <summary>Shape checks the three public forms share: an address that can be written back to, and URLs that are web addresses.</summary>
internal static partial class FormsRules
{
    /// <summary>Something@something.something, no spaces: what a person can be written back to. Delivery is the test of the rest.</summary>
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailShape();

    [GeneratedRegex(@"^\d{5}$", RegexOptions.CultureInvariant)]
    private static partial Regex ZipShape();

    [GeneratedRegex(@"^@?[A-Za-z0-9._]{1,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex InstagramShape();

    /// <summary>
    /// An address the mail sender will take as it stands: the shape, no characters an address list or display name
    /// uses, and a parse that gives back exactly what was typed. Anything looser is accepted here and poisoned at send.
    /// </summary>
    public static bool IsEmail(string? value)
    {
        var text = value?.Trim();
        return text is { Length: > 0 and <= FormsPolicy.EmailMaxLength }
            && EmailShape().IsMatch(text)
            && text.IndexOfAny(['<', '>', '"', ',', ';', ':', '(', ')', '[', ']', '\\']) < 0
            && System.Net.Mail.MailAddress.TryCreate(text, out var parsed)
            && parsed.Address == text;
    }

    public static bool IsZip(string? value) => string.IsNullOrWhiteSpace(value) || ZipShape().IsMatch(value.Trim());

    public static bool IsInstagram(string? value) => string.IsNullOrWhiteSpace(value) || InstagramShape().IsMatch(value.Trim());

    /// <summary>Empty, or an absolute <c>http</c>/<c>https</c> address: never <c>javascript:</c>, which staff would click.</summary>
    public static bool IsWebAddress(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (value.Trim().Length <= FormsPolicy.UrlMaxLength
            && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp));

    /// <summary>Empty, or exactly one of the choices the form offers.</summary>
    public static bool IsOneOf(string? value, IReadOnlyList<string> choices) =>
        string.IsNullOrWhiteSpace(value) || choices.Contains(value.Trim(), StringComparer.Ordinal);

    public static bool FitsIn(string? value, int maxLength) => value is null || value.Trim().Length <= maxLength;
}

internal sealed class ArtistSubmissionValidator : AbstractValidator<ArtistSubmissionViewModel>
{
    public ArtistSubmissionValidator()
    {
        RuleFor(submission => submission.Artist).NotNull().OverridePropertyName("artist").WithMessage("Tell us who you are.");
        RuleFor(submission => submission.Artist!.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).OverridePropertyName("artist.name").WithMessage("Please tell us your name.")
            .Must(name => FormsRules.FitsIn(name, FormsPolicy.NameMaxLength)).OverridePropertyName("artist.name")
            .WithMessage($"Keep your name under {FormsPolicy.NameMaxLength} characters.")
            .When(submission => submission.Artist is not null);
        RuleFor(submission => submission.Artist!.Email)
            .Must(FormsRules.IsEmail).OverridePropertyName("artist.email").WithMessage("That email address does not look right.")
            .When(submission => submission.Artist is not null);
        RuleFor(submission => submission.Artist!)
            .Custom((artist, context) =>
            {
                Check(context, "artist.pronouns", FormsRules.FitsIn(artist.Pronouns, FormsPolicy.PronounsMaxLength), "Keep pronouns short.");
                Check(context, "artist.phone", FormsRules.FitsIn(artist.Phone, FormsPolicy.PhoneMaxLength), "That phone number is too long.");
                Check(context, "artist.neighborhood", FormsRules.FitsIn(artist.Neighborhood, FormsPolicy.NeighborhoodMaxLength), "Keep the neighborhood short.");
                Check(context, "artist.zip", FormsRules.IsZip(artist.Zip), "Use a five-digit ZIP code.");
                Check(context, "artist.website", FormsRules.IsWebAddress(artist.Website), "Use a full web address, starting with https://.");
                Check(context, "artist.instagram", FormsRules.IsInstagram(artist.Instagram), "Use your Instagram handle, like @yourhandle.");
            })
            .When(submission => submission.Artist is not null);

        RuleFor(submission => submission.Wants)
            .Must(wants => wants is { Count: > 0 }).OverridePropertyName("wants").WithMessage("Pick at least one thing you are here for.")
            .Must(wants => wants is null || wants.All(want => FormsPolicy.Goals.Contains(want, StringComparer.Ordinal)))
            .OverridePropertyName("wants").WithMessage($"Choose from {string.Join(", ", FormsPolicy.Goals)}.");

        RuleFor(submission => submission.Work!)
            .Custom((work, context) =>
            {
                Check(context, "work.medium", FormsRules.IsOneOf(work.Medium, FormsPolicy.Mediums), "Choose a medium from the list.");
                Check(context, "work.otherMedia", FormsRules.FitsIn(work.OtherMedia, FormsPolicy.OtherMediaMaxLength), "Keep that short.");
                Check(context, "work.years", work.Years is null or >= 0 and <= FormsPolicy.MaxYears, $"Use a number from 0 to {FormsPolicy.MaxYears}.");
                Check(context, "work.priceBand", FormsRules.IsOneOf(work.PriceBand, FormsPolicy.PriceBands), "Choose a price range from the list.");
                Check(context, "work.bio", FormsRules.FitsIn(work.Bio, FormsPolicy.BioMaxLength), $"Keep the bio under {FormsPolicy.BioMaxLength} characters.");
                Check(context, "work.statement", FormsRules.FitsIn(work.Statement, FormsPolicy.StatementMaxLength), $"Keep the statement under {FormsPolicy.StatementMaxLength} characters.");
            })
            .When(submission => submission.Work is not null);

        RuleFor(submission => submission.Sell!)
            .Custom((sell, context) =>
            {
                Check(context, "sell.piecesReady", sell.PiecesReady is null or >= 0 and <= FormsPolicy.MaxPieces, "Use a whole number of pieces.");
                Check(context, "sell.restock", FormsRules.IsOneOf(sell.Restock, FormsPolicy.Restock), "Choose an answer from the list.");
            })
            .When(submission => submission.Sell is not null);

        RuleFor(submission => submission.Exhibit!)
            .Custom((exhibit, context) =>
            {
                Check(context, "exhibit.showTitle", FormsRules.FitsIn(exhibit.ShowTitle, FormsPolicy.TitleMaxLength), "Keep the title short.");
                Check(context, "exhibit.workCount", exhibit.WorkCount is null or >= 1 and <= FormsPolicy.MaxWorkCount, "Use a whole number of works.");
                Check(context, "exhibit.largest", FormsRules.FitsIn(exhibit.Largest, FormsPolicy.LargestMaxLength), "Keep the size short, like 48 × 36.");
                Check(context, "exhibit.window", FormsRules.IsOneOf(exhibit.Window, FormsPolicy.ShowWindows), "Choose a time from the list.");
                Check(context, "exhibit.gallery", FormsRules.IsOneOf(exhibit.Gallery, FormsPolicy.Galleries), "Choose a gallery from the list.");
            })
            .When(submission => submission.Exhibit is not null);

        RuleFor(submission => submission.Teach!)
            .Custom((teach, context) =>
            {
                Check(context, "teach.classTitle", FormsRules.FitsIn(teach.ClassTitle, FormsPolicy.TitleMaxLength), "Keep the title short.");
                Check(context, "teach.maxStudents", teach.MaxStudents is null or >= 1 and <= FormsPolicy.MaxStudents, $"Use a number from 1 to {FormsPolicy.MaxStudents}.");
                Check(context, "teach.format", FormsRules.IsOneOf(teach.Format, FormsPolicy.ClassFormats), "Choose a format from the list.");
                Check(context, "teach.materials", FormsRules.FitsIn(teach.Materials, FormsPolicy.MaterialsMaxLength), "Keep the materials list shorter.");
            })
            .When(submission => submission.Teach is not null);

        RuleFor(submission => submission.Portfolio)
            .Must(FormsRules.IsWebAddress).OverridePropertyName("portfolio").WithMessage("Use a full web address, starting with https://.");
        RuleFor(submission => submission.Heard)
            .Must(heard => FormsRules.IsOneOf(heard, FormsPolicy.Heard)).OverridePropertyName("heard").WithMessage("Choose an answer from the list.");
        RuleFor(submission => submission.Consent)
            .Must(consent => consent?.OwnWork == true).OverridePropertyName("consent.ownWork")
            .WithMessage("Please confirm the work is yours.");
    }

    private static void Check<T>(ValidationContext<T> context, string field, bool ok, string message)
    {
        if (!ok)
        {
            context.AddFailure(field, message);
        }
    }
}

internal sealed class ContactMessageValidator : AbstractValidator<ContactMessageViewModel>
{
    public ContactMessageValidator()
    {
        RuleFor(message => message.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).OverridePropertyName("name").WithMessage("Please tell us your name.")
            .Must(name => FormsRules.FitsIn(name, FormsPolicy.NameMaxLength)).OverridePropertyName("name").WithMessage("That name is too long.");
        RuleFor(message => message.Email)
            .Must(FormsRules.IsEmail).OverridePropertyName("email").WithMessage("That email address does not look right.");
        RuleFor(message => message.Message)
            .Must(text => !string.IsNullOrWhiteSpace(text)).OverridePropertyName("message").WithMessage("Please write a message.")
            .Must(text => FormsRules.FitsIn(text, FormsPolicy.MessageMaxLength)).OverridePropertyName("message")
            .WithMessage($"Keep the message under {FormsPolicy.MessageMaxLength} characters.");
    }
}

internal sealed class NewsletterSignupValidator : AbstractValidator<NewsletterSignupViewModel>
{
    public NewsletterSignupValidator()
    {
        RuleFor(signup => signup.Email)
            .Must(FormsRules.IsEmail).OverridePropertyName("email").WithMessage("That email address does not look right.");
    }
}

internal sealed class SubmissionListValidator : AbstractValidator<SubmissionListViewModel>
{
    public SubmissionListValidator()
    {
        RuleFor(request => request.Limit).InclusiveBetween(1, PageRequest.MaxLimit).When(request => request.Limit is not null);
        RuleFor(request => request.Status).IsInEnum().When(request => request.Status is not null);
        RuleFor(request => request.Sort).SortableBy(FormsListOrder.Submissions);
    }
}

internal sealed class SubmissionUpdateValidator : AbstractValidator<SubmissionUpdateViewModel>
{
    public SubmissionUpdateValidator()
    {
        RuleFor(update => update.Status).IsInEnum().When(update => update.Status is not null);
        RuleFor(update => update.Notes)
            .Must(notes => notes is null || notes.Length <= FormsPolicy.NotesMaxLength).OverridePropertyName("notes")
            .WithMessage($"Keep notes under {FormsPolicy.NotesMaxLength} characters.");
        RuleFor(update => update)
            .Must(update => update.Status is not null || update.Notes is not null)
            .OverridePropertyName("body")
            .WithMessage("Change the status or the notes.");
    }
}

internal sealed class ContactListValidator : AbstractValidator<ContactListViewModel>
{
    public ContactListValidator()
    {
        RuleFor(request => request.Limit).InclusiveBetween(1, PageRequest.MaxLimit).When(request => request.Limit is not null);
        RuleFor(request => request.Sort).SortableBy(FormsListOrder.Contacts);
    }
}

internal sealed class ContactUpdateValidator : AbstractValidator<ContactUpdateViewModel>
{
    public ContactUpdateValidator()
    {
        RuleFor(update => update.Handled).NotNull().OverridePropertyName("handled").WithMessage("Say whether it is handled.");
    }
}

internal sealed class NewsletterListValidator : AbstractValidator<NewsletterListViewModel>
{
    public NewsletterListValidator()
    {
        RuleFor(request => request.Limit).InclusiveBetween(1, PageRequest.MaxLimit).When(request => request.Limit is not null);
        RuleFor(request => request.Search).MaximumLength(FormsPolicy.EmailMaxLength).When(request => request.Search is not null);
        RuleFor(request => request.Sort).SortableBy(FormsListOrder.Subscribers);
    }
}
