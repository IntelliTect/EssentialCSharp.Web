namespace EssentialCSharp.Chat.Common.Services;

public sealed class AIChatUnavailableException(string message) : InvalidOperationException(message);
