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
    public async Task AddConfiguredChatServices_WithInvalidAzureEndpoint_FallsBackToLocal_WhenLocalConfigIsValid()
    {
        var services = CreateServices(new Dictionary<string, string?>
        {
            ["AIOptions:Endpoint"] = "not-a-valid-uri",
            ["AIOptions:ChatDeploymentName"] = "chat-deployment",
            ["AIOptions:VectorGenerationDeploymentName"] = "embedding-deployment",
            ["ConnectionStrings:PostgresVectorStore"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["AIOptions:UseLocalAI"] = "true",
            ["AIOptions:LocalEndpoint"] = "http://localhost:11434",
            ["AIOptions:LocalChatModel"] = "qwen2.5-coder:7b"
        });

        var descriptor = GetChatServiceDescriptor(services);
        await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(LocalChatService));
    }

    [Test]
    public async Task AddConfiguredChatServices_WithValidLocalConfig_SelectsLocalBackend()
    {
        var services = CreateServices(new Dictionary<string, string?>
        {
            ["AIOptions:UseLocalAI"] = "true",
            ["AIOptions:LocalEndpoint"] = "http://localhost:11434",
            ["AIOptions:LocalChatModel"] = "qwen2.5-coder:7b"
        });

        var descriptor = GetChatServiceDescriptor(services);
        await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(LocalChatService));
    }

    [Test]
    public async Task AddConfiguredChatServices_WithInvalidLocalEndpoint_SelectsUnavailableBackend()
    {
        var services = CreateServices(new Dictionary<string, string?>
        {
            ["AIOptions:UseLocalAI"] = "true",
            ["AIOptions:LocalEndpoint"] = "invalid-uri",
            ["AIOptions:LocalChatModel"] = "qwen2.5-coder:7b"
        });

        var descriptor = GetChatServiceDescriptor(services);
        await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(UnavailableChatService));
    }

    [Test]
    public async Task AddConfiguredChatServices_WithMissingConfig_SelectsUnavailableBackend()
    {
        var services = CreateServices(new Dictionary<string, string?>());

        var descriptor = GetChatServiceDescriptor(services);
        await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(UnavailableChatService));
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
}
