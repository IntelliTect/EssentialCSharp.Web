using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

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
}