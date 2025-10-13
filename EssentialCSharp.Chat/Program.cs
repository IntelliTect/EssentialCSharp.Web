using System.CommandLine;
using System.Text.Json;
using Azure.Identity;
using EssentialCSharp.Chat.Common.Extensions;
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
        Option<DirectoryInfo> directoryOption = new("--directory")
        {
            Description = "Directory containing markdown files.",
            Required = true
        };
        Option<string> filePatternOption = new("--file-pattern")
        {
            Description = "File pattern to match (e.g. *.md)",
            DefaultValueFactory = _ => "*.md"
        };
        Option<DirectoryInfo?> outputDirectoryOption = new("--output-directory")
        {
            Description = "Directory to write chunked output files. If not provided, output is written to console.",
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

        var chatCommand = new Command("chat", "Start an interactive AI chat session.")
        {
            new Option<bool>("--stream"),
            new Option<bool>("--web-search"),
            new Option<bool>("--contextual-search"),
            new Option<string>("--system-prompt")
        };

        buildVectorDbCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var config = CreateConfiguration();

            var builder = Kernel.CreateBuilder();
            builder.Services.Configure<AIOptions>(config.GetRequiredSection("AIOptions"));

            // Use shared extension to register Azure OpenAI services with configuration
            builder.Services.AddAzureOpenAIServices(config);

            builder.Services.AddLogging(loggingBuilder =>
                {
                    loggingBuilder.AddSimpleConsole(options =>
                    {
                        options.TimestampFormat = "HH:mm:ss ";
                        options.SingleLine = true;
                    });
                });

            // Build the kernel and get the data uploader.
            var kernel = builder.Build();
            var directory = parseResult.GetValue(directoryOption);
            var filePattern = parseResult.GetValue(filePatternOption) ?? "*.md";
            var markdownService = kernel.GetRequiredService<MarkdownChunkingService>();
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
        });

        chatCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var config = CreateConfiguration();

            // https://learn.microsoft.com/api/mcp

            //SseClientTransport microsoftLearnMcp = new SseClientTransport(
            //    new SseClientTransportOptions
            //    {
            //        Name = "Microsoft Learn MCP",
            //        Endpoint = new Uri("https://learn.microsoft.com/api/mcp"),
            //    });

            //IMcpClient mcpClient = await McpClientFactory.CreateAsync(clientTransport: microsoftLearnMcp, cancellationToken: cancellationToken);

            var enableStreaming = parseResult.GetValue<bool>("--stream");
            var customSystemPrompt = parseResult.GetValue<string>("--system-prompt");


            AIOptions aiOptions = config.GetRequiredSection("AIOptions").Get<AIOptions>() ?? throw new InvalidOperationException(
                "AIOptions section is missing or not configured correctly in appsettings.json or environment variables.");

            // Create service collection and register dependencies
            var services = new ServiceCollection();
            services.Configure<AIOptions>(config.GetRequiredSection("AIOptions"));
            services.AddLogging(builder => builder.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "HH:mm:ss ";
                options.SingleLine = true;
            }));

            // Use shared extension to register Azure OpenAI services with configuration
            services.AddAzureOpenAIServices(config);

            var serviceProvider = services.BuildServiceProvider();
            var aiChatService = serviceProvider.GetRequiredService<AIChatService>();

            Console.WriteLine("🤖 AI Chat Session Started!");
            Console.WriteLine("Features enabled:");
            Console.WriteLine($"  • Streaming: {(enableStreaming ? "✅" : "❌")}");
            if (!string.IsNullOrEmpty(customSystemPrompt))
                Console.WriteLine($"  • Custom System Prompt: {customSystemPrompt}");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  • 'exit' or 'quit' - End the chat session");
            Console.WriteLine("  • 'clear' - Start a new conversation context");
            Console.WriteLine("  • 'help' - Show this help message");
            Console.WriteLine("  • 'history' - Show conversation history");
            Console.WriteLine("  • Any other text - Chat with the AI");
            Console.WriteLine("=====================================");

            // Track conversation context with response IDs
            string? previousResponseId = null;
            var conversationHistory = new List<(string Role, string Content)>();

            while (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine();
                Console.Write("👤 You: ");
                var userInput = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(userInput))
                    continue;

                userInput = userInput.Trim();

                if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                    userInput.Equals("quit", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Goodbye! 👋");
                    break;
                }

                if (userInput.Equals("clear", StringComparison.OrdinalIgnoreCase))
                {
                    // Reset conversation context when PreviousResponseId is implemented
                    previousResponseId = null;
                    conversationHistory.Clear();
                    Console.WriteLine("🧹 Conversation context cleared. Starting fresh!");
                    continue;
                }

                if (userInput.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine();
                    Console.WriteLine("Commands:");
                    Console.WriteLine("  • 'exit' or 'quit' - End the chat session");
                    Console.WriteLine("  • 'clear' - Start a new conversation context");
                    Console.WriteLine("  • 'help' - Show this help message");
                    Console.WriteLine("  • 'history' - Show conversation history");
                    Console.WriteLine("  • Any other text - Chat with the AI");
                    continue;
                }

                if (userInput.Equals("history", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine();
                    Console.WriteLine("📜 Conversation History:");
                    if (conversationHistory.Count == 0)
                    {
                        Console.WriteLine("  No conversation history yet.");
                    }
                    else
                    {
                        for (int i = 0; i < conversationHistory.Count; i++)
                        {
                            var (role, content) = conversationHistory[i];
                            var emoji = role == "User" ? "👤" : "🤖";
                            Console.WriteLine($"  {i + 1}. {emoji} {role}: {content}");
                        }
                    }
                    continue;
                }

                conversationHistory.Add(("User", userInput));

                try
                {
                    Console.Write("🤖 AI: ");

                    if (enableStreaming)
                    {
                        // Use streaming with optional tools and conversation context
                        var fullResponse = new System.Text.StringBuilder();

                        await foreach (var (text, responseId) in aiChatService.GetChatCompletionStream(
                            prompt: userInput/*, mcpClient: mcpClient*/, previousResponseId: previousResponseId, systemPrompt: customSystemPrompt, cancellationToken: cancellationToken))
                        {
                            if (!string.IsNullOrEmpty(text))
                            {
                                Console.Write(text);
                                fullResponse.Append(text);
                            }
                            if (!string.IsNullOrEmpty(responseId))
                            {
                                previousResponseId = responseId; // Update for next turn
                            }
                        }
                        Console.WriteLine();

                        conversationHistory.Add(("Assistant", fullResponse.ToString()));
                    }
                    else
                    {
                        // Non-streaming response with optional tools and conversation context
                        var (response, responseId) = await aiChatService.GetChatCompletion(
                           prompt: userInput, previousResponseId: previousResponseId, systemPrompt: customSystemPrompt, cancellationToken: cancellationToken);

                        Console.WriteLine(response);
                        conversationHistory.Add(("Assistant", response));

                        if (!string.IsNullOrEmpty(responseId))
                        {
                            previousResponseId = responseId;
                        }
                    }

                    Console.WriteLine();
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine();
                    Console.WriteLine("Operation cancelled. Goodbye! 👋");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"❌ Error: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"   Details: {ex.InnerException.Message}");
                    }
                }
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
        rootCommand.Subcommands.Add(chatCommand);

        return rootCommand.Parse(args).Invoke();
    }

    /// <summary>
    /// Creates and configures the IConfiguration used by multiple commands.
    /// Supports Azure Key Vault integration for secure secret management.
    /// </summary>
    /// <returns>The configured IConfigurationRoot</returns>
    /// <remarks>
    /// Configuration precedence (highest to lowest):
    /// 1. Environment Variables
    /// 2. Azure Key Vault (if configured)
    /// 3. User Secrets (development only)
    /// 4. appsettings.json
    /// 
    /// To enable Key Vault, set the "KeyVaultName" configuration value in appsettings.json or user secrets:
    /// {
    ///   "KeyVaultName": "your-keyvault-name"
    /// }
    /// 
    /// The application will use DefaultAzureCredential for authentication, which supports:
    /// - Managed Identity (in Azure)
    /// - Azure CLI (local development)
    /// - Visual Studio (local development)
    /// - Environment variables (AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID)
    /// </remarks>
    private static IConfigurationRoot CreateConfiguration()
    {
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(IntelliTect.Multitool.RepositoryPaths.GetDefaultRepoRoot())
            .AddJsonFile("EssentialCSharp.Web/appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"EssentialCSharp.Web/appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true, reloadOnChange: true)
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables();

        // Build a temporary configuration to check for Key Vault settings
        var tempConfig = configBuilder.Build();
        var keyVaultName = tempConfig["KeyVaultName"];

        // If Key Vault is configured, add it to the configuration pipeline
        if (!string.IsNullOrEmpty(keyVaultName))
        {
            try
            {
                var keyVaultUri = new Uri($"https://{keyVaultName}.vault.azure.net/");

                // Use DefaultAzureCredential which works both locally and in Azure
                var credential = new DefaultAzureCredential();

                configBuilder.AddAzureKeyVault(keyVaultUri, credential);

                Console.WriteLine($"✅ Connected to Azure Key Vault: {keyVaultName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Warning: Could not connect to Azure Key Vault '{keyVaultName}': {ex.Message}");
                Console.WriteLine("   Continuing with other configuration sources...");
            }
        }

        return configBuilder.Build();
    }
}
