using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace EssentialCSharp.Web.Tests;

public class ChapterDescriptionServiceTests
{
    [Test]
    public async Task GeneratedDescriptionsOverrideHeadingFallback()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(directory, "Chapters"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "Chapters", "sitemap.descriptions.json"),
                """[{"Keys":["example-page","alias"],"Value":"A tailored chapter description."}]""");
            Mock<IWebHostEnvironment> environment = new();
            environment.SetupGet(e => e.ContentRootPath).Returns(directory);
            ChapterDescriptionService service = new(environment.Object);
            SiteMapping mapping = CreateMapping("alias", "Examples");

            await Assert.That(service.GetDescription(mapping)).IsEqualTo("A tailored chapter description.");
            await Assert.That(service.GetDescription(CreateMapping("missing", "Other Examples"))).Contains("Other Examples");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task WithoutGeneratedDescriptionsUsesPageHeading()
    {
        Mock<IWebHostEnvironment> environment = new();
        environment.SetupGet(e => e.ContentRootPath).Returns(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        ChapterDescriptionService service = new(environment.Object);

        await Assert.That(service.GetDescription(CreateMapping("sample", "Sample Heading"))).Contains("Sample Heading");
    }

    private static SiteMapping CreateMapping(string key, string heading) =>
        new([key], key, ["Chapters", "01", "Pages", "01.html"], 1, 1, 1,
            "Chapter One", heading, null, 1);
}
