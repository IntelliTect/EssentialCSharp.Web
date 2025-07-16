using System.CommandLine;
using Microsoft.SemanticKernel.Text;

namespace EssentialCSharp.Chat;

public class Program
{
    private static readonly char[] _LineSeparators = ['\r', '\n'];

    static int Main(string[] args)
    {
        // Configure command-line options following System.CommandLine patterns
        var directoryOption = new Option<DirectoryInfo?>("--directory")
        {
            Description = "Directory containing markdown files to chunk",
            DefaultValueFactory = _ => new DirectoryInfo(@"D:\EssentialCSharp.Web\EssentialCSharp.Web\Markdown\")
        };

        var maxTokensOption = new Option<int>("--max-tokens")
        {
            Description = "Maximum tokens per chunk",
            DefaultValueFactory = _ => 500
        };

        var overlapTokensOption = new Option<int>("--overlap")
        {
            Description = "Number of tokens to overlap between chunks",
            DefaultValueFactory = _ => 50
        };

        var chunkHeaderOption = new Option<string?>("--header")
        {
            Description = "Optional header to prepend to each chunk"
        };

        var filePatternOption = new Option<string>("--pattern")
        {
            Description = "File pattern to match",
            DefaultValueFactory = _ => "*.md"
        };

        var outputFormatOption = new Option<string>("--format")
        {
            Description = "Output format: summary, detailed, or json",
            DefaultValueFactory = _ => "summary"
        };

        // Create root command
        var rootCommand = new RootCommand("Semantic Kernel TextChunker - Extract and Chunk Markdown Files")
        {
            directoryOption,
            maxTokensOption,
            overlapTokensOption,
            chunkHeaderOption,
            filePatternOption,
            outputFormatOption
        };

        // Set the action for the root command
        rootCommand.SetAction(parseResult =>
        {
            var directory = parseResult.GetValue(directoryOption);
            var maxTokens = parseResult.GetValue(maxTokensOption);
            var overlapTokens = parseResult.GetValue(overlapTokensOption);
            var chunkHeader = parseResult.GetValue(chunkHeaderOption);
            var filePattern = parseResult.GetValue(filePatternOption);
            var outputFormat = parseResult.GetValue(outputFormatOption);

            return ProcessMarkdownFiles(directory!, maxTokens, overlapTokens, chunkHeader, filePattern!, outputFormat!);
        });

        return rootCommand.Parse(args).Invoke();
    }

    /// <summary>
    /// Process markdown files in the specified directory using Semantic Kernel's TextChunker
    /// Following Microsoft Learn documentation for proper implementation
    /// </summary>
    internal static int ProcessMarkdownFiles(
        DirectoryInfo directory, 
        int maxTokensPerParagraph, 
        int overlapTokens, 
        string? chunkHeader, 
        string filePattern, 
        string outputFormat)
    {
        try
        {
            // Validate input parameters
            if (!directory.Exists)
            {
                Console.Error.WriteLine($"Error: Directory '{directory.FullName}' does not exist.");
                return 1;
            }

            if (maxTokensPerParagraph <= 0)
            {
                Console.Error.WriteLine("Error: max-tokens must be a positive number.");
                return 1;
            }

            if (overlapTokens < 0 || overlapTokens >= maxTokensPerParagraph)
            {
                Console.Error.WriteLine("Error: overlap-tokens must be between 0 and max-tokens.");
                return 1;
            }

            // Find markdown files
            var markdownFiles = directory.GetFiles(filePattern, SearchOption.TopDirectoryOnly);
            
            if (markdownFiles.Length == 0)
            {
                Console.WriteLine($"No files matching pattern '{filePattern}' found in '{directory.FullName}'");
                return 0;
            }

            Console.WriteLine($"Processing {markdownFiles.Length} markdown files...");
            Console.WriteLine($"Max tokens per chunk: {maxTokensPerParagraph}");
            Console.WriteLine($"Overlap tokens: {overlapTokens} ({(double)overlapTokens / maxTokensPerParagraph * 100:F1}%)");
            Console.WriteLine($"Chunk header: {(string.IsNullOrEmpty(chunkHeader) ? "None" : $"'{chunkHeader}'")}");
            Console.WriteLine();

            int totalChunks = 0;
            var results = new List<FileChunkingResult>();

            foreach (var file in markdownFiles)
            {
                var result = ProcessSingleMarkdownFile(file, maxTokensPerParagraph, overlapTokens, chunkHeader);
                results.Add(result);
                totalChunks += result.ChunkCount;

                // Output per-file summary
                Console.WriteLine($"File: {file.Name}");
                Console.WriteLine($"  Original size: {result.OriginalCharCount:N0} characters");
                Console.WriteLine($"  Chunks created: {result.ChunkCount}");
                Console.WriteLine($"  Average chunk size: {(result.ChunkCount > 0 ? result.TotalChunkCharacters / result.ChunkCount : 0):N0} characters");
                Console.WriteLine();

                // Output detailed chunks if requested
                if (outputFormat.Equals("detailed", StringComparison.OrdinalIgnoreCase))
                {
                    OutputDetailedChunks(result);
                }
            }

            // Output summary
            Console.WriteLine("=== SUMMARY ===");
            Console.WriteLine($"Total files processed: {markdownFiles.Length}");
            Console.WriteLine($"Total chunks created: {totalChunks}");
            Console.WriteLine($"Average chunks per file: {(markdownFiles.Length > 0 ? (double)totalChunks / markdownFiles.Length : 0):F1}");

            // Output JSON if requested
            if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                OutputJsonResults(results);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Process a single markdown file using Semantic Kernel's SplitMarkdownParagraphs method
    /// Implementation follows Microsoft Learn documentation exactly
    /// </summary>
    internal static FileChunkingResult ProcessSingleMarkdownFile(
        FileInfo file, 
        int maxTokensPerParagraph, 
        int overlapTokens, 
        string? chunkHeader)
    {
        // Read the markdown content
        var content = File.ReadAllText(file.FullName);
        
        // Prepare lines for chunking - following Microsoft examples
        var lines = content.Split(_LineSeparators, StringSplitOptions.RemoveEmptyEntries).ToList();
        
        // Apply Semantic Kernel TextChunker.SplitMarkdownParagraphs 
        // Following the exact API signature from Microsoft Learn documentation
        // Suppress the experimental warning as this is the intended usage per documentation
#pragma warning disable SKEXP0050
        var chunks = TextChunker.SplitMarkdownParagraphs(
            lines, 
            maxTokensPerParagraph, 
            overlapTokens, 
            chunkHeader);
#pragma warning restore SKEXP0050

        // Calculate statistics
        var result = new FileChunkingResult
        {
            FileName = file.Name,
            FilePath = file.FullName,
            OriginalCharCount = content.Length,
            ChunkCount = chunks.Count,
            Chunks = chunks,
            TotalChunkCharacters = chunks.Sum(c => c.Length)
        };

        return result;
    }

    /// <summary>
    /// Output detailed chunk information for inspection
    /// </summary>
    internal static void OutputDetailedChunks(FileChunkingResult result)
    {
        Console.WriteLine($"=== DETAILED CHUNKS for {result.FileName} ===");
        
        for (int i = 0; i < result.Chunks.Count; i++)
        {
            var chunk = result.Chunks[i];
            Console.WriteLine($"Chunk {i + 1}/{result.Chunks.Count}:");
            Console.WriteLine($"  Length: {chunk.Length} characters");
            Console.WriteLine($"  Preview: {chunk.Substring(0, Math.Min(100, chunk.Length)).Replace('\n', ' ').Replace('\r', ' ')}...");
            Console.WriteLine("  ---");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Output results in JSON format for programmatic consumption
    /// </summary>
    internal static void OutputJsonResults(List<FileChunkingResult> results)
    {
        Console.WriteLine();
        Console.WriteLine("=== JSON OUTPUT ===");
        Console.WriteLine("{");
        Console.WriteLine("  \"results\": [");
        
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            Console.WriteLine("    {");
            Console.WriteLine($"      \"fileName\": \"{result.FileName}\",");
            Console.WriteLine($"      \"filePath\": \"{result.FilePath.Replace("\\", "\\\\")}\",");
            Console.WriteLine($"      \"originalCharCount\": {result.OriginalCharCount},");
            Console.WriteLine($"      \"chunkCount\": {result.ChunkCount},");
            Console.WriteLine($"      \"totalChunkCharacters\": {result.TotalChunkCharacters},");
            Console.WriteLine($"      \"averageChunkSize\": {(result.ChunkCount > 0 ? result.TotalChunkCharacters / result.ChunkCount : 0)}");
            Console.WriteLine(i < results.Count - 1 ? "    }," : "    }");
        }
        
        Console.WriteLine("  ]");
        Console.WriteLine("}");
    }
}
