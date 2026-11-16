using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Tests;

public class HtmlToTextConverterTests
{
    [Test]
    [Arguments(null, "")]
    [Arguments("", "")]
    [Arguments("   ", "")]
    [Arguments("Plain text", "Plain text")]
    public async Task Convert_EmptyOrPlain_ReturnsInputOrEmpty(string? html, string expected)
    {
        await Assert.That(HtmlToTextConverter.Convert(html)).IsEqualTo(expected);
    }

    [Test]
    public async Task Convert_AnchorWithSingleQuotedHref_KeepsTextAndUrl()
    {
        string text = HtmlToTextConverter.Convert("Please reset your password by <a href='https://example.com/reset?code=abc'>clicking here</a>.");

        await Assert.That(text).IsEqualTo("Please reset your password by clicking here (https://example.com/reset?code=abc).");
    }

    [Test]
    public async Task Convert_AnchorWithDoubleQuotedHrefAndOtherAttributes_KeepsTextAndUrl()
    {
        string text = HtmlToTextConverter.Convert("""<a class="btn" href="https://example.com/a" target="_blank">Open</a>""");

        await Assert.That(text).IsEqualTo("Open (https://example.com/a)");
    }

    [Test]
    public async Task Convert_AnchorWithUnquotedHref_KeepsUrl()
    {
        await Assert.That(HtmlToTextConverter.Convert("<a href=https://example.com/a>go</a>")).IsEqualTo("go (https://example.com/a)");
    }

    [Test]
    public async Task Convert_HrefEntities_AreDecodedSoTheUrlIsUsable()
    {
        // HtmlEncoder.Default encodes '&' as &amp; and '+' as &#x2B; inside the emailed href.
        string text = HtmlToTextConverter.Convert("<a href='https://example.com/c?userId=1&amp;code=a&#x2B;b'>link</a>");

        await Assert.That(text).IsEqualTo("link (https://example.com/c?userId=1&code=a+b)");
    }

    [Test]
    public async Task Convert_AnchorWhoseTextIsTheUrl_DoesNotRepeatIt()
    {
        string text = HtmlToTextConverter.Convert("<a href='https://example.com/a'>https://example.com/a</a>");

        await Assert.That(text).IsEqualTo("https://example.com/a");
    }

    [Test]
    public async Task Convert_AnchorWithoutHref_KeepsOnlyText()
    {
        await Assert.That(HtmlToTextConverter.Convert("<a name='top'>Top</a>")).IsEqualTo("Top");
    }

    [Test]
    public async Task Convert_AnchorWithEmptyText_UsesUrl()
    {
        await Assert.That(HtmlToTextConverter.Convert("<a href='https://example.com/a'></a>")).IsEqualTo("https://example.com/a");
    }

    [Test]
    public async Task Convert_GreaterThanInsideAttributeValue_DoesNotEndTheTagEarly()
    {
        string text = HtmlToTextConverter.Convert("""<a title="a>b" href="https://example.com/a">Link</a>""");

        await Assert.That(text).IsEqualTo("Link (https://example.com/a)");
    }

    [Test]
    public async Task Convert_MultipleLinks_ConvertsEach()
    {
        string text = HtmlToTextConverter.Convert("<a href='https://a.example'>A</a> and <a href='https://b.example'>B</a>");

        await Assert.That(text).IsEqualTo("A (https://a.example) and B (https://b.example)");
    }

    [Test]
    public async Task Convert_EntitiesInText_AreDecoded()
    {
        string text = HtmlToTextConverter.Convert("Tom &amp; Jerry &lt;3 &quot;C#&quot; &#39;rocks&#39; &copy;&nbsp;2026");

        await Assert.That(text).IsEqualTo("Tom & Jerry <3 \"C#\" 'rocks' © 2026");
    }

    [Test]
    public async Task Convert_EncodedMarkup_StaysTextAndIsNotStripped()
    {
        // Decoding must happen after tag removal so user-supplied text like "&lt;b&gt;" is not treated as a tag.
        await Assert.That(HtmlToTextConverter.Convert("Hi &lt;b&gt;there&lt;/b&gt;")).IsEqualTo("Hi <b>there</b>");
    }

    [Test]
    public async Task Convert_BlockElementsAndBreaks_BecomeLines()
    {
        string text = HtmlToTextConverter.Convert("<p>First</p><p>Second<br>Third<br/>Fourth</p><div>Fifth</div>");

        await Assert.That(text).IsEqualTo("First\n\nSecond\nThird\nFourth\n\nFifth");
    }

    [Test]
    public async Task Convert_InlineFormattingTags_AreDropped()
    {
        await Assert.That(HtmlToTextConverter.Convert("This is <b>bold</b> and <em>emphasised</em>.")).IsEqualTo("This is bold and emphasised.");
    }

    [Test]
    public async Task Convert_ScriptStyleHeadAndComments_AreRemovedWithTheirContent()
    {
        string text = HtmlToTextConverter.Convert("<head><title>T</title></head><style>p{color:red}</style><script>alert(1)</script><!-- note -->Visible");

        await Assert.That(text).IsEqualTo("Visible");
    }

