namespace Arts6311.Domain.Managers.Results;

/// <summary>
/// Stable error codes shared by every module. Codes are part of the API contract (api-contract.md): renaming one
/// is a breaking change. Module-specific codes live in the module and follow the same <c>area.reason</c> shape.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation.failed";
    public const string NotFound = "resource.not_found";
    public const string Conflict = "resource.conflict";
    public const string ConcurrencyConflict = "concurrency.conflict";
    public const string Forbidden = "access.forbidden";
    public const string CursorInvalid = "paging.cursor.invalid";
}
