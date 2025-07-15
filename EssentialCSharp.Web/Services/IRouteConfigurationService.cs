using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace EssentialCSharp.Web.Services;

public interface IRouteConfigurationService
{
    IReadOnlySet<string> GetStaticRoutes();
}
