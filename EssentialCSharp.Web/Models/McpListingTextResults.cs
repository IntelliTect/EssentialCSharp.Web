using System.Globalization;
using System.Text;

namespace EssentialCSharp.Web.Models;

public sealed record ListingSourceCodeTextResult(
    int ChapterNumber,
    int ListingNumber,
    string LanguageHint,
    string Content)
{
    public string ToMcpString()
    {
        StringBuilder sb = new();
        AppendTo(sb, "##");
        return sb.ToString();
    }

    internal void AppendTo(StringBuilder sb, string headingPrefix)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"{headingPrefix} Listing {ChapterNumber}.{ListingNumber}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"```{LanguageHint}");
        sb.AppendLine(Content);
        sb.AppendLine("```");
    }
}

public sealed record RelatedBookExplanationTextResult(
    string? Heading,
    int? ChapterNumber,
    string ChunkText)
{
    internal void AppendTo(StringBuilder sb)
    {
        if (!string.IsNullOrEmpty(Heading))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"**{Heading}** (Chapter {ChapterNumber})");
        }

        sb.AppendLine(ChunkText);
        sb.AppendLine();
    }
}

public sealed record ListingWithContextTextResult(
    ListingSourceCodeTextResult Listing,
    IReadOnlyList<RelatedBookExplanationTextResult> RelatedBookExplanations)
{
    public string ToMcpString()
    {
        StringBuilder sb = new();
        Listing.AppendTo(sb, "##");
        sb.AppendLine();

        if (RelatedBookExplanations.Count > 0)
        {
            sb.AppendLine("### Related Book Explanations");
            sb.AppendLine();

            foreach (RelatedBookExplanationTextResult explanation in RelatedBookExplanations)
            {
                explanation.AppendTo(sb);
            }
        }

        return sb.ToString();
    }
}

public sealed record ListingSearchTextResult(
    string Pattern,
    IReadOnlyList<ListingSourceCodeTextResult> Matches)
{
    public string ToMcpString()
    {
        StringBuilder sb = new();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Listings Containing '{Pattern}' ({Matches.Count} result{(Matches.Count == 1 ? "" : "s")})");
        sb.AppendLine();

        foreach (ListingSourceCodeTextResult match in Matches)
        {
            match.AppendTo(sb, "###");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
