namespace EssentialCSharp.Chat.Common.Services;

/// <summary>
/// Thrown when a conversation's accumulated context exceeds the model's context window limit.
/// </summary>
/// <remarks>
/// This occurs when using <c>previous_response_id</c> chaining over many turns — the server
/// reconstructs the full history on each request, and that history eventually exceeds the model's
/// maximum input tokens. Callers should prompt the user to start a new conversation rather than
/// retrying with the same <c>previousResponseId</c>.
/// </remarks>
public sealed class ConversationContextLimitExceededException : Exception
{
    /// <summary>
    /// The <c>previous_response_id</c> that caused the overflow, if known.
    /// </summary>
    public string? PreviousResponseId { get; }

    public ConversationContextLimitExceededException(string? previousResponseId)
        : base("This conversation has exceeded the model's context window limit.")
    {
        PreviousResponseId = previousResponseId;
    }

    public ConversationContextLimitExceededException(string? previousResponseId, Exception innerException)
        : base("This conversation has exceeded the model's context window limit.", innerException)
    {
        PreviousResponseId = previousResponseId;
    }
}
