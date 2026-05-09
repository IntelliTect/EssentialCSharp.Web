using System.Globalization;
using System.Text;

namespace EssentialCSharp.Web.Models;

public sealed record TextGuidelineResult(
    string Type,
    string Guideline,
    int ChapterNumber,
    string ChapterTitle,
    string Subsection)
{
    internal void AppendTo(StringBuilder sb)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"**[{Type}]** {Guideline}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  — Chapter {ChapterNumber}: {ChapterTitle} / {Subsection}");
        sb.AppendLine();
    }
}

public sealed record GuidelinesTextResult(
    string? Topic,
    IReadOnlyList<TextGuidelineResult> Guidelines)
{
    public string ToMcpString()
    {
        StringBuilder sb = new();
        string title = Topic is null
            ? $"# Essential C# Guidelines ({Guidelines.Count} result{(Guidelines.Count == 1 ? "" : "s")})"
            : $"# Essential C# Guidelines — Topic: {Topic} ({Guidelines.Count} result{(Guidelines.Count == 1 ? "" : "s")})";

        sb.AppendLine(title);
        sb.AppendLine();

        foreach (TextGuidelineResult guideline in Guidelines)
        {
            guideline.AppendTo(sb);
        }

        return sb.ToString();
    }
}
