using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Text;

namespace EssentialCSharp.Chat.Common.Services;

/// <summary>
/// Markdown chunking service using Semantic Kernel's TextChunker
/// </summary>
public partial class MarkdownChunkingService(
    ILogger<MarkdownChunkingService> logger,
    int maxTokensPerChunk = 256,
    int overlapTokens = 25)
{
    private static readonly string[] _NewLineSeparators = ["\r\n", "\n", "\r"];
    private readonly int _MaxTokensPerChunk = maxTokensPerChunk;
    private readonly int _OverlapTokens = overlapTokens;

    /// <summary>
    /// Process markdown files in the specified directory using Semantic Kernel's TextChunker
    /// </summary>
    public async Task<List<FileChunkingResult>> ProcessMarkdownFilesAsync(
        DirectoryInfo directory,
        string filePattern)
    {
        // Validate input parameters
        if (!directory.Exists)
        {
            LogDirectoryDoesNotExist(logger, directory.FullName);
            throw new InvalidOperationException($"Error: Directory '{directory.FullName}' does not exist.");
        }

        // Find markdown files
        var markdownFiles = directory.GetFiles(filePattern, SearchOption.TopDirectoryOnly);

        if (markdownFiles.Length == 0)
        {
            throw new InvalidOperationException($"No files matching pattern '{filePattern}' found in '{directory.FullName}'");
        }

        Console.WriteLine($"Processing {markdownFiles.Length} markdown files...");

        int totalChunks = 0;
        var results = new List<FileChunkingResult>();

        foreach (var file in markdownFiles)
        {
            string[] fileContent = await File.ReadAllLinesAsync(file.FullName);
            var result = ProcessSingleMarkdownFile(fileContent, file.Name, file.FullName);
            results.Add(result);
            totalChunks += result.ChunkCount;
        }
        Console.WriteLine($"Processed {markdownFiles.Length} markdown files with a total of {totalChunks} chunks.");

        return results;
    }

    /// <summary>
    /// Process a single markdown file using Semantic Kernel's SplitMarkdownParagraphs method
    /// </summary>
    public FileChunkingResult ProcessSingleMarkdownFile(
        string[] fileContent, string fileName, string filePath)
    {
        // Collapse consecutive blank lines to at most one blank line. Single blank lines must
        // be preserved because TextChunker.SplitMarkdownParagraphs uses them as paragraph
        // separators — stripping all blanks defeats paragraph-aware chunking.
        var normalizedLines = new List<string>(fileContent.Length);
        bool lastWasBlank = false;
        foreach (var raw in fileContent)
        {
            var line = raw.Trim();
            var isBlank = string.IsNullOrWhiteSpace(line);
            if (!isBlank || !lastWasBlank)
                normalizedLines.Add(line);
            lastWasBlank = isBlank;
        }
        string[] lines = [.. normalizedLines];
        string content = string.Join(Environment.NewLine, lines);

        var sections = MarkdownContentToHeadersAndSection(content);
        var allChunks = new List<MarkdownChunk>();
        int totalChunkCharacters = 0;
        int chunkCount = 0;

        foreach (var (Header, Content) in sections)
        {
#pragma warning disable SKEXP0050
            var chunks = TextChunker.SplitMarkdownParagraphs(
                lines: Content,
                maxTokensPerParagraph: _MaxTokensPerChunk,
                overlapTokens: _OverlapTokens,
                chunkHeader: Header + " - "
                );
#pragma warning restore SKEXP0050
            allChunks.AddRange(chunks.Select(c => new MarkdownChunk(Header, c)));
            chunkCount += chunks.Count;
            totalChunkCharacters += chunks.Sum(c => c.Length);
        }

        return new FileChunkingResult
        {
            FileName = fileName,
            FilePath = filePath,
            OriginalCharCount = content.Length,
            ChunkCount = chunkCount,
            Chunks = allChunks,
            TotalChunkCharacters = totalChunkCharacters
        };
    }

    /// <summary>
    /// Convert markdown content into a list of headers and their associated content sections.
    /// </summary>
    /// <param name="content"></param>
    /// <returns></returns>
    public static List<(string Header, List<string> Content)> MarkdownContentToHeadersAndSection(string content)
    {
        var lines = content.Split(_NewLineSeparators, StringSplitOptions.None);
        var sections = new List<(string Header, List<string> Content)>();
        var headerRegex = HeadingRegex();
        var listingPattern = ListingRegex();
        var headerStack = new List<(int Level, string Text)>();
        int i = 0;
        while (i < lines.Length)
        {
            // Find next header
            while (i < lines.Length && !headerRegex.IsMatch(lines[i]))
                i++;
            if (i >= lines.Length) break;

            var match = headerRegex.Match(lines[i]);
            int level = match.Groups[1].Value.Length;
            string headerText = match.Groups[2].Value.Trim();
            bool isListing = headerText.StartsWith("Listing", StringComparison.OrdinalIgnoreCase) && listingPattern.IsMatch(headerText);

            // If this is a listing header, append its content to the previous section
            if (isListing && sections.Count > 0)
            {
                i++; // skip the listing header
                var listingContent = new List<string>();
                while (i < lines.Length && !headerRegex.IsMatch(lines[i]))
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                        listingContent.Add(lines[i]);
                    i++;
                }
                // Append to previous section's content
                var prev = sections[^1];
                prev.Content.AddRange(listingContent);
                sections[^1] = prev;
                continue;
            }

            // Update header stack for non-listing headers
            if (headerStack.Count == 0 || level > headerStack.Last().Level)
            {
                headerStack.Add((level, headerText));
            }
            else
            {
                while (headerStack.Count > 0 && headerStack.Last().Level >= level)
                    headerStack.RemoveAt(headerStack.Count - 1);
                headerStack.Add((level, headerText));
            }
            i++;

            // Collect content until next header, preserving blank lines as paragraph separators
            // for TextChunker.SplitMarkdownParagraphs.
            var contentLines = new List<string>();
            while (i < lines.Length && !headerRegex.IsMatch(lines[i]))
            {
                contentLines.Add(lines[i]);
                i++;
            }

            // Strip leading and trailing blank lines; keep internal blanks for paragraph detection.
            while (contentLines.Count > 0 && string.IsNullOrWhiteSpace(contentLines[0]))
                contentLines.RemoveAt(0);
            while (contentLines.Count > 0 && string.IsNullOrWhiteSpace(contentLines[^1]))
                contentLines.RemoveAt(contentLines.Count - 1);

            // Compose full header context
            var fullHeader = string.Join(": ", headerStack.Select(h => h.Text));
            if (contentLines.Any(l => !string.IsNullOrWhiteSpace(l)))
                sections.Add((fullHeader, contentLines));
        }
        return sections;
    }

    [GeneratedRegex(@"^Listing \d+\.\d+(:.*)?$")]
    private static partial Regex ListingRegex();

    [GeneratedRegex(@"^(#{1,6}) +(.+)$")]
    private static partial Regex HeadingRegex();

    [LoggerMessage(Level = LogLevel.Error, Message = "Directory {DirectoryName} does not exist.")]
    private static partial void LogDirectoryDoesNotExist(ILogger<MarkdownChunkingService> logger, string directoryName);
}
