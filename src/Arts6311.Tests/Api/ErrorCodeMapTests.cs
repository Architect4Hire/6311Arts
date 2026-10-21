using System.Reflection;
using Arts6311.Api.Conventions;
using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Redirects.Managers;

namespace Arts6311.Tests.Api;

/// <summary>
/// ADR 0021 claims "one code, one status, in one place". These tests are what makes the claim true rather than
/// aspirational: a code added to <see cref="ErrorCodes"/> without a status would silently answer 400.
/// </summary>
public sealed class ErrorCodeMapTests
{
    [Fact]
    public void EveryErrorCodeHasADeclaredStatus()
    {
        // The failure this catches is quiet: StatusFor falls back to 400 for an unmapped code, so a new code would
        // ship with a plausible-looking status that the contract never promised.
        var unmapped = SharedErrorCodes()
            .Where(code => !ApiProblemDetails.IsDeclared(code.Value))
            .Select(code => $"{code.Name} ({code.Value})")
            .ToList();

        Assert.Empty(unmapped);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(405)]
    [InlineData(409)]
    [InlineData(413)]
    [InlineData(415)]
    [InlineData(500)]
    public void EachEdgeStatusHasItsOwnCodeThatMapsBack(int status)
    {
        // The other direction: a framework status whose code fell through to the generic one, or whose code does
        // not lead back to the same status.
        var code = ApiProblemDetails.CodeForStatus(status);

        Assert.NotEqual(ErrorCodes.RequestRejected, code);
        Assert.Equal(status, ApiProblemDetails.StatusFor(code));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(502)]
    [InlineData(503)]
    public void EveryServerFaultSharesOneCode(int status)
    {
        // Deliberately one code for the whole 5xx family: the caller can do nothing differently for a 502 than for
        // a 500, and the actual cause is in the logs under the correlation id. server.error maps to 500.
        Assert.Equal(ErrorCodes.ServerError, ApiProblemDetails.CodeForStatus(status));
    }

    [Fact]
    public void ACodeNeverMeansTwoStatuses()
    {
        // request.rejected used to be the code for every edge status outside a handful, so one code meant 405, 415
        // and 413 at once — exactly what ADR 0021 forbids. Each status now has its own code, and this holds the line.
        var byCode = new Dictionary<string, int>(StringComparer.Ordinal);

        // 4xx only: the 5xx family shares server.error by design (see EveryServerFaultSharesOneCode).
        foreach (var status in Enumerable.Range(400, 100))
        {
            var code = ApiProblemDetails.CodeForStatus(status);
            if (code == ErrorCodes.RequestRejected)
            {
                // The deliberate remainder: it maps to 400 and is not claimed to describe any other status.
                continue;
            }

            if (byCode.TryGetValue(code, out var seen))
            {
                Assert.Equal(seen, status);
            }

            byCode[code] = status;
        }

        Assert.NotEmpty(byCode);
    }

    [Fact]
    public void EveryCodeFollowsTheAreaReasonShape()
    {
        // api-contract.md: codes are `area.reason`, lower snake_case, with a dotted sub-area allowed
        // (`paging.cursor.invalid`). They are part of the contract, so the shape is checked rather than trusted.
        Assert.All(SharedErrorCodes(), code =>
            Assert.Matches("^[a-z][a-z0-9]*(\\.[a-z][a-z0-9_]*)+$", code.Value));
    }

    [Theory]
    [InlineData(MediaErrorCodes.TypeUnsupported, 415)]
    [InlineData(MediaErrorCodes.ExtensionMismatch, 415)]
    [InlineData(MediaErrorCodes.TooLarge, 413)]
    [InlineData(MediaErrorCodes.DimensionsOutOfRange, 400)]
    [InlineData(MediaErrorCodes.Undecodable, 400)]
    [InlineData(MediaErrorCodes.Duplicate, 409)]
    [InlineData(MediaErrorCodes.InUse, 409)]
    [InlineData(ErrorCodes.PreconditionRequired, 428)]
    public void TheMediaCodesHaveTheirDeclaredStatus(string code, int status)
    {
        Assert.Equal(status, ApiProblemDetails.StatusFor(code));
    }

    [Fact]
    public void EveryMediaCodeIsInTheTableAbove()
    {
        // A new media code without a row above would ship answering 400 by fallback.
        var codes = typeof(MediaErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                MediaErrorCodes.DimensionsOutOfRange, MediaErrorCodes.Duplicate, MediaErrorCodes.ExtensionMismatch,
                MediaErrorCodes.InUse, MediaErrorCodes.TooLarge, MediaErrorCodes.TypeUnsupported, MediaErrorCodes.Undecodable,
            ],
            codes);
        Assert.All(codes, code => Assert.Matches("^[a-z][a-z0-9]*(\\.[a-z][a-z0-9_]*)+$", code));
    }

    [Fact]
    public void EveryContentCodeIsAConflict()
    {
        // ADR 0031: a taken or locked slug, or a command that does not apply, is a state the editor resolves.
        var codes = typeof(ContentErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [ContentErrorCodes.SlugLocked, ContentErrorCodes.SlugTaken, ContentErrorCodes.StatusTransitionInvalid],
            codes);
        Assert.All(codes, code =>
        {
            Assert.Equal(409, ApiProblemDetails.StatusFor(code));
            Assert.Matches("^[a-z][a-z0-9]*(\\.[a-z][a-z0-9_]*)+$", code);
        });
    }

    [Fact]
    public void EveryCatalogCodeIsAConflict()
    {
        // ADR 0037: a SKU another record in the CMS already uses is a state the editor resolves by choosing another.
        var codes = typeof(CatalogErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal([CatalogErrorCodes.SkuTaken], codes);
        Assert.All(codes, code =>
        {
            Assert.Equal(409, ApiProblemDetails.StatusFor(code));
            Assert.Matches("^[a-z][a-z0-9]*(\\.[a-z][a-z0-9_]*)+$", code);
        });
    }

    [Fact]
    public void EveryRedirectCodeIsAConflict()
    {
        // ADR 0035: a taken old address, a loop or a chain is a state the admin resolves.
        var codes = typeof(RedirectErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal([RedirectErrorCodes.FromTaken, RedirectErrorCodes.LoopOrChain], codes);
        Assert.All(codes, code =>
        {
            Assert.Equal(409, ApiProblemDetails.StatusFor(code));
            Assert.Matches("^[a-z][a-z0-9]*(\\.[a-z][a-z0-9_]*)+$", code);
        });
    }

    [Fact]
    public void TheScanActuallySeesTheCodes()
    {
        var codes = SharedErrorCodes().Select(code => code.Value).ToList();

        Assert.Contains(ErrorCodes.ValidationFailed, codes);
        Assert.Contains(ErrorCodes.IdempotencyKeyReused, codes);
        Assert.Contains(ErrorCodes.ServerError, codes);
        Assert.Contains(IdentityErrorCodes.LockedOut, codes);
        Assert.Contains(IdentityErrorCodes.TokenInvalid, codes);
    }

    /// <summary>
    /// Every shared code constant: the kernel's, plus Identity's, which are raised on the internal routes and so
    /// are part of the contract the gateway reads even though no browser sees them.
    /// </summary>
    private static List<(string Name, string Value)> SharedErrorCodes() =>
    [
        .. new[] { typeof(ErrorCodes), typeof(IdentityErrorCodes) }
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (field.Name, (string)field.GetRawConstantValue()!)),
    ];
}
