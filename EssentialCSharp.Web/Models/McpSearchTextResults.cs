using System.Globalization;
using System.Text;

namespace EssentialCSharp.Web.Models;

public sealed record SearchBookContentMatchTextResult(
    double Score,
    int? ChapterNumber,
    string? Heading,
    string ChunkText)
{
    internal void AppendTo(StringBuilder sb, int index)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"--- Result {index} (Score: {Score:F4}) ---");

        if (ChapterNumber.HasValue)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Chapter: {ChapterNumber}");
        }

        if (!string.IsNullOrEmpty(Heading))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Section: {Heading}");
        }

        sb.AppendLine();
        sb.AppendLine(ChunkText);
        sb.AppendLine();
    }
}

public sealed record SearchBookContentTextResult(
    IReadOnlyList<SearchBookContentMatchTextResult> Matches)
{
    public string ToMcpString()
    {
        StringBuilder sb = new();

        for (int i = 0; i < Matches.Count; i++)
        {
            Matches[i].AppendTo(sb, i + 1);
        }

        return sb.ToString();
    }
}

public sealed record BookSectionLinkTextResult(
    string Heading,
    int ChapterNumber,
    string Link)
{
    internal void AppendBulletTo(StringBuilder sb) =>
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **{Heading}** (Ch. {ChapterNumber}) — `{Link}`");

    internal void AppendIndentedBulletTo(StringBuilder sb) =>
        sb.AppendLine(CultureInfo.InvariantCulture, $"  - {Heading} (Ch. {ChapterNumber}) — `{Link}`");
}

public sealed record SemanticBookContentMatchTextResult(
    int ChapterNumber,
    string Heading,
    string Excerpt)
{
    internal void AppendTo(StringBuilder sb)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **{Heading}** (Ch. {ChapterNumber})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  > {Excerpt}...");
    }
}

public sealed record LookupConceptTextResult(
    string Concept,
    IReadOnlyList<BookSectionLinkTextResult> HeadingMatches,
    IReadOnlyList<SemanticBookContentMatchTextResult> SemanticMatches)
{
    public string ToMcpString()
    {
        StringBuilder sb = new();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Book Coverage: '{Concept}'");
        sb.AppendLine();

        if (HeadingMatches.Count > 0)
        {
            sb.AppendLine("## Sections with matching headings");
            foreach (BookSectionLinkTextResult headingMatch in HeadingMatches)
            {
                headingMatch.AppendBulletTo(sb);
            }

            sb.AppendLine();
        }

        if (SemanticMatches.Count > 0)
        {
            sb.AppendLine("## Related content (semantic search)");
            foreach (SemanticBookContentMatchTextResult semanticMatch in SemanticMatches)
            {
                semanticMatch.AppendTo(sb);
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}

public sealed record TopicCoverageTextResult(
    string Topic,
    string Assessment,
    IReadOnlyList<BookSectionLinkTextResult> RelevantSections)
{
    public string ToMcpString()
    {
        StringBuilder sb = new();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Topic Coverage: '{Topic}'");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Assessment:** {Assessment}");

        if (RelevantSections.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Relevant sections:**");
            foreach (BookSectionLinkTextResult section in RelevantSections)
            {
                section.AppendIndentedBulletTo(sb);
            }
        }

        return sb.ToString();
    }
}

public sealed record DiagnosticBookContentMatchTextResult(
    int? ChapterNumber,
    string? Heading,
    string ChunkText)
{
    internal void AppendTo(StringBuilder sb)
    {
        if (!string.IsNullOrEmpty(Heading))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"**{Heading}** (Ch. {ChapterNumber})");
        }

        sb.AppendLine(ChunkText);
        sb.AppendLine();
    }
}

public sealed record DiagnosticHelpTextResult(
    string Diagnostic,
    string? SearchTerm,
    IReadOnlyList<BookSectionLinkTextResult> RelevantSections,
    IReadOnlyList<DiagnosticBookContentMatchTextResult> RelevantBookContent,
    IReadOnlyList<TextGuidelineResult> RelatedGuidelines)
{
    public string ToMcpString()
    {
        StringBuilder sb = new();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Book Help for: {Diagnostic}");

        if (!string.IsNullOrEmpty(SearchTerm))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Searching for: '{SearchTerm}'");
        }

        sb.AppendLine();

        if (RelevantSections.Count > 0)
        {
            sb.AppendLine("## Relevant Book Sections");
            foreach (BookSectionLinkTextResult section in RelevantSections)
            {
                section.AppendBulletTo(sb);
            }

            sb.AppendLine();
        }

        if (RelevantBookContent.Count > 0)
        {
            sb.AppendLine("## Relevant Book Content");
            foreach (DiagnosticBookContentMatchTextResult match in RelevantBookContent)
            {
                match.AppendTo(sb);
            }
        }

        if (RelatedGuidelines.Count > 0)
        {
            sb.AppendLine("## Related Guidelines");
            foreach (TextGuidelineResult guideline in RelatedGuidelines)
            {
                guideline.AppendTo(sb);
            }
        }

        return sb.ToString();
    }
}

public sealed record RelatedSectionMatchTextResult(
    string Heading,
    string Location,
    string Excerpt)
{
    internal void AppendTo(StringBuilder sb)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **{Heading}** ({Location})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  > {Excerpt}...");
        sb.AppendLine();
    }
}

public sealed record RelatedSectionsTextResult(
    string Heading,
    int ChapterNumber,
    string ChapterTitle,
    IReadOnlyList<RelatedSectionMatchTextResult> RelatedSections)
{
    public string ToMcpString()
    {
        StringBuilder sb = new();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Sections Related to: {Heading}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"(Chapter {ChapterNumber}: {ChapterTitle})");
        sb.AppendLine();

        if (RelatedSections.Count == 0)
        {
            sb.AppendLine("No related sections found.");
            return sb.ToString();
        }

        foreach (RelatedSectionMatchTextResult relatedSection in RelatedSections)
        {
            relatedSection.AppendTo(sb);
        }

        return sb.ToString();
    }
}
