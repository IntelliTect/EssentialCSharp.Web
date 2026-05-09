using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace EssentialCSharp.Web.Controllers;

[AllowAnonymous]
public class McpSetupController : BaseController
{
    private readonly IEnumerable<McpServerTool> _tools;

    public McpSetupController(IRouteConfigurationService routeConfigurationService, IHttpContextAccessor httpContextAccessor, IEnumerable<McpServerTool> tools)
        : base(routeConfigurationService, httpContextAccessor)
    {
        _tools = tools;
    }

    [Route("/mcp-setup")]
    public IActionResult Index()
    {
        ViewBag.PageTitle = "MCP Setup";
        var toolInfos = _tools
            .OrderBy(t => t.ProtocolTool.Name)
            .Select(t =>
            {
                var parameters = new List<McpParamInfo>();
                if (t.ProtocolTool.InputSchema.ValueKind == JsonValueKind.Object
                    && t.ProtocolTool.InputSchema.TryGetProperty("properties", out JsonElement props)
                    && props.ValueKind == JsonValueKind.Object)
                {
                    t.ProtocolTool.InputSchema.TryGetProperty("required", out JsonElement requiredEl);
                    foreach (JsonProperty prop in props.EnumerateObject())
                    {
                        string desc = prop.Value.TryGetProperty("description", out JsonElement d) ? d.GetString() ?? "" : "";
                        bool required = requiredEl.ValueKind == JsonValueKind.Array
                            && requiredEl.EnumerateArray().Any(r => r.GetString() == prop.Name);
                        parameters.Add(new McpParamInfo(prop.Name, desc, required));
                    }
                }
                return new McpToolInfo(
                    t.ProtocolTool.Name ?? "",
                    t.ProtocolTool.Title ?? t.ProtocolTool.Name ?? "",
                    t.ProtocolTool.Description ?? "",
                    parameters);
            })
            .ToList();

        return View(toolInfos);
    }
}

public sealed record McpToolInfo(string Name, string Title, string Description, IReadOnlyList<McpParamInfo> Parameters);
public sealed record McpParamInfo(string Name, string Description, bool Required);
