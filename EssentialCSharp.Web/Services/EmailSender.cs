using Mailjet.Client;
using Mailjet.Client.Resources;
using Mailjet.Client.TransactionalEmails;
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
        Mailjet.Client.TransactionalEmails.Response.TransactionalEmailResponse response = await mailjetClient.SendTransactionalEmailAsync(email);
        switch (response.Messages.Length)
        {
            case 0:
                LogNoMessagesReturned(_Logger);
                break;
            case 1 when response.Messages.First().Status == "success":
                LogEmailQueued(_Logger);
                break;
            default:
                LogEmailSendFailure(_Logger);
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpectedly no messages returned in the mailer response")]
    private static partial void LogNoMessagesReturned(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email queued successfully.")]
    private static partial void LogEmailQueued(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send email.")]
    private static partial void LogEmailSendFailure(ILogger logger);
}