    [Test]
    public async Task Convert_WhitespaceIsCollapsedAndBlankRunsLimited()
    {
        string text = HtmlToTextConverter.Convert("  Line   one \r\n\r\n\r\n\r\n   Line\ttwo  ");

        await Assert.That(text).IsEqualTo("Line one\n\nLine two");
    }

    [Test]
    public async Task Convert_UnterminatedAnchor_KeepsTextAndUrl()
    {
        await Assert.That(HtmlToTextConverter.Convert("<a href='https://example.com'>never closed")).IsEqualTo("never closed (https://example.com)");
    }

    [Test]
    public async Task Convert_AnchorMissingItsClosingTagBeforeAnotherAnchor_KeepsBothTargets()
    {
        string text = HtmlToTextConverter.Convert("<a href='https://a.example'>A <a href='https://b.example'>B</a>");

        await Assert.That(text).IsEqualTo("A (https://a.example) B (https://b.example)");
    }

    [Test]
    [Arguments("<a href='https://example.com/x'>go</a>", "go (https://example.com/x)")]
    [Arguments("<a href='HTTP://EXAMPLE.COM/x'>go</a>", "go (HTTP://EXAMPLE.COM/x)")]
    [Arguments("<a href='mailto:help@example.com'>mail us</a>", "mail us (mailto:help@example.com)")]
    [Arguments("<a href='javascript:alert(1)'>click</a>", "click")]
    [Arguments("<a href='JavaScript:alert(1)'>click</a>", "click")]
    [Arguments("<a href='data:text/html;base64,AAAA'>click</a>", "click")]
    [Arguments("<a href='vbscript:x'>click</a>", "click")]
    [Arguments("<a href='/relative/path'>click</a>", "click")]
    [Arguments("<a href='javascript:alert(1)'></a>", "")]
    public async Task Convert_LinkTargets_AreOnlyEmittedForHttpHttpsAndMailto(string html, string expected)
    {
        await Assert.That(HtmlToTextConverter.Convert(html)).IsEqualTo(expected);
    }

    [Test]
    public async Task Convert_EncodedJavascriptScheme_IsAlsoDropped()
    {
        // &#106; is "j": the scheme check must run on the decoded value.
        await Assert.That(HtmlToTextConverter.Convert("<a href='&#106;avascript:alert(1)'>click</a>")).IsEqualTo("click");
    }

    [Test]
    public async Task TryConvert_ReturnsTrueForNormalInput()
    {
        bool converted = HtmlToTextConverter.TryConvert("<p>Hello</p>", out string text);

        using (Assert.Multiple())
        {
            await Assert.That(converted).IsTrue();
            await Assert.That(text).IsEqualTo("Hello");
        }
    }

    [Test]
    public async Task TryConvert_InputOverTheLengthCap_ReturnsFalseWithoutConverting()
    {
        bool converted = HtmlToTextConverter.TryConvert(new string('x', HtmlToTextConverter.MaxInputLength + 1), out string text);

        using (Assert.Multiple())
        {
            await Assert.That(converted).IsFalse();
            await Assert.That(text).IsEmpty();
        }
    }

    [Test]
    [Arguments("<a ", 20_000)]
    [Arguments("<script>", 12_000)]
    [Arguments("<!--", 20_000)]
    public async Task TryConvert_PathologicalInputUnderTheCap_ReturnsConsistentlyWithoutThrowing(string unit, int repetitions)
    {
        // Safety property only: a single call completes (the 30 s bound is a hang detector, not a performance claim),
        // never throws, and reports its outcome consistently (empty text when it gives up). Whether the regex time
        // limit trips depends on the machine, so nothing here depends on it, and the call is made only once so two
        // calls can never land on opposite sides of the limit.
        string html = string.Concat(Enumerable.Repeat(unit, repetitions));
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        bool converted = HtmlToTextConverter.TryConvert(html, out string text);
        stopwatch.Stop();

        using (Assert.Multiple())
        {
            await Assert.That(html.Length).IsLessThanOrEqualTo(HtmlToTextConverter.MaxInputLength);
            await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(30));
            if (!converted)
            {
                await Assert.That(text).IsEmpty();
            }
        }
    }

    [Test]
    public async Task Convert_AdjacentAnchorsWithoutWhitespace_AreSeparated()
    {
        string text = HtmlToTextConverter.Convert("<a href='http://a.example'>one</a><a href='http://b.example'>two</a>");

        await Assert.That(text).IsEqualTo("one (http://a.example) two (http://b.example)");
    }

    [Test]
    public async Task Convert_AnchorFollowedDirectlyByWord_IsSeparated()
    {
        await Assert.That(HtmlToTextConverter.Convert("<a href='http://a.example'>one</a>two")).IsEqualTo("one (http://a.example) two");
    }

    [Test]
    public async Task Convert_AnchorFollowedByPunctuation_StaysAttached()
    {
        await Assert.That(HtmlToTextConverter.Convert("(<a href='http://a.example'>one</a>), done.")).IsEqualTo("(one (http://a.example)), done.");
    }
}
