using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

/// <summary>Startup selection and the non-Development guard for the local-only MailPit sender.</summary>
public class EmailSenderStartupTests : IntegrationTestBase
{
    private const string MailPitKey = "ConnectionStrings:mailpit";
    private const string MailPitConnectionString = "Endpoint=smtp://localhost:1025";

    [Test]
    public async Task Development_WithMailPit_SendsThroughSmtp()
    {
        await using var factory = Factory.Inner.WithWebHostBuilder(builder =>
            builder.UseSetting(MailPitKey, MailPitConnectionString));

        Type senderType = InScope(factory, services => services.GetRequiredService<IEmailSender>().GetType());

        await Assert.That(senderType).IsEqualTo(typeof(SmtpEmailSender));
    }

    [Test]
    public async Task Development_WithoutMailPit_KeepsIdentityDefaultSender()
    {
        // The shared factory runs in Development with no mailpit connection string.
        Type senderType = InScope(Factory.Inner, services => services.GetRequiredService<IEmailSender>().GetType());

        await Assert.That(senderType).IsNotEqualTo(typeof(SmtpEmailSender));
        await Assert.That(senderType).IsNotEqualTo(typeof(EmailSender));
    }

    [Test]
    public async Task NonDevelopment_WithMailPit_FailsStartup()
    {
        await using var factory = Factory.Inner.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            // Production loads no appsettings.Development.json, so satisfy the earlier required settings (neither is
            // contacted at startup; the factory swaps in SQLite). Startup then reaches the guard under test instead
            // of failing for an unrelated reason, which the message check below would also catch.
            builder.UseSetting("ConnectionStrings:EssentialCSharpWebContextConnection", "Server=unused;Database=unused");
            builder.UseSetting("DataProtection:AzureKeyVaultKeyUri", "https://example.vault.azure.net/keys/test");
            builder.UseSetting(MailPitKey, MailPitConnectionString);
        });

        Exception? failure = null;
        try
        {
            _ = factory.Services;
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        InvalidOperationException? guard = Flatten(failure)
            .OfType<InvalidOperationException>()
            .FirstOrDefault(ex => ex.Message.Contains(MailPitKey, StringComparison.Ordinal));
        await Assert.That(guard).IsNotNull()
            .Because($"startup should reject {MailPitKey} outside Development; got: {failure?.ToString() ?? "no exception"}");
    }

    private static T InScope<T>(WebApplicationFactory<Program> factory, Func<IServiceProvider, T> action)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return action(scope.ServiceProvider);
    }

    private static IEnumerable<Exception> Flatten(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions.SelectMany(Flatten))
                {
                    yield return inner;
                }
            }

            yield return exception;
        }
    }
}
