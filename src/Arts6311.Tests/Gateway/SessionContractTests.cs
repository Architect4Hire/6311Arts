using System.Reflection;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Domain.Modules.Identity.Managers.ServiceModels;
using Arts6311.Domain.Modules.Identity.Managers.Validation;
using Arts6311.Gateway.Edge;
using Arts6311.Gateway.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// The gateway's mirrored copies against the originals in the Domain.
/// </summary>
/// <remarks>
/// The gateway holds no domain code and cannot reference the Domain (ADR 0022), so role names, the response
/// shapes it reads and the Data Protection application name are all duplicated there. None of those drifts would
/// fail a build: a renamed role would simply stop reaching <c>/admin</c>, and a renamed field would deserialize
/// to null. This test project references both sides, so it is the only place they can be held together — the
/// same arrangement <c>InternalTokenContractTests</c> makes for the token.
/// </remarks>
public sealed class SessionContractTests
{
    [Fact]
    public void TheRoleNamesAgree()
    {
        Assert.Equal(IdentityRoles.Admin, GatewayRoles.Admin);
        Assert.Equal(IdentityRoles.Editor, GatewayRoles.Editor);
        Assert.Equal(IdentityRoles.Member, GatewayRoles.Member);
        Assert.Equal(IdentityRoles.Staff, GatewayRoles.Staff);
    }

    [Fact]
    public void StaffIsReadTheSameWayOnBothSides()
    {
        // The lifetimes hang off this: a session the gateway does not call staff keeps the longer member window.
        foreach (string[] roles in new[]
        {
            new[] { IdentityRoles.Admin },
            [IdentityRoles.Editor],
            [IdentityRoles.Member],
            [IdentityRoles.Member, IdentityRoles.Editor],
            [],
        })
        {
            Assert.Equal(IdentityRoles.IsStaff(roles), GatewayRoles.IsStaff(roles));
        }
    }

    [Fact]
    public void TheUserSummaryTheGatewayReadsMatchesTheOneTheApiReturns()
    {
        // Name by name, because the gateway deserializes the API's JSON into its own record: a field renamed on
        // one side arrives as null on the other, and a null display name is not an error anywhere.
        Assert.Equal(PropertyNames<UserSummaryServiceModel>(), PropertyNames<UserSummary>());
    }

    [Fact]
    public void TheValidationResultTheGatewayReadsMatchesTheApis()
    {
        Assert.Equal(PropertyNames<SessionValidationServiceModel>(), PropertyNames<SessionValidationResult>());
    }

    [Fact]
    public void TheDataProtectionApplicationNameMatches()
    {
        Assert.Equal(DataProtectionApplicationName.Value, GatewayDataProtection.ApplicationName);
    }

    [Fact]
    public void TheEdgeSpellsTheSharedErrorCodesAsTheApiDoes()
    {
        // A client acts on the code, not on which process produced it. "auth.unauthenticated" must mean the same
        // thing whether the edge or the API refused the request (api-contract.md).
        Assert.Equal(ErrorCodes.Unauthenticated, GatewayProblem.CodeFor(StatusCodes.Status401Unauthorized));
        Assert.Equal(ErrorCodes.Forbidden, GatewayProblem.CodeFor(StatusCodes.Status403Forbidden));
        Assert.Equal(ErrorCodes.RequestRejected, GatewayProblem.CodeFor(StatusCodes.Status400BadRequest));
    }

    [Fact]
    public void TheCredentialFailureCodesTheGatewayCollapsesAreTheOnesTheApiRaises()
    {
        // If the API grew a fourth refusal code, /bff/login would log it as "unknown" — still one answer to the
        // browser, which is the security property, but the operator would be told nothing useful.
        string[] apiCodes =
        [
            IdentityErrorCodes.CredentialsInvalid,
            IdentityErrorCodes.LockedOut,
            IdentityErrorCodes.EmailUnconfirmed,
            IdentityErrorCodes.PasswordRejected,
        ];

        Assert.Contains(GatewayProblem.Codes.CredentialsInvalid, apiCodes, StringComparer.Ordinal);
    }

    [Fact]
    public void TheLinkCodesTheGatewayReadsAreTheOnesTheApiRaises()
    {
        // The gateway branches on these to word its answer: a renamed one on the API's side would turn every
        // rejected password into "this link has expired" (ADR 0028).
        Assert.Equal(IdentityErrorCodes.TokenInvalid, GatewayProblem.Codes.TokenInvalid);
        Assert.Equal(IdentityErrorCodes.PasswordRejected, GatewayProblem.Codes.PasswordRejected);
    }

    [Fact]
    public void TheGatewaysLinkCeilingsAreTheApisValidators()
    {
        // Mirrored so a body the API would refuse is refused at the edge; a drift either way is a 400 nobody can
        // explain, or an oversized token handed to Data Protection.
        Assert.Equal(CredentialCheckValidator.EmailMaxLength, BffEndpoints.AccountLimits.EmailMaxLength);
        Assert.Equal(PasswordResetCompletionValidator.UserIdMaxLength, BffEndpoints.AccountLimits.UserIdMaxLength);
        Assert.Equal(PasswordResetCompletionValidator.TokenMaxLength, BffEndpoints.AccountLimits.TokenMaxLength);
        Assert.Equal(CredentialCheckValidator.PasswordMaxLength, BffEndpoints.AccountLimits.PasswordMaxLength);
    }

    [Theory]
    [InlineData("ada@example.test")]
    [InlineData("Ada@Example.TEST")]
    [InlineData("straße@example.test")]
    [InlineData("étienne@example.test")]
    [InlineData(" padded@example.test ")]
    public void TheCooldownNormalizesAnAddressExactlyAsIdentityLooksItUp(string escaped)
    {
        // Every spelling that reaches one account must share one cooldown, and no more: this is the mirror of
        // Identity's UpperInvariantLookupNormalizer, which the gateway cannot reference (ADR 0022).
        var email = System.Text.RegularExpressions.Regex.Unescape(escaped);

        Assert.Equal(
            new UpperInvariantLookupNormalizer().NormalizeEmail(email),
            AccountEmailRequests.NormalizeEmail(email));
    }

    private static string[] PropertyNames<T>() =>
        [.. typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Where(name => name != "EqualityContract")
            .Order(StringComparer.Ordinal)];
}
