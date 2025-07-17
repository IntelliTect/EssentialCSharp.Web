using System.CommandLine;
using System.Text.Json;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace EssentialCSharp.Chat;

public class Program
{
    private static readonly JsonSerializerOptions _JsonOptions = new() { WriteIndented = true };

    static int Main(string[] args)
    {


#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        Option<DirectoryInfo> directoryOption = new("--directory")
        {
            Description = "Directory containing markdown files.",
            Required = true
        };
        Option<string> filePatternOption = new("--file-pattern")
        {
            Description = "File pattern to match (e.g. *.md)",
            Required = false,
            DefaultValueFactory = _ => "*.md"
        };
        Option<DirectoryInfo?> outputDirectoryOption = new("--output-directory")
        {
            Description = "Directory to write chunked output files. If not provided, output is written to console.",
            Required = false
        };

        RootCommand rootCommand = new("EssentialCSharp.Chat Utilities");

        var chunkMarkdownCommand = new Command("chunk-markdown", "Chunk markdown files in a directory.")
        {
            directoryOption,
            filePatternOption,
            outputDirectoryOption
        };

        var buildVectorDbCommand = new Command("build-vector-db", "Build a vector database from markdown chunks.")
        {
            directoryOption,
            filePatternOption,
        };

        buildVectorDbCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            IConfigurationRoot config = new ConfigurationBuilder()
                .SetBasePath(IntelliTect.Multitool.RepositoryPaths.GetDefaultRepoRoot())
                .AddJsonFile("EssentialCSharp.Web/appsettings.json")
                .AddUserSecrets<Program>()
                .AddEnvironmentVariables()
                .Build();

            var builder = Kernel.CreateBuilder();

            AIOptions aiOptions = config.GetRequiredSection("AIOptions").Get<AIOptions>() ?? throw new InvalidOperationException(
            "AIOptions section is missing or not configured correctly in appsettings.json or environment variables.");
            builder.Services.Configure<AIOptions>(config.GetRequiredSection("AIOptions"));

            // Register Azure OpenAI text embedding generation service
#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            builder.AddAzureOpenAITextEmbeddingGeneration(aiOptions.VectorGenerationDeploymentName, aiOptions.Endpoint, aiOptions.ApiKey);
#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

            builder.Services.AddLogging(loggingBuilder =>
                {
                    loggingBuilder.AddSimpleConsole(options =>
                    {
                        options.TimestampFormat = "HH:mm:ss ";
                        options.SingleLine = true;
                    });
                });

            builder.Services.AddPostgresVectorStore(
                aiOptions.PostgresConnectionString);

            builder.Services.AddSingleton<EmbeddingService>();
            builder.Services.AddSingleton<MarkdownChunkingService>();

            // Build the kernel and get the data uploader.
            var kernel = builder.Build();
            var directory = parseResult.GetValue(directoryOption);
            var filePattern = parseResult.GetValue(filePatternOption) ?? "*.md";
            var markdownService = kernel.GetRequiredService<MarkdownChunkingService>();
            try
            {
                if (directory is null)
                {
                    Console.Error.WriteLine("Error: Directory is required.");
                    return;
                }
                var results = await markdownService.ProcessMarkdownFilesAsync(directory, filePattern);
                // Convert results to BookContentChunks
                var bookContentChunks = results.SelectMany(result => result.ToBookContentChunks()).ToList();
                // Generate embeddings and upload to vector store
                var embeddingService = kernel.GetRequiredService<EmbeddingService>();
                await embeddingService.GenerateBookContentEmbeddingsAndUploadToVectorStore(bookContentChunks, cancellationToken, "markdown_chunks");
                Console.WriteLine($"Successfully processed {bookContentChunks.Count} chunks.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                throw;
            }
        });
        chunkMarkdownCommand.SetAction(async parseResult =>
        {
            var directory = parseResult.GetValue(directoryOption);
            var filePattern = parseResult.GetValue(filePatternOption) ?? "*.md";
            var outputDirectory = parseResult.GetValue(outputDirectoryOption);

            using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole());
            var logger = loggerFactory.CreateLogger<MarkdownChunkingService>();
            var service = new MarkdownChunkingService(logger);
            try
            {
                if (directory is null)
                {
                    Console.Error.WriteLine("Error: Directory is required.");
                    return;
                }
                var results = await service.ProcessMarkdownFilesAsync(directory, filePattern);

                int maxChunkLength = 0;
                int minChunkLength = 0;

                void WriteChunkingResult(FileChunkingResult result, TextWriter writer)
                {
                    // lets build up some stats over the chunking
                    var chunkAverage = result.Chunks.Average(chunk => chunk.Length);
                    var chunkMedian = result.Chunks.OrderBy(chunk => chunk.Length).ElementAt(result.Chunks.Count / 2).Length;
                    var chunkMax = result.Chunks.Max(chunk => chunk.Length);
                    var chunkMin = result.Chunks.Min(chunk => chunk.Length);
                    var chunkTotal = result.Chunks.Sum(chunk => chunk.Length);
                    var chunkStandardDeviation = Math.Sqrt(result.Chunks.Average(chunk => Math.Pow(chunk.Length - chunkAverage, 2)));
                    var numberOfOutliers = result.Chunks.Count(chunk => chunk.Length > chunkAverage + chunkStandardDeviation);

                    if (chunkMax > maxChunkLength) maxChunkLength = chunkMax;
                    if (chunkMin < minChunkLength || minChunkLength == 0) minChunkLength = chunkMin;

                    writer.WriteLine($"File: {result.FileName}");
                    writer.WriteLine($"Number of Chunks: {result.ChunkCount}");
                    writer.WriteLine($"Average Chunk Length: {chunkAverage}");
                    writer.WriteLine($"Median Chunk Length: {chunkMedian}");
                    writer.WriteLine($"Max Chunk Length: {chunkMax}");
                    writer.WriteLine($"Min Chunk Length: {chunkMin}");
                    writer.WriteLine($"Total Chunk Characters: {chunkTotal}");
                    writer.WriteLine($"Standard Deviation: {chunkStandardDeviation}");
                    writer.WriteLine($"Number of Outliers: {numberOfOutliers}");
                    writer.WriteLine($"Original Character Count: {result.OriginalCharCount}");
                    writer.WriteLine($"New Character Count: {result.TotalChunkCharacters}");
                    foreach (var chunk in result.Chunks)
                    {
                        writer.WriteLine();
                        writer.WriteLine(chunk);
                    }
                }

                if (outputDirectory != null)
                {
                    if (!outputDirectory.Exists)
                        outputDirectory.Create();
                    foreach (var result in results)
                    {
                        var outputFile = Path.Combine(outputDirectory.FullName, Path.GetFileNameWithoutExtension(result.FileName) + ".chunks.txt");
                        using var writer = new StreamWriter(outputFile, false);
                        WriteChunkingResult(result, writer);
                        Console.WriteLine($"Wrote: {outputFile}");
                    }
                }
                else
                {
                    foreach (var result in results)
                    {
                        WriteChunkingResult(result, Console.Out);
                    }
                }
                Console.WriteLine($"Max Chunk Length: {maxChunkLength}");
                Console.WriteLine($"Min Chunk Length: {minChunkLength}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return;
            }
        });
        rootCommand.Subcommands.Add(chunkMarkdownCommand);
        rootCommand.Subcommands.Add(buildVectorDbCommand);

        return rootCommand.Parse(args).Invoke();
    }


}
