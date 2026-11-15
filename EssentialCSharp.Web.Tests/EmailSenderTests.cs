using EssentialCSharp.Web.Services;
using Mailjet.Client;
using Mailjet.Client.TransactionalEmails;
using Mailjet.Client.TransactionalEmails.Response;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EssentialCSharp.Web.Tests;

public class EmailSenderTests
{
    private const string RecipientEmail = "reader@example.com";

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static async Task<CapturingLogger<EmailSender>> SendAsync(TransactionalEmailResponse response)
    {
        Mock<IMailjetClient> client = new();
        client.Setup(c => c.SendTransactionalEmailAsync(It.IsAny<TransactionalEmail>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(response);
        CapturingLogger<EmailSender> logger = new();
        EmailSender sender = new(client.Object, Options.Create(new AuthMessageSenderOptions { SendFromEmail = "noreply@example.com", SendFromName = "Test" }), logger);

        await sender.SendEmailAsync(RecipientEmail, "Subject", "<p>Body</p>");
        return logger;
    }

    [Test]
    public async Task Success_LogsInformationOnly()
    {
        CapturingLogger<EmailSender> logger = await SendAsync(new TransactionalEmailResponse
        {
            Messages = [new MessageResult { Status = "success" }]
        });

        await Assert.That(logger.Entries.Count).IsEqualTo(1);
        await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Information);
    }

    [Test]
    public async Task NoMessages_LogsAlertableErrorMessage()
    {
        CapturingLogger<EmailSender> logger = await SendAsync(new TransactionalEmailResponse { Messages = [] });

        await Assert.That(logger.Entries.Count).IsEqualTo(1);
        await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Error);
        // The Azure Monitor email-failure alert matches this exact text.
        await Assert.That(logger.Entries[0].Message).Contains("Unexpectedly no messages returned");
    }

    [Test]
    public async Task NullMessages_DoesNotThrowAndLogsError()
    {
        CapturingLogger<EmailSender> logger = await SendAsync(new TransactionalEmailResponse { Messages = null! });

        await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Error);
        await Assert.That(logger.Entries[0].Message).Contains("Unexpectedly no messages returned");
    }

    [Test]
    public async Task Failure_LogsMailjetErrorReason_WithoutRecipientAddress()
    {
        CapturingLogger<EmailSender> logger = await SendAsync(new TransactionalEmailResponse
        {
            Messages =
            [
                new MessageResult
                {
                    Status = "error",
                    To = [new EmailResult { Email = RecipientEmail }],
                    Errors =
                    [
                        new SendEmailError
                        {
                            ErrorCode = "mj-0013",
                            StatusCode = 400,
                            ErrorMessage = $"\"{RecipientEmail}\" is an invalid email address.\r\nSecond line",
                            ErrorRelatedTo = ["Messages[0].To[0].Email"],
                        }
                    ],
                }
            ]
        });

        string message = logger.Entries.Single().Message;
        await Assert.That(logger.Entries.Single().Level).IsEqualTo(LogLevel.Error);
        // The Azure Monitor email-failure alert matches this exact text.
        await Assert.That(message).Contains("Failed to send email");
        await Assert.That(message).Contains("error");
        await Assert.That(message).Contains("mj-0013");
        await Assert.That(message).Contains("HTTP 400");
        await Assert.That(message).Contains("is an invalid email address");
        await Assert.That(message).DoesNotContain(RecipientEmail);
        await Assert.That(message).DoesNotContain("\n");
    }

    [Test]
    public async Task Failure_WithoutErrorDetails_StillLogsAlertableMessage()
    {
        CapturingLogger<EmailSender> logger = await SendAsync(new TransactionalEmailResponse
        {
            Messages = [new MessageResult { Status = "error" }]
        });

        await Assert.That(logger.Entries.Single().Message).Contains("Failed to send email");
        await Assert.That(logger.Entries.Single().Message).Contains("no error details returned");
    }
}
