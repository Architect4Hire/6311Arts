
namespace Arts6311.Domain.Modules.Forms.Data.Entities;

/// <summary>A message from the contact form.</summary>
public sealed class ContactMessage
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool Handled { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }

    public Guid RowVersion { get; set; }
}
