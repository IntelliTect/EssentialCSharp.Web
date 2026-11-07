using EssentialCSharp.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// A misconfigured <c>Reading:</c> section must stop the host at startup instead of surfacing as odd behavior later.
/// </summary>
public class ReadingOptionsStartupTests : IntegrationTestBase
{
    [Test]
    [Arguments("Reading:HardCutoffWpm", "0")]   // violates a data-annotation range
    [Arguments("Reading:MaxCreditWpm", "100000")] // violates only a cross-field rule (credit above the cutoff)
    public async Task InvalidReadingConfiguration_PreventsTheHostFromStarting(string key, string value)
    {
        using var factory = Factory.Inner.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })));

        Exception? failure = null;
        try
        {
            using HttpClient client = McpTestHelper.CreateClient(factory);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        await Assert.That(failure).IsNotNull().Because("an invalid Reading configuration must fail host startup");
        await Assert.That(ExceptionChain(failure!)).Contains(e => e is OptionsValidationException);
    }

    private static IEnumerable<Exception> ExceptionChain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
