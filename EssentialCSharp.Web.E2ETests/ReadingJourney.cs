using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace EssentialCSharp.Web.E2ETests;

internal static class ReadingJourney
{
    public static async Task<ReadingJourneyResult> RunAsync(IPage page, Uri baseUrl)
    {
        await page.AddInitScriptAsync("if (localStorage.getItem('sidebarShown') === null) localStorage.setItem('sidebarShown', 'false');");
        await page.GotoAsync(new Uri(baseUrl, "home").ToString());
        await page.Locator("#sidebar-toggle-button").ClickAsync();
        await Expect(page.Locator("#sidebar")).ToBeVisibleAsync();

        string tocJson = await page.EvaluateAsync<string>("() => JSON.stringify(window.TOC_DATA)");
        TocEntry[] chapters = JsonSerializer.Deserialize<TocEntry[]>(tocJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The page did not provide table-of-contents data.");
        await Assert.That(chapters.Length).IsGreaterThan(1);
        TocEntry chapter = chapters[0];
        TocEntry section = chapter.Items[0];
        ILocator summaries = page.Locator("#toc > ul > li > details > summary");
        await Expect(summaries).ToHaveCountAsync(chapters.Length);
        await page.GetByRole(AriaRole.Button, new() { Name = "Filter contents", Exact = true }).ClickAsync();
        ILocator filter = page.Locator("#toc-filter-input");
        await Expect(filter).ToBeVisibleAsync();
        await filter.FillAsync("no matching chapter 9e76d102");
        await Expect(summaries).ToHaveCountAsync(0);
        await filter.FillAsync(chapter.Title);
        await Expect(summaries).ToHaveCountAsync(1);
        await Expect(summaries).ToHaveTextAsync(chapter.Title);

        Uri chapterUrl = Resolve(baseUrl, chapter.Href);
        await TocLink(page, chapter.Href).ClickAsync();
        await Expect(page).ToHaveURLAsync(chapterUrl.ToString());
        await ExpectChapterAsync(page, chapter);

        string nextHref = await page.EvaluateAsync<string>("() => window.NEXT_PAGE");
        TocEntry next = Flatten(chapters).Single(entry => entry.Href == nextHref);
        Uri nextUrl = Resolve(baseUrl, nextHref);
        // Arrow buttons intentionally omit fragments; TOC links and keyboard navigation retain them.
        await Expect(page.Locator("#next-btn")).ToHaveAttributeAsync("href", next.Key);
        await page.Locator("#next-btn").ClickAsync();
        await Expect(page).ToHaveURLAsync(WithoutFragment(nextUrl));
        await ExpectSectionAsync(page, next, inViewport: false);

        string previousHref = await page.EvaluateAsync<string>("() => window.PREVIOUS_PAGE");
        await Assert.That(previousHref).IsEqualTo(chapter.Href);
        await Expect(page.Locator("#prev-btn")).ToHaveAttributeAsync("href", chapter.Key);
        await page.Locator("#prev-btn").ClickAsync();
        await Expect(page).ToHaveURLAsync(WithoutFragment(chapterUrl));
        await ExpectChapterAsync(page, chapter);

        if (section.Items.Length > 0)
        {
            await TocLink(page, section.Href).Locator("xpath=ancestor::details[1]/summary").ClickAsync();
        }
        await Expect(TocLink(page, section.Href)).ToBeVisibleAsync();
        await TocLink(page, section.Href).ClickAsync();
        Uri sectionUrl = Resolve(baseUrl, section.Href);
        await Expect(page).ToHaveURLAsync(sectionUrl.ToString());
        await ExpectSectionAsync(page, section, inViewport: true);
        await Expect(TocLink(page, section.Href)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("current-section"));

        await page.GotoAsync(new Uri(baseUrl, "home").ToString());
        IResponse? response = await page.GotoAsync(sectionUrl.ToString());
        await Assert.That(response?.Status).IsEqualTo(200);
        await Expect(page).ToHaveURLAsync(sectionUrl.ToString());
        await ExpectSectionAsync(page, section, inViewport: true);

        return new(chapter.Href, section.Href, nextHref, previousHref);
    }

    private static ILocator TocLink(IPage page, string href) =>
        page.Locator($"#toc a[href=\"{EscapeAttribute(href)}\"]");

    private static async Task ExpectChapterAsync(IPage page, TocEntry chapter)
    {
        ILocator heading = page.Locator("#site-layout .container .chapter-title");
        await Expect(heading).ToBeVisibleAsync();
        await Expect(heading).ToHaveTextAsync(chapter.Title[(chapter.Title.IndexOf(':') + 1)..].Trim());
    }

    private static async Task ExpectSectionAsync(IPage page, TocEntry section, bool inViewport)
    {
        string anchor = Uri.UnescapeDataString(new Uri("https://reading.test/" + section.Href).Fragment.TrimStart('#'));
        await Assert.That(anchor).IsNotNullOrWhiteSpace();
        // Generated pages can repeat an id on a nested span; target the actual section heading container.
        ILocator target = page.Locator($"#site-layout .container .section-heading[id=\"{EscapeAttribute(anchor)}\"]");
        await Expect(target).ToHaveCountAsync(1);
        await Expect(target).ToBeVisibleAsync();
        await Expect(target).ToContainTextAsync(section.Title);
        if (inViewport)
        {
            await Expect(target).ToBeInViewportAsync();
        }
    }

    private static Uri Resolve(Uri baseUrl, string href)
    {
        Uri url = new(baseUrl, href);
        if (url.GetLeftPart(UriPartial.Authority) != baseUrl.GetLeftPart(UriPartial.Authority))
        {
            throw new InvalidOperationException($"Reading link points outside the tested site: {href}");
        }
        return url;
    }

    private static string WithoutFragment(Uri url) => new UriBuilder(url) { Fragment = "" }.Uri.ToString();

    private static string EscapeAttribute(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static IEnumerable<TocEntry> Flatten(IEnumerable<TocEntry> entries) =>
        entries.SelectMany(entry => new[] { entry }.Concat(Flatten(entry.Items)));

    internal sealed record TocEntry(string Key, string Href, string Title, TocEntry[] Items);
}

internal sealed record ReadingJourneyResult(string ChapterHref, string SectionHref, string NextHref, string PreviousHref);
