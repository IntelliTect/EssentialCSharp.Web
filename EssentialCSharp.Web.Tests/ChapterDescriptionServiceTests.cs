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

    // Shape emitted by the content package (a single "Key" per entry).
    [Test]
    public async Task SingularKeyEntriesFromContentPackageAreLoaded()
    {
        ChapterDescriptionService service = CreateServiceFromJson(
            """[{"Key":"example-page","Value":"Singular key description."}]""");

        await Assert.That(service.Count).IsEqualTo(1);
        await Assert.That(service.GetDescription(CreateMapping("EXAMPLE-PAGE", "Examples"))).IsEqualTo("Singular key description.");
    }

    [Test]
    [Arguments("""{"Key":"a","Value":"b"}""")]
    [Arguments("""[null]""")]
    [Arguments("""[{"Value":"no key"}]""")]
    [Arguments("""[{"Key":"a"}]""")]
    [Arguments("""[{"Keys":[""],"Value":"empty key"}]""")]
    [Arguments("""[{"Keys":"a","Value":"keys is not an array"}]""")]
    public async Task MalformedDescriptionsFailWithAClearMessage(string json)
    {
        await Assert.That(() => CreateServiceFromJson(json))
            .Throws<InvalidDataException>()
            .WithMessageContaining("sitemap.descriptions.json");
    }

    private static ChapterDescriptionService CreateServiceFromJson(string json)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(directory, "Chapters"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "Chapters", "sitemap.descriptions.json"), json);
            Mock<IWebHostEnvironment> environment = new();
            environment.SetupGet(e => e.ContentRootPath).Returns(directory);
            return new ChapterDescriptionService(environment.Object);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SiteMapping CreateMapping(string key, string heading) =>
        new([key], key, ["Chapters", "01", "Pages", "01.html"], 1, 1, 1,
            "Chapter One", heading, null, 1);
}
