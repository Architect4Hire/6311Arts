using Arts6311.Domain.Managers.Html;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// The shared sanitizer's own suite (prompt 2.6, ADR 0034): known XSS payloads go in, nothing executable comes out;
/// allowed copy comes out exactly as it went in; and sanitizing twice changes nothing.
/// </summary>
/// <remarks>
/// The payloads are drawn from the OWASP XSS Filter Evasion Cheat Sheet and the usual parser-confusion (mXSS) cases.
/// Each asserts on the output's own terms — no element, attribute or URL that can run code survives — rather than
/// on an exact string, except where the expected text is the point.
/// </remarks>
public sealed class HtmlSanitizerTests
{
    private const string Image = "/media/0199f0a0-0000-7000-8000-0000000000aa/800.webp";

    private static readonly AllowListHtmlSanitizer Sanitizer = new();

    public static TheoryData<string> XssPayloads() => new(
    [
        "<script>alert(1)</script>",
        "<SCRIPT SRC=//xss.example/xss.js></SCRIPT>",
        "<scr<script>ipt>alert(1)</scr</script>ipt>",
        "<IMG SRC=\"javascript:alert('XSS');\">",
        "<IMG SRC=javascript:alert('XSS')>",
        "<IMG SRC=JaVaScRiPt:alert('XSS')>",
        "<img src=x onerror=alert(1)>",
        "<img src=\"/media/0199f0a0-0000-7000-8000-0000000000aa/800.webp\" onerror=\"alert(1)\" alt=\"x\">",
        "<a href=\"javascript:alert(1)\">click</a>",
        "<a href=\"jav&#x09;ascript:alert(1)\">click</a>",
        "<a href=\"&#106;&#97;&#118;&#97;&#115;&#99;&#114;&#105;&#112;&#116;&#58;alert(1)\">click</a>",
        "<a href=\" javascript:alert(1)\">click</a>",
        "<a href=\"vbscript:msgbox(1)\">click</a>",
        "<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">click</a>",
        "<a href=\"//evil.example/\">protocol-relative</a>",
        "<a href=\"/\\evil.example/\">backslash</a>",
        "<p onclick=\"alert(1)\" onmouseover=alert(1)>hover</p>",
        "<p style=\"background:url(javascript:alert(1))\">styled</p>",
        "<p style=\"width: expression(alert(1))\">old IE</p>",
        "<div style=\"behavior: url(xss.htc)\">behaviour</div>",
        "<svg onload=alert(1)><circle r=40></circle></svg>",
        "<svg><script>alert(1)</script></svg>",
        "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
        "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">",
        "<iframe src=\"javascript:alert(1)\"></iframe>",
        "<iframe srcdoc=\"&lt;script&gt;alert(1)&lt;/script&gt;\"></iframe>",
        "<object data=\"javascript:alert(1)\"></object>",
        "<embed src=\"javascript:alert(1)\">",
        "<form action=\"javascript:alert(1)\"><button>go</button></form>",
        "<meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\">",
        "<base href=\"javascript:alert(1)//\">",
        "<link rel=stylesheet href=\"javascript:alert(1)\">",
        "<style>@import 'javascript:alert(1)';</style>",
        "<body onload=alert(1)>",
        "<details open ontoggle=alert(1)>",
        "<video><source onerror=\"alert(1)\"></video>",
        "<template><img src=x onerror=alert(1)></template>",
        "<!--<img src=x onerror=alert(1)>-->",
        "<![CDATA[<script>alert(1)</script>]]>",
        "<p>\"><script>alert(1)</script></p>",
        "<a href=\"https://ok.example/\" target=\"_blank\" rel=\"opener\">tabnab</a>",
        "<p id=\"x\" class=\"y\" data-x=\"z\">attributes</p>",
        "<xmp><img src=x onerror=alert(1)></xmp>",
        "<a href=\"https://ok.example/\" onfocus=\"alert(1)\" autofocus>focus</a>",
        "<a href=\"javascript&colon;alert(1)\">named colon</a>",
        "<a href=\"java&Tab;script:alert(1)\">named tab</a>",
        "<a href=\"java&NewLine;script:alert(1)\">named newline</a>",
        "<a href=\"java\rscript:alert(1)\">raw CR</a>",
        "<a href=\"\u0001javascript:alert(1)\">leading control</a>",
        "<a href=\"JAVASCRIPT:alert(1)\">upper</a>",
        "<a href=\"https://user@evil.example/\" ping=\"https://evil.example/\">ping</a>",
        "<image src=x onerror=alert(1)>",
        "<img/src/onerror=alert(1)>",
        "<img srcset=\"javascript:alert(1) 1x\" alt=\"x\">",
        "<button formaction=\"javascript:alert(1)\">go</button>",
        "<a xlink:href=\"javascript:alert(1)\">xlink</a>",
        "<a src=\"javascript:alert(1)\">src on a link</a>",
        "<table><tr><td><img src=x onerror=alert(1)></td></tr></table>",
        "<select><template><style><img src=x onerror=alert(1)></style></template></select>",
        "<math><annotation-xml encoding=\"text/html\"><style><img src=x onerror=alert(1)></style></annotation-xml></math>",
        "<ScRiPt>alert(1)</sCrIpT>",
        "<?php echo 1; ?><p>pi</p>",
        "<img src=\"/media/0199f0a0-0000-7000-8000-0000000000aa/800.webp\" alt='\"><script>alert(1)</script>'>",
        "<p title=\"x\" lang=\"en\" dir=\"rtl\">extra attributes</p>",
    ]);

