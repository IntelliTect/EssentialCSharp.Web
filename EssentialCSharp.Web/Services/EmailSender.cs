﻿using System.Text.RegularExpressions;
using Mailjet.Client;
using Mailjet.Client.Resources;
using Mailjet.Client.TransactionalEmails;
using Mailjet.Client.TransactionalEmails.Response;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

public partial class EmailSender(IMailjetClient mailjetClient, IOptions<AuthMessageSenderOptions> options,
                   ILogger<EmailSender> logger) : IEmailSender
{
    private readonly ILogger _Logger = logger;

    private AuthMessageSenderOptions Options { get; } = options.Value;

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        await Execute(subject, email, htmlMessage);
    }

    public async Task Execute(string subject, string toEmail, string message)
    {
        MailjetRequest request = new()
        {
            Resource = Send.Resource,
        };

        // construct your email with builder
        TransactionalEmail email = new TransactionalEmailBuilder()
               .WithFrom(new SendContact(Options.SendFromEmail, Options.SendFromName))
               .WithSubject(subject)
               .WithHtmlPart(message)
               .WithTo(new SendContact(toEmail))
               .Build();

        // invoke API to send email
        TransactionalEmailResponse response = await mailjetClient.SendTransactionalEmailAsync(email);
        MessageResult[] messages = response.Messages ?? [];
        switch (messages.Length)
        {
            case 0:
                LogNoMessagesReturned(_Logger);
                break;
            case 1 when messages[0].Status == "success":
                LogEmailQueued(_Logger);
                break;
            default:
                LogEmailSendFailure(_Logger, messages.Select(m => m.Status).FirstOrDefault(s => s != "success") ?? "unknown", DescribeErrors(messages));
                break;
        }
    }

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

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send email. Mailjet status: {Status}. Errors: {Errors}")]
    private static partial void LogEmailSendFailure(ILogger logger, string status, string errors);
}
