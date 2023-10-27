using Mailjet.Client;
using Mailjet.Client.Resources;
using Mailjet.Client.TransactionalEmails;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

public class EmailSender : IEmailSender
{

    private readonly ILogger _logger;
    private readonly IMailjetClient _mailjetClient;


    public EmailSender(IMailjetClient mailjetClient, IOptions<AuthMessageSenderOptions> optionsAccessor,
                       ILogger<EmailSender> logger)
    {
        Options = optionsAccessor.Value;
        _logger = logger;
        _mailjetClient = mailjetClient;
    }

    public AuthMessageSenderOptions Options { get; } //Set with Secret Manager.

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
        Mailjet.Client.TransactionalEmails.Response.TransactionalEmailResponse response = await _mailjetClient.SendTransactionalEmailAsync(email);
        switch (response.Messages.Length)
        {
            case 0:
                _logger.LogError("Unexpectedly no messages returned in the mailer response");
                break;
            case 1 when response.Messages.First().Status == "success":
                _logger.LogInformation("Email to {ToEmail} queued successfully!", toEmail);
                break;
            default:
                _logger.LogError("Failure To Send Email to {ToEmail} with the following Errors: {ErrorMessage}", toEmail, response.Messages.Aggregate(
                    string.Empty, (current, messageItem) =>
                    current + $"{messageItem.Errors.Aggregate(string.Empty,
                    (currentError, errorItem) =>
                    currentError + $"\t{errorItem.ErrorIdentifier}: {errorItem.ErrorCode}: {errorItem.ErrorMessage}\n")}\n"));
                break;
        }
    }
}
