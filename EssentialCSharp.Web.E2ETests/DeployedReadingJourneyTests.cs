using Microsoft.Playwright;
using TUnit.Playwright;

namespace EssentialCSharp.Web.E2ETests;

[Category("Deployed")]
[RequiresDeployedSite]
[CaptureOnFailure]
public class DeployedReadingJourneyTests : PageTest
{
    public override IReadOnlyList<string>? TraceContextOrigins =>
        [DeployedSite.RequiredBaseUrl.GetLeftPart(UriPartial.Authority)];

    [Test]
    public async Task Reader_CanFilterContentsTurnPagesAndFollowSectionDeepLinks()
    {
        await Context.AddCookiesAsync(
        [
            new Cookie
            {
                Name = "essential-csharp-consent",
                Value = "%7B%22analytics_storage%22%3A%22granted%22%2C%22_version%22%3A%223%22%7D",
                Url = DeployedSite.RequiredBaseUrl.GetLeftPart(UriPartial.Authority),
            },
        ]);

        await ReadingJourney.RunAsync(Page, DeployedSite.RequiredBaseUrl);
    }
}
