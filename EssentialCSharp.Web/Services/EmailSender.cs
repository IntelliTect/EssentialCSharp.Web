using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace EssentialCSharp.Web.Services
{
    public class EmailSender : IEmailSender
    {
#pragma warning disable IDE1006 // Naming Styles
        private readonly ILogger _logger;
#pragma warning restore IDE1006 // Naming Styles

        public EmailSender(IOptions<AuthMessageSenderOptions> optionsAccessor,
                           ILogger<EmailSender> logger)
        {
            Options = optionsAccessor.Value;
            _logger = logger;
        }

        public AuthMessageSenderOptions Options { get; } //Set with Secret Manager.

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (string.IsNullOrEmpty(Options.SendGridKey))
            {
                throw new InvalidOperationException("Null SendGridKey");
            }
            await Execute(Options.SendGridKey, subject, htmlMessage, email);
        }

        public async Task Execute(string apiKey, string subject, string message, string toEmail)
        {
            var client = new SendGridClient(apiKey);
            var msg = new SendGridMessage()
            {
                From = new EmailAddress("Joe@contoso.com", "Password Recovery"),
                Subject = subject,
                PlainTextContent = message,
                HtmlContent = message
            };
            msg.AddTo(new EmailAddress(toEmail));

            // Disable click tracking.
            // See https://sendgrid.com/docs/User_Guide/Settings/tracking.html
            msg.SetClickTracking(false, false);
            Response response = await client.SendEmailAsync(msg);
            if (response.IsSuccessStatusCode)
            {
#pragma warning disable CA2254 // Template should be a static expression
                _logger.LogInformation($"Email to {toEmail} queued successfully!");
            }
            else
            {
                _logger.LogError($"Failure Email to {toEmail}");
#pragma warning restore CA2254 // Template should be a static expression
            }
        }
    }
}
