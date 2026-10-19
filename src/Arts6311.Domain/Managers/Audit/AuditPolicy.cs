namespace Arts6311.Domain.Managers.Audit;

/// <summary>Column limits for <see cref="AuditLog"/>.</summary>
public static class AuditPolicy
{
    /// <summary>The length of an ASP.NET Core Identity user id column.</summary>
    public const int ActorUserIdMaxLength = 450;
    public const int ActionMaxLength = 100;
    public const int ResourceTypeMaxLength = 100;
    public const int ResourceIdMaxLength = 200;
    public const int SummaryMaxLength = 500;
    public const int ReferenceMaxLength = 500;
}
