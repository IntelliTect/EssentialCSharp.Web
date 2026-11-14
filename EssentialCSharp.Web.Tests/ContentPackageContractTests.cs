using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Loads the content files the app actually has on disk through the real services. With
/// <c>-p:AccessToNugetFeed=true</c> these are the files from the content package; otherwise they are the
/// placeholders in <c>Placeholders/</c>, which must mirror the package's file shapes. Assertions are
/// structural only so a failure never prints book content into a public log.
/// </summary>
public class ContentPackageContractTests : IntegrationTestBase
{
    [Test]
    public async Task ChapterDescriptionsLoad()
    {
        ChapterDescriptionService descriptions = Factory.Inner.Services.GetRequiredService<ChapterDescriptionService>();

        await Assert.That(descriptions.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task SiteMapAndGuidelinesLoad()
    {
        IServiceProvider services = Factory.Inner.Services;

        using (Assert.Multiple())
        {
            await Assert.That(services.GetRequiredService<ISiteMappingService>().SiteMappings).IsNotEmpty();
            await Assert.That(services.GetRequiredService<IGuidelinesService>().Guidelines).IsNotEmpty();
        }
    }
}
