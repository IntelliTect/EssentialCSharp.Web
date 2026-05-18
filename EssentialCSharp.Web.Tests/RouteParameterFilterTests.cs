using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Parameterized tests for the route parameter regex in RouteConfigurationService.
/// </summary>
public class RouteParameterFilterTests
{
    [Test]
    [Arguments("{chapter}")]
    [Arguments("{id:guid}")]
    [Arguments("{id?}")]
    [Arguments("{action=Index}")]
    [Arguments("{*catchall}")]
    [Arguments("chapter/{chapter}/listing/{listing}")]
    [Arguments("[optional]")]
    [Arguments("area/[optional]/page")]
    public async Task RouteParameterRegex_MatchesParameterizedRoutes(string route)
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch(route)).IsTrue();

    [Test]
    [Arguments("about")]
    [Arguments("mcp-setup")]
    [Arguments("identity/account/login")]
    [Arguments("api/listing")]
    [Arguments("")]
    public async Task RouteParameterRegex_DoesNotMatchStaticRoutes(string route)
        => await Assert.That(RouteConfigurationService.RouteParameterRegex().IsMatch(route)).IsFalse();
}
