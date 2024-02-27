using Mailjet.Client;
using Mailjet.Client.Resources;
using Mailjet.Client.TransactionalEmails;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

public class EmailSender(IMailjetClient mailjetClient, IOptions<AuthMessageSenderOptions> options,
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
                _Logger.LogError("Unexpectedly no messages returned in the mailer response");
                break;
            case 1 when response.Messages.First().Status == "success":
                _Logger.LogInformation("Email to queued successfully!");
                break;
            default:
                _Logger.LogError("Failure To Send Email");
                break;
        }
    }
}
