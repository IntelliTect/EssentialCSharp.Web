namespace EssentialCSharp.Chat.Common.Services;

public class ChatBackendUnavailableException : Exception
{
    public string ErrorCode { get; }

    public ChatBackendUnavailableException(
        string message,
        string errorCode = "chat_unavailable",
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "chat_unavailable" : errorCode;
    }
}
