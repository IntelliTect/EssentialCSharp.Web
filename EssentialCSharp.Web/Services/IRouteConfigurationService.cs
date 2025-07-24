namespace EssentialCSharp.Web.Services;

public interface IRouteConfigurationService
{
    IReadOnlySet<string> GetStaticRoutes();
}
