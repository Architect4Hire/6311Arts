using Arts6311.Domain.Managers.Html;
using Arts6311.Domain.Modules.Media.Managers;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// The kernel's picture rule can't name the Media module, so it keeps its own copy of Media's delivery contract
/// (ADR 0034). This pins the copy: every URL Media hands out passes, and the widths are Media's own.
/// </summary>
public sealed class HtmlPolicyMediaContractTests
{
    [Fact]
    public void TheWidthsAreMedias()
    {
        Assert.Equal(MediaPolicy.RenditionWidths, HtmlPolicy.MediaRenditionWidths);
    }

    [Fact]
    public void EveryRenditionUrlMediaHandsOutIsAPictureABodyMayShow()
    {
        var assetId = Guid.Parse("0199f0a0-0000-7000-8000-0000000000aa");
        var checksum = new string('a', 64);

        foreach (var rendition in MediaRenditionName.All)
        {
            var versioned = MediaUrls.For(assetId, rendition.ToString(), checksum);
            var plain = versioned[..versioned.IndexOf('?', StringComparison.Ordinal)];

            Assert.True(HtmlPolicy.TryParseMediaImage(versioned, out var parsed), versioned);
            Assert.Equal(assetId, parsed);
            Assert.True(HtmlPolicy.TryParseMediaImage(plain, out _), plain);
        }
    }
}
