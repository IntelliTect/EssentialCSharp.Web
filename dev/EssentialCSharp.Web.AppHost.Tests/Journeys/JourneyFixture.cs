using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.AppHost.Tests.Journeys;

/// <summary>
/// The standalone Web AppHost (fresh databases, no private feed) wired for browser journeys: its local chat backend
/// points at an in-process <see cref="FakeChatModelServer"/>, and tests can create confirmed users directly.
/// Shared per test session with <see cref="StandaloneWebSmokeTests"/> so a run boots the app once.
/// </summary>
public class JourneyFixture : StandaloneWebFixture
{
    public const string WebResource = "essentialcsharp-web";
    private const string WebDatabase = "EssentialCSharpWebContextConnection";

    // Created in InitializeAsync before the AppHost is built, because ConfigureBuilder needs its address.
    private FakeChatModelServer? _chatModel;

    public FakeChatModelServer ChatModel =>
        _chatModel ?? throw new InvalidOperationException($"{nameof(JourneyFixture)} has not been initialized.");

    public Uri WebUrl => App.GetEndpoint(WebResource, "http");

    public override async Task InitializeAsync()
    {
        _chatModel = await FakeChatModelServer.StartAsync();
        await base.InitializeAsync();
    }

    protected override void ConfigureBuilder(IDistributedApplicationTestingBuilder builder)
    {
        base.ConfigureBuilder(builder);

        ProjectResource web = builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == WebResource);
        Uri chatModelEndpoint = ChatModel.Endpoint;

        // Registered after the AppHost's own callbacks, so these values win. With a local endpoint and model set,
        // AddConfiguredChatServices selects the local (OpenAI-compatible) backend. The fake listens on 127.0.0.1,
        // which works because Web runs as a host process; if it ever runs in a container, use host.docker.internal.
        builder.CreateResourceBuilder(web).WithEnvironment(context =>
        {
            context.EnvironmentVariables["AIOptions__LocalEndpoint"] = chatModelEndpoint.ToString();
            context.EnvironmentVariables["AIOptions__LocalChatModel"] = "fake-model";
        });
    }

    /// <summary>
    /// Creates a confirmed user straight through Identity. Registration through the UI can't produce one: the
    /// confirmation link is never displayed, and Development uses a no-op email sender. Each test should use its
    /// own user so per-user chat rate limits don't couple tests together.
    /// </summary>
    public async Task<TestUser> CreateConfirmedUserAsync(string name)
    {
        string connectionString = await GetConnectionStringAsync(WebDatabase)
            ?? throw new InvalidOperationException($"No connection string for '{WebDatabase}'.");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<EssentialCSharpWebContext>(options => options.UseSqlServer(connectionString));
        services.AddIdentityCore<EssentialCSharpWebUser>().AddEntityFrameworkStores<EssentialCSharpWebContext>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        UserManager<EssentialCSharpWebUser> users = scope.ServiceProvider.GetRequiredService<UserManager<EssentialCSharpWebUser>>();

        string email = $"{name}-{Guid.NewGuid():N}@journeys.test";
        var testUser = new TestUser(email, $"Journey-{Guid.NewGuid():N}-Pw1!");
        IdentityResult result = await users.CreateAsync(
            new EssentialCSharpWebUser { UserName = email, Email = email, EmailConfirmed = true },
            testUser.Password);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create test user: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }

        return testUser;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_chatModel is not null)
        {
            await _chatModel.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
}

public sealed record TestUser(string Email, string Password);
