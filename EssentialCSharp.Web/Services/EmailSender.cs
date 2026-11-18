using System.Globalization;
using System.Text.RegularExpressions;
using Mailjet.Client;
using Mailjet.Client.Exceptions;
using Mailjet.Client.TransactionalEmails;
using Mailjet.Client.TransactionalEmails.Response;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace EssentialCSharp.Web.Services;

/// <summary>
/// Sends the app's transactional emails (account confirmation and password reset) through Mailjet Send API v3.1.
/// Every failure is logged (without recipient addresses) and then surfaced as an <see cref="EmailSendException"/>.
/// </summary>
public partial class EmailSender(IMailjetClient mailjetClient, IOptions<AuthMessageSenderOptions> options,
                   ILogger<EmailSender> logger) : IEmailSender
{
    private const int MaxLoggedExceptionMessageLength = 500;

    private AuthMessageSenderOptions Options { get; } = options.Value;

    /// <exception cref="EmailSendException">The email was not accepted by Mailjet or Mailjet could not be reached.</exception>
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        TransactionalEmailResponse response;
        try
        {
            // These emails carry single-use security links, so click tracking (which rewrites links through a
            // Mailjet tracking domain) and open tracking (a tracking pixel) are turned off for them.
            TransactionalEmailBuilder builder = new TransactionalEmailBuilder()
                .WithFrom(new SendContact(Options.SendFromEmail, Options.SendFromName))
                .WithSubject(subject)
                .WithHtmlPart(htmlMessage)
                .WithTo(new SendContact(email))
                .WithTrackClicks(TrackClicks.disabled)
                .WithTrackOpens(TrackOpens.disabled);

            if (HtmlToTextConverter.TryConvert(htmlMessage, out string textMessage))
            {
                // Mailjet generates nothing from an empty text part, so only send one when there is text.
                if (textMessage.Length > 0)
                {
                    builder.WithTextPart(textMessage);
                }
            }
            else
            {
                // The HTML part alone is still a valid message; never fail a send over the text alternative.
                LogPlainTextPartSkipped(logger);
            }

            TransactionalEmail message = builder.Build();

            response = await mailjetClient.SendTransactionalEmailAsync(message, Options.SandboxMode);
        }
        // JsonException: the SDK parses JSON-typed response bodies without a guard, so a truncated gateway response throws.
        catch (Exception ex) when (ex is MailjetException or HttpRequestException or JsonException or TaskCanceledException { InnerException: TimeoutException })
        {
            LogMailjetRequestFailed(logger, ex.GetType().Name, DescribeHttpStatus(ex), Truncate(Sanitize(ex.Message)));
            throw new EmailSendException("The email could not be sent because the request to Mailjet failed.", ex);
        }

        MessageResult[] messages = response.Messages ?? [];
        switch (messages.Length)
        {
            case 0:
                LogNoMessagesReturned(logger);
                throw new EmailSendException("The email could not be sent because Mailjet returned no message results.");
            case 1 when messages[0].Status == "success":
                // Sandbox mode reports "success" too (with empty message IDs) but never delivers the message.
                if (Options.SandboxMode)
                {
                    LogEmailAcceptedInSandbox(logger);
                }
                else
                {
                    LogEmailQueued(logger);
                }

                break;
            default:
                string status = messages.Select(m => m.Status).FirstOrDefault(s => s != "success") ?? "unknown";
                LogEmailSendFailure(logger, status, DescribeErrors(messages));
                throw new EmailSendException($"The email could not be sent because Mailjet reported status '{Sanitize(status)}'.");
        }
    }

    private static string DescribeHttpStatus(Exception exception) => exception switch
    {
        MailjetServerException { HttpCode: > 0 } server => server.HttpCode.ToString(CultureInfo.InvariantCulture),
        HttpRequestException { StatusCode: not null } http => ((int)http.StatusCode.Value).ToString(CultureInfo.InvariantCulture),
        _ => "none",
    };

    /// <summary>
    /// Summarizes Mailjet error details (code, HTTP status, message, related fields) for logging.
    /// Email addresses are stripped from Mailjet's text and the per-message recipient lists are never read,
    /// so recipient addresses do not reach the logs. Line breaks are collapsed to prevent log forging.
    /// </summary>
    internal static string DescribeErrors(IEnumerable<MessageResult> messages)
    {
        List<string> descriptions = [];
        foreach (SendEmailError error in messages.Where(m => m.Errors is not null).SelectMany(m => m.Errors))
        {
            string related = error.ErrorRelatedTo is { Length: > 0 } ? $" [related to: {string.Join(", ", error.ErrorRelatedTo)}]" : "";
            descriptions.Add(Sanitize($"{error.ErrorCode} (HTTP {error.StatusCode}): {error.ErrorMessage}{related}"));
        }

        return descriptions.Count == 0 ? "no error details returned" : string.Join("; ", descriptions);
    }

    private static string Truncate(string value) => value.Length <= MaxLoggedExceptionMessageLength ? value : value[..MaxLoggedExceptionMessageLength];

    private static string Sanitize(string value)
    {
        string singleLine = LineBreakRegex().Replace(value, " ");
        return EmailAddressRegex().Replace(singleLine, "[redacted-email]");
    }

    [GeneratedRegex(@"[\r\n]+")]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex(@"[^\s@<>""',;()\[\]]+@[^\s@<>""',;()\[\]]+")]
    private static partial Regex EmailAddressRegex();

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpectedly no messages returned in the mailer response")]
    private static partial void LogNoMessagesReturned(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email queued successfully.")]
    private static partial void LogEmailQueued(ILogger logger);

    // Warning because a deployed environment with sandbox mode left on would otherwise silently drop all mail.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Email accepted by Mailjet sandbox mode; it was validated but NOT delivered. Disable AuthMessageSender:SandboxMode outside testing.")]
    private static partial void LogEmailAcceptedInSandbox(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send email. Mailjet status: {Status}. Errors: {Errors}")]
    private static partial void LogEmailSendFailure(ILogger logger, string status, string errors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not derive a plain-text part from the email HTML; sending the HTML part only.")]
    private static partial void LogPlainTextPartSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send email. Mailjet request failed ({ExceptionType}, HTTP status: {HttpStatus}): {Details}")]
    private static partial void LogMailjetRequestFailed(ILogger logger, string exceptionType, string httpStatus, string details);
}
