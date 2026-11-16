using System.Net;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// End-to-end checks of the real Mailjet registration (<see cref="IServiceCollectionExtensions.AddMailjetEmailSender"/>)
/// combined with the production global HTTP defaults and the real <see cref="EmailSender"/>. Only the primary HTTP handler
/// is faked. These guard the contract between the two: a timeout or HTTP failure must reach callers as
/// <see cref="EmailSendException"/> (not a Polly or HttpClient exception) and a failed send must be attempted exactly once.
/// </summary>
public class MailjetEmailSenderPipelineTests
{
    private static readonly Dictionary<string, string?> s_validSettings = new()
    {
        ["AuthMessageSender:APIKey"] = "key",
        ["AuthMessageSender:SecretKey"] = "secret",
        ["AuthMessageSender:SendFromEmail"] = "no-reply@example.com",
        ["AuthMessageSender:SendFromName"] = "Essential C# Team",
    };

    private static IHost BuildHost(HttpMessageHandler primaryHandler, TimeSpan? timeoutOverride = null)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Production",
        });
        builder.Configuration.AddInMemoryCollection(s_validSettings);
        builder.Services.AddLogging();
        // Same global defaults Program.cs applies (standard resilience handler on every HttpClient).
        builder.Services.AddGlobalHttpClientDefaults();
        builder.Services.AddMailjetEmailSender(
            builder.Configuration.GetSection(AuthMessageSenderOptions.AuthMessageSender), builder.Environment);

        // Configured after the production registration, so these run last: swap the primary handler and,
        // for the timeout test, shorten the request timeout so the test does not wait the real 10 seconds.
        IHttpClientBuilder mailjetClient = builder.Services.AddHttpClient(nameof(Mailjet.Client.IMailjetClient));
        if (timeoutOverride is { } timeout)
        {
            mailjetClient.ConfigureHttpClient(client => client.Timeout = timeout);
        }
        mailjetClient.ConfigurePrimaryHttpMessageHandler(() => primaryHandler);

        return builder.Build();
    }

    [Test]
    public async Task SendEmailAsync_WhenMailjetNeverResponds_ThrowsEmailSendExceptionAfterOneAttempt()
    {
        HangingHandler handler = new();
        using IHost host = BuildHost(handler, timeoutOverride: TimeSpan.FromMilliseconds(250));
        IEmailSender sender = host.Services.GetRequiredService<IEmailSender>();

        await Assert.That(async () => await sender.SendEmailAsync("reader@example.com", "Subject", "<p>Hello</p>"))
            .Throws<EmailSendException>();

        // The send is attempted once. (The retry guarantee itself is proven by the server-error test below.)
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SendEmailAsync_WhenMailjetReturnsServerError_ThrowsEmailSendExceptionAfterOneAttempt()
    {
        StatusHandler handler = new(HttpStatusCode.ServiceUnavailable);
        using IHost host = BuildHost(handler);
        IEmailSender sender = host.Services.GetRequiredService<IEmailSender>();

        await Assert.That(async () => await sender.SendEmailAsync("reader@example.com", "Subject", "<p>Hello</p>"))
            .Throws<EmailSendException>();

        // The global standard resilience handler would retry a 503 POST up to three times; the Mailjet client must not.
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        private int _callCount;
        public int CallCount => _callCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
        }
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        private int _callCount;
        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}
