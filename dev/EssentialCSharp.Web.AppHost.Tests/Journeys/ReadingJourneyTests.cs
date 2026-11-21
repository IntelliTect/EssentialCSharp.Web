using EssentialCSharp.Web.E2ETests;
using Microsoft.Playwright;
using TUnit.Playwright;

namespace EssentialCSharp.Web.AppHost.Tests.Journeys;

[Category("Docker")]
[Category("Journey")]
[RequiresLinuxContainers]
[CaptureOnFailure]
[ClassDataSource<JourneyFixture>(Shared = SharedType.PerTestSession)]
public class ReadingJourneyTests(JourneyFixture fixture) : PageTest
{
    [Test]
    public async Task Reader_CanFilterContentsTurnPagesAndFollowSectionDeepLinks()
    {
        await Context.AddCookiesAsync(
        [
            new Cookie
            {
                Name = "essential-csharp-consent",
                Value = "%7B%22analytics_storage%22%3A%22granted%22%2C%22_version%22%3A%223%22%7D",
                Url = fixture.WebUrl.GetLeftPart(UriPartial.Authority),
            },
        ]);

        ReadingJourneyResult journey = await ReadingJourney.RunAsync(Page, fixture.WebUrl);

        await Assert.That(journey.ChapterHref).IsEqualTo("introducing-c#overview");
        await Assert.That(journey.SectionHref).IsEqualTo("hello-world#hello-world");
        await Assert.That(journey.NextHref).IsEqualTo("c-syntax-fundamentals#c-syntax-fundamentals");
        await Assert.That(journey.PreviousHref).IsEqualTo("introducing-c#overview");
    }
}
