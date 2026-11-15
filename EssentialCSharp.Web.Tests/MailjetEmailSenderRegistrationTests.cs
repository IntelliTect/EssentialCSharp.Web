using System.Net;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Services;
using Mailjet.Client;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

public class MailjetEmailSenderRegistrationTests
{
    private static readonly Dictionary<string, string?> s_validSettings = new()
    {
        ["AuthMessageSender:APIKey"] = "key",
        ["AuthMessageSender:SecretKey"] = "secret",
        ["AuthMessageSender:SendFromEmail"] = "no-reply@example.com",
        ["AuthMessageSender:SendFromName"] = "Essential C# Team",
    };

    private static IHost BuildHost(string environmentName, Dictionary<string, string?> settings, Action<IServiceCollection>? configureServices = null)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environmentName,
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddLogging();
        // The same global defaults Program.cs applies: every HttpClient gets the standard (retrying) resilience handler.
        builder.Services.AddGlobalHttpClientDefaults();
        builder.Services.AddMailjetEmailSender(
            builder.Configuration.GetSection(AuthMessageSenderOptions.AuthMessageSender), builder.Environment);
        configureServices?.Invoke(builder.Services);
        return builder.Build();
    }

    [Test]
    [Arguments("Production")]
    [Arguments("Staging")]
    public async Task Start_OutsideDevelopmentWithoutMailjetSettings_FailsFast(string environmentName)
    {
        using IHost host = BuildHost(environmentName, []);

        OptionsValidationException exception = await CaptureAsync<OptionsValidationException>(() => host.StartAsync());

        await Assert.That(exception.Failures.Count()).IsEqualTo(3);
    }

    [Test]
    public async Task Start_OutsideDevelopmentWithInvalidFromEmail_FailsFast()
    {
        Dictionary<string, string?> settings = new(s_validSettings) { ["AuthMessageSender:SendFromEmail"] = "nope" };
        using IHost host = BuildHost("Production", settings);

        await Assert.That(async () => await host.StartAsync()).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Start_OutsideDevelopmentWithValidSettings_Succeeds()
    {
        using IHost host = BuildHost("Production", s_validSettings);

        await host.StartAsync();

        AuthMessageSenderOptions options = host.Services.GetRequiredService<IOptions<AuthMessageSenderOptions>>().Value;
        await Assert.That(options.SendFromEmail).IsEqualTo("no-reply@example.com");
        await Assert.That(options.SandboxMode).IsFalse();
        await Assert.That(host.Services.GetRequiredService<IEmailSender>()).IsTypeOf<EmailSender>();
        await Assert.That(host.Services.GetRequiredService<IMailjetClient>()).IsNotNull();
    }

    [Test]
    public async Task Start_InDevelopmentWithoutMailjetSettings_SkipsValidationAndRegistration()
    {
        using IHost host = BuildHost("Development", []);

        await host.StartAsync();

        await Assert.That(host.Services.GetService<IEmailSender>()).IsNull();
        await Assert.That(host.Services.GetService<IMailjetClient>()).IsNull();
    }

    [Test]
    public async Task HttpClient_UsesConfiguredCredentialsAndExplicitTimeout()
    {
        using IHost host = BuildHost("Production", s_validSettings);
        HttpClient client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IMailjetClient));

        await Assert.That(client.Timeout).IsEqualTo(IServiceCollectionExtensions.MailjetRequestTimeout);
        await Assert.That(client.Timeout).IsEqualTo(TimeSpan.FromSeconds(10));
        await Assert.That(client.DefaultRequestHeaders.Authorization?.Scheme).IsEqualTo("Basic");
        string expectedCredentials = Convert.ToBase64String("key:secret"u8);
        await Assert.That(client.DefaultRequestHeaders.Authorization?.Parameter).IsEqualTo(expectedCredentials);
    }

    [Test]
    public async Task HttpClient_DoesNotRetryAFailedSend()
    {
        CountingHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using IHost host = BuildHost("Production", s_validSettings,
            services => services.AddHttpClient(nameof(IMailjetClient)).ConfigurePrimaryHttpMessageHandler(() => handler));
        HttpClient client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IMailjetClient));

        using HttpResponseMessage response = await client.PostAsync("v3.1/send", new StringContent("{}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    private static async Task<TException> CaptureAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} but nothing was thrown.");
    }

    private sealed class CountingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private int _callCount;
        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return respond(request, cancellationToken);
        }
    }
}
