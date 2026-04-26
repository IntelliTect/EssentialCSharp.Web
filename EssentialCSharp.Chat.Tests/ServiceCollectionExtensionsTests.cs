using EssentialCSharp.Chat.Common.Extensions;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EssentialCSharp.Chat.Tests;

public class ServiceCollectionExtensionsTests
{
    [Test]
    public async Task AddAIServices_WhenDevelopmentWithoutConfiguration_ThrowsInvalidOperationException()
    {
        var builder = CreateBuilder(Environments.Development);

        await Assert.That(() => builder.AddAIServices(builder.Configuration))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AddAIServices_WhenUseLocalAI_RegistersLocalAIService()
    {
        var builder = CreateBuilder(
            Environments.Development,
            new Dictionary<string, string?>
            {
                ["AIOptions:UseLocalAI"] = bool.TrueString,
                ["ConnectionStrings:ollama-chat"] = "Endpoint=http://localhost:11434;Model=qwen2.5-coder:7b"
            });

        builder.AddAIServices(builder.Configuration);

        var descriptor = builder.Services.LastOrDefault(service => service.ServiceType == typeof(IAIChatService));
        await Assert.That(descriptor).IsNotNull();
        await Assert.That(descriptor!.ImplementationType).IsEqualTo(typeof(LocalAIChatService));
    }

    [Test]
    public async Task AddAIServices_WhenAzureEndpointConfigured_RegistersAzureAIService()
    {
        var builder = CreateBuilder(
            Environments.Production,
            new Dictionary<string, string?>
            {
                ["AIOptions:Endpoint"] = "https://example.openai.azure.com/",
                ["AIOptions:ChatDeploymentName"] = "chat",
                ["AIOptions:VectorGenerationDeploymentName"] = "embeddings",
                ["ConnectionStrings:PostgresVectorStore"] = "Host=test.postgres.database.azure.com;Database=app;Username=user"
            });

        builder.AddAIServices(builder.Configuration);

        await Assert.That(builder.Services.Any(service => service.ServiceType == typeof(AIChatService))).IsTrue();
        await Assert.That(builder.Services.Any(service => service.ServiceType == typeof(IAIChatService))).IsTrue();
    }

    [Test]
    public async Task AddAIServices_WhenProductionWithoutConfiguration_ThrowsInvalidOperationException()
    {
        var builder = CreateBuilder(Environments.Production);

        await Assert.That(() => builder.AddAIServices(builder.Configuration))
            .Throws<InvalidOperationException>();
    }

    private static HostApplicationBuilder CreateBuilder(
        string environmentName,
        Dictionary<string, string?>? settings = null)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environmentName
        });

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        return builder;
    }
}
