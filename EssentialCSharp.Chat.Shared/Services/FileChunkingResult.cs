namespace EssentialCSharp.Chat.Common.Services;

/// <summary>
/// A single chunk from a markdown file, paired with the section heading it belongs to.
/// </summary>
/// <param name="Heading">Full breadcrumb heading for the section (e.g. "Chapter: 1: Intro: Summary").</param>
/// <param name="ChunkText">The raw chunk text, including the "Heading - " prefix prepended by TextChunker.</param>
public record MarkdownChunk(string Heading, string ChunkText);

/// <summary>
/// Data structure to hold chunking results for a single file
/// </summary>
public class FileChunkingResult
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int OriginalCharCount { get; set; }
    public int ChunkCount { get; set; }
    public List<MarkdownChunk> Chunks { get; set; } = [];
    public int TotalChunkCharacters { get; set; }
}
