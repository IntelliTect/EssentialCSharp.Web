using System.Security.Cryptography;
using System.Text;
using EssentialCSharp.Chat.Common.Models;

namespace EssentialCSharp.Chat.Common.Services;

public static partial class ChunkingResultExtensions
{
    /// <summary>
    /// Converts a <see cref="FileChunkingResult"/> into a list of <see cref="BookContentChunk"/> records
    /// ready for embedding and vector store upload.
    /// </summary>
    /// <remarks>
    /// <see cref="BookContentChunk.ChapterNumber"/> is set to null for files that do not match
    /// the <c>ChapterNN</c> naming pattern (e.g. appendix or non-chapter markdown files).
    /// </remarks>
    public static List<BookContentChunk> ToBookContentChunks(this FileChunkingResult result)
    {
        int? chapterNumber = ExtractChapterNumber(result.FileName);

        var chunks = result.Chunks
            .Select((markdownChunk, index) =>
            {
                var contentHash = ComputeSha256Hash(markdownChunk.ChunkText);
                return new BookContentChunk
                {
                    Id = $"{result.FileName}_{index}",
                    FileName = result.FileName,
                    Heading = markdownChunk.Heading,
                    ChunkText = markdownChunk.ChunkText,
                    ChapterNumber = chapterNumber,
                    ChunkIndex = index,
                    ContentHash = contentHash
                };
            })
            .ToList();

        return chunks;
    }

    private static int? ExtractChapterNumber(string fileName)
    {
        // Example: "Chapter01.md" -> 1; non-chapter files return null.
        var match = ChapterNumberRegex().Match(fileName);
        if (match.Success && int.TryParse(match.Groups["ChapterNumber"].Value, out int chapterNumber))
            return chapterNumber;
        return null;
    }

    private static string ComputeSha256Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(bytes);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"Chapter(?<ChapterNumber>\d{2})")]
    private static partial System.Text.RegularExpressions.Regex ChapterNumberRegex();
}
