namespace EssentialCSharp.Chat;

public enum AIServiceMode
{
    Disabled,
    Local,
    Azure
}

public sealed record AIConfigurationState(AIServiceMode Mode)
{
    public const string DevelopmentUnavailableMessage =
        "AI chat is unavailable for this local run. Start the site with Aspire local AI or configure Azure AI to enable chat.";

    public bool IsAvailable => Mode is AIServiceMode.Local or AIServiceMode.Azure;
    public bool IsDisabled => Mode == AIServiceMode.Disabled;
    public bool UsesLocalAI => Mode == AIServiceMode.Local;
    public bool UsesAzureAI => Mode == AIServiceMode.Azure;

    public static AIConfigurationState From(AIOptions? options)
    {
        options ??= new AIOptions();

        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            return new(AIServiceMode.Azure);
        }

        if (options.UseLocalAI)
        {
            return new(AIServiceMode.Local);
        }

        return new(AIServiceMode.Disabled);
    }
}
