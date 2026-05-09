using System.Globalization;
using System.Text;
using HtmlAgilityPack;

namespace EssentialCSharp.Web.Models;

public sealed record SectionContentTextResult(
    string Heading,
    int ChapterNumber,
    string ChapterTitle,
    string Body)
{
    public string ToMcpString()
    {
        StringBuilder sb = new();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## {Heading}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Chapter {ChapterNumber}: {ChapterTitle}");
        sb.AppendLine();
        sb.Append(Body);
        return sb.ToString();
    }
}

public sealed record SectionContentExtractionResult(
    SectionContentTextResult? Content,
    string? ErrorMessage)
{
    public static SectionContentExtractionResult FromHtml(SiteMapping mapping, string htmlContent, int maxChars)
    {
        HtmlDocument doc = new();
        doc.LoadHtml(htmlContent);

        HtmlNode? sectionNode = doc.DocumentNode.SelectSingleNode(
            $"//div[@id='{mapping.AnchorId}' and contains(@class,'section-heading')]");

        if (sectionNode is null)
        {
            return new SectionContentExtractionResult(null, $"Section heading element not found for anchor '{mapping.AnchorId}'.");
        }

        HtmlNode? parent = sectionNode.ParentNode;
        if (parent is null)
        {
            return new SectionContentExtractionResult(null, $"Section heading element not found for anchor '{mapping.AnchorId}'.");
        }

        StringBuilder body = new();
        bool collecting = false;
        foreach (HtmlNode child in parent.ChildNodes)
        {
            if (!collecting)
            {
                if (child == sectionNode)
                {
                    collecting = true;
                }

                continue;
            }

            if (child.Name == "div" &&
                child.HasAttributes &&
                !string.IsNullOrEmpty(child.GetAttributeValue("id", "")) &&
                child.GetAttributeValue("class", "").Contains("section-heading"))
            {
                break;
            }

            ExtractNodeContent(child, body);

            if (body.Length >= maxChars)
            {
                body.Append("\n\n[Content truncated — use a larger maxChars value to see more.]");
                break;
            }
        }

        if (body.Length == 0)
        {
            return new SectionContentExtractionResult(null, $"No content found after section heading '{mapping.RawHeading}'.");
        }

        return new SectionContentExtractionResult(
            new SectionContentTextResult(mapping.RawHeading, mapping.ChapterNumber, mapping.ChapterTitle, body.ToString()),
            null);
    }

    private static void ExtractNodeContent(HtmlNode node, StringBuilder sb)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            string text = HtmlEntity.DeEntitize(node.InnerText).Trim();
            if (!string.IsNullOrEmpty(text))
            {
                sb.AppendLine(text);
            }

            return;
        }

        string nodeClass = node.GetAttributeValue("class", "");

        if (node.Name == "table")
        {
            AppendTable(node, sb);
            return;
        }

        if (node.Name is not ("div" or "p" or "ul" or "ol" or "li" or "span"))
        {
            return;
        }

        if (nodeClass.Contains("table-heading"))
        {
            string text = CollapseWhitespace(node.InnerText);
            if (!string.IsNullOrEmpty(text))
            {
                sb.AppendLine(text);
                sb.AppendLine();
            }

            return;
        }

        if (nodeClass.Contains("code-block-section"))
        {
            HtmlNode? headingNode = node.SelectSingleNode(".//div[contains(@class,'code-block-heading')]");
            if (headingNode is not null)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"\n**{HtmlEntity.DeEntitize(headingNode.InnerText).Trim()}**");
            }

            sb.AppendLine("```csharp");
            HtmlNodeCollection? codeLines = node.SelectNodes(".//div[contains(@class,'code-line')]");
            if (codeLines is not null)
            {
                foreach (HtmlNode lineClone in codeLines.Select(line => line.CloneNode(deep: true)))
                {
                    HtmlNode? lineNumberSpan = lineClone.SelectSingleNode(".//span[contains(@class,'code-line-number')]");
                    lineNumberSpan?.Remove();
                    sb.AppendLine(HtmlEntity.DeEntitize(lineClone.InnerText));
                }
            }

            sb.AppendLine("```");
            return;
        }

        if (node.Name is "p" || nodeClass.Contains("paragraph"))
        {
            string text = HtmlEntity.DeEntitize(node.InnerText).Trim();
            if (!string.IsNullOrEmpty(text))
            {
                sb.AppendLine(text);
                sb.AppendLine();
            }

            return;
        }

        foreach (HtmlNode child in node.ChildNodes)
        {
            ExtractNodeContent(child, sb);
        }
    }

    private static void AppendTable(HtmlNode tableNode, StringBuilder sb)
    {
        HtmlNodeCollection? rows = tableNode.SelectNodes(".//tr");
        if (rows is null)
        {
            return;
        }

        bool wroteAnyRow = false;
        bool wroteHeaderSeparator = false;
        foreach (HtmlNode row in rows)
        {
            HtmlNodeCollection? cells = row.SelectNodes("./th|./td");
            if (cells is null || cells.Count == 0)
            {
                continue;
            }

            List<string> values = cells
                .Select(cell => CollapseWhitespace(cell.InnerText).Replace("|", "\\|", StringComparison.Ordinal))
                .ToList();

            if (values.All(string.IsNullOrEmpty))
            {
                continue;
            }

            sb.Append("| ");
            sb.Append(string.Join(" | ", values));
            sb.AppendLine(" |");
            wroteAnyRow = true;

            bool isHeaderRow = row.GetAttributeValue("class", "").Contains("header-row", StringComparison.Ordinal)
                || row.SelectNodes("./th") is { Count: > 0 };

            if (!wroteHeaderSeparator && isHeaderRow)
            {
                sb.Append("| ");
                sb.Append(string.Join(" | ", Enumerable.Repeat("---", values.Count)));
                sb.AppendLine(" |");
                wroteHeaderSeparator = true;
            }
        }

        if (wroteAnyRow)
        {
            sb.AppendLine();
        }
    }

    private static string CollapseWhitespace(string text) =>
        string.Join(" ", HtmlEntity.DeEntitize(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
