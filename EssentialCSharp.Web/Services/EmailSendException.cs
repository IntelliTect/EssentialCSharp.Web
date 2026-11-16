namespace EssentialCSharp.Web.Services;

/// <summary>
/// Thrown by <see cref="EmailSender"/> when an email could not be handed to the mail provider, whether the provider
/// rejected the message, returned an empty or unexpected response, or could not be reached.
/// </summary>
/// <remarks>
/// Messages never contain recipient addresses, but <see cref="Exception.InnerException"/> (the original provider or
/// network exception) can: Mailjet's raw error body may echo the recipient. Callers must therefore log only that a send
/// failed and never pass this exception (or its inner exception) to a logger. The sender has already logged sanitized
/// details. Callers should catch this type to show a friendly message, and must not let it reveal whether an account exists.
/// </remarks>
internal sealed class EmailSendException(string message, Exception? innerException = null)
    : Exception(message, innerException);
