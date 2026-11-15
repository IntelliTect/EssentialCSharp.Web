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
/// The standalone Web AppHost with its chat backend and hCaptcha verification pointed at in-process fakes.
/// Also used by <see cref="StandaloneWebSmokeTests"/> so a test run boots the app once.
/// </summary>
public class JourneyFixture : StandaloneWebFixture
{
    public const string WebResource = "essentialcsharp-web";
    private const string WebDatabase = "EssentialCSharpWebContextConnection";

    // Started before the AppHost is built because ConfigureBuilder needs their addresses.
    private FakeChatModelServer? _chatModel;
    private FakeHCaptcha? _hCaptcha;

    public FakeChatModelServer ChatModel =>
        _chatModel ?? throw new InvalidOperationException($"{nameof(JourneyFixture)} has not been initialized.");

    private FakeHCaptcha HCaptcha =>
        _hCaptcha ?? throw new InvalidOperationException($"{nameof(JourneyFixture)} has not been initialized.");

    public Uri WebUrl => App.GetEndpoint(WebResource, "http");

    public override async Task InitializeAsync()
    {
        _chatModel = await FakeChatModelServer.StartAsync();
        _hCaptcha = await FakeHCaptcha.StartAsync();
        await base.InitializeAsync();
    }

    protected override void ConfigureBuilder(IDistributedApplicationTestingBuilder builder)
    {
        base.ConfigureBuilder(builder);

        ProjectResource web = builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == WebResource);
        Uri chatModelEndpoint = ChatModel.Endpoint;
        Uri hCaptchaApi = HCaptcha.ApiBaseUrl;

        // Added after the AppHost's callbacks, so these override them and select the local chat backend.
        builder.CreateResourceBuilder(web).WithEnvironment(context =>
        {
            context.EnvironmentVariables["AIOptions__LocalEndpoint"] = chatModelEndpoint.ToString();
            context.EnvironmentVariables["AIOptions__LocalChatModel"] = "fake-model";
            context.EnvironmentVariables["HCaptcha__ApiBaseUrl"] = hCaptchaApi.ToString();
        });
    }

    /// <summary>
    /// Creates a confirmed user directly; UI registration can't, because the confirmation link is only emailed.
    /// Use one user per test: chat rate limits are per user.
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
        if (_hCaptcha is not null)
        {
            await _hCaptcha.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
}

public sealed record TestUser(string Email, string Password);
