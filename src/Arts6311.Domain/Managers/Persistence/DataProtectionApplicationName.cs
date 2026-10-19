namespace Arts6311.Domain.Managers.Persistence;

/// <summary>
/// The Data Protection application name, shared by every host.
/// </summary>
/// <remarks>
/// It has to be a constant rather than the default, which is derived from the entry assembly and so differs
/// between the API and the Worker. Different names isolate the key rings even when both point at the same blob,
/// and the symptom would be Identity tokens that mint in one process and fail to validate in the other
/// (ADR 0023).
/// </remarks>
public static class DataProtectionApplicationName
{
    public const string Value = "Arts6311";
}
