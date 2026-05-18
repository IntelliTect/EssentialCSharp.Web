using EssentialCSharp.Web.Services;
using System.Text.RegularExpressions;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Parameterized tests for the route parameter regex in RouteConfigurationService.
/// Tests the pattern directly so any change to RouteParameterPattern is immediately caught.
/// </summary>
public class RouteParameterFilterTests
{
    private static readonly Regex s_regex = new(RouteConfigurationService.RouteParameterPattern);

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
        => await Assert.That(s_regex.IsMatch(route)).IsTrue();

    [Test]
    [Arguments("about")]
    [Arguments("mcp-setup")]
    [Arguments("identity/account/login")]
    [Arguments("api/listing")]
    [Arguments("")]
    public async Task RouteParameterRegex_DoesNotMatchStaticRoutes(string route)
        => await Assert.That(s_regex.IsMatch(route)).IsFalse();
}
