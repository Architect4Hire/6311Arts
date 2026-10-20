namespace Arts6311.Domain.Modules.Redirects.Managers;

/// <summary>
/// The Redirects module's stable error codes. Part of the API contract; each has its status in
/// <c>ApiProblemDetails</c> (api-contract.md).
/// </summary>
public static class RedirectErrorCodes
{
    /// <summary>Another redirect already leaves from this address. 409.</summary>
    public const string FromTaken = "redirects.from_taken";

    /// <summary>
    /// The redirect would make a loop, or a chain longer than one hop: its destination already redirects onward, or
    /// another redirect already arrives at its old address. 409 (prompt 2.7: rejected at write time).
    /// </summary>
    public const string LoopOrChain = "redirects.loop_or_chain";
}
