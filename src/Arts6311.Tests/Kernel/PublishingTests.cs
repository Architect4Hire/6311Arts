using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;

namespace Arts6311.Tests.Kernel;

/// <summary>The content lifecycle, slugs and row-version tags every CMS type shares (content.md, api-contract.md).</summary>
public sealed class PublishingTests
{
    public static TheoryData<PublishStatus, PublishCommand, PublishStatus?> Transitions => new()
    {
        { PublishStatus.Draft, PublishCommand.Publish, PublishStatus.Published },
        { PublishStatus.Draft, PublishCommand.Unpublish, null },
        { PublishStatus.Draft, PublishCommand.Archive, PublishStatus.Archived },
        { PublishStatus.Scheduled, PublishCommand.Publish, PublishStatus.Published },
        { PublishStatus.Scheduled, PublishCommand.Unpublish, null },
        { PublishStatus.Scheduled, PublishCommand.Archive, PublishStatus.Archived },
        { PublishStatus.Published, PublishCommand.Publish, null },
        { PublishStatus.Published, PublishCommand.Unpublish, PublishStatus.Draft },
        { PublishStatus.Published, PublishCommand.Archive, PublishStatus.Archived },
        { PublishStatus.Archived, PublishCommand.Publish, null },
        { PublishStatus.Archived, PublishCommand.Unpublish, null },
        { PublishStatus.Archived, PublishCommand.Archive, null },
    };

    [Theory]
    [MemberData(nameof(Transitions))]
    public void EveryStatusAndCommandHasADecidedOutcome(PublishStatus from, PublishCommand command, PublishStatus? expected)
    {
        Assert.Equal(expected, PublishLifecycle.Next(from, command));
    }

    [Fact]
    public void TheTableAboveCoversEveryPair()
    {
        // A status or command added later must be decided here, not fall through to "refused" unnoticed.
        Assert.Equal(Enum.GetValues<PublishStatus>().Length * Enum.GetValues<PublishCommand>().Length, Transitions.Count);
    }

    [Theory]
    [InlineData("Candice Johnson", "candice-johnson")]
    [InlineData("Marco Da'Vila", "marco-da-vila")]
    [InlineData("Anabelle’s First Summon", "anabelle-s-first-summon")]
    [InlineData("  Socorro Muciño ", "socorro-mucino")]
    [InlineData("Ralph \"Skunkie\" Davis!!", "ralph-skunkie-davis")]
    [InlineData("Piloto", "piloto")]
    [InlineData("—", "")]
    public void ASlugIsMadeFromANameAsTheSpikeMakesIt(string name, string slug)
    {
        Assert.Equal(slug, Slugs.FromName(name));
    }

    [Fact]
    public void AMadeSlugIsNeverLongerThanTheLimitOrEndsInAHyphen()
    {
        var slug = Slugs.FromName(string.Join(' ', Enumerable.Repeat("abcdefghi", 20)));

        Assert.True(slug.Length <= Slugs.MaxLength);
        Assert.True(Slugs.IsValid(slug));
    }

    [Theory]
    [InlineData("jane-doe", true)]
    [InlineData("a", true)]
    [InlineData("route-66", true)]
    [InlineData("Jane-Doe", false)]
    [InlineData("jane--doe", false)]
    [InlineData("-jane", false)]
    [InlineData("jane-", false)]
    [InlineData("jane doe", false)]
    [InlineData("jané", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyLowerCaseHyphenatedAsciiIsASlug(string? slug, bool valid)
    {
        Assert.Equal(valid, Slugs.IsValid(slug));
    }

    [Fact]
    public void ASlugAtTheLimitIsValidAndOnePastItIsNot()
    {
        Assert.True(Slugs.IsValid(new string('a', Slugs.MaxLength)));
        Assert.False(Slugs.IsValid(new string('a', Slugs.MaxLength + 1)));
    }

    [Fact]
    public void AnEntityTagRoundTripsItsRowVersion()
    {
        var version = Guid.NewGuid();

        Assert.Equal(version, EntityTag.Parse(EntityTag.Format(version)));
        Assert.Equal(version, EntityTag.Parse($"  {EntityTag.Format(version)} "));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"0123456789abcdef0123456789abcdef\"")]
    [InlineData("\"0123456789abcdef0123456789abcdef\", \"0123456789abcdef0123456789abcdee\"")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("\"not-a-guid\"")]
    public void OnlyOneStrongTagOfOursParses(string ifMatch)
    {
        Assert.Null(EntityTag.Parse(ifMatch));
    }

    [Fact]
    public void VerifyTellsMissingFromMalformedFromStale()
    {
        var version = Guid.NewGuid();

        Assert.Null(EntityTag.Verify(version, EntityTag.Format(version), "this thing"));
        Assert.Equal(ErrorCodes.PreconditionRequired, EntityTag.Verify(version, null, "this thing")!.Code);
        Assert.Equal(ErrorCodes.PreconditionRequired, EntityTag.Verify(version, " ", "this thing")!.Code);

        var malformed = EntityTag.Verify(version, "*", "this thing")!;
        Assert.Equal(ErrorCodes.ValidationFailed, malformed.Code);
        Assert.Contains("ifMatch", malformed.FieldErrors.Keys);

        Assert.Equal(ErrorCodes.ConcurrencyConflict, EntityTag.Verify(version, EntityTag.Format(Guid.NewGuid()), "this thing")!.Code);
    }
}
