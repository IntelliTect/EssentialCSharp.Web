namespace EssentialCSharp.Chat.Common.Services;

public class ChatBackendUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
