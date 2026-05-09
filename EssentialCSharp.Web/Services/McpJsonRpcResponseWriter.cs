using ModelContextProtocol.Protocol;

namespace EssentialCSharp.Web.Services;

internal static class McpJsonRpcResponseWriter
{
    public static Task WriteErrorAsync(
        HttpResponse response,
        int statusCode,
        int errorCode,
        string message,
        CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.Headers.CacheControl = "no-store";

        string payload = System.Text.Json.JsonSerializer.Serialize(new McpJsonRpcErrorResponse(
            "2.0",
            null,
            new JsonRpcErrorDetail
            {
                Code = errorCode,
                Message = message
            }));

        return response.WriteAsync(payload, cancellationToken);
    }

    private sealed record McpJsonRpcErrorResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("jsonrpc")] string JsonRpc,
        [property: System.Text.Json.Serialization.JsonPropertyName("id")] object? Id,
        [property: System.Text.Json.Serialization.JsonPropertyName("error")] JsonRpcErrorDetail Error);
}
