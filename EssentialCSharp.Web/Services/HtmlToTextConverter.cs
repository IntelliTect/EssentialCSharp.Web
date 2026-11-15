using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EssentialCSharp.Web.Services;

/// <summary>
/// Derives a plain-text alternative from the small HTML fragments the app emails (confirmation and password-reset links).
/// It is not a general HTML renderer. Links are kept as "text (url)" so the target is not lost (only http, https and
/// mailto targets are emitted; other schemes such as javascript: are dropped and only the link text is kept),
/// block-level elements become line breaks, all other tags are dropped and HTML entities are decoded.
/// The output is plain text, so nothing in it is ever interpreted as markup.
/// </summary>
/// <remarks>
/// The patterns can backtrack badly on adversarial markup, and <c>IEmailSender</c> accepts arbitrary HTML, so
/// input is length-capped and every pattern has a match timeout. <see cref="TryConvert"/> reports failure instead of
/// throwing so the caller can send the email without a text part.
/// </remarks>
internal static partial class HtmlToTextConverter
{
    /// <summary>Inputs longer than this are not converted. The app's emails are a few hundred characters.</summary>
    internal const int MaxInputLength = 100_000;

    private const int MatchTimeoutMilliseconds = 500;

    /// <summary>Converts <paramref name="html"/> to text, or returns an empty string when it cannot be converted safely.</summary>
    public static string Convert(string? html) => TryConvert(html, out string text) ? text : string.Empty;

    /// <summary>
    /// Converts <paramref name="html"/> to plain text. Returns false (and an empty string) when the input is too long
    /// or conversion exceeded its time limit.
    /// </summary>
    public static bool TryConvert(string? html, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrWhiteSpace(html))
        {
            return true;
        }

        if (html.Length > MaxInputLength)
        {
            return false;
        }

        try
        {
            string converted = NonContentElementRegex().Replace(html, "");
            converted = AnchorRegex().Replace(converted, ReplaceAnchor);
            converted = LineBreakTagRegex().Replace(converted, "\n");
            converted = TagRegex().Replace(converted, "");
            converted = WebUtility.HtmlDecode(converted);
            text = NormalizeWhitespace(converted);
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string ReplaceAnchor(Match match)
    {
        string url = ExtractSafeUrl(match.Groups["attrs"].Value);
        string label = NormalizeInline(WebUtility.HtmlDecode(TagRegex().Replace(match.Groups["inner"].Value, "")));
        // Whitespace just before the closing tag must survive so adjacent anchors do not run together.
        // The same applies when the anchor is directly followed by another tag or by a word (captured by the "next" lookahead).
        string inner = match.Groups["inner"].Value;
        bool needsSeparator = (inner.Length > 0 && char.IsWhiteSpace(inner[^1])) || match.Groups["next"].Success;
        string trailingSpace = needsSeparator ? " " : "";
        if (url.Length == 0)
        {
            return label + trailingSpace;
        }

        return (label.Length == 0 || string.Equals(label, url, StringComparison.Ordinal)
            ? url
            : $"{label} ({url})") + trailingSpace;
    }

    private static string ExtractSafeUrl(string attributes)
    {
        Match href = HrefRegex().Match(attributes);
        if (!href.Success)
        {
            return string.Empty;
        }

        string raw = href.Groups["double"].Success ? href.Groups["double"].Value
            : href.Groups["single"].Success ? href.Groups["single"].Value
            : href.Groups["bare"].Value;
        string url = WebUtility.HtmlDecode(raw).Trim();
        return IsAllowedScheme(url) ? url : string.Empty;
    }

    private static bool IsAllowedScheme(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeInline(string value) => InlineWhitespaceRegex().Replace(value, " ").Trim();

    private static string NormalizeWhitespace(string value)
    {
        StringBuilder result = new();
        int blankLines = 0;
        foreach (string rawLine in value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = NormalizeInline(rawLine.Replace(' ', ' '));
            if (line.Length == 0)
            {
                blankLines++;
                continue;
            }

            if (result.Length > 0)
            {
                result.Append('\n');
                if (blankLines > 0)
                {
                    result.Append('\n');
                }
            }

            blankLines = 0;
            result.Append(line);
        }

        return result.ToString();
    }

    [GeneratedRegex(@"<(script|style|head)\b[^>]*>.*?</\1\s*>|<!--.*?-->", RegexOptions.IgnoreCase | RegexOptions.Singleline, MatchTimeoutMilliseconds)]
    private static partial Regex NonContentElementRegex();

    // The attribute group skips over quoted values so a ">" inside an attribute does not end the tag early.
    // An anchor ends at its closing tag, at the next anchor or at the end of the input, so an unterminated anchor
    // still keeps its link target. The optional "next" group is an empty lookahead that only records whether a tag,
    // letter or digit follows the anchor, so the emitted "(url)" can be separated from it.
    [GeneratedRegex(@"<a\b(?<attrs>(?:[^>""']|""[^""]*""|'[^']*')*)>(?<inner>(?:(?!<a\b).)*?)(?:</a\s*>|(?=<a\b)|$)(?<next>(?=[<\p{L}\p{N}]))?", RegexOptions.IgnoreCase | RegexOptions.Singleline, MatchTimeoutMilliseconds)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"\bhref\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<bare>[^\s""'>]+))", RegexOptions.IgnoreCase, MatchTimeoutMilliseconds)]
    private static partial Regex HrefRegex();

    [GeneratedRegex(@"<br\b[^>]*>|</?(?:p|div|li|ul|ol|tr|table|h[1-6]|blockquote)\b[^>]*>", RegexOptions.IgnoreCase, MatchTimeoutMilliseconds)]
    private static partial Regex LineBreakTagRegex();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.None, MatchTimeoutMilliseconds)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"[ \t\f\v]+", RegexOptions.None, MatchTimeoutMilliseconds)]
    private static partial Regex InlineWhitespaceRegex();
}
