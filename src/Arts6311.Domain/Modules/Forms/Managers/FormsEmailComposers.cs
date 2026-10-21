using Arts6311.Domain.Managers.Email;
using Arts6311.Domain.Modules.Forms.Facade;
using Arts6311.Domain.Managers.Results;
using Microsoft.Extensions.Options;
using static Arts6311.Domain.Modules.Forms.Managers.FormsComposition;

namespace Arts6311.Domain.Modules.Forms.Managers;

/// <summary>
/// The four form emails (add-notification). Each reloads its record by id — the payload names only what to look at —
/// and returns <c>null</c> when the record is gone, which the kernel's handler records as nothing left to send.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The visitor's confirmations are fixed words.</strong> Anyone can type any address into a public form, so
/// a confirmation that repeated the name or message would let a stranger send their text to someone else under our
/// name. They say only that we have it.
/// </para>
/// <para>
/// Staff notifications carry what was sent and a link into the CMS, built from the public origin (ADR 0001); they go
/// to <see cref="FormsOptions.StaffAddress"/> only, and never carry staff notes.
/// </para>
/// </remarks>
internal sealed class SubmissionReceivedComposer(IFormsFacade facade) : IEmailComposer
{
    public async Task<EmailMessage?> ComposeAsync(string subjectId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(subjectId, out var id) || Found(await facade.GetSubmissionAsync(id, cancellationToken)) is not { } found)
        {
            return null;
        }

        return new EmailMessage(
            found.Email,
            "We have your submission — 6311arts",
            """
            Hello,

            Thank you for sending us your work. Your submission has reached 6311arts, and someone from the gallery
            will read it and get back to you, usually within three weeks.

            If you didn't send a submission to 6311arts, you can ignore this message.

            6311arts
            6311 N. Clark St., Chicago
            """);
    }
}

internal sealed class SubmissionStaffComposer(
    IFormsFacade facade, IOptions<FormsOptions> forms, IOptions<EmailOptions> email) : IEmailComposer
{
    public async Task<EmailMessage?> ComposeAsync(string subjectId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(subjectId, out var id) || Found(await facade.GetSubmissionAsync(id, cancellationToken)) is not { } found)
        {
            return null;
        }

        var submission = found;
        var link = $"{email.Value.SiteOrigin.TrimEnd('/')}/admin/forms/submissions/{submission.Id}";
        return new EmailMessage(
            forms.Value.StaffAddress,
            $"New artist submission: {OneLine(submission.Name)}",
            $"""
            A new artist submission has arrived.

            Name: {OneLine(submission.Name)}
            Email: {OneLine(submission.Email)}
            Here for: {string.Join(", ", submission.Wants)}
            Pictures: {submission.Images.Count}

            Read it in the CMS: {link}
            """);
    }

    private static string OneLine(string text) => text.ReplaceLineEndings(" ");
}

internal sealed class ContactReceivedComposer(IFormsFacade facade) : IEmailComposer
{
    public async Task<EmailMessage?> ComposeAsync(string subjectId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(subjectId, out var id) || Found(await facade.GetContactAsync(id, cancellationToken)) is not { } found)
        {
            return null;
        }

        return new EmailMessage(
            found.Email,
            "We have your message — 6311arts",
            """
            Hello,

            Thank you for getting in touch. Your message has reached 6311arts, and we'll reply as soon as we can.

            If you didn't write to 6311arts, you can ignore this message.

            6311arts
            6311 N. Clark St., Chicago
            """);
    }
}

internal sealed class ContactStaffComposer(
    IFormsFacade facade, IOptions<FormsOptions> forms, IOptions<EmailOptions> email) : IEmailComposer
{
    public async Task<EmailMessage?> ComposeAsync(string subjectId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(subjectId, out var id) || Found(await facade.GetContactAsync(id, cancellationToken)) is not { } found)
        {
            return null;
        }

        var message = found;
        var link = $"{email.Value.SiteOrigin.TrimEnd('/')}/admin/forms/messages/{message.Id}";
        return new EmailMessage(
            forms.Value.StaffAddress,
            $"New message from {message.Name.ReplaceLineEndings(" ")}",
            $"""
            A message has arrived through the contact form.

            From: {message.Name.ReplaceLineEndings(" ")} <{message.Email.ReplaceLineEndings(" ")}>

            {message.Message}

            Reply to the sender's address above. In the CMS: {link}
            """);
    }
}

/// <summary>Shared by the composers: what counts as nothing left to send.</summary>
internal static class FormsComposition
{
    /// <summary>
    /// The record, or <c>null</c> only when it is gone. Any other failure throws, so the handler retries instead of
    /// recording a skip that would lose the email for good.
    /// </summary>
    public static T? Found<T>(OperationResult<T> result)
        where T : class =>
        result.Succeeded
            ? result.Value
            : result.Error!.Code == ErrorCodes.NotFound
                ? null
                : throw new InvalidOperationException($"Reading the email's subject failed ({result.Error.Code}).");
}
