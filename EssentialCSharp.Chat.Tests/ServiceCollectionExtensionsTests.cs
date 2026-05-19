using EssentialCSharp.Chat.Common.Extensions;
using EssentialCSharp.Chat.Common.Models;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Chat.Tests;

public class ServiceCollectionExtensionsTests
{
    [Test]
    public async Task AddConfiguredChatServices_WithFullAzureConfig_SelectsAzureChatService_AndBindsEmbeddingRetry()
    {
        var services = CreateServices(new Dictionary<string, string?>
        {
            ["AIOptions:Endpoint"] = "https://example.openai.azure.com",
            ["AIOptions:ChatDeploymentName"] = "chat-deployment",
            ["AIOptions:VectorGenerationDeploymentName"] = "embedding-deployment",
            ["ConnectionStrings:PostgresVectorStore"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["AIOptions:EmbeddingRetry:MaxRetries"] = "7"
        });

        var descriptor = GetChatServiceDescriptor(services);
        await Assert.That(descriptor.ImplementationFactory).IsNotNull();
        await Assert.That(services.Any(d => d.ServiceType == typeof(AIChatService))).IsTrue();

        using var provider = services.BuildServiceProvider();
        var retry = provider.GetRequiredService<IOptions<EmbeddingRetryOptions>>().Value;
        await Assert.That(retry.MaxRetries).IsEqualTo(7);
    }

    [Test]
    [Arguments("invalid-azure-falls-back-to-local")]
    [Arguments("valid-local")]
    [Arguments("connection-string-fallback")]
    [Arguments("invalid-local")]
    [Arguments("missing-config")]
    public async Task AddConfiguredChatServices_SelectsExpectedBackend(string scenario)
    {
        var (configValues, expectedChatServiceType) = CreateBackendSelectionScenario(scenario);
        var services = CreateServices(configValues);

        var descriptor = GetChatServiceDescriptor(services);
        await Assert.That(descriptor.ImplementationType).IsEqualTo(expectedChatServiceType);
    }

    private static ServiceCollection CreateServices(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConfiguredChatServices(configuration);
        return services;
    }

    private static ServiceDescriptor GetChatServiceDescriptor(IServiceCollection services) =>
        services.Single(d => d.ServiceType == typeof(IChatCompletionService));

    private static (Dictionary<string, string?> ConfigValues, Type ExpectedChatServiceType) CreateBackendSelectionScenario(string scenario) => scenario switch
    {
        "invalid-azure-falls-back-to-local" => (
            new Dictionary<string, string?>
            {
                ["AIOptions:Endpoint"] = "not-a-valid-uri",
                ["AIOptions:ChatDeploymentName"] = "chat-deployment",
                ["AIOptions:VectorGenerationDeploymentName"] = "embedding-deployment",
                ["ConnectionStrings:PostgresVectorStore"] = "Host=localhost;Database=test;Username=test;Password=test",
                ["AIOptions:UseLocalAI"] = "true",
                ["AIOptions:LocalEndpoint"] = "http://localhost:11434",
                ["AIOptions:LocalChatModel"] = "qwen2.5-coder:7b"
            },
            typeof(LocalChatService)),
        "valid-local" => (
            new Dictionary<string, string?>
            {
                ["AIOptions:UseLocalAI"] = "true",
                ["AIOptions:LocalEndpoint"] = "http://localhost:11434",
                ["AIOptions:LocalChatModel"] = "qwen2.5-coder:7b"
            },
            typeof(LocalChatService)),
        "connection-string-fallback" => (
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:ollama-chat"] = "http://localhost:11434",
                ["AIOptions:LocalChatModel"] = "qwen2.5-coder:7b"
            },
            typeof(LocalChatService)),
        "invalid-local" => (
            new Dictionary<string, string?>
            {
                ["AIOptions:UseLocalAI"] = "true",
                ["AIOptions:LocalEndpoint"] = "invalid-uri",
                ["AIOptions:LocalChatModel"] = "qwen2.5-coder:7b"
            },
            typeof(UnavailableChatService)),
        "missing-config" => (
            new Dictionary<string, string?>(),
            typeof(UnavailableChatService)),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown backend selection scenario.")
    };
}