    [Theory]
    [MemberData(nameof(XssPayloads))]
    public void NothingExecutableSurvivesAKnownPayload(string payload)
    {
        var result = Sanitizer.Sanitize(payload);

        AssertSafe(result.Html);

        // And what it says it removed is words, never markup to render.
        Assert.All(result.Removed, entry => Assert.DoesNotContain("javascript", entry, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(XssPayloads))]
    public void APayloadsOutputIsStableUnderASecondPass(string payload)
    {
        var once = Sanitizer.Sanitize(payload).Html;

        Assert.Equal(once, Sanitizer.Sanitize(once).Html);
    }

    [Fact]
    public void AllowedCopyComesThroughExactly()
    {
        const string copy =
            "<h2>Your privacy</h2><p>We collect <strong>only</strong> what we <em>need</em>.<br>Thanks.</p>"
            + "<h2>What we keep</h2><ul><li>Your name</li><li>Your email</li></ul>"
            + "<ol><li>One</li><li>Two</li></ol>";

        var result = Sanitizer.Sanitize(copy);

        Assert.Equal(copy, result.Html);
        Assert.Empty(result.Removed);
    }

    [Theory]
    [InlineData("/visit")]
    [InlineData("#cookies")]
    [InlineData("https://squareup.com/legal")]
    [InlineData("mailto:hello@6311arts.com")]
    [InlineData("tel:+17735550100")]
    public void EveryLinkBecomesItsWordsWhileLinksAreOff(string href)
    {
        // ADR 0045: no links at this time, even to an address the link rule would allow.
        var result = Sanitizer.Sanitize($"<p>See <a href=\"{href}\">this</a>.</p>");

        Assert.Equal("<p>See this.</p>", result.Html);
        Assert.Contains("a link (its text kept)", result.Removed);
    }

    [Fact]
    public void EveryHeadingBecomesTheOneLevelABodyHas()
    {
        var result = Sanitizer.Sanitize("<h1>A</h1><h3>B</h3><h4>C</h4><h5>D</h5><h6>E</h6><h2>F</h2>");

        Assert.Equal("<h2>A</h2><h2>B</h2><h2>C</h2><h2>D</h2><h2>E</h2><h2>F</h2>", result.Html);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void AQuoteLosesItsTagButKeepsItsParagraphs()
    {
        var result = Sanitizer.Sanitize("<blockquote><p>Art for everyone.</p></blockquote>");

        Assert.Equal("<p>Art for everyone.</p>", result.Html);
        Assert.Contains("<blockquote> (its text kept)", result.Removed);
    }

    [Fact]
    public void AWrapperLosesItsTagButKeepsItsWords()
    {
        var result = Sanitizer.Sanitize("<div><span style=\"color:red\">Open <b>Thursday</b> to <i>Sunday</i></span></div>");

        Assert.Equal("Open <strong>Thursday</strong> to <em>Sunday</em>", result.Html);
        Assert.Contains("<div> (its text kept)", result.Removed);
        Assert.Contains("<span> (its text kept)", result.Removed);
    }

    [Fact]
    public void ScriptAndStyleGoWithTheirContentsNotAsText()
    {
        var result = Sanitizer.Sanitize("<p>Before</p><script>steal()</script><style>p{}</style><p>After</p>");

        Assert.Equal("<p>Before</p><p>After</p>", result.Html);
        Assert.DoesNotContain("steal", result.Html, StringComparison.Ordinal);
        Assert.Contains("<script> and its contents", result.Removed);
    }

    [Theory]
    [InlineData("b", 66_000)]
    [InlineData("div", 39_000)]
    [InlineData("blockquote", 15_000)]
    public void ABodyNestedPastTheLimitIsRefusedBeforeAnythingParsesIt(string tag, int depth)
    {
        // Measured: 66,000 nested <b> overflowed the stack in the tree walks and killed the process (security review).
        var result = Sanitizer.Sanitize(string.Concat(Enumerable.Repeat($"<{tag}>", depth)) + "x");

        Assert.True(result.IsEmpty);
        Assert.NotNull(result.Refused);
    }

    [Theory]
    [InlineData("b", "u")]
    [InlineData("blockquote", "u")]
    [InlineData("div", "em")]
    public void AnEndTagThatClosesNothingDoesNotBuyAnotherLevelOfNesting(string nested, string stray)
    {
        // The evasion the depth estimate used to allow (security review): every `</stray>` decremented the
        // counter even though no such element was open, so this reported a depth of about 1 while the parser
        // built a tree nesting once per `<nested>` — the exact shape
        // ABodyNestedPastTheLimitIsRefusedBeforeAnythingParsesIt proves kills the process.
        var depth = HtmlPolicy.MaxNestingDepth * 3;
        var evasive = string.Concat(Enumerable.Repeat($"<{nested}></{stray}>", depth)) + "x";

        Assert.True(HtmlPolicy.EstimateNestingDepth(evasive) > HtmlPolicy.MaxNestingDepth);

        var result = Sanitizer.Sanitize(evasive);
        Assert.True(result.IsEmpty);
        Assert.NotNull(result.Refused);
    }

    [Fact]
    public void AnEndTagStillClosesWhatTheMarkupLeftOpenInsideIt()
    {
        // `</blockquote>` closes the <em> the markup never closed, as a parser would, so what follows is not
        // charged for it. Without this the estimate would drift up on sloppy but harmless copy and start
        // refusing it.
        Assert.Equal(2, HtmlPolicy.EstimateNestingDepth("<blockquote><em>a</blockquote><strong>b</strong>"));

        // And a stray end tag before anything is open cannot push the count below zero.
        Assert.Equal(1, HtmlPolicy.EstimateNestingDepth("</em></em><strong>b</strong>"));
    }

    [Fact]
    public void OrdinaryCopyIsNeverTooDeepUnclosedParagraphsAndItemsIncluded()
    {
        var unclosed = string.Concat(Enumerable.Repeat("<p>para<ul><li>one<li>two</ul>", 5_000));
        var deepButFine = string.Concat(Enumerable.Repeat("<blockquote>", HtmlPolicy.MaxNestingDepth)) + "x";

        Assert.Null(Sanitizer.Sanitize(unclosed).Refused);
        Assert.Null(Sanitizer.Sanitize(deepButFine).Refused);
        Assert.Equal(HtmlPolicy.MaxNestingDepth, HtmlPolicy.EstimateNestingDepth(deepButFine));
        Assert.Equal(2, HtmlPolicy.EstimateNestingDepth("<blockquote><p>a<br><img src=x><ul><li>b</ul></blockquote>"));
    }

    [Fact]
    public void NoAttributeIsKept()
    {
        var result = Sanitizer.Sanitize(
            $"<p alt=\"x\" href=\"/visit\">P</p><h2 id=\"h\" src=\"{Image}\">H</h2><ul title=\"t\"><li value=\"2\">L</li></ul>");

        Assert.Equal("<p>P</p><h2>H</h2><ul><li>L</li></ul>", result.Html);
    }

    [Fact]
    public void WhatWasRemovedIsDescribedInPlainBoundedWords()
    {
        var result = Sanitizer.Sanitize(
            "<x-very-long-custom-element-name-that-goes-on-and-on onclick-evil<b>=1>t</x-very-long-custom-element-name-that-goes-on-and-on>");

        Assert.NotEmpty(result.Removed);
        Assert.All(result.Removed, entry =>
        {
            Assert.True(entry.Length <= 60, entry);
            Assert.DoesNotContain("<b", entry, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ALinkThatLosesItsAddressBecomesPlainText()
    {
        var result = Sanitizer.Sanitize("<p><a href=\"javascript:alert(1)\">Click</a> here</p>");

        Assert.Equal("<p>Click here</p>", result.Html);
    }

    [Theory]
    [InlineData(Image)]
    [InlineData("/media/0199f0a0-0000-7000-8000-0000000000aa/400.jpg?v=0123456789ab")]
    [InlineData("https://elsewhere.example/picture.jpg")]
    [InlineData("/media/not-a-guid/800.webp")]
    [InlineData("/media/0199f0a0-0000-7000-8000-0000000000aa/800.gif")]
    [InlineData("/media/0199f0a0-0000-7000-8000-0000000000aa/original")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    [InlineData("//cdn.example/0199f0a0-0000-7000-8000-0000000000aa/800.webp")]
    [InlineData("/media/0199f0a0-0000-7000-8000-0000000000aa/1200.webp")]
    [InlineData("/media/0199f0a0-0000-7000-8000-0000000000aa/99.jpg")]
    public void EveryPictureIsRemovedWhilePicturesAreOff(string src)
    {
        // ADR 0045: a record's pictures are its hero or gallery, so even a library rendition leaves the body.
        var result = Sanitizer.Sanitize($"<p>Look<img src=\"{src}\" alt=\"x\"></p>");

        Assert.Equal("<p>Look</p>", result.Html);
        Assert.Empty(result.Images);
        Assert.Empty(result.MediaAssetIds);
        Assert.Contains("a picture", result.Removed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p>")]
    [InlineData("<p> </p><ul><li></li></ul>")]
    [InlineData("<script>only()</script>")]
    public void NothingWorthKeepingIsEmpty(string? html)
    {
        var result = Sanitizer.Sanitize(html);

        Assert.True(result.IsEmpty);
        Assert.Empty(result.Images);
    }

    [Fact]
    public void AllowedCopyIsStableUnderASecondPass()
    {
        var once = Sanitizer.Sanitize("<h2>A &amp; B</h2><p>5 &lt; 6 &gt; 4 “quoted” — dash<br>line</p>").Html;

        Assert.Equal(once, Sanitizer.Sanitize(once).Html);
        Assert.Contains("A &amp; B", once, StringComparison.Ordinal);
        Assert.Contains("5 &lt; 6 &gt; 4", once, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/media/0199f0a0-0000-7000-8000-0000000000aa/800.webp", true)]
    [InlineData("/media/0199f0a0-0000-7000-8000-0000000000aa/800.webp\n", false)]
    [InlineData("/media/0199f0a0-0000-7000-8000-0000000000aa/800.webp?v=0123456789ab\n", false)]
    [InlineData(" /media/0199f0a0-0000-7000-8000-0000000000aa/800.webp", false)]
    public void TheImageRuleIsAnchoredAtBothEnds(string src, bool allowed)
    {
        Assert.Equal(allowed, HtmlPolicy.TryParseMediaImage(src, out _));
    }

    [Theory]
    [InlineData("/about", true)]
    [InlineData("/", true)]
    [InlineData("#section", true)]
    [InlineData("https://6311arts.com/", true)]
    [InlineData("mailto:hello@6311arts.com", true)]
    [InlineData("tel:+17735550100", true)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("#", false)]
    [InlineData("about", false)]
    [InlineData("ftp://files.example/", false)]
    [InlineData("https:///nohost", false)]
    [InlineData("java script:alert(1)", false)]
    public void TheLinkRuleKeepsOnlyThisSiteAndTheAllowedSchemes(string href, bool allowed)
    {
        Assert.Equal(allowed, HtmlPolicy.IsAllowedLink(href));
    }

    /// <summary>No element, attribute or URL that could run code or load something from elsewhere.</summary>
    private static void AssertSafe(string html)
    {
        var parsed = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(string.Empty);
        parsed.Body!.InnerHtml = html;

        foreach (var element in parsed.Body.QuerySelectorAll("*"))
        {
            Assert.Contains(element.LocalName, HtmlPolicy.AllowedTags);
            foreach (var attribute in element.Attributes)
            {
                Assert.Contains(attribute.Name, HtmlPolicy.AllowedAttributes);
            }

            if (element.GetAttribute("href") is { } href)
            {
                Assert.True(HtmlPolicy.IsAllowedLink(href), $"href {href}");
            }

            if (element.GetAttribute("src") is { } src)
            {
                Assert.True(HtmlPolicy.TryParseMediaImage(src, out _), $"src {src}");
            }
        }

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(parsed.Body.QuerySelectorAll("script, style, iframe, object, embed, svg, math, form, base, meta, link"));
    }
}
