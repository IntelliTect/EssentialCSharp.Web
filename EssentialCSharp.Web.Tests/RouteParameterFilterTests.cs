using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Unit tests for the route parameter regex in RouteConfigurationService.
/// Calls RouteParameterRegex() directly to verify which patterns match (and should be
/// excluded from the sitemap) vs. which do not (and should be included).
/// </summary>
public class RouteParameterFilterTests
{
    // --- patterns that SHOULD match (route is parameterised → excluded from sitemap) ---

    [Test]
    public async Task RouteParameterRegex_MatchesSimpleCurlyBraceParameter()
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch("{chapter}")).IsTrue();

    [Test]
    public async Task RouteParameterRegex_MatchesConstrainedParameter()
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch("{id:guid}")).IsTrue();

    [Test]
    public async Task RouteParameterRegex_MatchesOptionalParameter()
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch("{id?}")).IsTrue();

    [Test]
    public async Task RouteParameterRegex_MatchesParameterWithDefault()
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch("{action=Index}")).IsTrue();

    [Test]
    public async Task RouteParameterRegex_MatchesCatchAllParameter()
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch("{*catchall}")).IsTrue();

    [Test]
    public async Task RouteParameterRegex_MatchesParameterEmbeddedInPath()
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch("chapter/{chapter}/listing/{listing}")).IsTrue();

    [Test]
    public async Task RouteParameterRegex_MatchesSquareBracketSegment()
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch("[optional]")).IsTrue();

    [Test]
    public async Task RouteParameterRegex_MatchesSquareBracketEmbeddedInPath()
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch("area/[optional]/page")).IsTrue();

    // --- patterns that should NOT match (static route → eligible for sitemap) ---

    [Test]
    public async Task RouteParameterRegex_DoesNotMatchBareWord()
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch("about")).IsFalse();

    [Test]
    public async Task RouteParameterRegex_DoesNotMatchHyphenatedRoute()
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch("mcp-setup")).IsFalse();

    [Test]
    public async Task RouteParameterRegex_DoesNotMatchStaticMultiSegmentPath()
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch("identity/account/login")).IsFalse();

    [Test]
    public async Task RouteParameterRegex_DoesNotMatchApiPrefixAlone()
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch("api/listing")).IsFalse();

    [Test]
    public async Task RouteParameterRegex_DoesNotMatchEmptyString()
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch(string.Empty)).IsFalse();
}
