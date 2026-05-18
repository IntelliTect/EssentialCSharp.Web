using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class RouteConfigurationServiceTests
{
    private readonly WebApplicationFactory _Factory;

    public RouteConfigurationServiceTests(WebApplicationFactory factory)
    {
        _Factory = factory;
    }

    [Test]
    public async Task GetStaticRoutes_ShouldReturnExpectedRoutes()
    {
        // Act
        var routes = _Factory.InServiceScope(serviceProvider =>
        {
            var routeConfigurationService = serviceProvider.GetRequiredService<IRouteConfigurationService>();
            return routeConfigurationService.GetStaticRoutes().ToList();
        });

        // Assert
        await Assert.That(routes).IsNotEmpty();

        // Check for expected routes from the HomeController
        await Assert.That(routes).Contains("home");
        await Assert.That(routes).Contains("about");
        await Assert.That(routes).Contains("guidelines");
        await Assert.That(routes).Contains("announcements");
        await Assert.That(routes).Contains("termsofservice");
    }

    [Test]
    public async Task GetIndexableRoutes_ShouldExcludeApiControllerRoutes()
    {
        // Act
        var routes = _Factory.InServiceScope(serviceProvider =>
        {
            var routeConfigurationService = serviceProvider.GetRequiredService<IRouteConfigurationService>();
            return routeConfigurationService.GetIndexableRoutes().ToList();
        });

        // Assert - no indexable route should start with api/ (matches the actual filter behavior)
        await Assert.That(routes).DoesNotContain(route =>
            route.StartsWith("api/", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task GetIndexableRoutes_ShouldExcludeParameterizedRoutes()
    {
        // Act
        var routes = _Factory.InServiceScope(serviceProvider =>
        {
            var routeConfigurationService = serviceProvider.GetRequiredService<IRouteConfigurationService>();
            return routeConfigurationService.GetIndexableRoutes().ToList();
        });

        // Assert - Routes with parameters should NOT be included
        await Assert.That(routes).DoesNotContain(route => 
            route.Contains('{'));
        await Assert.That(routes).DoesNotContain(route => 
            route.Contains("chapter", StringComparison.OrdinalIgnoreCase) && 
            route.Contains('{'));
    }

    [Test]
    public async Task GetIndexableRoutes_ShouldIncludeValidContentRoutes()
    {
        // Act
        var routes = _Factory.InServiceScope(serviceProvider =>
        {
            var routeConfigurationService = serviceProvider.GetRequiredService<IRouteConfigurationService>();
            return routeConfigurationService.GetIndexableRoutes().ToList();
        });

        // Assert - Valid content routes should be included
        await Assert.That(routes).Contains("home");
        await Assert.That(routes).Contains("about");
        await Assert.That(routes).Contains("guidelines");
        await Assert.That(routes).Contains("announcements");
        await Assert.That(routes).Contains("termsofservice");
        await Assert.That(routes).Contains("mcp-setup");
    }

    [Test]
    public async Task GetStaticRoutes_StillReturnsAllRoutes_ForBackwardCompatibility()
    {
        // Act
        var staticRoutes = _Factory.InServiceScope(serviceProvider =>
        {
            var routeConfigurationService = serviceProvider.GetRequiredService<IRouteConfigurationService>();
            return routeConfigurationService.GetStaticRoutes().ToList();
        });

        var indexableRoutes = _Factory.InServiceScope(serviceProvider =>
        {
            var routeConfigurationService = serviceProvider.GetRequiredService<IRouteConfigurationService>();
            return routeConfigurationService.GetIndexableRoutes().ToList();
        });

        // Assert - Static routes should include more than indexable routes (API routes, parameterized routes)
        await Assert.That(staticRoutes.Count).IsGreaterThan(indexableRoutes.Count);
    }
}