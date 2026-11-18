using System.Text.Json;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Every other reading test substitutes a fake word-count service, so none of them would notice if the host stopped
/// building the real cache at startup (every reading credit would then be dropped as an unknown page). This runs the
/// app with its own services and checks the cache was built before the first request.
/// </summary>
public class ReadingHostWiringTests : IntegrationTestBase
{
    [Test]
    public async Task ContentPage_RenderedByTheRealHost_CarriesWordCountsFromTheBuiltCache()
    {
        // /hello-world is a page of the content shipped with the repository (placeholder or real); only that it has
        // some prose is asserted, not how much.
        using HttpClient client = CreateClientWithoutAutoRedirect();

        using HttpResponseMessage response = await GetFollowingGetRedirectsAsync(client, "/hello-world");
        using JsonDocument context = ReadingTestSupport.ParseReadingContext(await ReadingTestSupport.ReadOkHtmlAsync(response));
        JsonElement root = context.RootElement;
        int pageWords = ReadingTestSupport.Field(root, "pageWords").GetInt32();
        int chapterWords = ReadingTestSupport.Field(root, "chapterWords").GetInt32();
        int bookWords = ReadingTestSupport.Field(root, "bookWords").GetInt32();

        using (Assert.Multiple())
        {
            await Assert.That(pageWords).IsGreaterThan(0).Because("the word-count cache was not built when the host started");
            await Assert.That(chapterWords).IsGreaterThanOrEqualTo(pageWords);
            await Assert.That(bookWords).IsGreaterThanOrEqualTo(chapterWords);
        }
    }
}
