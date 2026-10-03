using System.Text.Json;

namespace EssentialCSharp.Chat.Common.Services;

internal static class ChatContentFilterErrorClassifier
{
    internal static bool IsContentFilterFailure(string? code, string? reason, string? message) =>
        IsContentFilterCode(code) ||
        IsContentFilterCode(reason) ||
        (message is not null &&
            (message.Contains("content_filter", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("content filter", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("ResponsibleAIPolicyViolation", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("content policy violation", StringComparison.OrdinalIgnoreCase)));

    internal static bool ContainsContentFilterErrorCode(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            return ContainsContentFilterErrorCode(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsContentFilterCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = string.Concat(value.Where(char.IsLetterOrDigit));
        return normalized.Equals("contentfilter", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("contentfiltererror", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("contentfiltered", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("responsibleaipolicyviolation", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("contentpolicyviolation", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsContentFilterErrorCode(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name.Equals("code", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    IsContentFilterCode(property.Value.GetString()))
                {
                    return true;
                }

                if (ContainsContentFilterErrorCode(property.Value))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (ContainsContentFilterErrorCode(item))
                    return true;
            }
        }

        return false;
    }
}
