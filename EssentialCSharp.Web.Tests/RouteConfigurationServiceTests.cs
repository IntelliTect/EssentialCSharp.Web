using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

public class RouteConfigurationServiceTests : IntegrationTestBase
{
    [Test]
    public async Task GetStaticRoutes_ShouldReturnExpectedRoutes()
    {
        // Act
        var routes = InServiceScope(serviceProvider =>
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
}
