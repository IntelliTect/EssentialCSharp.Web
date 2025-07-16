namespace EssentialCSharp.Chat;

/// <summary>
/// Data structure to hold chunking results for a single file
/// </summary>
public class FileChunkingResult
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int OriginalCharCount { get; set; }
    public int ChunkCount { get; set; }
    public List<string> Chunks { get; set; } = new();
    public int TotalChunkCharacters { get; set; }
}
