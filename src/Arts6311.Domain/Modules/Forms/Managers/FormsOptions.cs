namespace Arts6311.Domain.Modules.Forms.Managers;

/// <summary>Where the forms' staff notifications go (ADR 0047).</summary>
public sealed class FormsOptions
{
    public const string SectionName = "Forms";

    /// <summary>
    /// The staff inbox for new submissions and contact messages. Per environment, from the AppHost's
    /// <c>forms-staff-email</c> parameter: its default is a non-routable <c>.localhost</c> address, so nothing real is
    /// emailed unless an environment says so on purpose, and production alone sets the centre's inbox.
    /// </summary>
    public string StaffAddress { get; set; } = string.Empty;

    /// <summary>
    /// Throws if the staff address is missing or not an address, at startup rather than mid-send — and, in
    /// production, if it is still a placeholder (<c>.localhost</c>, <c>.invalid</c>), which would quietly lose every
    /// staff notification.
    /// </summary>
    public void Validate(bool production = false)
    {
        if (string.IsNullOrWhiteSpace(StaffAddress) || !StaffAddress.Contains('@', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(StaffAddress)} must be an email address.");
        }

        if (production
            && (StaffAddress.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                || StaffAddress.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(StaffAddress)} is a placeholder; set the forms-staff-email parameter for production.");
        }
    }
}
