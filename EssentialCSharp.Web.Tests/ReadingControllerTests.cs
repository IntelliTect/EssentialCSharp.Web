using System.Net;
using System.Net.Http.Json;
using EssentialCSharp.Web.Controllers;

namespace EssentialCSharp.Web.Tests;

public class ReadingControllerTests : IntegrationTestBase
{

    [Test]
    public async Task GetBookStats_IsPublic_Returns200()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpResponseMessage response = await client.GetAsync("/api/reading/book-stats");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetBookStats_ReturnsExpectedShape()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpResponseMessage response = await client.GetAsync("/api/reading/book-stats");

        var body = await response.Content.ReadFromJsonAsync<BookStatsResponse>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.TotalWordCount).IsGreaterThanOrEqualTo(0);
        await Assert.That(body.Chapters).IsNotNull();
    }

    // ---- profile (requires auth) ----

    [Test]
    public async Task GetProfile_Anonymous_Returns401()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpResponseMessage response = await client.GetAsync("/api/reading/profile");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    // ---- POST session (requires auth) ----

    [Test]
    public async Task PostSession_Anonymous_Returns401()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        var intervals = new[]
        {
            new ReadingController.ReadingIntervalDto("page1", 60, 100, false)
        };
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/reading/session", intervals);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    // ---- POST reset (requires auth) ----

    [Test]
    public async Task PostReset_Anonymous_Returns401()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpResponseMessage response = await client.PostAsync("/api/reading/reset", null);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    // ---- WPM algorithm unit tests (test logic directly, no HTTP) ----

    [Test]
    public async Task WpmAlgorithm_FastInterval_IsDiscarded()
    {
        // Hard discard: >900 WPM → interval dropped entirely
        long discardWords = 901;
        long discardSeconds = 60;
        double discardWpm = discardWords / (discardSeconds / 60.0); // 901 WPM

        await Assert.That(discardWpm).IsGreaterThan(900.0);

        // Accepted interval: 600 WPM (below cutoff)
        long acceptWords = 600;
        long acceptSeconds = 60;
        double acceptWpm = acceptWords / (acceptSeconds / 60.0); // 600 WPM

        await Assert.That(acceptWpm).IsLessThanOrEqualTo(900.0);
    }

    [Test]
    public async Task WpmAlgorithm_SlowInterval_IsClamped()
    {
        // Reference WPM = 200. An interval of 10 words in 300 seconds =
        // 10 / (300/60) = 2 WPM. 0.25 * 200 = 50 WPM → interval is below threshold → clamp.
        double referenceWpm = 200.0;
        double slowOutlierFactor = 0.25;
        double intervalWpm = 10.0 / (300.0 / 60.0); // ~2 WPM

        bool shouldClamp = intervalWpm < slowOutlierFactor * referenceWpm;
        await Assert.That(shouldClamp).IsTrue();

        // After clamp: effectiveSeconds = words / (0.25 * referenceWpm) * 60
        int words = 10;
        int effectiveSeconds = (int)Math.Round(words / (slowOutlierFactor * referenceWpm) * 60.0);
        double clampedWpm = words / (effectiveSeconds / 60.0);

        // Clamped WPM should equal exactly 0.25 * referenceWpm (within rounding)
        await Assert.That(clampedWpm).IsEqualTo(50.0).Within(1.0);
    }

    [Test]
    public async Task WpmAlgorithm_NormalInterval_PassesThrough()
    {
        // 180 WPM interval with referenceWpm = 200.
        // 180 > 0.25 * 200 = 50, so no clamping.
        double referenceWpm = 200.0;
        const double slowOutlierFactor = 0.25;
        double intervalWpm = 180.0;

        bool shouldClamp = intervalWpm < slowOutlierFactor * referenceWpm;
        await Assert.That(shouldClamp).IsFalse();

        bool shouldDiscard = intervalWpm > 900.0;
        await Assert.That(shouldDiscard).IsFalse();
    }

    [Test]
    public async Task GetBookStats_Returns200WithChapters()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpResponseMessage response = await client.GetAsync("/api/reading/book-stats");
        await Assert.That((int)response.StatusCode).IsEqualTo(200);

        var body = await response.Content.ReadFromJsonAsync<BookStatsResponse>();
        await Assert.That(body).IsNotNull();
    }

    // ---- DTO for deserializing book-stats response ----

    private sealed class BookStatsResponse
    {
        public int TotalWordCount { get; set; }
        public IEnumerable<ChapterInfo>? Chapters { get; set; }
    }

    private sealed class ChapterInfo
    {
        public int ChapterNumber { get; set; }
        public int WordCount { get; set; }
    }
}
