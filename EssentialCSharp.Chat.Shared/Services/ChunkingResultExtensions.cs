using System.Security.Cryptography;
using System.Text;
using EssentialCSharp.Chat.Common.Models;

namespace EssentialCSharp.Chat.Common.Services;

public static partial class ChunkingResultExtensions
{
    public static List<BookContentChunk> ToBookContentChunks(this FileChunkingResult result)
    {
        var chunks = new List<BookContentChunk>();
        int? chapterNumber = ExtractChapterNumber(result.FileName);

        foreach (var chunk in result.Chunks)
        {
            string chunkText = chunk;
            string contentHash = ComputeSha256Hash(chunkText);

            chunks.Add(new BookContentChunk
            {
                Id = Guid.NewGuid().ToString(),
                FileName = result.FileName,
                Heading = ExtractHeading(chunkText),
                ChunkText = chunkText,
                ChapterNumber = chapterNumber,
                ContentHash = contentHash
            });
        }
        return chunks;
    }

    private static string ExtractHeading(string chunkText)
    {
        // get characters until the first " - " or newline
        var firstLine = chunkText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None)[0];
        var headingParts = firstLine.Split([" - "], StringSplitOptions.None);
        return headingParts.Length > 0 ? headingParts[0].Trim() : string.Empty;
    }

    private static int ExtractChapterNumber(string fileName)
    {
        // Example: "Chapter01.md" -> 1
        // Regex: Chapter(?<ChapterNumber>[0-9]{2})
        var match = ChapterNumberRegex().Match(fileName);
        if (match.Success && int.TryParse(match.Groups["ChapterNumber"].Value, out int chapterNumber))

        {
            return chapterNumber;
        }
        throw new InvalidOperationException($"File name '{fileName}' does not contain a valid chapter number in the expected format.");
    }

    private static string ComputeSha256Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(bytes);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"Chapter(?<ChapterNumber>\d{2})")]
    private static partial System.Text.RegularExpressions.Regex ChapterNumberRegex();
}
