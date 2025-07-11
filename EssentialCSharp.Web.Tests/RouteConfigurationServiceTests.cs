using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

public class RouteConfigurationServiceTests : IClassFixture<WebApplicationFactory>
{
    private readonly WebApplicationFactory _Factory;
    private readonly IRouteConfigurationService _RouteConfigurationService;

    internal RouteConfigurationServiceTests(WebApplicationFactory factory)
    {
        _Factory = factory;

        // Get the service from the DI container to test with real routes
        var scope = _Factory.Services.CreateScope();
        _RouteConfigurationService = scope.ServiceProvider.GetRequiredService<IRouteConfigurationService>();
    }

    [Fact]
    public void GetStaticRoutes_ShouldReturnExpectedRoutes()
    {
        // Act
        var routes = _RouteConfigurationService.GetStaticRoutes().ToList();

        // Assert
        Assert.NotEmpty(routes);

        // Check for expected routes from the HomeController
        Assert.Contains("home", routes);
        Assert.Contains("about", routes);
        Assert.Contains("guidelines", routes);
        Assert.Contains("announcements", routes);
        Assert.Contains("termsofservice", routes);
    }

    [Fact]
    public void GetStaticRoutes_ShouldIncludeAllHomeControllerRoutes()
    {
        // Act
        var routes = _RouteConfigurationService.GetStaticRoutes().ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Assert - check all expected routes from HomeController
        var expectedRoutes = new[] { "home", "about", "guidelines", "announcements", "termsofservice" };

        foreach (var expectedRoute in expectedRoutes)
        {
            Assert.True(routes.Contains(expectedRoute),
                $"Expected route '{expectedRoute}' was not found in discovered routes: [{string.Join(", ", routes)}]");
        }
    }

    [Fact]
    public void GetStaticRoutes_ShouldNotIncludeIdentityRoutes()
    {
        // Act
        var routes = _RouteConfigurationService.GetStaticRoutes();

        // Assert - ensure no Identity area routes are included
        Assert.DoesNotContain("identity", routes, StringComparer.OrdinalIgnoreCase);
    }


}
