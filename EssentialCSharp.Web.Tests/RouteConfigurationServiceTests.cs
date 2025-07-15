using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

public class RouteConfigurationServiceTests : IClassFixture<WebApplicationFactory>
{
    private readonly WebApplicationFactory _Factory;

    public RouteConfigurationServiceTests(WebApplicationFactory factory)
    {
        _Factory = factory;
    }

    [Fact]
    public void GetStaticRoutes_ShouldReturnExpectedRoutes()
    {
        // Act
        var routes = _Factory.InServiceScope(serviceProvider =>
        {
            var routeConfigurationService = serviceProvider.GetRequiredService<IRouteConfigurationService>();
            return routeConfigurationService.GetStaticRoutes().ToList();
        });

        // Assert
        Assert.NotEmpty(routes);

        // Check for expected routes from the HomeController
        Assert.Contains("home", routes);
        Assert.Contains("about", routes);
        Assert.Contains("guidelines", routes);
        Assert.Contains("announcements", routes);
        Assert.Contains("termsofservice", routes);
    }
}
